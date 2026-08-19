import { AudioMixerController } from "./audio-mixer.js?v=17";
import {
    buildContextComposition,
    ContextCompositionController
} from "./context-composition.js?v=17";
import { NowPlayingController } from "./now-playing.js?v=17";
import { ResearchInboxController } from "./research-inbox.js?v=17";
import { ResearchWorkspaceController } from "./research-workspace.js?v=17";
import { SpotifyControlsController } from "./spotify-controls.js?v=17";

const elements = {
    activityAnnouncer: document.querySelector("#activity-announcer"),
    appShell: document.querySelector("#app-shell"),
    audioApplicationList: document.querySelector("#audio-application-list"),
    audioApplicationSummary: document.querySelector("#audio-application-summary"),
    audioApplicationTemplate: document.querySelector("#audio-application-template"),
    audioEmptyState: document.querySelector("#audio-empty-state"),
    audioMasterMute: document.querySelector("#audio-master-mute"),
    audioMasterPercentage: document.querySelector("#audio-master-percentage"),
    audioMasterVolume: document.querySelector("#audio-master-volume"),
    audioOutputDevice: document.querySelector("#audio-output-device"),
    audioOutputList: document.querySelector("#audio-output-list"),
    audioOutputMenu: document.querySelector("#audio-output-menu"),
    audioOutputTrigger: document.querySelector("#audio-output-trigger"),
    audioWorkspace: document.querySelector("#audio-workspace"),
    authenticationForm: document.querySelector("#authentication-form"),
    authenticationMessage: document.querySelector("#authentication-message"),
    authenticationPanel: document.querySelector("#authentication-panel"),
    authenticationToken: document.querySelector("#authentication-token"),
    compactNowPlaying: document.querySelector("#compact-now-playing"),
    connectionIndicator: document.querySelector("#connection-indicator"),
    connectionState: document.querySelector("#connection-state"),
    contextActionArea: document.querySelector("#context-action-area"),
    contextActionGrid: document.querySelector("#context-action-grid"),
    contextAudioCapabilities: document.querySelector("#context-audio-capabilities"),
    contextEntryButton: document.querySelector("#context-entry-button"),
    contextAge: document.querySelector("#context-age"),
    contextSelection: document.querySelector("#context-selection"),
    contextWorkspace: document.querySelector("#context-workspace"),
    contextVolumeTemplate: document.querySelector("#context-volume-template"),
    currentTime: document.querySelector("#current-time"),
    diagnosticAuthentication: document.querySelector("#diagnostic-authentication"),
    diagnosticProtocol: document.querySelector("#diagnostic-protocol"),
    diagnosticsBackdrop: document.querySelector("#diagnostics-backdrop"),
    diagnosticsClose: document.querySelector("#diagnostics-close"),
    diagnosticsPanel: document.querySelector("#diagnostics-panel"),
    expandedNowPlaying: document.querySelector("#expanded-now-playing"),
    feedbackIcon: document.querySelector("#feedback-icon"),
    feedbackMessage: document.querySelector("#feedback-message"),
    feedbackToast: document.querySelector("#feedback-toast"),
    headerContext: document.querySelector("#header-context"),
    headerContextMode: document.querySelector("#header-context-mode"),
    headerProcess: document.querySelector("#header-process"),
    latencyValue: document.querySelector("#latency-value"),
    layoutDebug: document.querySelector("#layout-debug"),
    navHome: document.querySelector("#nav-home"),
    navAudio: document.querySelector("#nav-audio"),
    navMasterVolume: document.querySelector("#nav-master-volume"),
    navMedia: document.querySelector("#nav-media"),
    navMore: document.querySelector("#nav-more"),
    nowPlayingTemplate: document.querySelector("#now-playing-template"),
    offlineMessage: document.querySelector("#offline-message"),
    offlineState: document.querySelector("#offline-state"),
    pingButton: document.querySelector("#ping-button"),
    processId: document.querySelector("#process-id"),
    researchAudioCapabilities: document.querySelector("#research-audio-capabilities"),
    researchDomain: document.querySelector("#research-domain"),
    researchIntegrationStatus: document.querySelector("#research-integration-status"),
    researchSave: document.querySelector("#research-save"),
    researchPageActionGrid: document.querySelector("#research-page-action-grid"),
    researchSearchActionGrid: document.querySelector("#research-search-action-grid"),
    researchSelectionPanel: document.querySelector("#research-selection-panel"),
    researchSelectionPreview: document.querySelector("#research-selection-preview"),
    researchTitle: document.querySelector("#research-title"),
    researchWorkspace: document.querySelector("#research-workspace"),
    researchInboxWorkspace: document.querySelector("#research-inbox-workspace"),
    researchInboxList: document.querySelector("#research-inbox-list"),
    researchInboxEmpty: document.querySelector("#research-inbox-empty"),
    researchInboxMore: document.querySelector("#research-inbox-more"),
    researchInboxDetail: document.querySelector("#research-inbox-detail"),
    researchInboxClose: document.querySelector("#research-inbox-close"),
    researchInboxOpen: document.querySelector("#research-inbox-open"),
    researchInboxCount: document.querySelector("#research-inbox-count"),
    viewportDimensions: document.querySelector("#viewport-dimensions"),
    workspace: document.querySelector("#workspace"),
    workspaceDescription: document.querySelector("#workspace-description"),
    workspaceEyebrow: document.querySelector("#workspace-eyebrow"),
    workspaceProcess: document.querySelector("#workspace-process"),
    workspaceSurface: document.querySelector("#workspace-surface"),
    workspaceTitle: document.querySelector("#workspace-title")
};

const nowPlaying = new NowPlayingController(
    elements.compactNowPlaying,
    elements.expandedNowPlaying,
    elements.nowPlayingTemplate);
const spotifyControls = new SpotifyControlsController(
    nowPlaying.compactSpotifyAccessoryRoot,
    nowPlaying.expandedSpotifyAccessoryRoot);
const audioMixer = new AudioMixerController({
    applicationList: elements.audioApplicationList,
    applicationSummary: elements.audioApplicationSummary,
    emptyState: elements.audioEmptyState,
    masterMute: elements.audioMasterMute,
    masterPercentage: elements.audioMasterPercentage,
    masterSlider: elements.audioMasterVolume,
    outputDevice: elements.audioOutputDevice,
    outputDeviceList: elements.audioOutputList,
    outputDeviceMenu: elements.audioOutputMenu,
    outputDeviceTrigger: elements.audioOutputTrigger,
    template: elements.audioApplicationTemplate
});
const contextComposition = new ContextCompositionController({
    contextRoot: elements.contextAudioCapabilities,
    expandedRoot: nowPlaying.expandedAudioAccessoryRoot,
    researchRoot: elements.researchAudioCapabilities,
    template: elements.contextVolumeTemplate,
    requestCommand: request => audioMixer.requestCommand(request),
    onNavigateAudio: () => elements.navAudio.click()
});
const researchWorkspace = new ResearchWorkspaceController({
    root: elements.researchWorkspace,
    title: elements.researchTitle,
    domain: elements.researchDomain,
    status: elements.researchIntegrationStatus,
    pageActionGrid: elements.researchPageActionGrid,
    selectionPanel: elements.researchSelectionPanel,
    selectionPreview: elements.researchSelectionPreview,
    searchActionGrid: elements.researchSearchActionGrid,
    saveButton: elements.researchSave
});
const researchInbox = new ResearchInboxController({
    root: elements.researchInboxWorkspace,
    list: elements.researchInboxList,
    empty: elements.researchInboxEmpty,
    more: elements.researchInboxMore,
    detail: elements.researchInboxDetail,
    close: elements.researchInboxClose,
    openButton: elements.researchInboxOpen,
    count: elements.researchInboxCount
});

let connected = false;
let contextChangedAt;
let contextSelectionPending = false;
let diagnosticsOpen = false;
let feedbackTimer;
let lastContextSelection = "automatic";
let lastRenderedActionKey;
let lastRenderedContextKey;
let latestContextState = {
    contextId: "default",
    displayName: "Default",
    selectionMode: "automatic",
    source: "fallback"
};
let latestMediaState;
let latestAudioState;
let latestPcState;
let latestSpotifyState;
let latestBrowserState;
let layoutDebugEnabled = false;
let manualPingPending = false;
let mediaCommandPending = false;
let researchInboxOpen = false;

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

function updateControlStates() {
    elements.contextSelection.disabled = !connected || contextSelectionPending;
    elements.navHome.disabled = !connected || contextSelectionPending;
    elements.navMedia.disabled = !connected || contextSelectionPending;
    elements.navAudio.disabled = !connected || contextSelectionPending;
    elements.pingButton.disabled = !connected || manualPingPending;
    elements.researchInboxOpen.disabled = !connected;
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
    return totalMinutes < 60
        ? `${totalMinutes}m ago`
        : `${Math.floor(totalMinutes / 60)}h ago`;
}

function showFeedback(message, tone = "neutral", durationMilliseconds = 3500) {
    clearTimeout(feedbackTimer);
    elements.feedbackToast.hidden = false;
    elements.feedbackToast.dataset.tone = tone;
    elements.feedbackMessage.textContent = message;
    elements.feedbackIcon.textContent = tone === "failure"
        ? "!"
        : tone === "warning"
            ? "△"
            : tone === "success"
                ? "✓"
                : "·";

    if (durationMilliseconds > 0) {
        feedbackTimer = setTimeout(() => {
            elements.feedbackToast.hidden = true;
        }, durationMilliseconds);
    }
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

function createContextAction(action) {
    const button = document.createElement("button");
    button.className = "touch-action";
    button.type = "button";
    button.dataset.contextAction = action.id;

    const icon = document.createElement("span");
    icon.className = "touch-action-icon";
    icon.setAttribute("aria-hidden", "true");
    icon.textContent = action.icon;

    const copy = document.createElement("span");
    const title = document.createElement("span");
    title.className = "touch-action-title";
    title.textContent = action.title;
    const detail = document.createElement("span");
    detail.className = "touch-action-detail";
    detail.textContent = action.detail;
    copy.append(title, detail);
    button.append(icon, copy);
    return button;
}

function renderContextActions(actions) {
    const actionKey = actions.map(action => action.id).join(":");
    if (actionKey !== lastRenderedActionKey) {
        elements.contextActionGrid.replaceChildren(...actions.map(createContextAction));
        lastRenderedActionKey = actionKey;
    }

    elements.contextActionArea.hidden = actions.length === 0;
}

function workspaceContent(composition) {
    const process = latestPcState?.isAvailable
        ? latestPcState.processName || "Unknown application"
        : "No foreground application";

    const definitions = {
        audio: {
            eyebrow: "Audio context",
            title: "System audio",
            description: "Master output, device selection, and current application sessions.",
            actions: []
        },
        browser: browserWorkspaceDefinition(),
        gaming: {
            eyebrow: "Gaming context",
            title: "Priority game mix",
            description: composition.contextAudio?.entries.length > 0
                ? "Foreground game, Discord, media, then other active sessions—without leaving the game."
                : "No matching game or active audio sessions are currently exposed by Windows.",
            actions: []
        },
        media: {
            eyebrow: "Media context",
            title: "No active media",
            description: "Start playback in an application that publishes Windows system media controls.",
            actions: []
        },
        default: {
            eyebrow: "Automatic workspace",
            title: "Command strip ready",
            description: "No higher-value contextual controls are available right now.",
            actions: [{
                id: "system-details",
                icon: "⚙",
                title: "System details",
                detail: "Context and diagnostics"
            }]
        }
    };

    const definition = definitions[latestContextState.contextId] || definitions.default;
    elements.workspaceEyebrow.textContent = definition.eyebrow;
    elements.workspaceTitle.textContent = definition.title;
    elements.workspaceDescription.textContent = definition.description;
    elements.workspaceProcess.textContent = process;
    renderContextActions(definition.actions);
}

function browserWorkspaceDefinition() {
    const browserConnected = latestBrowserState?.connectionState === "connected";
    if (!browserConnected) {
        return {
            eyebrow: "Browser / Research · Bridge offline",
            title: "Firefox context",
            description: "The local Firefox bridge is not connected. Foreground context continues to work.",
            actions: []
        };
    }

    const title = latestBrowserState.pageTitle ||
        latestBrowserState.hostName ||
        "Firefox connected";
    const host = latestBrowserState.hostName || "Internal or restricted Firefox page";
    const selection = latestBrowserState.hasSelectedText
        ? "Text selected"
        : "No text selected";
    return {
        eyebrow: "Browser / Research · Firefox connected",
        title,
        description: `${host} · ${selection}`,
        actions: []
    };
}

function renderWorkspace() {
    const composition = buildContextComposition({
        contextState: latestContextState,
        pcState: latestPcState,
        mediaState: latestMediaState,
        audioState: latestAudioState
    });
    const presentation = connected && !researchInboxOpen ? composition.mediaPresentation : "hidden";
    const audioActive = latestContextState.contextId === "audio";
    const researchActive = latestContextState.contextId === "browser";
    elements.appShell.dataset.context = latestContextState.contextId || "default";
    elements.appShell.dataset.mediaPresentation = presentation;
    elements.appShell.dataset.mediaEmphasis = composition.mediaEmphasis || "normal";
    elements.contextWorkspace.dataset.mode = composition.workspaceMode;
    nowPlaying.setPresentation(presentation);
    contextComposition.render(composition);
    elements.audioWorkspace.hidden = researchInboxOpen || !audioActive;
    elements.researchWorkspace.hidden = researchInboxOpen || !researchActive;
    elements.researchInboxWorkspace.hidden = !researchInboxOpen;
    elements.contextWorkspace.hidden = researchInboxOpen || presentation === "expanded" || audioActive || researchActive;
    workspaceContent(composition);
}

function updateNavigationState() {
    const automatic = lastContextSelection === "automatic";
    const mediaPinned = lastContextSelection === "media";
    const audioPinned = lastContextSelection === "audio";
    elements.navHome.dataset.active = automatic ? "true" : "false";
    elements.navMedia.dataset.active = mediaPinned ? "true" : "false";
    elements.navAudio.dataset.active = audioPinned ? "true" : "false";
    elements.navMore.dataset.active = diagnosticsOpen || researchInboxOpen ? "true" : "false";
    elements.navHome.setAttribute("aria-pressed", automatic ? "true" : "false");
    elements.navMedia.setAttribute("aria-pressed", mediaPinned ? "true" : "false");
    elements.navAudio.setAttribute("aria-pressed", audioPinned ? "true" : "false");
    elements.navMore.setAttribute("aria-pressed", diagnosticsOpen || researchInboxOpen ? "true" : "false");
}

function setDiagnosticsOpen(open, focusContextSelection = false) {
    diagnosticsOpen = open;
    elements.diagnosticsPanel.hidden = !open;
    elements.diagnosticsBackdrop.hidden = !open;
    elements.contextEntryButton.setAttribute("aria-expanded", open ? "true" : "false");
    elements.navMore.setAttribute("aria-expanded", open ? "true" : "false");
    updateNavigationState();

    if (open) {
        window.requestAnimationFrame(() => {
            (focusContextSelection ? elements.contextSelection : elements.diagnosticsClose).focus();
        });
    }
}

export const dashboardUi = {
    addEvent(message, tone = "neutral") {
        elements.activityAnnouncer.textContent = message;
        if (tone !== "neutral") {
            showFeedback(message, tone, tone === "failure" ? 7000 : 3200);
        }
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
        elements.contextSelection.addEventListener("change", () =>
            callback(elements.contextSelection.value));
        elements.navHome.addEventListener("click", () => callback("automatic"));
        elements.navMedia.addEventListener("click", () => callback("media"));
        elements.navAudio.addEventListener("click", () => callback("audio"));
    },

    bindNavigation() {
        elements.contextEntryButton.addEventListener("click", () =>
            setDiagnosticsOpen(!diagnosticsOpen, true));
        elements.navMore.addEventListener("click", () => setDiagnosticsOpen(!diagnosticsOpen));
        elements.contextActionGrid.addEventListener("click", event => {
            const action = event.target.closest("[data-context-action]")?.dataset.contextAction;
            if (action === "system-details") {
                setDiagnosticsOpen(true);
            }
        });
        elements.diagnosticsBackdrop.addEventListener("click", () => setDiagnosticsOpen(false));
        elements.diagnosticsClose.addEventListener("click", () => setDiagnosticsOpen(false));
        window.addEventListener("keydown", event => {
            if (event.key === "Escape" && diagnosticsOpen) {
                setDiagnosticsOpen(false);
            }
        });
    },

    bindMediaControls(callback) {
        nowPlaying.bindCommands(callback);
    },

    bindAudioControls(callback) {
        audioMixer.bindCommands(callback);
    },

    bindSpotifyControls(callback) {
        spotifyControls.bindCommands(callback);
    },

    bindBrowserControls(callback) {
        researchWorkspace.bindCommands(callback);
    },

    bindResearchInbox(actions) {
        researchInbox.bindActions({
            ...actions,
            onOpenChanged(open) {
                researchInboxOpen = open;
                if (open) setDiagnosticsOpen(false);
                renderWorkspace();
                updateNavigationState();
            },
            showFeedback(message, tone) {
                showFeedback(message, tone, tone === "failure" ? 7000 : 2400);
            }
        });
    },

    bindPing(callback) {
        elements.pingButton.addEventListener("click", callback);
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

    renderPcState(state) {
        latestPcState = state;
        const processName = state.isAvailable
            ? state.processName || "Unknown application"
            : "Foreground unavailable";
        elements.headerProcess.textContent = processName;
        elements.headerProcess.title = state.windowTitle || processName;
        elements.processId.textContent = state.isAvailable ? state.processId : "—";
        renderWorkspace();
    },

    renderContextState(state) {
        latestContextState = state;
        contextChangedAt = new Date(state.activeSinceUtc);
        lastContextSelection = state.selectionMode === "manual" ? state.contextId : "automatic";
        elements.contextSelection.value = lastContextSelection;
        elements.headerContext.textContent = state.displayName;
        elements.headerContextMode.textContent = state.selectionMode === "manual" ? "Pinned" : "Auto";
        elements.contextAge.textContent = formatElapsed(contextChangedAt);
        updateNavigationState();
        renderWorkspace();

        const contextKey = `${state.contextId}:${state.selectionMode}:${state.activeSinceUtc}`;
        if (contextKey !== lastRenderedContextKey) {
            elements.activityAnnouncer.textContent = `${state.displayName} context active.`;
            lastRenderedContextKey = contextKey;
        }
    },

    renderMediaState(state) {
        latestMediaState = state;
        nowPlaying.setMediaState(state);
        renderWorkspace();
    },

    renderAudioState(state) {
        latestAudioState = state;
        audioMixer.setAudioState(state);
        const output = state?.isAvailable === true ? state.outputDevice : null;
        elements.navMasterVolume.textContent = output
            ? `${Math.round(Math.min(1, Math.max(0, output.volume)) * 100)}%`
            : "--%";
        elements.navAudio.title = output
            ? `${output.friendlyName}: ${elements.navMasterVolume.textContent}`
            : "Audio mixer unavailable";
        renderWorkspace();
    },

    renderSpotifyState(state) {
        latestSpotifyState = state;
        spotifyControls.setState(state);
    },

    renderBrowserState(state) {
        latestBrowserState = state;
        researchWorkspace.setBrowserState(state);
        renderWorkspace();
    },

    renderResearchInboxState(state) {
        researchInbox.setState(state);
    },

    setProtocolVersion(version) {
        elements.diagnosticProtocol.textContent = `WebSocket v${version}`;
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

    setAvailableBrowserSearchActions(actions = []) {
        researchWorkspace.setSearchActions(actions);
    },

    setContextSelectionPending() {
        contextSelectionPending = true;
        showFeedback("Updating context…", "neutral", 0);
        updateControlStates();
    },

    showContextSelectionResult(result) {
        contextSelectionPending = false;
        if (!result.succeeded) {
            elements.contextSelection.value = lastContextSelection;
        }
        showFeedback(result.message, result.succeeded ? "success" : "failure", result.succeeded ? 2600 : 7000);
        elements.activityAnnouncer.textContent = result.message;
        updateControlStates();
    },

    setMediaCommandPending(commandId) {
        mediaCommandPending = true;
        nowPlaying.setCommandPending(true);
        showFeedback(commandId === "media.seek" ? "Seeking…" : "Sending media control…", "neutral", 0);
    },

    showMediaCommandResult(result) {
        mediaCommandPending = false;
        nowPlaying.setCommandPending(false);
        showFeedback(result.message, result.succeeded ? "success" : "failure", result.succeeded ? 2200 : 7000);
        elements.activityAnnouncer.textContent = `${result.commandId}: ${result.message}`;
    },

    showAudioCommandResult(result) {
        const pending = audioMixer.handleCommandResult(result);
        const isVolume = result.commandId === "audio.setMasterVolume" ||
            result.commandId === "audio.setApplicationVolume";

        if (!result.succeeded) {
            showFeedback(result.message, "failure", 7000);
        } else if (!isVolume) {
            showFeedback(result.message, "success", 1800);
        }

        if (!isVolume || !result.succeeded || pending?.commandId !== result.commandId) {
            elements.activityAnnouncer.textContent = `${result.commandId}: ${result.message}`;
        }
    },

    showSpotifyCommandResult(result) {
        spotifyControls.handleCommandResult(result);
        showFeedback(
            result.message,
            result.succeeded ? "success" : "failure",
            result.succeeded ? 2200 : 7000);
        elements.activityAnnouncer.textContent = `${result.commandId}: ${result.message}`;
    },

    setBrowserCommandPending(commandId) {
        researchWorkspace.setPending(commandId);
        showFeedback("Sending browser action…", "neutral", 0);
    },

    showBrowserCommandResult(result) {
        researchWorkspace.clearPending();
        showFeedback(
            result.message,
            result.succeeded ? "success" : "failure",
            result.succeeded ? 2200 : 7000);
        elements.activityAnnouncer.textContent = `${result.commandId}: ${result.message}`;
    },

    setResearchCommandPending(commandId) {
        if (commandId === "research.saveCurrent") {
            researchWorkspace.setPending(commandId);
            showFeedback("Saving to Research Inbox…", "neutral", 0);
        }
    },

    showResearchCommandResult(result) {
        researchWorkspace.clearPending();
        showFeedback(
            result.message,
            result.succeeded ? "success" : "failure",
            result.succeeded ? 2600 : 7000);
        elements.activityAnnouncer.textContent = `${result.commandId}: ${result.message}`;
    },

    showCommandResult(result) {
        showFeedback(result.message, result.succeeded ? "success" : "failure", result.succeeded ? 2200 : 7000);
        elements.activityAnnouncer.textContent = result.message;
    },

    showDisconnectedCommand() {
        if (!mediaCommandPending) {
            return;
        }

        mediaCommandPending = false;
        nowPlaying.setCommandPending(false);
        showFeedback("Disconnected before command completion.", "failure", 7000);
    },

    setConnection(state, text) {
        connected = state === "connected";
        nowPlaying.setConnected(connected);
        audioMixer.setConnected(connected);
        contextComposition.setConnected(connected);
        researchWorkspace.setConnected(connected);
        spotifyControls.setConnected(connected);
        elements.appShell.dataset.connection = state;
        elements.connectionIndicator.dataset.state = state;
        elements.connectionState.textContent = text.toLowerCase().includes("retrying")
            ? "Retrying"
            : text;
        elements.diagnosticAuthentication.textContent = connected ? "Authenticated" : "Unavailable";
        elements.offlineState.hidden = connected;
        elements.workspaceSurface.inert = !connected;
        elements.compactNowPlaying.inert = !connected;
        elements.audioWorkspace.inert = !connected;

        if (connected) {
            elements.offlineMessage.textContent = "Trying to reconnect. Controls will return automatically.";
        } else {
            manualPingPending = false;
            contextSelectionPending = false;
            elements.latencyValue.textContent = "—";
            elements.headerContext.textContent = state === "connecting" ? "Waiting" : "Unavailable";
            elements.headerContextMode.textContent = "Offline";
            elements.headerProcess.textContent = "Waiting for PC";
            elements.navMasterVolume.textContent = "--%";
            elements.navAudio.title = "Audio mixer unavailable while disconnected";
            elements.offlineMessage.textContent = state === "connecting"
                ? "Connecting to the Windows host."
                : "Trying to reconnect. Controls will return automatically.";
            setActionState(elements.pingButton, null);
        }

        renderWorkspace();
        updateControlStates();
    },

    setManualPingPending() {
        manualPingPending = true;
        setActionState(elements.pingButton, "processing");
        showFeedback("Measuring round trip…", "neutral", 0);
        updateControlStates();
    },

    showPong(roundTripMilliseconds, source) {
        const roundedLatency = roundTripMilliseconds.toFixed(1);
        elements.latencyValue.textContent = `${roundedLatency} ms`;

        if (source === "manual") {
            manualPingPending = false;
            setActionState(elements.pingButton, "success");
            showFeedback(`Round trip: ${roundedLatency} ms.`, "success", 2600);
            updateControlStates();
        }
    },

    showPingTimeout(source) {
        elements.latencyValue.textContent = "Timed out";

        if (source === "manual") {
            manualPingPending = false;
            setActionState(elements.pingButton, "failure");
            showFeedback("Diagnostic ping timed out.", "failure", 7000);
            updateControlStates();
        }
    },

    tickClock() {
        const now = new Date();
        elements.currentTime.dateTime = now.toISOString();
        elements.currentTime.textContent = now.toLocaleTimeString([], {
            hour: "2-digit",
            minute: "2-digit"
        });
        elements.contextAge.textContent = formatElapsed(contextChangedAt);
    }
};
