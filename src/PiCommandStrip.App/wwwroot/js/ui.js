const maximumEventEntries = 8;

const elements = {
    actionResult: document.querySelector("#action-result"),
    authenticationForm: document.querySelector("#authentication-form"),
    authenticationMessage: document.querySelector("#authentication-message"),
    authenticationPanel: document.querySelector("#authentication-panel"),
    authenticationToken: document.querySelector("#authentication-token"),
    connectionIndicator: document.querySelector("#connection-indicator"),
    connectionState: document.querySelector("#connection-state"),
    contextAge: document.querySelector("#context-age"),
    contextStatus: document.querySelector("#context-status"),
    currentTime: document.querySelector("#current-time"),
    eventCount: document.querySelector("#event-count"),
    eventLog: document.querySelector("#event-log"),
    latencyValue: document.querySelector("#latency-value"),
    openNotepadButton: document.querySelector("#open-notepad-button"),
    pingButton: document.querySelector("#ping-button"),
    processId: document.querySelector("#process-id"),
    processName: document.querySelector("#process-name"),
    windowTitle: document.querySelector("#window-title")
};

let commandPending = false;
let connected = false;
let contextChangedAt;
let manualPingPending = false;

function updateButtonStates() {
    elements.openNotepadButton.disabled = !connected || commandPending;
    elements.pingButton.disabled = !connected || manualPingPending;
}

function formatElapsed(timestamp) {
    if (!timestamp || Number.isNaN(timestamp.getTime())) {
        return "—";
    }

    const totalSeconds = Math.max(0, Math.floor((Date.now() - timestamp.getTime()) / 1000));
    if (totalSeconds < 5) {
        return "Just now";
    }

    if (totalSeconds < 60) {
        return `${totalSeconds}s ago`;
    }

    const totalMinutes = Math.floor(totalSeconds / 60);
    if (totalMinutes < 60) {
        return `${totalMinutes}m ago`;
    }

    const totalHours = Math.floor(totalMinutes / 60);
    return `${totalHours}h ago`;
}

export const dashboardUi = {
    addEvent(message, tone = "neutral", timestamp = new Date()) {
        const item = document.createElement("li");
        item.className = "event-item";
        item.dataset.tone = tone;

        const time = document.createElement("time");
        time.className = "event-time";
        time.dateTime = timestamp.toISOString();
        time.textContent = timestamp.toLocaleTimeString([], {
            hour: "2-digit",
            minute: "2-digit",
            second: "2-digit"
        });

        const text = document.createElement("span");
        text.className = "event-message";
        text.textContent = message;

        item.append(time, text);
        elements.eventLog.prepend(item);

        while (elements.eventLog.children.length > maximumEventEntries) {
            elements.eventLog.lastElementChild.remove();
        }

        elements.eventCount.textContent = `${elements.eventLog.children.length} / ${maximumEventEntries}`;
    },

    bindOpenNotepad(callback) {
        elements.openNotepadButton.addEventListener("click", callback);
    },

    bindAuthentication(callback) {
        elements.authenticationForm.addEventListener("submit", event => {
            event.preventDefault();
            const token = elements.authenticationToken.value.trim();
            elements.authenticationToken.value = "";
            callback(token);
        });
    },

    showAuthenticationRequired(message = "Authentication is required.") {
        elements.authenticationPanel.hidden = false;
        elements.authenticationMessage.textContent = message;
        elements.authenticationToken.focus();
    },

    showAuthenticating() {
        elements.authenticationPanel.hidden = false;
        elements.authenticationMessage.textContent = "Authenticatingâ€¦";
    },

    showAuthenticated() {
        elements.authenticationPanel.hidden = true;
    },

    bindPing(callback) {
        elements.pingButton.addEventListener("click", callback);
    },

    renderPcState(state) {
        contextChangedAt = new Date(state.observedAtUtc);

        if (state.isAvailable) {
            elements.contextStatus.dataset.state = "available";
            elements.contextStatus.textContent = "Active context";
            elements.processName.textContent = state.processName;
            elements.windowTitle.textContent = state.windowTitle || "No window title";
            elements.processId.textContent = state.processId;
            this.addEvent(`Context changed to ${state.processName} (PID ${state.processId}).`, "neutral");
        } else {
            elements.contextStatus.dataset.state = "unavailable";
            elements.contextStatus.textContent = "Context unavailable";
            elements.processName.textContent = "No active context";
            elements.windowTitle.textContent = "Windows did not report a usable foreground window.";
            elements.processId.textContent = "—";
            this.addEvent("Foreground context became unavailable.", "warning");
        }

        elements.contextAge.textContent = formatElapsed(contextChangedAt);
    },

    setCommandPending() {
        commandPending = true;
        elements.actionResult.dataset.state = "pending";
        elements.actionResult.textContent = "Opening Notepad…";
        updateButtonStates();
    },

    showCommandResult(result) {
        commandPending = false;
        elements.actionResult.dataset.state = result.succeeded ? "success" : "failure";
        elements.actionResult.textContent = result.message;
        this.addEvent(
            `open_notepad: ${result.message}`,
            result.succeeded ? "success" : "failure",
            new Date(result.completedAtUtc));
        updateButtonStates();
    },

    showDisconnectedCommand() {
        if (!commandPending) {
            return;
        }

        commandPending = false;
        elements.actionResult.dataset.state = "failure";
        elements.actionResult.textContent = "Disconnected before command completion.";
        this.addEvent("Command interrupted by disconnect.", "failure");
        updateButtonStates();
    },

    setConnection(state, text) {
        connected = state === "connected";
        elements.connectionIndicator.dataset.state = state;
        elements.connectionState.textContent = text;

        if (!connected) {
            manualPingPending = false;
        }

        updateButtonStates();
    },

    setManualPingPending() {
        manualPingPending = true;
        elements.actionResult.dataset.state = "pending";
        elements.actionResult.textContent = "Measuring round trip…";
        updateButtonStates();
    },

    showPong(roundTripMilliseconds, source) {
        const roundedLatency = roundTripMilliseconds.toFixed(1);
        elements.latencyValue.textContent = `${roundedLatency} ms`;

        if (source === "manual") {
            manualPingPending = false;
            elements.actionResult.dataset.state = "success";
            elements.actionResult.textContent = `Pong received in ${roundedLatency} ms.`;
            this.addEvent(`Ping completed in ${roundedLatency} ms.`, "success");
            updateButtonStates();
        }
    },

    showPingTimeout(source) {
        elements.latencyValue.textContent = "Timed out";

        if (source === "manual") {
            manualPingPending = false;
            elements.actionResult.dataset.state = "failure";
            elements.actionResult.textContent = "Ping timed out.";
            this.addEvent("Ping timed out.", "failure");
            updateButtonStates();
        }
    },

    tickClock() {
        const now = new Date();
        elements.currentTime.dateTime = now.toISOString();
        elements.currentTime.textContent = now.toLocaleTimeString([], {
            hour: "2-digit",
            minute: "2-digit",
            second: "2-digit"
        });
        elements.contextAge.textContent = formatElapsed(contextChangedAt);
    }
};
