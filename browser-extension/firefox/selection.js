const port = browser.runtime.connect({ name: "selection-observer" });
let debounceTimer;
let lastSelection;

function publishSelection() {
    clearTimeout(debounceTimer);
    debounceTimer = setTimeout(() => {
        const selectedText = window.getSelection()?.toString().trim() || null;
        const bounded = selectedText?.slice(0, 1000) || null;
        if (bounded === lastSelection) return;
        lastSelection = bounded;
        port.postMessage({ type: "selection_changed", selectedText: bounded });
    }, 150);
}

document.addEventListener("selectionchange", publishSelection, { passive: true });
window.addEventListener("pagehide", () => {
    clearTimeout(debounceTimer);
    if (lastSelection) {
        port.postMessage({ type: "selection_changed", selectedText: null });
    }
});
publishSelection();
