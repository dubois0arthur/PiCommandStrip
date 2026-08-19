const protocolVersion = "2";
const defaultPort = 5078;
const reconnectMaximumMilliseconds = 30000;
const reconnectMinimumMilliseconds = 1000;
const stateDebounceMilliseconds = 100;

const browserCommandIds = new Set([
    "browser.back",
    "browser.forward",
    "browser.reload",
    "browser.newTab",
    "browser.closeTab",
    "browser.reopenClosedTab",
    "browser.copyCurrentUrl",
    "browser.searchSelection"
]);

let activePageSignal = {
    tabId: null,
    url: null,
    text: null,
    canGoBack: null,
    canGoForward: null
};
let authenticated = false;
let bridgeStatus = "unconfigured";
let captureTimer;
let configuration = { token: "", port: defaultPort };
let instanceIdentifier;
let lastSentState;
let reconnectAttempt = 0;
let reconnectTimer;
let socket;

function createMessageId() {
    return typeof crypto.randomUUID === "function"
        ? crypto.randomUUID()
        : `${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

function createEnvelope(type, payload) {
    return {
        type,
        messageId: createMessageId(),
        timestampUtc: new Date().toISOString(),
        payload
    };
}

function notifyStatus() {
    browser.runtime.sendMessage({
        type: "bridge_status_changed",
        status: bridgeStatus
    }).catch(() => {
        // No options page is listening.
    });
}

function setStatus(status) {
    bridgeStatus = status;
    notifyStatus();
}

function closeSocket() {
    clearTimeout(reconnectTimer);
    authenticated = false;
    const current = socket;
    socket = undefined;
    if (current?.readyState === WebSocket.OPEN ||
        current?.readyState === WebSocket.CONNECTING) {
        current.close(1000, "Bridge reconfiguring.");
    }
}

function scheduleReconnect() {
    clearTimeout(reconnectTimer);
    if (!configuration.token) {
        setStatus("unconfigured");
        return;
    }

    const base = Math.min(
        reconnectMaximumMilliseconds,
        reconnectMinimumMilliseconds * (2 ** reconnectAttempt));
    const delay = Math.round(base * (0.85 + Math.random() * 0.3));
    reconnectAttempt++;
    reconnectTimer = setTimeout(connect, delay);
}

function connect() {
    clearTimeout(reconnectTimer);
    if (!configuration.token ||
        socket?.readyState === WebSocket.OPEN ||
        socket?.readyState === WebSocket.CONNECTING) {
        return;
    }

    setStatus("connecting");
    const current = new WebSocket(
        `ws://127.0.0.1:${configuration.port}/browser-integration/ws`);
    socket = current;

    current.addEventListener("open", () => {
        if (socket !== current) return;
        current.send(JSON.stringify(createEnvelope("browser_hello", {
            protocolVersion,
            authenticationToken: configuration.token,
            browserType: "firefox",
            sourceIdentifier: browser.runtime.id,
            instanceIdentifier
        })));
    });

    current.addEventListener("message", event => {
        if (socket !== current) return;
        try {
            const message = JSON.parse(event.data);
            if (message.type === "browser_bridge_ready") {
                authenticated = true;
                reconnectAttempt = 0;
                lastSentState = undefined;
                setStatus("connected");
                scheduleCapture();
            } else if (message.type === "browser_command") {
                handleBrowserCommand(message);
            } else if (message.type === "error") {
                setStatus(message.payload?.code?.startsWith("authentication_")
                    ? "authentication_failed"
                    : "error");
            }
        } catch {
            setStatus("error");
        }
    });

    current.addEventListener("error", () => {
        if (socket === current) setStatus("error");
    });

    current.addEventListener("close", () => {
        if (socket !== current) return;
        socket = undefined;
        authenticated = false;
        if (bridgeStatus !== "authentication_failed") {
            setStatus("disconnected");
        }
        scheduleReconnect();
    });
}

function sendCommandResult(requestMessageId, succeeded, code) {
    if (!authenticated || socket?.readyState !== WebSocket.OPEN) return;
    socket.send(JSON.stringify(createEnvelope("browser_command_result", {
        requestMessageId,
        succeeded,
        code
    })));
}

function isSafeSearchUrl(value) {
    if (typeof value !== "string" || value.length > 2048) return false;
    try {
        const url = new URL(value);
        return url.protocol === "https:" && !url.username && !url.password;
    } catch {
        return false;
    }
}

async function handleBrowserCommand(message) {
    const requestMessageId = message?.messageId;
    const payload = message?.payload;
    const commandId = payload?.commandId;
    if (typeof requestMessageId !== "string" ||
        !browserCommandIds.has(commandId)) {
        sendCommandResult(requestMessageId, false, "invalid_command");
        return;
    }

    try {
        const tabs = await browser.tabs.query({ active: true, lastFocusedWindow: true });
        const activeTab = tabs[0];
        const requiresTab = commandId !== "browser.newTab" &&
            commandId !== "browser.reopenClosedTab";
        if (requiresTab &&
            (!activeTab || !Number.isInteger(payload.expectedActiveTabId) ||
             activeTab.id !== payload.expectedActiveTabId)) {
            sendCommandResult(requestMessageId, false, "stale_tab");
            return;
        }

        switch (commandId) {
            case "browser.back":
                await browser.tabs.goBack(activeTab.id);
                break;
            case "browser.forward":
                await browser.tabs.goForward(activeTab.id);
                break;
            case "browser.reload":
                await browser.tabs.reload(activeTab.id);
                break;
            case "browser.newTab":
                await browser.tabs.create({ active: true });
                break;
            case "browser.closeTab":
                await browser.tabs.remove(activeTab.id);
                break;
            case "browser.reopenClosedTab": {
                const sessions = await browser.sessions.getRecentlyClosed({ maxResults: 10 });
                const closedTab = sessions.find(session => session.tab?.sessionId);
                if (!closedTab) {
                    sendCommandResult(requestMessageId, false, "no_closed_tab");
                    return;
                }
                await browser.sessions.restore(closedTab.tab.sessionId);
                break;
            }
            case "browser.copyCurrentUrl":
                if (typeof activeTab.url !== "string" ||
                    !/^https?:\/\//i.test(activeTab.url)) {
                    sendCommandResult(requestMessageId, false, "clipboard_failed");
                    return;
                }
                await navigator.clipboard.writeText(activeTab.url);
                break;
            case "browser.searchSelection":
                if (!isSafeSearchUrl(payload.searchUrl)) {
                    sendCommandResult(requestMessageId, false, "invalid_command");
                    return;
                }
                await browser.tabs.create({ url: payload.searchUrl, active: true });
                break;
        }

        sendCommandResult(requestMessageId, true, "ok");
        scheduleCapture();
    } catch {
        sendCommandResult(
            requestMessageId,
            false,
            commandId === "browser.copyCurrentUrl" ? "clipboard_failed" : "command_failed");
        scheduleCapture();
    }
}

function normalizeSelection(value) {
    const text = typeof value === "string" ? value.trim() : "";
    if (!text) return null;
    return text.length <= 1000 ? text : text.slice(0, 1000);
}

async function readActiveTab() {
    const tabs = await browser.tabs.query({ active: true, lastFocusedWindow: true });
    const tab = tabs[0];
    if (!tab) {
        activePageSignal = { tabId: null, url: null, text: null, canGoBack: null, canGoForward: null };
        return {
            activeTabId: null,
            url: null,
            title: null,
            selectedText: null,
            canGoBack: null,
            canGoForward: null
        };
    }

    const url = typeof tab.url === "string" ? tab.url : null;
    if (activePageSignal.tabId !== tab.id || activePageSignal.url !== url) {
        activePageSignal = { tabId: tab.id, url, text: null, canGoBack: null, canGoForward: null };
    }

    return {
        activeTabId: Number.isInteger(tab.id) ? tab.id : null,
        url,
        title: typeof tab.title === "string" ? tab.title : null,
        selectedText: activePageSignal.text,
        canGoBack: activePageSignal.canGoBack,
        canGoForward: activePageSignal.canGoForward
    };
}

async function captureAndSend() {
    if (!authenticated || socket?.readyState !== WebSocket.OPEN) return;
    try {
        const state = await readActiveTab();
        const meaning = JSON.stringify(state);
        if (meaning === lastSentState) return;
        socket.send(JSON.stringify(createEnvelope("browser_state_update", state)));
        lastSentState = meaning;
    } catch {
        // A tab can disappear between an event and the query; the next event retries.
    }
}

function scheduleCapture() {
    clearTimeout(captureTimer);
    captureTimer = setTimeout(captureAndSend, stateDebounceMilliseconds);
}

browser.tabs.onActivated.addListener(() => {
    activePageSignal = { tabId: null, url: null, text: null, canGoBack: null, canGoForward: null };
    scheduleCapture();
});

browser.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
    if (!tab.active) return;
    if (Object.hasOwn(changeInfo, "url")) {
        activePageSignal = { tabId, url: changeInfo.url ?? null, text: null, canGoBack: null, canGoForward: null };
    }
    if (Object.hasOwn(changeInfo, "url") ||
        Object.hasOwn(changeInfo, "title") ||
        Object.hasOwn(changeInfo, "status")) {
        scheduleCapture();
    }
});

browser.tabs.onRemoved.addListener(tabId => {
    if (activePageSignal.tabId === tabId) {
        activePageSignal = { tabId: null, url: null, text: null, canGoBack: null, canGoForward: null };
    }
    scheduleCapture();
});

browser.windows.onFocusChanged.addListener(scheduleCapture);

browser.runtime.onConnect.addListener(port => {
    if (port.name !== "selection-observer") return;
    port.onMessage.addListener(message => {
        const senderTab = port.sender?.tab;
        if (message?.type !== "page_state_changed" || !senderTab?.active) return;
        activePageSignal = {
            tabId: senderTab.id,
            url: typeof senderTab.url === "string" ? senderTab.url : null,
            text: normalizeSelection(message.selectedText),
            canGoBack: typeof message.canGoBack === "boolean" ? message.canGoBack : null,
            canGoForward: typeof message.canGoForward === "boolean" ? message.canGoForward : null
        };
        scheduleCapture();
    });
});

browser.runtime.onMessage.addListener(message => {
    if (message?.type === "bridge_status_request") {
        return Promise.resolve({ status: bridgeStatus });
    }
    return undefined;
});

browser.storage.onChanged.addListener((changes, areaName) => {
    if (areaName !== "local" ||
        (!Object.hasOwn(changes, "pairingToken") &&
         !Object.hasOwn(changes, "bridgePort"))) {
        return;
    }
    initializeConfiguration();
});

async function initializeConfiguration() {
    const stored = await browser.storage.local.get([
        "pairingToken",
        "bridgePort",
        "instanceIdentifier"
    ]);
    instanceIdentifier = stored.instanceIdentifier;
    if (!instanceIdentifier) {
        instanceIdentifier = createMessageId();
        await browser.storage.local.set({ instanceIdentifier });
    }
    const parsedPort = Number(stored.bridgePort);
    configuration = {
        token: typeof stored.pairingToken === "string"
            ? stored.pairingToken.trim()
            : "",
        port: Number.isInteger(parsedPort) && parsedPort >= 1024 && parsedPort <= 65535
            ? parsedPort
            : defaultPort
    };
    closeSocket();
    reconnectAttempt = 0;
    if (configuration.token) connect();
    else setStatus("unconfigured");
}

initializeConfiguration();
