import { DashboardSocket } from "./protocol.js";
import { dashboardUi } from "./ui.js";

const automaticPingIntervalMilliseconds = 10000;
const tokenStorageKey = "pi-command-strip-token";
let lastConnectionState;

function readLayoutFixture() {
    const isLoopback = window.location.hostname === "127.0.0.1" ||
        window.location.hostname === "localhost" ||
        window.location.hostname === "::1";
    if (!isLoopback || new URLSearchParams(window.location.search).get("layoutDebug") !== "1") {
        return null;
    }

    return new URLSearchParams(window.location.search).get("layoutFixture");
}

const layoutFixture = readLayoutFixture();

function applyLayoutFixture(state) {
    if (layoutFixture === "no-media") {
        return {
            hasActiveSession: false,
            playbackState: "none",
            lastUpdatedAtUtc: new Date().toISOString()
        };
    }

    if (layoutFixture === "long-media") {
        return {
            hasActiveSession: true,
            sessionSourceIdentifier: "Firefox",
            sourceName: "Firefox",
            title: "A deliberately very long browser media title that must truncate predictably without pushing the touchscreen controls off screen",
            artist: null,
            albumTitle: null,
            playbackState: "playing",
            positionMilliseconds: 3723000,
            totalDurationMilliseconds: 7265000,
            supportsPrevious: false,
            supportsNext: false,
            supportsPlay: true,
            supportsPause: true,
            supportsSeeking: true,
            artworkUrl: null,
            lastUpdatedAtUtc: new Date().toISOString()
        };
    }

    return state;
}

function applyContextFixture(state) {
    if (layoutFixture !== "audio") {
        return state;
    }

    return {
        ...state,
        contextId: "audio",
        displayName: "Audio",
        selectionMode: "manual",
        source: "layout_fixture",
        trigger: "audio",
        activeSinceUtc: new Date().toISOString()
    };
}

function applyAudioFixture(state) {
    if (layoutFixture !== "audio") {
        return state;
    }

    const application = (suffix, displayName, volume, active, extra = {}) => ({
        applicationId: suffix.repeat(64).slice(0, 64),
        processIds: [4000 + suffix.charCodeAt(0)],
        processName: displayName.toLowerCase(),
        displayName,
        volume,
        isMuted: false,
        state: active ? "active" : "inactive",
        sessionCount: 1,
        hasMixedVolume: false,
        hasMixedMute: false,
        ...extra
    });

    return {
        isAvailable: true,
        outputDevice: {
            deviceId: "layout-fixture-output",
            friendlyName: "Speakers (USB Audio Device)",
            volume: 0.72,
            isMuted: false
        },
        applications: [
            application("a", "Cyberpunk 2077", 0.78, true),
            application("b", "Discord", 0.61, true, { sessionCount: 2 }),
            application("c", "Spotify", 0.44, true),
            application("d", "Firefox — YouTube and research tabs", 0.35, false, {
                sessionCount: 3,
                hasMixedVolume: true
            }),
            application("e", "System notification host", 0.2, false),
            application("f", "A deliberately long application name that must truncate", 0.5, false)
        ],
        revision: (state?.revision || 0) + 1,
        lastUpdatedUtc: new Date().toISOString()
    };
}

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
        dashboardUi.renderContextState(applyContextFixture(state));
    },

    onMediaState(state) {
        dashboardUi.renderMediaState(applyLayoutFixture(state));
    },

    onAudioState(state) {
        dashboardUi.renderAudioState(applyAudioFixture(state));
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
        } else if (result.commandId.startsWith("media.")) {
            dashboardUi.showMediaCommandResult(result);
        } else if (result.commandId.startsWith("audio.")) {
            dashboardUi.showAudioCommandResult(result);
        }
    },

    onServerHello(server) {
        dashboardUi.setAvailableContexts(server.availableContexts);
        dashboardUi.setProtocolVersion(server.protocolVersion);
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

dashboardUi.bindContextSelection(selection => {
    if (dashboardSocket.sendContextSelection(selection)) {
        dashboardUi.setContextSelectionPending();
    }
});

dashboardUi.bindMediaControls((commandId, positionMilliseconds) => {
    if (dashboardSocket.sendMediaCommand(commandId, positionMilliseconds)) {
        dashboardUi.setMediaCommandPending(commandId);
    }
});

dashboardUi.bindAudioControls(request => dashboardSocket.sendAudioCommand(
    request.commandId,
    request));

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

dashboardUi.bindNavigation();
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
