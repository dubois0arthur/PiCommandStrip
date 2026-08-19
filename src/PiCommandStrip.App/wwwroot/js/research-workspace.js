const selectedTextPreviewLimit = 180;

const pageActions = [
    { commandId: "browser.back", label: "Back", icon: "←", capability: "canGoBack" },
    { commandId: "browser.forward", label: "Forward", icon: "→", capability: "canGoForward" },
    { commandId: "browser.reload", label: "Reload", icon: "↻" },
    { commandId: "browser.newTab", label: "New tab", icon: "+" },
    { commandId: "browser.closeTab", label: "Close tab", icon: "×" },
    { commandId: "browser.reopenClosedTab", label: "Reopen", icon: "↶" },
    { commandId: "browser.copyCurrentUrl", label: "Copy URL", icon: "⧉" }
];

export function selectedTextPreview(selectedText, limit = selectedTextPreviewLimit) {
    const normalized = typeof selectedText === "string"
        ? selectedText.replace(/\s+/g, " ").trim()
        : "";
    if (normalized.length <= limit) return normalized;
    return `${normalized.slice(0, Math.max(0, limit - 1)).trimEnd()}…`;
}

export function buildResearchViewModel(browserState, searchActions = []) {
    const connected = browserState?.connectionState === "connected";
    const hasTab = connected && Number.isInteger(browserState?.activeTabId);
    const selectedText = selectedTextPreview(browserState?.selectedText);
    return {
        connected,
        title: connected
            ? browserState?.pageTitle || browserState?.hostName || "Firefox tab"
            : "Browser integration unavailable",
        domain: connected
            ? browserState?.hostName || "Internal or restricted page"
            : "Research actions will return when the local bridge reconnects.",
        selectionVisible: selectedText.length > 0,
        selectedText,
        pageActions: pageActions.map(action => ({
            ...action,
            enabled: connected &&
                (action.commandId === "browser.newTab" ||
                 action.commandId === "browser.reopenClosedTab" ||
                 (hasTab && (!action.capability || browserState?.[action.capability] === true)))
        })),
        searchActions: selectedText.length > 0
            ? searchActions.filter(action =>
                typeof action?.actionId === "string" &&
                typeof action?.displayName === "string")
            : []
    };
}

export class ResearchWorkspaceController {
    #connected = false;
    #onCommand;
    #pendingCommandId;
    #searchActions = [];
    #state;

    constructor({
        root,
        title,
        domain,
        status,
        pageActionGrid,
        selectionPanel,
        selectionPreview,
        searchActionGrid
    }) {
        this.root = root;
        this.title = title;
        this.domain = domain;
        this.status = status;
        this.pageActionGrid = pageActionGrid;
        this.selectionPanel = selectionPanel;
        this.selectionPreview = selectionPreview;
        this.searchActionGrid = searchActionGrid;

        pageActionGrid.addEventListener("click", event => {
            const button = event.target.closest("[data-browser-command]");
            if (!button || button.disabled) return;
            this.#onCommand?.({ commandId: button.dataset.browserCommand });
        });
        searchActionGrid.addEventListener("click", event => {
            const button = event.target.closest("[data-search-action]");
            if (!button || button.disabled) return;
            this.#onCommand?.({
                commandId: "browser.searchSelection",
                searchActionId: button.dataset.searchAction
            });
        });
    }

    bindCommands(callback) {
        this.#onCommand = callback;
    }

    setConnected(connected) {
        this.#connected = connected;
        if (!connected) this.#pendingCommandId = undefined;
        this.#render();
    }

    setBrowserState(state) {
        this.#state = state;
        this.#render();
    }

    setSearchActions(actions) {
        this.#searchActions = Array.isArray(actions) ? actions : [];
        this.#render();
    }

    setPending(commandId) {
        this.#pendingCommandId = commandId;
        this.#render();
    }

    clearPending() {
        this.#pendingCommandId = undefined;
        this.#render();
    }

    #render() {
        const model = buildResearchViewModel(this.#state, this.#searchActions);
        this.root.dataset.integration = model.connected ? "connected" : "disconnected";
        this.title.textContent = model.title;
        this.title.title = model.title;
        this.domain.textContent = model.domain;
        this.status.hidden = model.connected;
        this.status.textContent = "Bridge offline";

        this.pageActionGrid.replaceChildren(...model.pageActions.map(action => {
            const button = document.createElement("button");
            button.className = "research-action";
            button.type = "button";
            button.dataset.browserCommand = action.commandId;
            button.disabled = !this.#connected || !action.enabled || Boolean(this.#pendingCommandId);
            button.dataset.pending = this.#pendingCommandId === action.commandId ? "true" : "false";
            button.innerHTML = `<span aria-hidden="true">${action.icon}</span><strong></strong>`;
            button.querySelector("strong").textContent = action.label;
            return button;
        }));

        this.selectionPanel.hidden = !model.selectionVisible;
        this.selectionPreview.textContent = model.selectedText;
        this.selectionPreview.title = model.selectedText;
        this.searchActionGrid.replaceChildren(...model.searchActions.map(action => {
            const button = document.createElement("button");
            button.className = "research-search-action";
            button.type = "button";
            button.dataset.searchAction = action.actionId;
            button.disabled = !this.#connected || Boolean(this.#pendingCommandId);
            button.dataset.pending = this.#pendingCommandId === "browser.searchSelection"
                ? "true"
                : "false";
            button.textContent = action.displayName;
            return button;
        }));
    }
}
