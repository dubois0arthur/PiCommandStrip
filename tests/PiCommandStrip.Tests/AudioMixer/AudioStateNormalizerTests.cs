using PiCommandStrip.App.AudioMixer;

namespace PiCommandStrip.Tests.AudioMixer;

public sealed class AudioStateNormalizerTests
{
    private static readonly DateTimeOffset UpdatedAt =
        new(2026, 8, 10, 20, 0, 0, TimeSpan.Zero);

    private readonly AudioStateNormalizer _normalizer = new();

    [Fact]
    public void Normalize_WithoutOutputDevice_ReturnsUnavailableState()
    {
        var state = _normalizer.Normalize(
            new AudioMixerSnapshot(null, [Session("session-1", processName: "spotify")]),
            UpdatedAt);

        Assert.False(state.IsAvailable);
        Assert.Null(state.OutputDevice);
        Assert.Empty(state.Applications);
    }

    [Fact]
    public void Normalize_GroupsRecognizableProcessSessionsAcrossProcesses()
    {
        var snapshot = new AudioMixerSnapshot(
            OutputDevice(),
            [
                Session(
                    "firefox-2",
                    processId: 220,
                    processName: "firefox",
                    displayName: null,
                    volume: 0.8f,
                    isMuted: true,
                    state: AudioSessionStatus.Inactive),
                Session(
                    "firefox-1",
                    processId: 110,
                    processName: "Firefox.exe",
                    displayName: "YouTube",
                    volume: 0.4f,
                    state: AudioSessionStatus.Active)
            ]);

        var state = _normalizer.Normalize(snapshot, UpdatedAt);

        var application = Assert.Single(state.Applications);
        Assert.Equal([110, 220], application.ProcessIds);
        Assert.Equal("firefox", application.ProcessName);
        Assert.Equal("YouTube", application.DisplayName);
        Assert.Equal(0.8f, application.Volume);
        Assert.False(application.IsMuted);
        Assert.Equal(AudioSessionStates.Active, application.State);
        Assert.Equal(2, application.SessionCount);
        Assert.True(application.HasMixedVolume);
        Assert.True(application.HasMixedMute);
        Assert.Equal(["firefox-1", "firefox-2"], application.SessionInstanceIds);
    }

    [Fact]
    public void Normalize_DoesNotMergeUnknownSessionsByDisplayNameAlone()
    {
        var snapshot = new AudioMixerSnapshot(
            OutputDevice(),
            [
                Session("unknown-1", displayName: "Browser audio"),
                Session("unknown-2", displayName: "Browser audio")
            ]);

        var state = _normalizer.Normalize(snapshot, UpdatedAt);

        Assert.Equal(2, state.Applications.Count);
        Assert.All(state.Applications, application =>
            Assert.Equal("Browser audio", application.DisplayName));
        Assert.NotEqual(
            state.Applications[0].ApplicationId,
            state.Applications[1].ApplicationId);
    }

    [Fact]
    public void Normalize_GroupsMetadataPoorSessionsUsingWindowsGroupingId()
    {
        var groupingId = Guid.Parse("a04df08f-bf20-48bc-b6d5-b09547b85383");
        var snapshot = new AudioMixerSnapshot(
            OutputDevice(),
            [
                Session("group-1", groupingId: groupingId, displayName: "Game audio"),
                Session("group-2", groupingId: groupingId, displayName: null)
            ]);

        var application = Assert.Single(_normalizer.Normalize(snapshot, UpdatedAt).Applications);

        Assert.Null(application.ProcessName);
        Assert.Equal("Game audio", application.DisplayName);
        Assert.Equal(2, application.SessionCount);
    }

    [Fact]
    public void Normalize_FiltersExplicitSystemAndExpiredSessionsButKeepsMissingMetadata()
    {
        var snapshot = new AudioMixerSnapshot(
            OutputDevice(),
            [
                Session("system", isSystemSounds: true),
                Session("expired", state: AudioSessionStatus.Expired),
                Session("imperfect", state: AudioSessionStatus.Unknown)
            ]);

        var application = Assert.Single(_normalizer.Normalize(snapshot, UpdatedAt).Applications);

        Assert.Equal("Unknown audio session", application.DisplayName);
        Assert.Null(application.ProcessName);
        Assert.Equal(AudioSessionStates.Unknown, application.State);
        Assert.Equal(["imperfect"], application.SessionInstanceIds);
    }

    [Fact]
    public void Normalize_ProducesStableApplicationIdAndClampedVolumes()
    {
        var first = _normalizer.Normalize(
            new AudioMixerSnapshot(
                new AudioOutputDeviceSnapshot(" device ", " Speakers ", 2, false),
                [Session("one", processName: "Spotify.exe", volume: float.NaN)]),
            UpdatedAt);
        var second = _normalizer.Normalize(
            new AudioMixerSnapshot(
                OutputDevice(),
                [Session("two", processName: "spotify", volume: -1)]),
            UpdatedAt.AddSeconds(1));

        Assert.Equal("device", first.OutputDevice?.DeviceId);
        Assert.Equal("Speakers", first.OutputDevice?.FriendlyName);
        Assert.Equal(1, first.OutputDevice?.Volume);
        Assert.Equal(0, first.Applications[0].Volume);
        Assert.Equal(
            first.Applications[0].ApplicationId,
            second.Applications[0].ApplicationId);
    }

    private static AudioOutputDeviceSnapshot OutputDevice() =>
        new("device-1", "Speakers", 0.65f, false);

    private static AudioSessionSnapshot Session(
        string instanceId,
        string? sessionIdentifier = null,
        Guid? groupingId = null,
        int? processId = null,
        string? processName = null,
        string? displayName = null,
        float volume = 0.5f,
        bool isMuted = false,
        AudioSessionStatus state = AudioSessionStatus.Inactive,
        bool isSystemSounds = false) =>
        new(
            instanceId,
            sessionIdentifier,
            groupingId,
            processId,
            processName,
            displayName,
            volume,
            isMuted,
            state,
            isSystemSounds);
}
