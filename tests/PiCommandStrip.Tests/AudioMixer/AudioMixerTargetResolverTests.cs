using PiCommandStrip.App.AudioMixer;

namespace PiCommandStrip.Tests.AudioMixer;

public sealed class AudioMixerTargetResolverTests
{
    private static readonly string CurrentApplicationId = new('c', 64);

    [Fact]
    public void ResolveApplication_CurrentIdentifier_ReturnsUnderlyingSessions()
    {
        var application = Application(CurrentApplicationId, ["session-1", "session-2"]);
        var state = State([application]);

        var resolved = AudioMixerTargetResolver.ResolveApplication(state, CurrentApplicationId);

        Assert.Same(application, resolved);
        Assert.Equal(["session-1", "session-2"], resolved!.SessionInstanceIds);
    }

    [Theory]
    [InlineData("dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd")]
    [InlineData("")]
    [InlineData("not-a-generated-id")]
    public void ResolveApplication_UnknownOrMalformedIdentifier_ReturnsNull(string identifier)
    {
        var state = State([Application(CurrentApplicationId, ["session-1"])]);

        var resolved = AudioMixerTargetResolver.ResolveApplication(state, identifier);

        Assert.Null(resolved);
    }

    private static AudioState State(IReadOnlyList<ApplicationAudioState> applications) =>
        new(
            true,
            new AudioOutputDeviceState("device", "Speakers", 0.5f, false),
            [new AudioOutputDeviceDescriptorState(
                "device",
                "Speakers",
                AudioOutputDeviceStates.Active,
                true)],
            applications,
            1,
            DateTimeOffset.UtcNow);

    private static ApplicationAudioState Application(
        string id,
        IReadOnlyList<string> sessionIds) =>
        new(
            id,
            [42],
            "example",
            "Example",
            0.5f,
            false,
            AudioSessionStates.Active,
            sessionIds.Count,
            false,
            false,
            sessionIds);
}
