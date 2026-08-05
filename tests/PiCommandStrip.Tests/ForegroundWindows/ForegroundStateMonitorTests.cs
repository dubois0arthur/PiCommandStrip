using PiCommandStrip.App.ForegroundWindows;

namespace PiCommandStrip.Tests.ForegroundWindows;

public sealed class ForegroundStateMonitorTests
{
    private static readonly DateTimeOffset InitialTime =
        new(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CheckOnceAsync_IdenticalState_BroadcastsOnceAndRetainsChangeTimestamp()
    {
        var firstObservation = AvailableState("devenv", 1200, "PiCommandStrip", InitialTime.AddSeconds(1));
        var identicalLaterObservation = firstObservation with
        {
            ObservedAtUtc = InitialTime.AddSeconds(2)
        };
        var provider = new SequenceForegroundWindowProvider(firstObservation, identicalLaterObservation);
        var broadcaster = new RecordingPcStateBroadcaster();
        var stateStore = new ForegroundStateStore(new FixedTimeProvider(InitialTime));
        var monitor = new ForegroundStateMonitor(provider, stateStore, broadcaster);

        await monitor.CheckOnceAsync(CancellationToken.None);
        await monitor.CheckOnceAsync(CancellationToken.None);

        var broadcast = Assert.Single(broadcaster.States);
        Assert.Equal(firstObservation, broadcast);
        Assert.Equal(firstObservation.ObservedAtUtc, stateStore.Current.ObservedAtUtc);
    }

    [Fact]
    public async Task CheckOnceAsync_ChangedWindowTitle_BroadcastsNewState()
    {
        var firstObservation = AvailableState("notepad", 42, "Notes.txt", InitialTime.AddSeconds(1));
        var changedObservation = firstObservation with
        {
            WindowTitle = "Todo.txt",
            ObservedAtUtc = InitialTime.AddSeconds(2)
        };
        var provider = new SequenceForegroundWindowProvider(firstObservation, changedObservation);
        var broadcaster = new RecordingPcStateBroadcaster();
        var monitor = new ForegroundStateMonitor(
            provider,
            new ForegroundStateStore(new FixedTimeProvider(InitialTime)),
            broadcaster);

        await monitor.CheckOnceAsync(CancellationToken.None);
        await monitor.CheckOnceAsync(CancellationToken.None);

        Assert.Equal(2, broadcaster.States.Count);
        Assert.Equal(firstObservation, broadcaster.States[0]);
        Assert.Equal(changedObservation, broadcaster.States[1]);
    }

    [Fact]
    public async Task CheckOnceAsync_ForegroundBecomesUnavailable_BroadcastsClearedState()
    {
        var available = AvailableState("calc", 84, "Calculator", InitialTime.AddSeconds(1));
        var unavailable = ForegroundWindowState.Unavailable(InitialTime.AddSeconds(2));
        var provider = new SequenceForegroundWindowProvider(available, unavailable);
        var broadcaster = new RecordingPcStateBroadcaster();
        var monitor = new ForegroundStateMonitor(
            provider,
            new ForegroundStateStore(new FixedTimeProvider(InitialTime)),
            broadcaster);

        await monitor.CheckOnceAsync(CancellationToken.None);
        await monitor.CheckOnceAsync(CancellationToken.None);

        Assert.Equal(2, broadcaster.States.Count);
        Assert.Equal(unavailable, broadcaster.States[1]);
        Assert.False(broadcaster.States[1].IsAvailable);
        Assert.Null(broadcaster.States[1].ProcessId);
    }

    private static ForegroundWindowState AvailableState(
        string processName,
        int processId,
        string windowTitle,
        DateTimeOffset observedAtUtc) =>
        new(true, processName, processId, windowTitle, observedAtUtc);

    private sealed class SequenceForegroundWindowProvider(params ForegroundWindowState[] states)
        : IForegroundWindowProvider
    {
        private readonly Queue<ForegroundWindowState> _states = new(states);

        public ValueTask<ForegroundWindowState> ObserveAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_states.Dequeue());
        }
    }

    private sealed class RecordingPcStateBroadcaster : IPcStateBroadcaster
    {
        public List<ForegroundWindowState> States { get; } = [];

        public Task BroadcastAsync(ForegroundWindowState state, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            States.Add(state);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
