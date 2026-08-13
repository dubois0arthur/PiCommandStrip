using PiCommandStrip.App.AudioMixer;

namespace PiCommandStrip.Tests.AudioMixer;

public sealed class AudioStateStoreTests
{
    private static readonly DateTimeOffset InitialTime =
        new(2026, 8, 10, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryUpdate_DuplicateObservation_SuppressesTimestampOnlyChange()
    {
        var store = CreateStore();
        var first = State(updatedAt: InitialTime.AddSeconds(1));
        var duplicate = first with { LastUpdatedUtc = InitialTime.AddSeconds(2) };

        Assert.True(store.TryUpdate(first, out var changed));
        Assert.False(store.TryUpdate(duplicate, out var retained));
        Assert.Equal(1, changed.Revision);
        Assert.Same(changed, retained);
    }

    [Fact]
    public void TryUpdate_VolumeChange_IncrementsRevision()
    {
        var store = CreateStore();
        store.TryUpdate(State(volume: 0.4f), out _);

        Assert.True(store.TryUpdate(State(volume: 0.7f), out var changed));

        Assert.Equal(2, changed.Revision);
        Assert.Equal(0.7f, changed.Applications[0].Volume);
    }

    [Fact]
    public void TryUpdate_MuteChange_IsMeaningful()
    {
        var store = CreateStore();
        store.TryUpdate(State(isMuted: false), out _);

        Assert.True(store.TryUpdate(State(isMuted: true), out var changed));

        Assert.True(changed.Applications[0].IsMuted);
    }

    [Fact]
    public void TryUpdate_SessionRemoval_IsMeaningful()
    {
        var store = CreateStore();
        var withTwoSessions = State(sessionIds: ["one", "two"]);
        var withOneSession = State(sessionIds: ["one"]);
        store.TryUpdate(withTwoSessions, out _);

        Assert.True(store.TryUpdate(withOneSession, out var changed));

        Assert.Equal(1, changed.Applications[0].SessionCount);
        Assert.Equal(["one"], changed.Applications[0].SessionInstanceIds);
    }

    [Fact]
    public void TryUpdate_OutputDeviceChange_IsMeaningful()
    {
        var store = CreateStore();
        store.TryUpdate(State(deviceId: "speakers"), out _);

        Assert.True(store.TryUpdate(State(deviceId: "headphones"), out var changed));

        Assert.Equal("headphones", changed.OutputDevice?.DeviceId);
    }

    [Fact]
    public void TryUpdate_OutputVolumeAndMuteChanges_AreMeaningful()
    {
        var store = CreateStore();
        store.TryUpdate(State(outputVolume: 0.8f, outputMuted: false), out _);

        Assert.True(store.TryUpdate(
            State(outputVolume: 0.35f, outputMuted: true),
            out var changed));

        Assert.Equal(0.35f, changed.OutputDevice?.Volume);
        Assert.True(changed.OutputDevice?.IsMuted);
    }

    [Fact]
    public void TryUpdate_BecomingUnavailable_RemovesDeviceAndApplications()
    {
        var store = CreateStore();
        store.TryUpdate(State(), out _);

        Assert.True(store.TryUpdate(
            AudioState.Unavailable(InitialTime.AddSeconds(2)),
            out var changed));

        Assert.False(changed.IsAvailable);
        Assert.Null(changed.OutputDevice);
        Assert.Empty(changed.Applications);
        Assert.Equal(2, changed.Revision);
    }

    private static AudioStateStore CreateStore() =>
        new(new FixedTimeProvider(InitialTime));

    private static AudioState State(
        string deviceId = "speakers",
        float volume = 0.5f,
        bool isMuted = false,
        float outputVolume = 0.8f,
        bool outputMuted = false,
        IReadOnlyList<string>? sessionIds = null,
        DateTimeOffset? updatedAt = null)
    {
        var ids = sessionIds ?? ["one"];
        var application = new ApplicationAudioState(
            "application",
            [42],
            "example",
            "Example",
            volume,
            isMuted,
            AudioSessionStates.Active,
            ids.Count,
            false,
            false,
            ids);
        return new AudioState(
            true,
            new AudioOutputDeviceState(deviceId, "Output", outputVolume, outputMuted),
            [application],
            0,
            updatedAt ?? InitialTime.AddSeconds(1));
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
