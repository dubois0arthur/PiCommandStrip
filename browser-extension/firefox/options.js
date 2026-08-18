const form = document.querySelector("#settings-form");
const tokenInput = document.querySelector("#pairing-token");
const portInput = document.querySelector("#bridge-port");
const status = document.querySelector("#status");

function describeStatus(value) {
    return ({
        connected: "Connected to PiCommandStrip on this PC.",
        connecting: "Connecting to the local PiCommandStrip bridge…",
        disconnected: "Bridge unavailable; retrying automatically.",
        authentication_failed: "Pairing failed. Check the browser pairing token.",
        unconfigured: "Enter the browser pairing token to connect.",
        error: "The local bridge returned an error; retrying automatically."
    })[value] || "Bridge status unavailable.";
}

function updateStatus(value) {
    status.textContent = describeStatus(value);
    status.dataset.state = value;
}

async function load() {
    const stored = await browser.storage.local.get(["pairingToken", "bridgePort"]);
    tokenInput.value = stored.pairingToken || "";
    portInput.value = stored.bridgePort || 5078;
    try {
        const result = await browser.runtime.sendMessage({ type: "bridge_status_request" });
        updateStatus(result?.status);
    } catch {
        updateStatus("disconnected");
    }
}

form.addEventListener("submit", async event => {
    event.preventDefault();
    const token = tokenInput.value.trim();
    const port = Number(portInput.value);
    if (!token || !Number.isInteger(port) || port < 1024 || port > 65535) {
        updateStatus("authentication_failed");
        return;
    }
    await browser.storage.local.set({ pairingToken: token, bridgePort: port });
    updateStatus("connecting");
});

browser.runtime.onMessage.addListener(message => {
    if (message?.type === "bridge_status_changed") updateStatus(message.status);
});

load();
