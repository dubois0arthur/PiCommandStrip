const elements = {
    actionResult: document.querySelector("#action-result"),
    activityAnnouncer: document.querySelector("#activity-announcer"),
    appShell: document.querySelector("#app-shell"),
    authenticationForm: document.querySelector("#authentication-form"),
    authenticationMessage: document.querySelector("#authentication-message"),
    authenticationPanel: document.querySelector("#authentication-panel"),
    authenticationToken: document.querySelector("#authentication-token"),
    connectionIndicator: document.querySelector("#connection-indicator"),
    connectionState: document.querySelector("#connection-state"),
    contextPanel: document.querySelector(".context-panel"),
    contextAge: document.querySelector("#context-age"),
    contextSelection: document.querySelector("#context-selection"),
    contextStatus: document.querySelector("#context-status"),
    currentTime: document.querySelector("#current-time"),
    headerContext: document.querySelector("#header-context"),
    latencyValue: document.querySelector("#latency-value"),
    layoutDebug: document.querySelector("#layout-debug"),
    navigationOverrideButton: document.querySelector("#navigation-override-button"),
    offlineMessage: document.querySelector("#offline-message"),
    offlineState: document.querySelector("#offline-state"),
    actionsPanel: document.querySelector(".actions-panel"),
    openNotepadButton: document.querySelector("#open-notepad-button"),
    pingButton: document.querySelector("#ping-button"),
    processId: document.querySelector("#process-id"),
    processName: document.querySelector("#process-name"),
    viewportDimensions: document.querySelector("#viewport-dimensions"),
    warningMessage: document.querySelector("#warning-message"),
    windowTitle: document.querySelector("#window-title")
};

let commandPending = false;
let connected = false;
let contextChangedAt;
let contextSelectionPending = false;
let lastContextSelection = "automatic";
let lastRenderedContextKey;
let layoutDebugEnabled = false;
let manualPingPending = false;

function updateButtonStates() {
    elements.openNotepadButton.disabled = !connected || commandPending;
    elements.pingButton.disabled = !connected || manualPingPending;
    elements.contextSelection.disabled = !connected || contextSelectionPending;
}

function setActionState(button, state) {
    if (state) {
        button.dataset.state = state;
    } else {
        delete button.dataset.state;
    }

    if (state === "processing") {
        button.setAttribute("aria-busy", "true");
    } else {
        button.removeAttribute("aria-busy");
    }
}

function setWarning(message) {
    elements.warningMessage.textContent = message;
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

function updateViewportDimensions() {
    elements.viewportDimensions.textContent =
        `${window.innerWidth} × ${window.innerHeight} CSS px · DPR ${window.devicePixelRatio.toFixed(2)}`;
}

function setLayoutDebug(enabled) {
    layoutDebugEnabled = enabled;
    document.body.classList.toggle("layout-debug-mode", enabled);
    elements.layoutDebug.hidden = !enabled;

    if (enabled) {
        updateViewportDimensions();
    }
}

export const dashboardUi = {
    addEvent(message, tone = "neutral") {
        elements.activityAnnouncer.textContent = message;

        if (tone === "warning" || tone === "failure") {
            setWarning(message);
        }
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

    bindContextSelection(callback) {
        elements.contextSelection.addEventListener("change", () => {
            callback(elements.contextSelection.value);
        });
    },

    bindNavigationOverride() {
        elements.navigationOverrideButton.addEventListener("click", () => {
            elements.activityAnnouncer.textContent = "Primary command strip selected.";
            window.requestAnimationFrame(() => elements.processName.focus({ preventScroll: true }));
        });
    },

    initializeLayoutDebug() {
        const query = new URLSearchParams(window.location.search);
        setLayoutDebug(query.get("layoutDebug") === "1");

        window.addEventListener("keydown", event => {
            if (event.ctrlKey && event.shiftKey && event.key.toLowerCase() === "d") {
                event.preventDefault();
                setLayoutDebug(!layoutDebugEnabled);
            }
        });

        window.addEventListener("resize", updateViewportDimensions);
        window.visualViewport?.addEventListener("resize", updateViewportDimensions);
    },

    showAuthenticationRequired(message = "Authentication is required.") {
        elements.authenticationPanel.hidden = false;
        elements.authenticationMessage.textContent = message;
        elements.authenticationToken.focus();
    },

    showAuthenticating() {
        elements.authenticationPanel.hidden = false;
        elements.authenticationMessage.textContent = "Authenticating…";
    },

    showAuthenticated() {
        elements.authenticationPanel.hidden = true;
    },

    bindPing(callback) {
        elements.pingButton.addEventListener("click", callback);
    },

    renderPcState(state) {
        if (state.isAvailable) {
            const processName = state.processName || "Unknown application";
            elements.processName.textContent = processName;
            elements.windowTitle.textContent = state.windowTitle || "No window title";
            elements.processId.textContent = state.processId;
            setWarning("Trusted private network only.");
        } else {
            elements.processName.textContent = "No foreground process";
            elements.windowTitle.textContent = "Windows did not report a usable foreground window.";
            elements.processId.textContent = "—";
            this.addEvent("Foreground process unavailable; the Default context remains available.", "warning");
        }
    },

    renderContextState(state) {
        contextChangedAt = new Date(state.activeSinceUtc);
        const selectionValue = state.selectionMode === "manual"
            ? state.contextId
            : "automatic";
        lastContextSelection = selectionValue;
        elements.contextSelection.value = selectionValue;
        elements.headerContext.textContent = state.displayName;
        elements.contextStatus.dataset.state = "available";

        const sourceLabels = {
            fallback: "fallback",
            foreground_process: state.trigger,
            manual_override: "pinned"
        };
        const modeLabel = state.selectionMode === "manual" ? "Manual" : "Automatic";
        elements.contextStatus.textContent = `${modeLabel} · ${sourceLabels[state.source] || state.source}`;
        elements.contextAge.textContent = formatElapsed(contextChangedAt);

        const contextKey = `${state.contextId}:${state.selectionMode}:${state.activeSinceUtc}`;
        if (contextKey !== lastRenderedContextKey) {
            this.addEvent(`${state.displayName} context active in ${modeLabel.toLowerCase()} mode.`);
            lastRenderedContextKey = contextKey;
        }
    },

    setAvailableContexts(contexts = []) {
        const automaticOption = document.createElement("option");
        automaticOption.value = "automatic";
        automaticOption.textContent = "Automatic";
        const options = [automaticOption];

        contexts.forEach(context => {
            const option = document.createElement("option");
            option.value = context.contextId;
            option.textContent = `Pin: ${context.displayName}`;
            options.push(option);
        });

        elements.contextSelection.replaceChildren(...options);
        elements.contextSelection.value = lastContextSelection;
    },

    setContextSelectionPending() {
        contextSelectionPending = true;
        elements.actionResult.dataset.state = "pending";
        elements.actionResult.textContent = "Updating context selection…";
        updateButtonStates();
    },

    showContextSelectionResult(result) {
        contextSelectionPending = false;
        elements.actionResult.dataset.state = result.succeeded ? "success" : "failure";
        elements.actionResult.textContent = result.message;

        if (!result.succeeded) {
            elements.contextSelection.value = lastContextSelection;
        }

        this.addEvent(result.message, result.succeeded ? "success" : "failure");
        updateButtonStates();
    },

    setCommandPending() {
        commandPending = true;
        elements.actionResult.dataset.state = "pending";
        elements.actionResult.textContent = "Opening Notepad…";
        setActionState(elements.openNotepadButton, "processing");
        updateButtonStates();
    },

    showCommandResult(result) {
        commandPending = false;
        const state = result.succeeded ? "success" : "failure";
        elements.actionResult.dataset.state = state;
        elements.actionResult.textContent = result.message;
        setActionState(elements.openNotepadButton, state);
        this.addEvent(
            `open_notepad: ${result.message}`,
            state);
        updateButtonStates();
    },

    showDisconnectedCommand() {
        if (!commandPending) {
            return;
        }

        commandPending = false;
        elements.actionResult.dataset.state = "failure";
        elements.actionResult.textContent = "Disconnected before command completion.";
        setActionState(elements.openNotepadButton, "failure");
        this.addEvent("Command interrupted by disconnect.", "failure");
        updateButtonStates();
    },

    setConnection(state, text) {
        connected = state === "connected";
        elements.appShell.dataset.connection = state;
        elements.connectionIndicator.dataset.state = state;
        elements.connectionState.textContent = text.toLowerCase().includes("retrying")
            ? "Retrying connection"
            : text;
        elements.offlineState.hidden = connected;
        [elements.contextPanel, elements.actionsPanel].forEach(panel => {
            panel.inert = !connected;
            panel.setAttribute("aria-hidden", connected ? "false" : "true");
        });

        if (connected) {
            elements.offlineMessage.textContent = "Trying to reconnect. Context and commands will return automatically.";
            setWarning("Trusted private network only.");
        } else {
            manualPingPending = false;
            contextSelectionPending = false;
            elements.latencyValue.textContent = "—";
            elements.headerContext.textContent = state === "connecting" ? "Waiting for PC" : "PC unavailable";
            elements.offlineMessage.textContent = state === "connecting"
                ? "Connecting to the Windows host. Controls will enable when authentication completes."
                : "Trying to reconnect. Context and commands will return automatically.";
            setWarning("PC unavailable — controls disabled.");
            setActionState(elements.pingButton, null);
        }

        updateButtonStates();
    },

    setManualPingPending() {
        manualPingPending = true;
        elements.actionResult.dataset.state = "pending";
        elements.actionResult.textContent = "Measuring round trip…";
        setActionState(elements.pingButton, "processing");
        updateButtonStates();
    },

    showPong(roundTripMilliseconds, source) {
        const roundedLatency = roundTripMilliseconds.toFixed(1);
        elements.latencyValue.textContent = `${roundedLatency} ms`;

        if (source === "manual") {
            manualPingPending = false;
            elements.actionResult.dataset.state = "success";
            elements.actionResult.textContent = `Pong received in ${roundedLatency} ms.`;
            setActionState(elements.pingButton, "success");
            this.addEvent(`Ping completed in ${roundedLatency} ms.`);
            updateButtonStates();
        }
    },

    showPingTimeout(source) {
        elements.latencyValue.textContent = "Timed out";

        if (source === "manual") {
            manualPingPending = false;
            elements.actionResult.dataset.state = "failure";
            elements.actionResult.textContent = "Ping timed out.";
            setActionState(elements.pingButton, "failure");
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
