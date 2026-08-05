const reconnectDelayMilliseconds = 2000;
const pingTimeoutMilliseconds = 5000;

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

export class DashboardSocket {
    #callbacks;
    #connectionHadError = false;
    #pendingPings = new Map();
    #reconnectTimer;
    #socket;
    #stopped = false;

    constructor(callbacks) {
        this.#callbacks = callbacks;
    }

    get isConnected() {
        return this.#socket?.readyState === WebSocket.OPEN;
    }

    connect() {
        this.#stopped = false;
        this.#openConnection();
    }

    disconnect() {
        this.#stopped = true;
        clearTimeout(this.#reconnectTimer);
        this.#clearPendingPings();

        if (this.#socket?.readyState === WebSocket.OPEN) {
            this.#socket.close(1000, "Dashboard closing.");
        }
    }

    sendPing(source = "manual") {
        if (!this.isConnected) {
            return false;
        }

        const messageId = createMessageId();
        const timeout = setTimeout(() => {
            this.#pendingPings.delete(messageId);
            this.#callbacks.onPingTimeout?.(source);
        }, pingTimeoutMilliseconds);

        this.#pendingPings.set(messageId, {
            source,
            startedAt: performance.now(),
            timeout
        });

        this.#send("ping", messageId, {});
        return true;
    }

    sendOpenNotepad() {
        if (!this.isConnected) {
            return null;
        }

        const messageId = createMessageId();
        this.#send("command_request", messageId, { commandId: "open_notepad" });
        return messageId;
    }

    #openConnection() {
        this.#connectionHadError = false;
        this.#callbacks.onStatusChange?.("connecting", "Connecting");

        const scheme = window.location.protocol === "https:" ? "wss" : "ws";
        let socket;

        try {
            socket = new WebSocket(`${scheme}://${window.location.host}/ws`);
            this.#socket = socket;
        } catch (error) {
            this.#callbacks.onStatusChange?.("error", "Connection error");
            this.#callbacks.onProtocolError?.("Could not create WebSocket connection.", error);
            this.#scheduleReconnect();
            return;
        }

        socket.addEventListener("open", () => {
            if (this.#socket !== socket) {
                return;
            }

            this.#callbacks.onStatusChange?.("connected", "Connected");
            this.#send("client_hello", createMessageId(), {
                clientName: "browser-dashboard",
                protocolVersion: "1"
            });
        });

        socket.addEventListener("message", event => this.#handleMessage(event));

        socket.addEventListener("error", () => {
            if (this.#socket !== socket) {
                return;
            }

            this.#connectionHadError = true;
            this.#callbacks.onStatusChange?.("error", "Connection error");
        });

        socket.addEventListener("close", () => {
            if (this.#socket !== socket) {
                return;
            }

            this.#clearPendingPings();
            this.#socket = undefined;
            const state = this.#connectionHadError ? "error" : "disconnected";
            const text = this.#connectionHadError ? "Connection error — retrying" : "Disconnected — retrying";
            this.#callbacks.onStatusChange?.(state, text);
            this.#callbacks.onDisconnected?.();
            this.#scheduleReconnect();
        });
    }

    #handleMessage(event) {
        try {
            const message = JSON.parse(event.data);

            switch (message.type) {
                case "server_hello":
                    this.#callbacks.onServerHello?.(message.payload);
                    break;
                case "pc_state":
                    this.#callbacks.onPcState?.(message.payload);
                    break;
                case "pong":
                    this.#handlePong(message.payload);
                    break;
                case "command_result":
                    this.#callbacks.onCommandResult?.(message.payload);
                    break;
                case "error":
                    this.#callbacks.onServerError?.(message.payload);
                    break;
                default:
                    this.#callbacks.onProtocolError?.(`Unknown server message type: ${message.type}`);
                    break;
            }
        } catch (error) {
            this.#callbacks.onProtocolError?.("Could not process a server message.", error);
        }
    }

    #handlePong(payload) {
        const pendingPing = this.#pendingPings.get(payload?.requestMessageId);
        if (!pendingPing) {
            return;
        }

        clearTimeout(pendingPing.timeout);
        this.#pendingPings.delete(payload.requestMessageId);
        this.#callbacks.onPong?.({
            roundTripMilliseconds: performance.now() - pendingPing.startedAt,
            source: pendingPing.source
        });
    }

    #send(type, messageId, payload) {
        this.#socket.send(JSON.stringify(createEnvelope(type, messageId, payload)));
    }

    #scheduleReconnect() {
        if (this.#stopped) {
            return;
        }

        clearTimeout(this.#reconnectTimer);
        this.#reconnectTimer = setTimeout(
            () => this.#openConnection(),
            reconnectDelayMilliseconds);
    }

    #clearPendingPings() {
        this.#pendingPings.forEach(ping => clearTimeout(ping.timeout));
        this.#pendingPings.clear();
    }
}
