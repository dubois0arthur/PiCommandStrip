const mediaCommandIds = Object.freeze({
    next: "media.next",
    playPause: "media.playPause",
    previous: "media.previous",
    seek: "media.seek"
});

function formatMediaTime(milliseconds) {
    if (!Number.isFinite(milliseconds) || milliseconds < 0) {
        return "--:--";
    }

    const totalSeconds = Math.floor(milliseconds / 1000);
    const seconds = totalSeconds % 60;
    const totalMinutes = Math.floor(totalSeconds / 60);
    const minutes = totalMinutes % 60;
    const hours = Math.floor(totalMinutes / 60);

    if (hours > 0) {
        return `${hours}:${minutes.toString().padStart(2, "0")}:${seconds.toString().padStart(2, "0")}`;
    }

    return `${minutes}:${seconds.toString().padStart(2, "0")}`;
}

class NowPlayingComponent {
    #artwork;
    #artworkContainer;
    #artworkFallback;
    #card;
    #currentTime;
    #failedArtworkUrl;
    #isInteracting = false;
    #mediaState;
    #nextButton;
    #onCommand;
    #playPauseButton;
    #playPauseIcon;
    #previousButton;
    #progress;
    #root;
    #totalTime;
    #variant;

    constructor(root, variant, template, onCommand) {
        this.#root = root;
        this.#onCommand = onCommand;
        this.#variant = variant;

        const content = template.content.cloneNode(true);
        const card = content.querySelector(".now-playing-card");
        card.classList.add(`now-playing-${variant}`);
        this.#card = card;
        root.replaceChildren(content);

        this.#artworkContainer = root.querySelector('[data-media-role="artwork-container"]');
        this.#artwork = root.querySelector('[data-media-role="artwork"]');
        this.#artworkFallback = root.querySelector('[data-media-role="artwork-fallback"]');
        this.source = root.querySelector('[data-media-role="source"]');
        this.title = root.querySelector('[data-media-role="title"]');
        this.byline = root.querySelector('[data-media-role="byline"]');
        this.#currentTime = root.querySelector('[data-media-role="current-time"]');
        this.#totalTime = root.querySelector('[data-media-role="total-time"]');
        this.#progress = root.querySelector('[data-media-role="progress"]');
        this.#previousButton = root.querySelector('[data-media-role="previous"]');
        this.#playPauseButton = root.querySelector('[data-media-role="play-pause"]');
        this.#playPauseIcon = root.querySelector('[data-media-role="play-pause-icon"]');
        this.#nextButton = root.querySelector('[data-media-role="next"]');

        this.#artwork.addEventListener("error", () => {
            if (this.#artwork.dataset.requestedArtwork === this.#artwork.getAttribute("src")) {
                this.#failedArtworkUrl = this.#artwork.getAttribute("src");
                this.#showArtworkFallback();
            }
        });
        this.#artwork.addEventListener("load", () => {
            this.#failedArtworkUrl = undefined;
        });

        this.#previousButton.addEventListener("click", () =>
            this.#onCommand(mediaCommandIds.previous));
        this.#playPauseButton.addEventListener("click", () =>
            this.#onCommand(mediaCommandIds.playPause));
        this.#nextButton.addEventListener("click", () =>
            this.#onCommand(mediaCommandIds.next));

        this.#progress.addEventListener("pointerdown", () => {
            this.#isInteracting = true;
        });
        this.#progress.addEventListener("input", () => {
            this.#isInteracting = true;
            this.updateProgress(Number(this.#progress.value), true);
        });
        this.#progress.addEventListener("change", () => {
            const position = Math.round(Number(this.#progress.value));
            this.#isInteracting = false;
            this.#onCommand(mediaCommandIds.seek, position);
        });
        this.#progress.addEventListener("blur", () => {
            this.#isInteracting = false;
        });
        this.#progress.addEventListener("pointercancel", () => {
            this.#isInteracting = false;
        });
    }

    render(state, connected, commandPending) {
        this.#mediaState = state;
        const active = state?.hasActiveSession === true;
        const source = state?.sourceName || state?.sessionSourceIdentifier || "Windows media";
        const title = state?.title || (active ? "Untitled media" : "No active media");
        const byline = state?.artist || (active ? source : "Start playback in Spotify or a browser.");
        const isPlaying = state?.playbackState === "playing";
        const supportsPlayPause = state?.supportsPlayPause === true ||
            (isPlaying ? state?.supportsPause === true : state?.supportsPlay === true);
        const artworkUrl = active &&
            typeof state?.artworkUrl === "string" &&
            /^\/media\/artwork\/[a-f0-9]{64}$/.test(state.artworkUrl)
            ? state.artworkUrl
            : null;

        this.#renderArtwork(artworkUrl);
        this.source.textContent = source;
        this.title.textContent = title;
        this.title.title = title;
        this.byline.textContent = byline;
        this.byline.title = byline;
        this.#playPauseIcon.textContent = isPlaying ? "\u275A\u275A" : "\u25B6";
        this.#playPauseButton.setAttribute("aria-label", isPlaying ? "Pause" : "Play");

        const controlsEnabled = connected && active && !commandPending;
        this.#previousButton.disabled = !controlsEnabled || state?.supportsPrevious !== true;
        this.#playPauseButton.disabled = !controlsEnabled || !supportsPlayPause;
        this.#nextButton.disabled = !controlsEnabled || state?.supportsNext !== true;
        this.#progress.disabled = !controlsEnabled ||
            state?.supportsSeeking !== true ||
            !Number.isFinite(state?.totalDurationMilliseconds) ||
            state.totalDurationMilliseconds <= 0;

        this.#root.dataset.playbackState = state?.playbackState || "none";
        this.#root.dataset.commandPending = commandPending ? "true" : "false";
        this.updateProgress(state?.positionMilliseconds, false);
    }

    #renderArtwork(artworkUrl) {
        if (!artworkUrl) {
            this.#failedArtworkUrl = undefined;
            this.#showArtworkFallback();
            return;
        }

        if (this.#failedArtworkUrl === artworkUrl) {
            this.#showArtworkFallback();
            return;
        }

        this.#card.dataset.hasArtwork = "true";
        this.#artworkContainer.hidden = false;
        this.#artwork.hidden = false;
        this.#artworkFallback.hidden = true;

        if (this.#artwork.getAttribute("src") !== artworkUrl) {
            this.#artwork.dataset.requestedArtwork = artworkUrl;
            this.#artwork.src = artworkUrl;
        }
    }

    #showArtworkFallback() {
        this.#card.dataset.hasArtwork = "false";
        this.#artwork.hidden = true;
        this.#artwork.removeAttribute("src");
        delete this.#artwork.dataset.requestedArtwork;
        this.#artworkFallback.hidden = false;
        this.#artworkContainer.hidden = this.#variant === "compact";
    }

    updateProgress(positionMilliseconds, force = false) {
        if (this.#isInteracting && !force) {
            return;
        }

        const duration = this.#mediaState?.totalDurationMilliseconds;
        const hasDuration = Number.isFinite(duration) && duration > 0;
        const position = Number.isFinite(positionMilliseconds)
            ? Math.max(0, hasDuration ? Math.min(positionMilliseconds, duration) : positionMilliseconds)
            : 0;

        this.#progress.max = hasDuration ? Math.round(duration) : 1;
        this.#progress.value = Math.round(position);
        const percentage = hasDuration ? (position / duration) * 100 : 0;
        this.#progress.style.setProperty("--media-progress", `${percentage}%`);
        this.#currentTime.textContent = this.#mediaState?.hasActiveSession
            ? formatMediaTime(position)
            : "--:--";
        this.#totalTime.textContent = hasDuration ? formatMediaTime(duration) : "--:--";
    }
}

export class NowPlayingController {
    #commandCallback;
    #commandPending = false;
    #components;
    #connected = false;
    #mediaState;
    #presentation = "hidden";
    #positionBaseline = 0;
    #positionBaselineAt = performance.now();
    #progressFrame;

    constructor(compactRoot, expandedRoot, template) {
        const handleCommand = (commandId, positionMilliseconds) => {
            if (commandId === mediaCommandIds.seek &&
                Number.isFinite(positionMilliseconds) &&
                this.#mediaState) {
                this.#positionBaseline = positionMilliseconds;
                this.#positionBaselineAt = performance.now();
                this.#mediaState = {
                    ...this.#mediaState,
                    positionMilliseconds
                };
            }

            this.#commandCallback?.(commandId, positionMilliseconds);
        };

        this.compactRoot = compactRoot;
        this.expandedRoot = expandedRoot;
        this.#components = [
            new NowPlayingComponent(compactRoot, "compact", template, handleCommand),
            new NowPlayingComponent(expandedRoot, "expanded", template, handleCommand)
        ];
    }

    bindCommands(callback) {
        this.#commandCallback = callback;
    }

    setConnected(connected) {
        this.#connected = connected;
        if (!connected) {
            this.#commandPending = false;
        }
        this.#render();
    }

    setPresentation(presentation) {
        this.#presentation = ["compact", "expanded", "hidden"].includes(presentation)
            ? presentation
            : "hidden";
        this.#renderVisibility();
    }

    setCommandPending(pending) {
        this.#commandPending = pending;
        this.#render();
    }

    setMediaState(state) {
        this.#mediaState = state;
        this.#positionBaseline = Number.isFinite(state?.positionMilliseconds)
            ? state.positionMilliseconds
            : 0;
        this.#positionBaselineAt = performance.now();
        this.#render();
        this.#scheduleProgress();
    }

    #render() {
        this.#components.forEach(component =>
            component.render(this.#mediaState, this.#connected, this.#commandPending));
        this.#renderVisibility();
    }

    #renderVisibility() {
        const mediaActive = this.#mediaState?.hasActiveSession === true;
        const visiblePresentation = this.#connected && mediaActive
            ? this.#presentation
            : "hidden";
        this.expandedRoot.hidden = visiblePresentation !== "expanded";
        this.compactRoot.hidden = visiblePresentation !== "compact";
    }

    #scheduleProgress() {
        cancelAnimationFrame(this.#progressFrame);

        if (this.#mediaState?.playbackState !== "playing" ||
            !Number.isFinite(this.#mediaState?.totalDurationMilliseconds)) {
            return;
        }

        const tick = now => {
            const elapsed = now - this.#positionBaselineAt;
            const position = Math.min(
                this.#positionBaseline + elapsed,
                this.#mediaState.totalDurationMilliseconds);
            this.#components.forEach(component => component.updateProgress(position));

            if (position < this.#mediaState.totalDurationMilliseconds &&
                this.#mediaState.playbackState === "playing") {
                this.#progressFrame = requestAnimationFrame(tick);
            }
        };

        this.#progressFrame = requestAnimationFrame(tick);
    }
}
