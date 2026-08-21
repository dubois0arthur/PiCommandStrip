function formatTemperature(value) {
    return Number.isFinite(value) ? `${Math.round(value)}°C` : "—";
}

function formatUtilization(value) {
    return Number.isFinite(value) ? `${Math.round(value)}%` : "—";
}

function formatMemoryBytes(value) {
    if (!Number.isFinite(value) || value < 0) return null;
    return value / (1024 ** 3);
}

function formatMemory(usedBytes, totalBytes) {
    const used = formatMemoryBytes(usedBytes);
    const total = formatMemoryBytes(totalBytes);
    if (used === null && total === null) return "—";
    if (used === null) return `—/${total.toFixed(total >= 10 ? 0 : 1)} GB`;
    if (total === null) return `${used.toFixed(used >= 10 ? 0 : 1)} GB`;
    return `${used.toFixed(used >= 10 ? 0 : 1)}/${total.toFixed(total >= 10 ? 0 : 1)} GB`;
}

function metricText(metric) {
    if (!metric) return "—";
    const temperature = formatTemperature(metric.temperatureCelsius);
    const utilization = formatUtilization(metric.utilizationPercent);
    if (temperature === "—") return utilization;
    if (utilization === "—") return temperature;
    return `${temperature} · ${utilization}`;
}

function updateTemperatureState(metricRoot, alert, status) {
    const normalized = ["normal", "elevated", "warning"].includes(status)
        ? status
        : "unavailable";
    metricRoot.dataset.temperatureStatus = normalized;
    const hasAlert = normalized === "elevated" || normalized === "warning";
    alert.hidden = !hasAlert;
    alert.textContent = normalized === "warning" ? "!" : "△";
    alert.title = hasAlert ? `${normalized} temperature threshold exceeded` : "";
    alert.setAttribute("aria-label", hasAlert
        ? `${normalized} temperature threshold exceeded`
        : "");
}

export class SystemTelemetryController {
    #cpuAlert;
    #cpuMetric;
    #cpuSensor;
    #cpuValue;
    #gpuAlert;
    #gpuDiagnostic;
    #gpuMetric;
    #gpuSensor;
    #gpuValue;
    #memoryValue;
    #provider;
    #reasons;
    #root;

    constructor({ root, provider, cpuSensor, gpu, gpuSensor, reasons }) {
        this.#root = root;
        this.#provider = provider;
        this.#cpuSensor = cpuSensor;
        this.#gpuDiagnostic = gpu;
        this.#gpuSensor = gpuSensor;
        this.#reasons = reasons;
        this.#cpuMetric = root.querySelector('[data-telemetry-metric="cpu"]');
        this.#gpuMetric = root.querySelector('[data-telemetry-metric="gpu"]');
        this.#cpuValue = root.querySelector('[data-telemetry-value="cpu"]');
        this.#gpuValue = root.querySelector('[data-telemetry-value="gpu"]');
        this.#memoryValue = root.querySelector('[data-telemetry-value="memory"]');
        this.#cpuAlert = root.querySelector('[data-telemetry-alert="cpu"]');
        this.#gpuAlert = root.querySelector('[data-telemetry-alert="gpu"]');
    }

    setState(state) {
        const cpu = state?.cpu;
        const gpu = state?.gpu;
        const memory = state?.memory;
        const diagnostics = state?.diagnostics;
        this.#root.dataset.status = state?.status || "unavailable";
        this.#cpuValue.textContent = metricText(cpu);
        this.#gpuValue.textContent = metricText(gpu);
        this.#memoryValue.textContent = formatMemory(memory?.usedBytes, memory?.totalBytes);
        updateTemperatureState(this.#cpuMetric, this.#cpuAlert, cpu?.temperatureStatus);
        updateTemperatureState(this.#gpuMetric, this.#gpuAlert, gpu?.temperatureStatus);

        const statusDescription = state?.status === "available"
            ? "available"
            : state?.status === "partial"
                ? "partially available"
                : "unavailable";
        this.#root.setAttribute(
            "aria-label",
            `System telemetry ${statusDescription}. CPU ${this.#cpuValue.textContent}. GPU ${this.#gpuValue.textContent}. RAM ${this.#memoryValue.textContent}.`);
        this.#root.title = [cpu?.name, gpu?.name].filter(Boolean).join(" · ") || "System telemetry unavailable";

        this.#provider.textContent = diagnostics
            ? `${diagnostics.providerName} · ${diagnostics.providerStatus}`
            : "Unavailable";
        this.#provider.title = this.#provider.textContent;
        this.#cpuSensor.textContent = diagnostics?.cpuTemperatureSensor || "Not selected";
        this.#gpuDiagnostic.textContent = diagnostics?.gpuName || "Not selected";
        this.#gpuDiagnostic.title = diagnostics?.gpuIdentifier || this.#gpuDiagnostic.textContent;
        this.#gpuSensor.textContent = diagnostics?.gpuTemperatureSensor || "Not selected";
        const unavailable = Array.isArray(diagnostics?.unavailableReasons)
            ? diagnostics.unavailableReasons.filter(Boolean)
            : [];
        this.#reasons.hidden = unavailable.length === 0;
        this.#reasons.textContent = unavailable.length === 0
            ? ""
            : `Unavailable: ${unavailable.join(" ")}`;
    }
}
