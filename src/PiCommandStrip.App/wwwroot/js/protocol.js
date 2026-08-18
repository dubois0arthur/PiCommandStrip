const reconnectDelayMilliseconds = 2000;
const pingTimeoutMilliseconds = 5000;
const protocolVersion = "11";
const mediaCommandIds = new Set([
    "media.play",
    "media.pause",
    "media.playPause",
    "media.previous",
    "media.next",
    "media.seek"
]);
const audioCommandIds = new Set([
    "audio.setMasterVolume",
    "audio.setMasterMute",
    "audio.setApplicationVolume",
    "audio.setApplicationMute",
    "audio.setOutputDevice"
]);
const spotifyCommandIds = new Set([
    "spotify.setSaved",
    "spotify.setShuffle",
    "spotify.setRepeat"
]);

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

const terminalAuthenticationErrors = new Set([
    "authentication_missing",
    "authentication_failed",
    "authentication_rate_limited"
]);

export class DashboardSocket {
    #authenticated = false;
    #callbacks;
    #connectionHadError = false;
    #pendingPings = new Map();
    #reconnectTimer;
    #socket;
    #stopped = true;
    #token;

    constructor(callbacks) {
        this.#callbacks = callbacks;
    }

    get isConnected() {
        return this.#authenticated && this.#socket?.readyState === WebSocket.OPEN;
    }

    connect(token) {
        if (!token) {
            this.#callbacks.onAuthenticationRequired?.("Enter the pre-shared token.");
            return;
        }

        this.disconnect();
        this.#token = token;
        this.#stopped = false;
        this.#openConnection();
    }

    disconnect() {
        this.#stopped = true;
        this.#authenticated = false;
        clearTimeout(this.#reconnectTimer);
        this.#clearPendingPings();

        const socket = this.#socket;
        this.#socket = undefined;
        if (socket?.readyState === WebSocket.OPEN || socket?.readyState === WebSocket.CONNECTING) {
            socket.close(1000, "Dashboard closing.");
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

    sendMediaCommand(commandId, positionMilliseconds) {
        if (!this.isConnected || !mediaCommandIds.has(commandId)) {
            return null;
        }

        const payload = { commandId };
        if (commandId === "media.seek") {
            if (!Number.isFinite(positionMilliseconds) || positionMilliseconds < 0) {
                return null;
            }

            payload.positionMilliseconds = Math.round(positionMilliseconds);
        } else if (positionMilliseconds !== undefined) {
            return null;
        }

        const messageId = createMessageId();
        this.#send("command_request", messageId, payload);
        return messageId;
    }

    sendAudioCommand(commandId, { applicationId, volume, isMuted, deviceId } = {}) {
        if (!this.isConnected || !audioCommandIds.has(commandId)) {
            return null;
        }

        const payload = { commandId };
        const isApplicationCommand = commandId === "audio.setApplicationVolume" ||
            commandId === "audio.setApplicationMute";
        const isVolumeCommand = commandId === "audio.setMasterVolume" ||
            commandId === "audio.setApplicationVolume";
        const isOutputDeviceCommand = commandId === "audio.setOutputDevice";

        if (isOutputDeviceCommand) {
            if (typeof deviceId !== "string" ||
                deviceId.trim().length === 0 ||
                deviceId.length > 512) {
                return null;
            }
            payload.deviceId = deviceId;
        }

        if (isApplicationCommand) {
            if (typeof applicationId !== "string" ||
                !/^[0-9a-f]{64}$/i.test(applicationId)) {
                return null;
            }
            payload.applicationId = applicationId;
        }

        if (isVolumeCommand) {
            if (!Number.isFinite(volume) || volume < 0 || volume > 1) {
                return null;
            }
            payload.volume = Math.round(volume * 1000) / 1000;
        } else if (!isOutputDeviceCommand) {
            if (typeof isMuted !== "boolean") {
                return null;
            }
            payload.isMuted = isMuted;
        }

        const messageId = createMessageId();
        this.#send("command_request", messageId, payload);
        return messageId;
    }

    sendSpotifyCommand(commandId, { isSaved, shuffleEnabled, repeatState } = {}) {
        if (!this.isConnected || !spotifyCommandIds.has(commandId)) {
            return null;
        }

        const payload = { commandId };
        if (commandId === "spotify.setSaved") {
            if (typeof isSaved !== "boolean") return null;
            payload.isSaved = isSaved;
        } else if (commandId === "spotify.setShuffle") {
            if (typeof shuffleEnabled !== "boolean") return null;
            payload.shuffleEnabled = shuffleEnabled;
        } else {
            if (!["off", "context", "track"].includes(repeatState)) return null;
            payload.repeatState = repeatState;
        }

        const messageId = createMessageId();
        this.#send("command_request", messageId, payload);
        return messageId;
    }

    sendContextSelection(selection) {
        if (!this.isConnected || !selection) {
            return null;
        }

        const messageId = createMessageId();
        const payload = selection === "automatic"
            ? { mode: "automatic" }
            : { mode: "manual", contextId: selection };
        this.#send("context_selection_request", messageId, payload);
        return messageId;
    }

    #openConnection() {
        if (this.#stopped) {
            return;
        }

        this.#authenticated = false;
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
            if (this.#socket !== socket || this.#stopped) {
                socket.close(1000, "Dashboard connection stopped.");
                return;
            }

            this.#callbacks.onStatusChange?.("connecting", "Authenticating");
            this.#callbacks.onAuthenticating?.();
        });

        socket.addEventListener("message", event => {
            if (this.#socket === socket) {
                this.#handleMessage(event);
            }
        });

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

            this.#authenticated = false;
            this.#clearPendingPings();
            this.#socket = undefined;
            const state = this.#connectionHadError ? "error" : "disconnected";
            const text = this.#stopped
                ? "Authentication required"
                : this.#connectionHadError
                    ? "Connection error - retrying"
                    : "Disconnected - retrying";
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
                    this.#send("client_hello", createMessageId(), {
                        clientName: "browser-dashboard",
                        protocolVersion,
                        authenticationToken: this.#token
                    });
                    this.#callbacks.onServerHello?.(message.payload);
                    break;
                case "pc_state":
                    if (!this.#authenticated) {
                        this.#authenticated = true;
                        this.#callbacks.onStatusChange?.("connected", "Authenticated");
                        this.#callbacks.onAuthenticated?.();
                    }
                    this.#callbacks.onPcState?.(message.payload);
                    break;
                case "context_state":
                    this.#callbacks.onContextState?.(message.payload);
                    break;
                case "media_state":
                    this.#callbacks.onMediaState?.(message.payload);
                    break;
                case "audio_state":
                    this.#callbacks.onAudioState?.(message.payload);
                    break;
                case "spotify_state":
                    this.#callbacks.onSpotifyState?.(message.payload);
                    break;
                case "browser_state":
                    this.#callbacks.onBrowserState?.(message.payload);
                    break;
                case "context_selection_result":
                    this.#callbacks.onContextSelectionResult?.(message.payload);
                    break;
                case "pong":
                    this.#handlePong(message.payload);
                    break;
                case "command_result":
                    this.#callbacks.onCommandResult?.(message.payload);
                    break;
                case "error":
                    this.#handleServerError(message.payload);
                    break;
                default:
                    this.#callbacks.onProtocolError?.(`Unknown server message type: ${message.type}`);
                    break;
            }
        } catch (error) {
            this.#callbacks.onProtocolError?.("Could not process a server message.", error);
        }
    }

    #handleServerError(error) {
        this.#callbacks.onServerError?.(error);

        if (error?.code === "authentication_expired") {
            this.#socket?.close(4001, "Authentication attempt expired.");
            return;
        }

        if (terminalAuthenticationErrors.has(error?.code)) {
            this.#stopped = true;
            this.#token = undefined;
            this.#callbacks.onAuthenticationRequired?.(error.message);
            this.#socket?.close(4003, "Authentication failed.");
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
