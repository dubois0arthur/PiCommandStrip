const healthIndicator = document.querySelector("#health-indicator");
const healthStatus = document.querySelector("#health-status");
const healthDetails = document.querySelector("#health-details");
const applicationName = document.querySelector("#application-name");
const timestamp = document.querySelector("#timestamp");

const websocketIndicator = document.querySelector("#websocket-indicator");
const websocketStatus = document.querySelector("#websocket-status");
const pingButton = document.querySelector("#ping-button");
const pingResult = document.querySelector("#ping-result");

const pcStateIndicator = document.querySelector("#pc-state-indicator");
const pcStateStatus = document.querySelector("#pc-state-status");
const processName = document.querySelector("#process-name");
const processId = document.querySelector("#process-id");
const windowTitle = document.querySelector("#window-title");
const pcStateTimestamp = document.querySelector("#pc-state-timestamp");
const openNotepadButton = document.querySelector("#open-notepad-button");
const commandResult = document.querySelector("#command-result");

const reconnectDelayMilliseconds = 2000;
const pingTimeoutMilliseconds = 5000;
const pendingPings = new Map();
const pendingCommands = new Set();

let socket;
let reconnectTimer;
let connectionHadError = false;

function createMessageId() {
    if (typeof crypto.randomUUID === "function") {
        return crypto.randomUUID();
    }

    const bytes = crypto.getRandomValues(new Uint8Array(16));
    bytes[6] = (bytes[6] & 0x0f) | 0x40;
    bytes[8] = (bytes[8] & 0x3f) | 0x80;
    const hex = Array.from(bytes, byte => byte.toString(16).padStart(2, "0"));

    return `${hex.slice(0, 4).join("")}-${hex.slice(4, 6).join("")}-${hex.slice(6, 8).join("")}-${hex.slice(8, 10).join("")}-${hex.slice(10).join("")}`;
}

function createEnvelope(type, messageId, payload) {
    return {
        type,
        messageId,
        timestampUtc: new Date().toISOString(),
        payload
    };
}

function setWebSocketStatus(state, text) {
    websocketIndicator.className = "status-indicator";

    if (state === "connecting") {
        websocketIndicator.classList.add("is-connecting");
    } else if (state === "connected") {
        websocketIndicator.classList.add("is-healthy");
    } else {
        websocketIndicator.classList.add("is-unavailable");
    }

    websocketStatus.textContent = text;
}

function sendClientHello() {
    const messageId = createMessageId();
    socket.send(JSON.stringify(createEnvelope("client_hello", messageId, {
        clientName: "browser-dashboard",
        protocolVersion: "1"
    })));
}

function handlePong(message) {
    const requestMessageId = message.payload?.requestMessageId;
    const pendingPing = pendingPings.get(requestMessageId);

    if (!pendingPing) {
        return;
    }

    clearTimeout(pendingPing.timeout);
    pendingPings.delete(requestMessageId);
    const roundTripMilliseconds = performance.now() - pendingPing.startedAt;
    pingResult.textContent = `Pong received in ${roundTripMilliseconds.toFixed(1)} ms.`;
}

function renderPcState(state) {
    pcStateIndicator.className = "status-indicator";

    if (state.isAvailable) {
        pcStateIndicator.classList.add("is-healthy");
        pcStateStatus.textContent = "Foreground application detected";
        processName.textContent = state.processName;
        processId.textContent = state.processId;
        windowTitle.textContent = state.windowTitle || "No window title";
    } else {
        pcStateIndicator.classList.add("is-unavailable");
        pcStateStatus.textContent = "No usable foreground window";
        processName.textContent = "—";
        processId.textContent = "—";
        windowTitle.textContent = "—";
    }

    pcStateTimestamp.textContent = new Date(state.observedAtUtc).toLocaleString();
}

function handleCommandResult(message) {
    if (message.payload?.commandId !== "open_notepad") {
        return;
    }

    pendingCommands.delete(message.payload.requestMessageId);
    openNotepadButton.disabled = !socket || socket.readyState !== WebSocket.OPEN;
    commandResult.className = message.payload.succeeded ? "is-success" : "is-failure";

    const completedAt = new Date(message.payload.completedAtUtc).toLocaleTimeString();
    commandResult.textContent = `${message.payload.message} (${completedAt})`;
}

function handleServerMessage(event) {
    try {
        const message = JSON.parse(event.data);

        switch (message.type) {
            case "server_hello":
                websocketStatus.textContent = `Connected to ${message.payload.applicationName} (protocol ${message.payload.protocolVersion})`;
                break;
            case "pong":
                handlePong(message);
                break;
            case "pc_state":
                renderPcState(message.payload);
                break;
            case "command_result":
                handleCommandResult(message);
                break;
            case "error":
                websocketStatus.textContent = `Server error: ${message.payload.message}`;
                break;
            default:
                console.warn("Unknown server message type.", message);
                break;
        }
    } catch (error) {
        setWebSocketStatus("error", "Error: invalid message from server");
        console.error("Could not process WebSocket message.", error);
    }
}

function scheduleReconnect() {
    clearTimeout(reconnectTimer);
    reconnectTimer = setTimeout(connectWebSocket, reconnectDelayMilliseconds);
}

function connectWebSocket() {
    connectionHadError = false;
    setWebSocketStatus("connecting", "Connecting");
    pingButton.disabled = true;
    openNotepadButton.disabled = true;

    const webSocketScheme = window.location.protocol === "https:" ? "wss" : "ws";

    try {
        socket = new WebSocket(`${webSocketScheme}://${window.location.host}/ws`);
    } catch (error) {
        setWebSocketStatus("error", "Error: could not create connection; retrying soon");
        console.error("Could not create WebSocket.", error);
        scheduleReconnect();
        return;
    }

    socket.addEventListener("open", () => {
        setWebSocketStatus("connected", "Connected");
        pingButton.disabled = false;
        openNotepadButton.disabled = false;
        sendClientHello();
    });

    socket.addEventListener("message", handleServerMessage);

    socket.addEventListener("error", () => {
        connectionHadError = true;
        setWebSocketStatus("error", "Error: WebSocket connection failed");
    });

    socket.addEventListener("close", () => {
        pingButton.disabled = true;
        openNotepadButton.disabled = true;
        pendingPings.forEach(ping => clearTimeout(ping.timeout));
        pendingPings.clear();
        const commandWasPending = pendingCommands.size > 0;
        pendingCommands.clear();
        pingResult.textContent = "No ping pending.";

        if (commandWasPending) {
            commandResult.className = "is-failure";
            commandResult.textContent = "Disconnected before the command completed.";
        }

        if (connectionHadError) {
            setWebSocketStatus("error", "Error: disconnected; retrying in 2 seconds");
        } else {
            setWebSocketStatus("disconnected", "Disconnected; retrying in 2 seconds");
        }

        scheduleReconnect();
    });
}

pingButton.addEventListener("click", () => {
    if (!socket || socket.readyState !== WebSocket.OPEN) {
        return;
    }

    const messageId = createMessageId();
    const timeout = setTimeout(() => {
        pendingPings.delete(messageId);
        pingResult.textContent = "Ping timed out.";
    }, pingTimeoutMilliseconds);

    pendingPings.set(messageId, {
        startedAt: performance.now(),
        timeout
    });

    pingResult.textContent = "Waiting for pong...";
    socket.send(JSON.stringify(createEnvelope("ping", messageId, {})));
});

openNotepadButton.addEventListener("click", () => {
    if (!socket || socket.readyState !== WebSocket.OPEN) {
        return;
    }

    const messageId = createMessageId();
    pendingCommands.add(messageId);
    openNotepadButton.disabled = true;
    commandResult.className = "";
    commandResult.textContent = "Opening Notepad...";
    socket.send(JSON.stringify(createEnvelope("command_request", messageId, {
        commandId: "open_notepad"
    })));
});

window.addEventListener("beforeunload", () => {
    clearTimeout(reconnectTimer);

    if (socket && socket.readyState === WebSocket.OPEN) {
        socket.close(1000, "Dashboard closing.");
    }
});

async function checkHealth() {
    try {
        const response = await fetch("/health", {
            headers: { Accept: "application/json" },
            cache: "no-store"
        });

        if (!response.ok) {
            throw new Error(`Health endpoint returned HTTP ${response.status}.`);
        }

        const health = await response.json();

        healthIndicator.classList.add("is-healthy");
        healthStatus.textContent = "/health is reachable";
        applicationName.textContent = health.applicationName;
        timestamp.textContent = new Date(health.timestampUtc).toLocaleString();
        healthDetails.hidden = false;
    } catch (error) {
        healthIndicator.classList.add("is-unavailable");
        healthStatus.textContent = "/health could not be reached";
        console.error("Health check failed.", error);
    }
}

checkHealth();
connectWebSocket();
