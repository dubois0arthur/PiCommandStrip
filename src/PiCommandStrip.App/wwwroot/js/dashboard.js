import { DashboardSocket } from "./protocol.js";
import { dashboardUi } from "./ui.js";

const automaticPingIntervalMilliseconds = 10000;
const tokenStorageKey = "pi-command-strip-token";
let lastConnectionState;

function readStoredToken() {
    try {
        return sessionStorage.getItem(tokenStorageKey);
    } catch {
        return null;
    }
}

function storeToken(token) {
    try {
        sessionStorage.setItem(tokenStorageKey, token);
    } catch {
        // The in-memory DashboardSocket still holds the token for this page.
    }
}

function clearStoredToken() {
    try {
        sessionStorage.removeItem(tokenStorageKey);
    } catch {
        // Storage can be unavailable in privacy-restricted browser sessions.
    }
}

const dashboardSocket = new DashboardSocket({
    onStatusChange(state, text) {
        dashboardUi.setConnection(state, text);

        if (state !== lastConnectionState) {
            if (state === "connected") {
                dashboardUi.addEvent("Connected to PC.", "success");
            } else if (state === "disconnected" || state === "error") {
                dashboardUi.addEvent(text, "warning");
            }
        }

        lastConnectionState = state;
    },

    onAuthenticated() {
        dashboardUi.showAuthenticated();
        dashboardSocket.sendPing("automatic");
    },

    onAuthenticating() {
        dashboardUi.showAuthenticating();
    },

    onAuthenticationRequired(message) {
        clearStoredToken();
        dashboardUi.showAuthenticationRequired(message);
    },

    onPcState(state) {
        dashboardUi.renderPcState(state);
    },

    onContextState(state) {
        dashboardUi.renderContextState(state);
    },

    onContextSelectionResult(result) {
        dashboardUi.showContextSelectionResult(result);
    },

    onPong(result) {
        dashboardUi.showPong(result.roundTripMilliseconds, result.source);
    },

    onPingTimeout(source) {
        dashboardUi.showPingTimeout(source);
    },

    onCommandResult(result) {
        if (result.commandId === "open_notepad") {
            dashboardUi.showCommandResult(result);
        }
    },

    onServerHello(server) {
        dashboardUi.setAvailableContexts(server.availableContexts);
    },

    onServerError(error) {
        dashboardUi.addEvent(`Server error: ${error.message}`, "failure");
    },

    onProtocolError(message, error) {
        dashboardUi.addEvent(message, "failure");
        if (error) {
            console.error(message, error);
        }
    },

    onDisconnected() {
        dashboardUi.showDisconnectedCommand();
    }
});

dashboardUi.bindOpenNotepad(() => {
    if (dashboardSocket.sendOpenNotepad()) {
        dashboardUi.setCommandPending();
    }
});

dashboardUi.bindContextSelection(selection => {
    if (dashboardSocket.sendContextSelection(selection)) {
        dashboardUi.setContextSelectionPending();
    }
});

dashboardUi.bindAuthentication(token => {
    if (!token) {
        dashboardUi.showAuthenticationRequired("Enter the pre-shared token.");
        return;
    }

    storeToken(token);
    dashboardSocket.connect(token);
});

dashboardUi.bindPing(() => {
    if (dashboardSocket.sendPing("manual")) {
        dashboardUi.setManualPingPending();
    }
});

dashboardUi.bindNavigationOverride();
dashboardUi.initializeLayoutDebug();
dashboardUi.addEvent("Dashboard initialized.");
dashboardUi.tickClock();
const storedToken = readStoredToken();
if (storedToken) {
    dashboardSocket.connect(storedToken);
} else {
    dashboardUi.setConnection("disconnected", "Authentication required");
    dashboardUi.showAuthenticationRequired();
}

const clockTimer = setInterval(() => dashboardUi.tickClock(), 1000);
const automaticPingTimer = setInterval(() => {
    if (dashboardSocket.isConnected) {
        dashboardSocket.sendPing("automatic");
    }
}, automaticPingIntervalMilliseconds);

window.addEventListener("beforeunload", () => {
    clearInterval(clockTimer);
    clearInterval(automaticPingTimer);
    dashboardSocket.disconnect();
});
