const statusIndicator = document.querySelector("#status-indicator");
const healthStatus = document.querySelector("#health-status");
const healthDetails = document.querySelector("#health-details");
const applicationName = document.querySelector("#application-name");
const timestamp = document.querySelector("#timestamp");

async function checkHealth() {
    try {
        const response = await fetch("/health", {
            headers: { Accept: "application/json" },
            cache: "no-store"
        });

        if (!response.ok) {
            throw new Error(`Health endpoint returned HTTP ${response.status}.`);
        }

        const health = await response.json();

        statusIndicator.classList.add("is-healthy");
        healthStatus.textContent = "/health is reachable";
        applicationName.textContent = health.applicationName;
        timestamp.textContent = new Date(health.timestampUtc).toLocaleString();
        healthDetails.hidden = false;
    } catch (error) {
        statusIndicator.classList.add("is-unavailable");
        healthStatus.textContent = "/health could not be reached";
        console.error("Health check failed.", error);
    }
}

checkHealth();
