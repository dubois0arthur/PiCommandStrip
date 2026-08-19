const port = browser.runtime.connect({ name: "selection-observer" });
let debounceTimer;
let lastPageState;

function publishPageState() {
    clearTimeout(debounceTimer);
    debounceTimer = setTimeout(() => {
        const selectedText = window.getSelection()?.toString().trim() || null;
        const bounded = selectedText?.slice(0, 1000) || null;
        const navigationState = typeof navigation !== "undefined" ? navigation : null;
        const pageState = {
            selectedText: bounded,
            canGoBack: typeof navigationState?.canGoBack === "boolean" ? navigationState.canGoBack : null,
            canGoForward: typeof navigationState?.canGoForward === "boolean" ? navigationState.canGoForward : null
        };
        const meaning = JSON.stringify(pageState);
        if (meaning === lastPageState) return;
        lastPageState = meaning;
        port.postMessage({ type: "page_state_changed", ...pageState });
    }, 150);
}

document.addEventListener("selectionchange", publishPageState, { passive: true });
if (typeof navigation !== "undefined") {
    navigation.addEventListener("currententrychange", publishPageState);
}
window.addEventListener("pagehide", () => {
    clearTimeout(debounceTimer);
    port.postMessage({
        type: "page_state_changed",
        selectedText: null,
        canGoBack: null,
        canGoForward: null
    });
});
publishPageState();
