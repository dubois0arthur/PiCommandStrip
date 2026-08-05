import { DashboardSocket } from "./protocol.js";
import { dashboardUi } from "./ui.js";

const automaticPingIntervalMilliseconds = 10000;
let lastConnectionState;

const dashboardSocket = new DashboardSocket({
    onStatusChange(state, text) {
        dashboardUi.setConnection(state, text);

        if (state !== lastConnectionState) {
            if (state === "connected") {
                dashboardUi.addEvent("Connected to PC.", "success");
            } else if (state === "disconnected" || state === "error") {
                dashboardUi.addEvent("Disconnected from PC; retrying.", "warning");
            }
        }

        lastConnectionState = state;
    },

    onServerHello() {
        dashboardSocket.sendPing("automatic");
    },

    onPcState(state) {
        dashboardUi.renderPcState(state);
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

dashboardUi.bindPing(() => {
    if (dashboardSocket.sendPing("manual")) {
        dashboardUi.setManualPingPending();
    }
});

dashboardUi.addEvent("Dashboard initialized.");
dashboardUi.tickClock();
dashboardSocket.connect();

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
