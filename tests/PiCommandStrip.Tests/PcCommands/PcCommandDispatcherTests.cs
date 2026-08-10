using Microsoft.Extensions.Logging.Abstractions;
using PiCommandStrip.App.PcCommands;

namespace PiCommandStrip.Tests.PcCommands;

public sealed class PcCommandDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_OpenNotepad_InvokesAllowlistedHandler()
    {
        var launcher = new RecordingNotepadLauncher();
        var dispatcher = CreateDispatcher(new OpenNotepadCommandHandler(launcher));

        var result = await dispatcher.DispatchAsync(
            new PcCommandInvocation(PcCommandIds.OpenNotepad),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("Notepad opened.", result.Message);
        Assert.Equal(1, launcher.LaunchCount);
    }

    [Fact]
    public async Task DispatchAsync_UnknownCommand_RejectsWithoutInvokingHandler()
    {
        var launcher = new RecordingNotepadLauncher();
        var dispatcher = CreateDispatcher(new OpenNotepadCommandHandler(launcher));

        var result = await dispatcher.DispatchAsync(
            new PcCommandInvocation("open_calculator"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Command identifier is not allowlisted.", result.Message);
        Assert.Equal(0, launcher.LaunchCount);
    }

    [Fact]
    public async Task DispatchAsync_HandlerThrows_ReturnsSafeErrorWithoutSensitiveDetails()
    {
        const string sensitiveExceptionText = @"Could not access C:\Users\Example\private\notepad.exe";
        var handler = new OpenNotepadCommandHandler(
            new ThrowingNotepadLauncher(sensitiveExceptionText));
        var dispatcher = CreateDispatcher(handler);

        var result = await dispatcher.DispatchAsync(
            new PcCommandInvocation(PcCommandIds.OpenNotepad),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("The command could not be completed.", result.Message);
        Assert.DoesNotContain("C:\\Users", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("notepad.exe", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static PcCommandDispatcher CreateDispatcher(IPcCommandHandler handler) =>
        new([handler], NullLogger<PcCommandDispatcher>.Instance);

    private sealed class RecordingNotepadLauncher : INotepadLauncher
    {
        public int LaunchCount { get; private set; }

        public void Launch() => LaunchCount++;
    }

    private sealed class ThrowingNotepadLauncher(string exceptionMessage) : INotepadLauncher
    {
        public void Launch() => throw new InvalidOperationException(exceptionMessage);
    }
}
