const sliderThrottleMilliseconds = 160;
const finalSendGapMilliseconds = 60;
const optimisticReconciliationMilliseconds = 1800;

function clampVolume(value) {
    return Math.min(1, Math.max(0, Number.isFinite(value) ? value : 0));
}

function toPercentage(value) {
    return Math.round(clampVolume(value) * 100);
}

class VolumeSliderController {
    #authoritativeValue = 0;
    #disposed = false;
    #finalTimer;
    #input;
    #interacting = false;
    #lastSentAt = Number.NEGATIVE_INFINITY;
    #lastSentValue;
    #listeners = new AbortController();
    #onRequest;
    #output;
    #pendingTarget;
    #reconcileTimer;
    #throttleTimer;

    constructor(input, output, onRequest) {
        this.#input = input;
        this.#output = output;
        this.#onRequest = onRequest;

        input.addEventListener("pointerdown", () => {
            if (!input.disabled) {
                this.#interacting = true;
            }
        }, { signal: this.#listeners.signal });
        input.addEventListener("input", () => this.#handleInput(), {
            signal: this.#listeners.signal
        });
        input.addEventListener("pointerup", () => this.#commit(), {
            signal: this.#listeners.signal
        });
        input.addEventListener("pointercancel", () => this.#commit(), {
            signal: this.#listeners.signal
        });
        input.addEventListener("change", () => this.#commit(), {
            signal: this.#listeners.signal
        });
        input.addEventListener("blur", () => this.#commit(), {
            signal: this.#listeners.signal
        });
    }

    setEnabled(enabled) {
        this.#input.disabled = !enabled;
        if (!enabled) {
            this.#interacting = false;
            this.#clearSendTimers();
            this.#clearPending();
            this.#render(this.#authoritativeValue);
        }
    }

    setAuthoritative(value) {
        this.#authoritativeValue = clampVolume(value);

        if (this.#interacting) {
            return;
        }

        if (this.#pendingTarget !== undefined) {
            if (Math.abs(this.#authoritativeValue - this.#pendingTarget) <= 0.01) {
                this.#clearPending();
            } else {
                return;
            }
        }

        this.#render(this.#authoritativeValue);
    }

    rejectPending(value) {
        if (this.#pendingTarget === undefined ||
            Math.abs(this.#pendingTarget - clampVolume(value)) > 0.01) {
            return;
        }

        this.#clearPending();
        this.#render(this.#authoritativeValue);
    }

    dispose() {
        this.#disposed = true;
        this.#listeners.abort();
        this.#clearSendTimers();
        this.#clearPending();
    }

    #handleInput() {
        if (this.#input.disabled) {
            return;
        }

        this.#interacting = true;
        const value = this.#readValue();
        this.#render(value);

        const elapsed = performance.now() - this.#lastSentAt;
        if (elapsed >= sliderThrottleMilliseconds) {
            this.#send(value, false);
            return;
        }

        clearTimeout(this.#throttleTimer);
        this.#throttleTimer = setTimeout(() => {
            this.#throttleTimer = undefined;
            this.#send(this.#readValue(), false);
        }, sliderThrottleMilliseconds - elapsed);
    }

    #commit() {
        if (!this.#interacting || this.#input.disabled || this.#disposed) {
            return;
        }

        this.#interacting = false;
        clearTimeout(this.#throttleTimer);
        this.#throttleTimer = undefined;
        const value = this.#readValue();
        this.#pendingTarget = value;
        clearTimeout(this.#reconcileTimer);
        this.#reconcileTimer = setTimeout(() => {
            this.#clearPending();
            this.#render(this.#authoritativeValue);
        }, optimisticReconciliationMilliseconds);

        const remainingGap = Math.max(
            0,
            finalSendGapMilliseconds - (performance.now() - this.#lastSentAt));
        clearTimeout(this.#finalTimer);
        this.#finalTimer = setTimeout(() => {
            this.#finalTimer = undefined;
            this.#send(value, true);
        }, remainingGap);
    }

    #send(value, isFinal) {
        if (this.#disposed || this.#input.disabled) {
            return;
        }

        const normalized = clampVolume(value);
        if (!isFinal && this.#lastSentValue === normalized) {
            return;
        }

        this.#lastSentAt = performance.now();
        this.#lastSentValue = normalized;
        this.#onRequest(normalized, this);
    }

    #readValue() {
        return clampVolume(Number(this.#input.value) / 100);
    }

    #render(value) {
        const percentage = toPercentage(value);
        this.#input.value = String(percentage);
        this.#input.style.setProperty("--audio-volume", `${percentage}%`);
        this.#output.textContent = `${percentage}%`;
    }

    #clearSendTimers() {
        clearTimeout(this.#throttleTimer);
        clearTimeout(this.#finalTimer);
        this.#throttleTimer = undefined;
        this.#finalTimer = undefined;
    }

    #clearPending() {
        clearTimeout(this.#reconcileTimer);
        this.#reconcileTimer = undefined;
        this.#pendingTarget = undefined;
    }
}

class MuteButtonController {
    #authoritativeValue = false;
    #button;
    #enabled = false;
    #onRequest;
    #pendingTarget;
    #pendingTimer;

    constructor(button, onRequest) {
        this.#button = button;
        this.#onRequest = onRequest;
        button.addEventListener("click", () => {
            if (!this.#enabled) {
                return;
            }

            const requested = !this.#displayedValue();
            this.#pendingTarget = requested;
            this.#render(requested);
            clearTimeout(this.#pendingTimer);
            this.#pendingTimer = setTimeout(() => this.rejectPending(requested),
                optimisticReconciliationMilliseconds);
            this.#onRequest(requested, this);
        });
    }

    setEnabled(enabled) {
        this.#enabled = enabled;
        this.#button.disabled = !enabled;
        if (!enabled) {
            this.#clearPending();
            this.#render(this.#authoritativeValue);
        }
    }

    setAuthoritative(value) {
        this.#authoritativeValue = value === true;
        if (this.#pendingTarget !== undefined) {
            if (this.#pendingTarget === this.#authoritativeValue) {
                this.#clearPending();
            } else {
                return;
            }
        }

        this.#render(this.#authoritativeValue);
    }

    rejectPending(value) {
        if (this.#pendingTarget !== value) {
            return;
        }

        this.#clearPending();
        this.#render(this.#authoritativeValue);
    }

    dispose() {
        this.#clearPending();
        this.#button.disabled = true;
    }

    #displayedValue() {
        return this.#pendingTarget ?? this.#authoritativeValue;
    }

    #render(isMuted) {
        this.#button.dataset.muted = isMuted ? "true" : "false";
        this.#button.setAttribute("aria-pressed", isMuted ? "true" : "false");
        this.#button.querySelector(".audio-mute-icon").textContent = isMuted ? "🔇" : "🔊";
        this.#button.querySelector(".audio-mute-label").textContent = isMuted ? "Muted" : "Mute";
    }

    #clearPending() {
        clearTimeout(this.#pendingTimer);
        this.#pendingTimer = undefined;
        this.#pendingTarget = undefined;
    }
}

class ApplicationRowController {
    #applicationId;
    #connected = false;
    #element;
    #mute;
    #name;
    #slider;
    #sliderLabel;
    #status;

    constructor(template, application, requestCommand) {
        this.#element = template.content.firstElementChild.cloneNode(true);
        this.#name = this.#element.querySelector('[data-audio-role="name"]');
        this.#status = this.#element.querySelector('[data-audio-role="status"]');
        this.#sliderLabel = this.#element.querySelector('[data-audio-role="slider-label"]');
        const slider = this.#element.querySelector('[data-audio-role="volume"]');
        const percentage = this.#element.querySelector('[data-audio-role="percentage"]');
        const muteButton = this.#element.querySelector('[data-audio-role="mute"]');

        this.#slider = new VolumeSliderController(slider, percentage, (value, controller) =>
            requestCommand({
                commandId: "audio.setApplicationVolume",
                applicationId: this.#applicationId,
                volume: value,
                controller,
                value
            }));
        this.#mute = new MuteButtonController(muteButton, (isMuted, controller) =>
            requestCommand({
                commandId: "audio.setApplicationMute",
                applicationId: this.#applicationId,
                isMuted,
                controller,
                value: isMuted
            }));
        this.update(application);
    }

    get applicationId() {
        return this.#applicationId;
    }

    get element() {
        return this.#element;
    }

    update(application) {
        this.#applicationId = application.applicationId;
        this.#element.dataset.applicationId = application.applicationId;
        this.#element.dataset.state = application.state || "unknown";
        this.#name.textContent = application.displayName || application.processName || "Unknown audio session";
        this.#name.title = this.#name.textContent;
        this.#sliderLabel.textContent = `${this.#name.textContent} volume`;

        const details = [];
        details.push(application.state === "active" ? "Active" : "Inactive");
        if (application.sessionCount > 1) {
            details.push(`${application.sessionCount} sessions`);
        }
        if (application.hasMixedVolume || application.hasMixedMute) {
            details.push("mixed settings");
        }
        this.#status.textContent = details.join(" · ");

        this.#slider.setAuthoritative(application.volume);
        this.#mute.setAuthoritative(application.isMuted);
        this.setConnected(this.#connected);
    }

    setConnected(connected) {
        this.#connected = connected;
        this.#slider.setEnabled(connected);
        this.#mute.setEnabled(connected);
    }

    dispose() {
        this.#slider.dispose();
        this.#mute.dispose();
        this.#element.remove();
    }
}

export class AudioMixerController {
    #applicationList;
    #applicationSummary;
    #connected = false;
    #emptyState;
    #masterMute;
    #masterSlider;
    #outputDevice;
    #pendingCommands = new Map();
    #requestCommand;
    #rows = new Map();
    #template;

    constructor({
        applicationList,
        applicationSummary,
        emptyState,
        masterMute,
        masterPercentage,
        masterSlider,
        outputDevice,
        template
    }) {
        this.#applicationList = applicationList;
        this.#applicationSummary = applicationSummary;
        this.#emptyState = emptyState;
        this.#outputDevice = outputDevice;
        this.#template = template;
        this.#masterSlider = new VolumeSliderController(
            masterSlider,
            masterPercentage,
            (value, controller) => this.#send({
                commandId: "audio.setMasterVolume",
                volume: value,
                controller,
                value
            }));
        this.#masterMute = new MuteButtonController(
            masterMute,
            (isMuted, controller) => this.#send({
                commandId: "audio.setMasterMute",
                isMuted,
                controller,
                value: isMuted
            }));
    }

    bindCommands(callback) {
        this.#requestCommand = callback;
    }

    setConnected(connected) {
        this.#connected = connected;
        if (!connected) {
            this.#pendingCommands.clear();
        }
        const masterAvailable = connected && this.#outputDevice.dataset.available === "true";
        this.#masterSlider.setEnabled(masterAvailable);
        this.#masterMute.setEnabled(masterAvailable);
        this.#rows.forEach(row => row.setConnected(connected));
    }

    setAudioState(state) {
        const output = state?.isAvailable === true ? state.outputDevice : null;
        this.#outputDevice.dataset.available = output ? "true" : "false";
        this.#outputDevice.textContent = output?.friendlyName || "No output device available";
        this.#masterSlider.setAuthoritative(output?.volume ?? 0);
        this.#masterMute.setAuthoritative(output?.isMuted === true);
        this.#masterSlider.setEnabled(this.#connected && output !== null);
        this.#masterMute.setEnabled(this.#connected && output !== null);

        const applications = [...(state?.applications || [])].sort((left, right) => {
            const activeDifference = Number(right.state === "active") - Number(left.state === "active");
            return activeDifference || (left.displayName || "").localeCompare(right.displayName || "");
        });
        const liveIds = new Set(applications.map(application => application.applicationId));

        this.#rows.forEach((row, applicationId) => {
            if (!liveIds.has(applicationId)) {
                row.dispose();
                this.#rows.delete(applicationId);
            }
        });

        applications.forEach(application => {
            let row = this.#rows.get(application.applicationId);
            if (!row) {
                row = new ApplicationRowController(
                    this.#template,
                    application,
                    request => this.#send(request));
                this.#rows.set(application.applicationId, row);
            }
            row.update(application);
            row.setConnected(this.#connected);
            this.#applicationList.append(row.element);
        });

        const activeCount = applications.filter(application => application.state === "active").length;
        this.#applicationSummary.textContent = applications.length === 0
            ? "0 available"
            : `${activeCount} active · ${applications.length} total`;
        this.#emptyState.hidden = applications.length !== 0;
        this.#applicationList.hidden = applications.length === 0;
    }

    handleCommandResult(result) {
        const pending = this.#pendingCommands.get(result.requestMessageId);
        this.#pendingCommands.delete(result.requestMessageId);

        if (!result.succeeded && pending) {
            pending.controller.rejectPending(pending.value);
        }

        return pending;
    }

    #send(request) {
        if (!this.#requestCommand || !this.#connected) {
            request.controller.rejectPending(request.value);
            return;
        }

        const requestMessageId = this.#requestCommand(request);
        if (!requestMessageId) {
            request.controller.rejectPending(request.value);
            return;
        }

        this.#pendingCommands.set(requestMessageId, request);
    }
}
