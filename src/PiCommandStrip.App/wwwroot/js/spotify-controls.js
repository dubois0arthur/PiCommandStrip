const spotifyCommands = Object.freeze({
    saved: "spotify.setSaved",
    shuffle: "spotify.setShuffle",
    repeat: "spotify.setRepeat"
});

function nextRepeatState(current) {
    if (current === "context") {
        return "track";
    }
    if (current === "track") {
        return "off";
    }
    return "context";
}

function createButton(label, icon, title) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "spotify-control";
    button.setAttribute("aria-label", label);
    button.title = title;
    const symbol = document.createElement("span");
    symbol.className = "spotify-control-icon";
    symbol.setAttribute("aria-hidden", "true");
    symbol.textContent = icon;
    const text = document.createElement("span");
    text.className = "spotify-control-label";
    text.textContent = label;
    button.append(symbol, text);
    return button;
}

class SpotifyControlSurface {
    #connected = false;
    #device;
    #isCompact;
    #like;
    #onCommand;
    #pendingCommand;
    #queueButton;
    #queuePanel;
    #repeat;
    #root;
    #shuffle;
    #state;

    constructor(root, isCompact, onCommand) {
        this.#root = root;
        this.#isCompact = isCompact;
        this.#onCommand = onCommand;
        const controls = document.createElement("div");
        controls.className = "spotify-controls";
        this.#like = createButton("Like", "♡", "Save this item to your Spotify library");
        this.#shuffle = createButton("Shuffle", "⇄", "Toggle Spotify shuffle");
        this.#repeat = createButton("Repeat", "↻", "Cycle Spotify repeat mode");
        controls.append(this.#like, this.#shuffle, this.#repeat);

        this.#like.addEventListener("click", () => this.#request(
            spotifyCommands.saved,
            { isSaved: this.#state?.isSaved !== true }));
        this.#shuffle.addEventListener("click", () => this.#request(
            spotifyCommands.shuffle,
            { shuffleEnabled: this.#state?.shuffleEnabled !== true }));
        this.#repeat.addEventListener("click", () => this.#request(
            spotifyCommands.repeat,
            { repeatState: nextRepeatState(this.#state?.repeatState) }));

        if (!isCompact) {
            this.#queueButton = createButton("Queue", "≡", "Show the next Spotify items");
            this.#queueButton.classList.add("spotify-queue-trigger");
            this.#queueButton.setAttribute("aria-expanded", "false");
            controls.append(this.#queueButton);
            this.#device = document.createElement("span");
            this.#device.className = "spotify-device";
            this.#queuePanel = document.createElement("section");
            this.#queuePanel.className = "spotify-queue";
            this.#queuePanel.hidden = true;
            this.#queueButton.addEventListener("click", () => {
                const open = this.#queuePanel.hidden;
                this.#queuePanel.hidden = !open;
                this.#queueButton.setAttribute("aria-expanded", open ? "true" : "false");
            });
            root.append(controls, this.#device, this.#queuePanel);
        } else {
            root.append(controls);
        }
    }

    setConnected(connected) {
        this.#connected = connected;
        if (!connected) {
            this.#pendingCommand = undefined;
        }
        this.#render();
    }

    setState(state) {
        this.#state = state;
        this.#render();
    }

    handleResult(result) {
        if (result.commandId === this.#pendingCommand) {
            this.#pendingCommand = undefined;
            this.#render();
            return true;
        }
        return false;
    }

    #request(commandId, values) {
        if (!this.#connected || this.#pendingCommand) {
            return;
        }
        const requestId = this.#onCommand?.({ commandId, ...values });
        if (requestId) {
            this.#pendingCommand = commandId;
            this.#render();
        }
    }

    #render() {
        const applies = this.#state?.appliesToCurrentMedia === true;
        this.#root.hidden = !applies;
        if (!applies) {
            if (this.#queuePanel) {
                this.#queuePanel.hidden = true;
                this.#queueButton.setAttribute("aria-expanded", "false");
            }
            return;
        }

        const available = this.#state.status === "available";
        const deviceRestricted = this.#state.device?.isRestricted === true;
        const enabled = this.#connected && available && !deviceRestricted && !this.#pendingCommand;
        const saved = this.#state.isSaved === true;
        const shuffle = this.#state.shuffleEnabled === true;
        const repeat = this.#state.repeatState || "off";
        this.#like.disabled = !enabled || typeof this.#state.isSaved !== "boolean";
        this.#shuffle.disabled = !enabled || typeof this.#state.shuffleEnabled !== "boolean";
        this.#repeat.disabled = !enabled || !["off", "context", "track"].includes(repeat);
        this.#like.setAttribute("aria-pressed", saved ? "true" : "false");
        this.#shuffle.setAttribute("aria-pressed", shuffle ? "true" : "false");
        this.#repeat.setAttribute("aria-pressed", repeat === "off" ? "false" : "true");
        this.#like.querySelector(".spotify-control-icon").textContent = saved ? "♥" : "♡";
        this.#shuffle.querySelector(".spotify-control-label").textContent = shuffle ? "Shuffle on" : "Shuffle";
        this.#repeat.querySelector(".spotify-control-label").textContent = repeat === "track"
            ? "Repeat one"
            : repeat === "context"
                ? "Repeat all"
                : "Repeat";
        [this.#like, this.#shuffle, this.#repeat].forEach(button => {
            button.dataset.pending = button === this.#buttonForCommand(this.#pendingCommand)
                ? "true"
                : "false";
        });

        if (!this.#isCompact) {
            const deviceName = this.#state.device?.name;
            this.#device.textContent = deviceName
                ? `Spotify device · ${deviceName}`
                : "Spotify playback device unavailable";
            this.#renderQueue(this.#state.queue || []);
        }
    }

    #buttonForCommand(commandId) {
        if (commandId === spotifyCommands.saved) return this.#like;
        if (commandId === spotifyCommands.shuffle) return this.#shuffle;
        if (commandId === spotifyCommands.repeat) return this.#repeat;
        return null;
    }

    #renderQueue(items) {
        const heading = document.createElement("strong");
        heading.textContent = "Up next";
        const list = document.createElement("ol");
        items.slice(0, 5).forEach(item => {
            const entry = document.createElement("li");
            const title = document.createElement("span");
            title.textContent = item.title || "Untitled item";
            const subtitle = document.createElement("small");
            subtitle.textContent = item.subtitle || item.itemType || "Spotify";
            entry.append(title, subtitle);
            list.append(entry);
        });
        if (list.childElementCount === 0) {
            const empty = document.createElement("p");
            empty.textContent = "Queue unavailable.";
            this.#queuePanel.replaceChildren(heading, empty);
        } else {
            this.#queuePanel.replaceChildren(heading, list);
        }
    }
}

export class SpotifyControlsController {
    #commandCallback;
    #surfaces;

    constructor(compactRoot, expandedRoot) {
        const request = values => this.#commandCallback?.(values);
        this.#surfaces = [
            new SpotifyControlSurface(compactRoot, true, request),
            new SpotifyControlSurface(expandedRoot, false, request)
        ];
    }

    bindCommands(callback) {
        this.#commandCallback = callback;
    }

    setConnected(connected) {
        this.#surfaces.forEach(surface => surface.setConnected(connected));
    }

    setState(state) {
        this.#surfaces.forEach(surface => surface.setState(state));
    }

    handleCommandResult(result) {
        let handled = false;
        this.#surfaces.forEach(surface => {
            handled = surface.handleResult(result) || handled;
        });
        return handled;
    }
}
