const applicationFamilies = Object.freeze({
    spotify: new Set(["spotify", "spotifyab", "spotifymusic"]),
    firefox: new Set(["firefox", "mozilla"]),
    chrome: new Set(["chrome", "googlechrome"]),
    msedge: new Set(["msedge", "microsoftedge"]),
    discord: new Set(["discord"])
});

const browserFamilies = new Set(["firefox", "chrome", "msedge"]);

export function normalizeProcessName(value) {
    if (typeof value !== "string") {
        return "";
    }

    const normalized = value.trim().toLowerCase();
    return normalized.endsWith(".exe") ? normalized.slice(0, -4) : normalized;
}

function sourceTokens(value) {
    return typeof value === "string"
        ? value.toLowerCase().split(/[^a-z0-9]+/).filter(Boolean)
        : [];
}

function identifyProcessFamily(value) {
    const processName = normalizeProcessName(value);
    for (const [family, aliases] of Object.entries(applicationFamilies)) {
        if (aliases.has(processName)) {
            return family;
        }
    }

    return null;
}

function identifySourceFamily(...values) {
    const tokens = values.flatMap(sourceTokens);
    for (const [family, aliases] of Object.entries(applicationFamilies)) {
        if (tokens.some(token => aliases.has(token))) {
            return family;
        }
    }

    return null;
}

function normalizedMediaSources(mediaState) {
    return [mediaState?.sourceName, mediaState?.sessionSourceIdentifier]
        .map(normalizeProcessName)
        .filter(Boolean);
}

function normalizeComparableTitle(value) {
    return typeof value === "string"
        ? value.toLowerCase().replace(/[^a-z0-9]+/g, " ").trim()
        : "";
}

function titleIdentifiesForegroundMedia(mediaState, foregroundWindowTitle) {
    const mediaTitle = normalizeComparableTitle(mediaState?.title);
    const windowTitle = normalizeComparableTitle(foregroundWindowTitle);
    if (mediaTitle.length < 8 || windowTitle.length < mediaTitle.length) {
        return false;
    }

    return windowTitle === mediaTitle ||
        windowTitle.startsWith(`${mediaTitle} `) ||
        windowTitle.endsWith(` ${mediaTitle}`) ||
        windowTitle.includes(` ${mediaTitle} `);
}

function uniqueApplication(applications) {
    return applications.length === 1 ? applications[0] : null;
}

function applicationsMatchingProcess(audioState, processName) {
    const normalized = normalizeProcessName(processName);
    if (!normalized) {
        return [];
    }

    return (audioState?.applications || []).filter(application =>
        normalizeProcessName(application.processName) === normalized);
}

function applicationsMatchingFamily(audioState, family) {
    if (!family) {
        return [];
    }

    return (audioState?.applications || []).filter(application =>
        identifyProcessFamily(application.processName) === family);
}

export function matchForegroundAudioApplication(audioState, foregroundProcess) {
    return uniqueApplication(applicationsMatchingProcess(audioState, foregroundProcess));
}

export function mediaBelongsToForeground(
    mediaState,
    foregroundProcess,
    foregroundWindowTitle)
{
    if (mediaState?.hasActiveSession !== true) {
        return false;
    }

    const normalizedForeground = normalizeProcessName(foregroundProcess);
    if (!normalizedForeground) {
        return false;
    }

    if (normalizedMediaSources(mediaState).includes(normalizedForeground)) {
        return true;
    }

    const foregroundFamily = identifyProcessFamily(normalizedForeground);
    const sourceFamily = identifySourceFamily(
        mediaState.sourceName,
        mediaState.sessionSourceIdentifier);
    if (foregroundFamily && sourceFamily && foregroundFamily === sourceFamily) {
        return true;
    }

    // A recognized but different source is affirmative evidence that the media
    // belongs elsewhere. The title heuristic is reserved for opaque Windows
    // media sources; otherwise a Spotify title repeated in a browser tab could
    // incorrectly expose the browser's volume control.
    if (sourceFamily) {
        return false;
    }

    return browserFamilies.has(foregroundFamily) &&
        titleIdentifiesForegroundMedia(mediaState, foregroundWindowTitle);
}

export function matchMediaAudioApplication(
    audioState,
    mediaState,
    foregroundProcess,
    foregroundWindowTitle)
{
    if (mediaState?.hasActiveSession !== true) {
        return null;
    }

    const directMatches = normalizedMediaSources(mediaState)
        .flatMap(source => applicationsMatchingProcess(audioState, source));
    const uniqueDirectMatches = [...new Map(directMatches.map(application =>
        [application.applicationId, application])).values()];
    if (uniqueDirectMatches.length === 1) {
        return uniqueDirectMatches[0];
    }
    if (uniqueDirectMatches.length > 1) {
        return null;
    }

    const sourceFamily = identifySourceFamily(
        mediaState.sourceName,
        mediaState.sessionSourceIdentifier);
    const familyMatch = uniqueApplication(applicationsMatchingFamily(audioState, sourceFamily));
    if (familyMatch) {
        return familyMatch;
    }

    if (!mediaBelongsToForeground(
        mediaState,
        foregroundProcess,
        foregroundWindowTitle)) {
        return null;
    }

    return matchForegroundAudioApplication(audioState, foregroundProcess);
}

export function selectGamingAudioApplications(
    audioState,
    mediaState,
    foregroundProcess,
    foregroundWindowTitle,
    limit = 4)
{
    const selected = [];
    const selectedIds = new Set();
    const add = application => {
        if (!application || selectedIds.has(application.applicationId) || selected.length >= limit) {
            return;
        }

        selectedIds.add(application.applicationId);
        selected.push(application);
    };

    add(matchForegroundAudioApplication(audioState, foregroundProcess));
    add(uniqueApplication(applicationsMatchingProcess(audioState, "discord")));
    add(matchMediaAudioApplication(
        audioState,
        mediaState,
        foregroundProcess,
        foregroundWindowTitle));

    const otherActiveApplications = [...(audioState?.applications || [])]
        .filter(application => application.state === "active")
        .sort((left, right) =>
            (left.displayName || "").localeCompare(right.displayName || ""));
    otherActiveApplications.forEach(add);
    return selected;
}
