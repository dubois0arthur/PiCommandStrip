import { DashboardSocket } from "./protocol.js?v=15";
import { dashboardUi } from "./ui.js?v=15";

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
    if (["no-media", "default-no-media"].includes(layoutFixture)) {
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

    const mediaFixtures = {
        media: ["Spotify", "Pretty (Ugly Before)", "Elliott Smith"],
        "default-media": ["Spotify", "Weird Fishes / Arpeggi", "Radiohead"],
        "browser-owned": ["Firefox", "A practical browser media session", "YouTube"],
        "browser-foreign": ["Spotify", "Everything In Its Right Place", "Radiohead"],
        gaming: ["Spotify", "Game soundtrack playlist", "Spotify"]
    };
    const fixture = mediaFixtures[layoutFixture];
    if (fixture) {
        return {
            hasActiveSession: true,
            sessionSourceIdentifier: `${fixture[0]}.exe`,
            sourceName: fixture[0],
            title: fixture[1],
            artist: fixture[2],
            albumTitle: null,
            playbackState: "playing",
            positionMilliseconds: 163000,
            totalDurationMilliseconds: 314000,
            supportsPrevious: true,
            supportsNext: true,
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
    const contextFixtures = {
        audio: ["audio", "Audio", "PiCommandStrip.App", "PiCommandStrip"],
        media: ["media", "Media", "Spotify", "Spotify Premium"],
        "default-media": ["default", "Default", "explorer", "Documents"],
        "default-no-media": ["default", "Default", "explorer", "Documents"],
        "browser-owned": ["browser", "Browser / Research", "firefox", "A practical browser media session — YouTube"],
        "browser-foreign": ["browser", "Browser / Research", "firefox", "PiCommandStrip research — Mozilla Firefox"],
        gaming: ["gaming", "Gaming", "cyberpunk2077", "Cyberpunk 2077"]
    };
    const fixture = contextFixtures[layoutFixture];
    if (!fixture) {
        return state;
    }

    return {
        ...state,
        contextId: fixture[0],
        displayName: fixture[1],
        selectionMode: "manual",
        source: "layout_fixture",
        trigger: layoutFixture,
        foregroundProcess: fixture[2],
        foregroundWindowTitle: fixture[3],
        activeSinceUtc: new Date().toISOString()
    };
}

function applyAudioFixture(state) {
    const audioFixtures = new Set([
        "audio",
        "media",
        "default-media",
        "default-no-media",
        "browser-owned",
        "browser-foreign",
        "gaming"
    ]);
    if (!audioFixtures.has(layoutFixture)) {
        return state;
    }

    const application = (suffix, processName, displayName, volume, active, extra = {}) => ({
        applicationId: suffix.repeat(64).slice(0, 64),
        processIds: [4000 + suffix.charCodeAt(0)],
        processName,
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
        outputDevices: [
            {
                deviceId: "layout-fixture-output",
                friendlyName: "Speakers (USB Audio Device)",
                state: "active",
                isDefault: true
            },
            {
                deviceId: "layout-fixture-headphones",
                friendlyName: "Headphones (Realtek USB2.0 Audio)",
                state: "active",
                isDefault: false
            },
            {
                deviceId: "layout-fixture-monitor",
                friendlyName: "DELL U2723QE (Display Audio)",
                state: "active",
                isDefault: false
            }
        ],
        applications: [
            application("a", "cyberpunk2077", "Cyberpunk 2077", 0.78, true),
            application("b", "discord", "Discord", 0.61, true, { sessionCount: 2 }),
            application("c", "spotify", "Spotify", 0.44, true),
            application("d", "firefox", "Firefox — YouTube and research tabs", 0.35,
                layoutFixture === "browser-owned", {
                sessionCount: 3,
                hasMixedVolume: true
            }),
            application("e", "obs64", "OBS Studio", 0.2, layoutFixture === "gaming"),
            application("f", "longprocess", "A deliberately long application name that must truncate", 0.5, false)
        ],
        revision: (state?.revision || 0) + 1,
        lastUpdatedUtc: new Date().toISOString()
    };
}

function applySpotifyFixture(state) {
    if (!["media", "default-media", "browser-foreign", "gaming"].includes(layoutFixture)) {
        return state;
    }

    return {
        status: "available",
        isConfigured: true,
        isAuthenticated: true,
        appliesToCurrentMedia: true,
        itemType: "track",
        isSaved: layoutFixture !== "gaming",
        shuffleEnabled: layoutFixture === "gaming",
        repeatState: layoutFixture === "default-media" ? "context" : "off",
        device: {
            name: "Office PC",
            type: "computer",
            isRestricted: false
        },
        queue: [
            { title: "Next item", subtitle: "Example artist", itemType: "track" },
            { title: "A deliberately long queued item title that must truncate", subtitle: "Another artist", itemType: "track" },
            { title: "Podcast episode", subtitle: "Example show", itemType: "episode" }
        ],
        lastUpdatedUtc: new Date().toISOString(),
        retryAfterUtc: null
    };
}

function applyBrowserFixture(state) {
    if (!["browser-owned", "browser-foreign"].includes(layoutFixture)) {
        return state;
    }

    return {
        connectionState: "connected",
        browserType: "firefox",
        sourceIdentifier: "firefox-bridge@picommandstrip.local",
        instanceIdentifier: "layout-fixture",
        activeTabId: 42,
        url: "https://developer.mozilla.org/en-US/docs/Mozilla/Add-ons/WebExtensions",
        hostName: "developer.mozilla.org",
        pageTitle: layoutFixture === "browser-owned"
            ? "A practical browser media session — YouTube"
            : "PiCommandStrip research — Mozilla Firefox",
        hasSelectedText: layoutFixture === "browser-foreign",
        canGoBack: null,
        canGoForward: null,
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

    onSpotifyState(state) {
        dashboardUi.renderSpotifyState(applySpotifyFixture(state));
    },

    onBrowserState(state) {
        dashboardUi.renderBrowserState(applyBrowserFixture(state));
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
        } else if (result.commandId.startsWith("spotify.")) {
            dashboardUi.showSpotifyCommandResult(result);
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

dashboardUi.bindSpotifyControls(request => dashboardSocket.sendSpotifyCommand(
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
