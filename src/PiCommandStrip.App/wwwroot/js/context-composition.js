import { MuteButtonController, VolumeSliderController } from "./audio-mixer.js?v=15";
import {
    matchForegroundAudioApplication,
    matchMediaAudioApplication,
    mediaBelongsToForeground,
    normalizeProcessName,
    selectGamingAudioApplications
} from "./capability-matching.js?v=15";

function applicationEntry(application, role) {
    return {
        key: `application:${application.applicationId}`,
        type: "application",
        role,
        application
    };
}

function masterEntry(audioState) {
    return audioState?.outputDevice
        ? {
            key: "master",
            type: "master",
            role: "Global output",
            output: audioState.outputDevice
        }
        : null;
}

function compactMediaPresentation(hasMedia) {
    return hasMedia ? "compact" : "hidden";
}

export function buildContextComposition({ contextState, pcState, mediaState, audioState }) {
    const contextId = contextState?.contextId || "default";
    const hasMedia = mediaState?.hasActiveSession === true;
    const foregroundProcess = contextState?.foregroundProcess || pcState?.processName;
    const foregroundWindowTitle = contextState?.foregroundWindowTitle || pcState?.windowTitle;
    const mediaApplication = matchMediaAudioApplication(
        audioState,
        mediaState,
        foregroundProcess,
        foregroundWindowTitle);
    const mediaOwnsForeground = mediaBelongsToForeground(
        mediaState,
        foregroundProcess,
        foregroundWindowTitle);
    const expandedApplication = mediaApplication
        ? [applicationEntry(mediaApplication, "Media application")]
        : [];
    const master = masterEntry(audioState);

    if (contextId === "audio") {
        return {
            workspaceMode: "audio",
            mediaPresentation: compactMediaPresentation(hasMedia),
            expandedAudio: null,
            contextAudio: null,
            mediaApplication,
            mediaOwnsForeground
        };
    }

    if (contextId === "media") {
        return {
            workspaceMode: hasMedia ? "media" : "media-empty",
            mediaPresentation: hasMedia ? "expanded" : "hidden",
            expandedAudio: hasMedia
                ? {
                    eyebrow: "Playback audio",
                    title: mediaApplication
                        ? `${mediaApplication.displayName} volume`
                        : "Audio controls",
                    entries: expandedApplication,
                    showAudioLink: true
                }
                : null,
            contextAudio: !hasMedia
                ? {
                    eyebrow: "Global audio",
                    title: "Master output",
                    entries: master ? [master] : [],
                    showAudioLink: true
                }
                : null,
            mediaApplication,
            mediaOwnsForeground
        };
    }

    if (contextId === "browser") {
        const promoteMedia = hasMedia && mediaOwnsForeground;
        return {
            workspaceMode: promoteMedia ? "browser-media" : "browser",
            mediaPresentation: promoteMedia
                ? "expanded"
                : compactMediaPresentation(hasMedia),
            expandedAudio: promoteMedia
                ? {
                    eyebrow: "Browser audio",
                    title: mediaApplication
                        ? `${mediaApplication.displayName} volume`
                        : "Full mixer available",
                    entries: expandedApplication,
                    showAudioLink: true
                }
                : null,
            contextAudio: null,
            mediaApplication,
            mediaOwnsForeground
        };
    }

    if (contextId === "gaming") {
        const gamingApplications = selectGamingAudioApplications(
            audioState,
            mediaState,
            foregroundProcess,
            foregroundWindowTitle);
        const foregroundApplication = matchForegroundAudioApplication(
            audioState,
            foregroundProcess);
        return {
            workspaceMode: "gaming",
            mediaPresentation: compactMediaPresentation(hasMedia),
            expandedAudio: null,
            contextAudio: {
                eyebrow: "Priority mix",
                title: gamingApplications.length > 0
                    ? "Game session audio"
                    : "No matching audio sessions",
                entries: gamingApplications.map(application =>
                    applicationEntry(
                        application,
                        application.applicationId === foregroundApplication?.applicationId
                            ? "Foreground game"
                            : normalizeProcessName(application.processName) === "discord"
                                ? "Voice chat"
                                : application.applicationId === mediaApplication?.applicationId
                                    ? "Active media"
                                    : "Other active audio")),
                showAudioLink: true
            },
            mediaApplication,
            mediaOwnsForeground
        };
    }

    return {
        workspaceMode: hasMedia ? "default-media" : "default",
        mediaPresentation: hasMedia ? "expanded" : "hidden",
        expandedAudio: hasMedia
            ? {
                eyebrow: "Playback audio",
                title: mediaApplication
                    ? `${mediaApplication.displayName} volume`
                    : "Full mixer available",
                entries: expandedApplication,
                showAudioLink: true
            }
            : null,
        contextAudio: !hasMedia
            ? {
                eyebrow: "Global audio",
                title: "Master output",
                entries: master ? [master] : [],
                showAudioLink: true
            }
            : null,
        mediaApplication,
        mediaOwnsForeground
    };
}

class ContextVolumeRowController {
    #applicationId;
    #available = false;
    #connected = false;
    #element;
    #mute;
    #name;
    #role;
    #slider;
    #sliderLabel;
    #type;

    constructor(template, entry, requestCommand) {
        this.#element = template.content.firstElementChild.cloneNode(true);
        this.#name = this.#element.querySelector('[data-context-audio-role="name"]');
        this.#role = this.#element.querySelector('[data-context-audio-role="role"]');
        this.#sliderLabel = this.#element.querySelector('[data-context-audio-role="slider-label"]');
        const slider = this.#element.querySelector('[data-context-audio-role="volume"]');
        const percentage = this.#element.querySelector('[data-context-audio-role="percentage"]');
        const muteButton = this.#element.querySelector('[data-context-audio-role="mute"]');

        this.#slider = new VolumeSliderController(slider, percentage, (volume, controller) =>
            requestCommand(this.#type === "master"
                ? {
                    commandId: "audio.setMasterVolume",
                    volume,
                    controller,
                    value: volume
                }
                : {
                    commandId: "audio.setApplicationVolume",
                    applicationId: this.#applicationId,
                    volume,
                    controller,
                    value: volume
                }));
        this.#mute = new MuteButtonController(muteButton, (isMuted, controller) =>
            requestCommand(this.#type === "master"
                ? {
                    commandId: "audio.setMasterMute",
                    isMuted,
                    controller,
                    value: isMuted
                }
                : {
                    commandId: "audio.setApplicationMute",
                    applicationId: this.#applicationId,
                    isMuted,
                    controller,
                    value: isMuted
                }));
        this.update(entry);
    }

    get element() {
        return this.#element;
    }

    update(entry) {
        this.#type = entry.type;
        this.#applicationId = entry.application?.applicationId;
        const state = entry.type === "master" ? entry.output : entry.application;
        const name = entry.type === "master"
            ? entry.output?.friendlyName || "Master output"
            : entry.application?.displayName || entry.application?.processName || "Audio application";
        this.#available = Boolean(state);
        this.#element.dataset.state = entry.application?.state || "active";
        this.#name.textContent = name;
        this.#name.title = name;
        this.#role.textContent = entry.role;
        this.#sliderLabel.textContent = `${name} volume`;
        this.#slider.setAuthoritative(state?.volume ?? 0);
        this.#mute.setAuthoritative(state?.isMuted === true);
        this.setConnected(this.#connected);
    }

    setConnected(connected) {
        this.#connected = connected;
        const enabled = connected && this.#available;
        this.#slider.setEnabled(enabled);
        this.#mute.setEnabled(enabled);
    }

    dispose() {
        this.#slider.dispose();
        this.#mute.dispose();
        this.#element.remove();
    }
}

class ContextVolumeSurface {
    #audioLink;
    #connected = false;
    #eyebrow;
    #list;
    #onNavigateAudio;
    #requestCommand;
    #root;
    #rows = new Map();
    #template;
    #title;

    constructor(root, template, requestCommand, onNavigateAudio) {
        this.#root = root;
        this.#template = template;
        this.#requestCommand = requestCommand;
        this.#onNavigateAudio = onNavigateAudio;

        const header = document.createElement("header");
        header.className = "context-volume-heading";
        const copy = document.createElement("div");
        this.#eyebrow = document.createElement("p");
        this.#eyebrow.className = "eyebrow";
        this.#title = document.createElement("h3");
        copy.append(this.#eyebrow, this.#title);
        this.#audioLink = document.createElement("button");
        this.#audioLink.className = "context-audio-link";
        this.#audioLink.type = "button";
        this.#audioLink.innerHTML = '<span aria-hidden="true">🔊</span><span>Full mixer</span>';
        this.#audioLink.addEventListener("click", () => {
            if (this.#connected) {
                this.#onNavigateAudio();
            }
        });
        header.append(copy, this.#audioLink);
        this.#list = document.createElement("div");
        this.#list.className = "context-volume-list";
        root.replaceChildren(header, this.#list);
    }

    setConnected(connected) {
        this.#connected = connected;
        this.#audioLink.disabled = !connected;
        this.#rows.forEach(row => row.setConnected(connected));
    }

    render(config) {
        this.#root.hidden = !config;
        if (!config) {
            this.#clearRows();
            return;
        }

        this.#eyebrow.textContent = config.eyebrow;
        this.#title.textContent = config.title;
        this.#audioLink.hidden = config.showAudioLink !== true;
        const liveKeys = new Set(config.entries.map(entry => entry.key));
        this.#rows.forEach((row, key) => {
            if (!liveKeys.has(key)) {
                row.dispose();
                this.#rows.delete(key);
            }
        });

        config.entries.forEach(entry => {
            let row = this.#rows.get(entry.key);
            if (!row) {
                row = new ContextVolumeRowController(
                    this.#template,
                    entry,
                    this.#requestCommand);
                this.#rows.set(entry.key, row);
            }
            row.update(entry);
            row.setConnected(this.#connected);
            this.#list.append(row.element);
        });
        this.#list.hidden = config.entries.length === 0;
    }

    #clearRows() {
        this.#rows.forEach(row => row.dispose());
        this.#rows.clear();
    }
}

export class ContextCompositionController {
    #contextSurface;
    #expandedSurface;

    constructor({
        contextRoot,
        expandedRoot,
        template,
        requestCommand,
        onNavigateAudio
    }) {
        this.#expandedSurface = new ContextVolumeSurface(
            expandedRoot,
            template,
            requestCommand,
            onNavigateAudio);
        this.#contextSurface = new ContextVolumeSurface(
            contextRoot,
            template,
            requestCommand,
            onNavigateAudio);
    }

    setConnected(connected) {
        this.#expandedSurface.setConnected(connected);
        this.#contextSurface.setConnected(connected);
    }

    render(composition) {
        this.#expandedSurface.render(composition.expandedAudio);
        this.#contextSurface.render(composition.contextAudio);
    }
}
