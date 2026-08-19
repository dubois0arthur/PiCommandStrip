const pageSize = 20;

export class ResearchInboxApi {
    #tokenProvider;

    constructor(tokenProvider) {
        this.#tokenProvider = tokenProvider;
    }

    getPage(beforeId) {
        const query = new URLSearchParams({ limit: String(pageSize) });
        if (Number.isSafeInteger(beforeId) && beforeId > 0) {
            query.set("beforeId", String(beforeId));
        }
        return this.#request(`/api/research-inbox?${query}`);
    }

    getItem(id) {
        return this.#request(`/api/research-inbox/${this.#safeId(id)}`);
    }

    setReviewed(id, isReviewed) {
        return this.#request(`/api/research-inbox/${this.#safeId(id)}/reviewed`, {
            method: "PATCH",
            body: JSON.stringify({ isReviewed: Boolean(isReviewed) })
        });
    }

    deleteItem(id) {
        return this.#request(`/api/research-inbox/${this.#safeId(id)}`, {
            method: "DELETE"
        });
    }

    async #request(url, options = {}) {
        const token = this.#tokenProvider();
        if (!token) throw new Error("Authentication is required.");
        const response = await fetch(url, {
            ...options,
            cache: "no-store",
            headers: {
                "Authorization": `Bearer ${token}`,
                "Content-Type": "application/json",
                ...options.headers
            }
        });
        if (!response.ok) {
            let message = "Research Inbox request failed.";
            try {
                const body = await response.json();
                if (typeof body?.error === "string") message = body.error;
            } catch {
                // Keep the safe generic message for empty or malformed failures.
            }
            throw new Error(message);
        }
        if (response.status === 204) return null;
        return response.json();
    }

    #safeId(value) {
        if (!Number.isSafeInteger(value) || value <= 0) {
            throw new Error("The Research Inbox item is invalid.");
        }
        return value;
    }
}

export class ResearchInboxController {
    #actions;
    #deleteConfirmationId;
    #hasMore = false;
    #items = [];
    #mutationInFlight = false;
    #nextBeforeId;
    #open = false;
    #selectedId;

    constructor({ root, list, empty, more, detail, close, openButton, count }) {
        this.root = root;
        this.list = list;
        this.empty = empty;
        this.more = more;
        this.detail = detail;
        this.closeButton = close;
        this.openButton = openButton;
        this.count = count;

        openButton.addEventListener("click", () => this.open());
        close.addEventListener("click", () => this.close());
        more.addEventListener("click", () => this.#loadPage(false));
        list.addEventListener("click", event => {
            const button = event.target.closest("[data-research-item-id]");
            const id = Number(button?.dataset.researchItemId);
            if (Number.isSafeInteger(id)) this.#showDetail(id);
        });
        detail.addEventListener("click", event => this.#handleDetailAction(event));
    }

    bindActions(actions) {
        this.#actions = actions;
    }

    setState(state) {
        this.count.textContent = String(Math.max(0, state?.unreviewedCount || 0));
        this.openButton.title = `${Math.max(0, state?.totalCount || 0)} saved research items`;
        if (this.#open && !this.#mutationInFlight && state?.changeType !== "initialized") {
            this.#loadPage(true);
        }
    }

    async open() {
        if (!this.#actions || this.#open) return;
        this.#open = true;
        this.root.hidden = false;
        this.#actions.onOpenChanged?.(true);
        await this.#loadPage(true);
        this.closeButton.focus();
    }

    close() {
        if (!this.#open) return;
        this.#open = false;
        this.root.hidden = true;
        this.#actions?.onOpenChanged?.(false);
        this.openButton.focus();
    }

    async #loadPage(reset) {
        if (!this.#actions) return;
        this.more.disabled = true;
        this.more.textContent = "Loading…";
        try {
            const page = await this.#actions.getPage(reset ? undefined : this.#nextBeforeId);
            this.#items = reset ? page.items : [...this.#items, ...page.items];
            this.#nextBeforeId = page.nextBeforeId;
            this.#hasMore = page.hasMore;
            this.#renderList();
            if (reset && this.#selectedId && !this.#items.some(item => item.id === this.#selectedId)) {
                this.#selectedId = undefined;
                this.#renderPlaceholder();
            }
        } catch (error) {
            this.#actions.showFeedback?.(error.message, "failure");
        } finally {
            this.more.disabled = false;
            this.more.textContent = "Load older";
        }
    }

    #renderList() {
        this.list.replaceChildren(...this.#items.map(item => {
            const button = document.createElement("button");
            button.className = "research-inbox-item";
            button.type = "button";
            button.dataset.researchItemId = String(item.id);
            button.dataset.reviewed = item.isReviewed ? "true" : "false";
            button.dataset.selected = item.id === this.#selectedId ? "true" : "false";

            const title = document.createElement("strong");
            title.textContent = item.title;
            title.title = item.title;
            const meta = document.createElement("span");
            meta.textContent = `${item.domain} · ${relativeAge(item.createdAtUtc)}`;
            const marker = document.createElement("span");
            marker.className = "research-inbox-item-marker";
            marker.textContent = item.hasSelectedText ? "Quote" : item.isReviewed ? "Reviewed" : "Page";
            button.append(title, meta, marker);
            return button;
        }));
        this.empty.hidden = this.#items.length > 0;
        this.more.hidden = !this.#hasMore;
    }

    async #showDetail(id) {
        this.#selectedId = id;
        this.#deleteConfirmationId = undefined;
        this.#renderList();
        this.detail.setAttribute("aria-busy", "true");
        this.detail.replaceChildren(textElement("p", "research-inbox-detail-placeholder", "Loading saved item…"));
        try {
            const item = await this.#actions.getItem(id);
            if (this.#selectedId !== id) return;
            this.#renderDetail(item);
        } catch (error) {
            this.#actions.showFeedback?.(error.message, "failure");
            this.#renderPlaceholder();
        } finally {
            this.detail.removeAttribute("aria-busy");
        }
    }

    #renderDetail(item) {
        const heading = textElement("h3", "research-inbox-detail-title", item.title);
        heading.title = item.title;
        const domain = textElement("p", "research-inbox-detail-domain",
            `${item.domain} · ${relativeAge(item.createdAtUtc)}`);
        const url = textElement("p", "research-inbox-detail-url", item.url);
        const selection = item.selectedText
            ? textElement("blockquote", "research-inbox-selection", item.selectedText)
            : textElement("p", "research-inbox-no-selection", "Saved page · no selected passage");

        const actions = document.createElement("div");
        actions.className = "research-inbox-detail-actions";
        actions.append(
            actionButton("open", "Open in Browser", "primary"),
            actionButton("review", item.isReviewed ? "Mark Unreviewed" : "Mark Reviewed"),
            actionButton("delete", "Delete", "danger"));
        this.detail.replaceChildren(heading, domain, url, selection, actions);
        this.detail.dataset.itemId = String(item.id);
        this.detail.dataset.reviewed = item.isReviewed ? "true" : "false";
    }

    async #handleDetailAction(event) {
        const button = event.target.closest("[data-inbox-action]");
        const id = Number(this.detail.dataset.itemId);
        if (!button || !Number.isSafeInteger(id) || button.disabled) return;
        const action = button.dataset.inboxAction;
        if (action === "open") {
            this.#actions.openItem(id);
            return;
        }
        if (action === "delete" && this.#deleteConfirmationId !== id) {
            this.#deleteConfirmationId = id;
            button.textContent = "Tap again to delete";
            return;
        }

        this.detail.querySelectorAll("button").forEach(control => control.disabled = true);
        this.#mutationInFlight = true;
        try {
            if (action === "delete") {
                await this.#actions.deleteItem(id);
                this.#selectedId = undefined;
                this.#renderPlaceholder();
                await this.#loadPage(true);
                this.#actions.showFeedback?.("Research item deleted.", "success");
            } else if (action === "review") {
                const reviewed = this.detail.dataset.reviewed !== "true";
                await this.#actions.setReviewed(id, reviewed);
                await this.#loadPage(true);
                await this.#showDetail(id);
                this.#actions.showFeedback?.(reviewed ? "Marked reviewed." : "Marked unreviewed.", "success");
            }
        } catch (error) {
            this.#actions.showFeedback?.(error.message, "failure");
            await this.#showDetail(id);
        } finally {
            this.#mutationInFlight = false;
        }
    }

    #renderPlaceholder() {
        delete this.detail.dataset.itemId;
        this.detail.replaceChildren(textElement(
            "p", "research-inbox-detail-placeholder", "Select a saved page to review it."));
    }
}

function relativeAge(timestamp) {
    const milliseconds = Date.now() - new Date(timestamp).getTime();
    if (!Number.isFinite(milliseconds)) return "Saved";
    const minutes = Math.max(0, Math.floor(milliseconds / 60000));
    if (minutes < 1) return "Just now";
    if (minutes < 60) return `${minutes}m ago`;
    const hours = Math.floor(minutes / 60);
    if (hours < 24) return `${hours}h ago`;
    const days = Math.floor(hours / 24);
    return days < 30 ? `${days}d ago` : new Date(timestamp).toLocaleDateString();
}

function textElement(tagName, className, text) {
    const element = document.createElement(tagName);
    element.className = className;
    element.textContent = text;
    return element;
}

function actionButton(action, label, tone) {
    const button = document.createElement("button");
    button.className = `research-inbox-detail-action${tone ? ` research-inbox-detail-action-${tone}` : ""}`;
    button.type = "button";
    button.dataset.inboxAction = action;
    button.textContent = label;
    return button;
}
