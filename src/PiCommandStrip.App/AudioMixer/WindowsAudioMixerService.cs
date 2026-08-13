using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace PiCommandStrip.App.AudioMixer;

public sealed class WindowsAudioMixerService(
    AudioStateStore stateStore,
    AudioStateNormalizer normalizer,
    IAudioStateBroadcaster broadcaster,
    TimeProvider timeProvider,
    ILogger<WindowsAudioMixerService> logger) : BackgroundService, IAudioMixerService
{
    private static readonly TimeSpan FallbackPollingInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FullSessionRescanInterval = TimeSpan.FromSeconds(15);

    private readonly Dictionary<string, SessionHandle> _sessions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _refreshSignal = new(0, 1);
    private MMDeviceEnumerator? _deviceEnumerator;
    private MMDevice? _outputDevice;
    private AudioEndpointVolume? _endpointVolume;
    private AudioSessionManager? _sessionManager;
    private int _pendingRefreshFlags = (int)RefreshFlags.All;
    private DateTimeOffset _nextFullSessionRescanUtc = DateTimeOffset.MinValue;

    public AudioState Current => stateStore.Current;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            logger.LogWarning("Audio mixer monitoring is unavailable because the host is not Windows");
            return;
        }

        // ASP.NET's worker threads use the MTA required by Core Audio callbacks.
        await Task.Yield();

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var flags = (RefreshFlags)Interlocked.Exchange(ref _pendingRefreshFlags, 0);
                if (flags is RefreshFlags.None)
                {
                    var signaled = await _refreshSignal.WaitAsync(
                        FallbackPollingInterval,
                        stoppingToken);
                    flags = (RefreshFlags)Interlocked.Exchange(ref _pendingRefreshFlags, 0);
                    if (!signaled)
                    {
                        flags |= RefreshFlags.Device | RefreshFlags.State;
                    }
                }

                var now = timeProvider.GetUtcNow();
                if (now >= _nextFullSessionRescanUtc)
                {
                    flags |= RefreshFlags.Sessions;
                    _nextFullSessionRescanUtc = now + FullSessionRescanInterval;
                }

                try
                {
                    var snapshot = Observe(flags);
                    var normalized = normalizer.Normalize(snapshot, now);
                    if (stateStore.TryUpdate(normalized, out var changedState))
                    {
                        await broadcaster.BroadcastAsync(changedState, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.LogWarning(
                        exception,
                        "Windows audio mixer observation failed; monitoring will retry");
                    await PublishUnavailableIfChangedAsync(now, stoppingToken);
                }
            }
        }
        finally
        {
            DisposeCoreAudioResources();
        }
    }

    private AudioMixerSnapshot Observe(RefreshFlags flags)
    {
        _deviceEnumerator ??= new MMDeviceEnumerator();

        if ((flags & RefreshFlags.Device) != 0 || _outputDevice is null)
        {
            RefreshOutputDevice();
        }

        if (_outputDevice is null || _endpointVolume is null || _sessionManager is null)
        {
            return new AudioMixerSnapshot(null, []);
        }

        if ((flags & RefreshFlags.Sessions) != 0)
        {
            ReconcileSessions();
        }

        var sessionSnapshots = new List<AudioSessionSnapshot>(_sessions.Count);
        var expiredSessionIds = new List<string>();

        foreach (var (sessionId, handle) in _sessions)
        {
            try
            {
                var snapshot = handle.ReadSnapshot();
                sessionSnapshots.Add(snapshot);
                if (snapshot.State is AudioSessionStatus.Expired)
                {
                    expiredSessionIds.Add(sessionId);
                }
            }
            catch (Exception exception)
            {
                logger.LogDebug(
                    exception,
                    "An audio session disappeared while its state was being read");
            }
        }

        foreach (var sessionId in expiredSessionIds)
        {
            RemoveSession(sessionId);
        }

        var outputSnapshot = new AudioOutputDeviceSnapshot(
            _outputDevice.ID,
            _outputDevice.FriendlyName,
            _endpointVolume.MasterVolumeLevelScalar,
            _endpointVolume.Mute);
        return new AudioMixerSnapshot(outputSnapshot, sessionSnapshots);
    }

    private void RefreshOutputDevice()
    {
        if (_deviceEnumerator is null)
        {
            return;
        }

        if (!_deviceEnumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
        {
            DisposeOutputDevice();
            return;
        }

        MMDevice? candidate = _deviceEnumerator.GetDefaultAudioEndpoint(
            DataFlow.Render,
            Role.Multimedia);
        var transferred = false;

        try
        {
            if (_outputDevice is not null &&
                _endpointVolume is not null &&
                _sessionManager is not null &&
                string.Equals(_outputDevice.ID, candidate.ID, StringComparison.Ordinal))
            {
                return;
            }

            DisposeOutputDevice();
            _outputDevice = candidate;
            transferred = true;
            _endpointVolume = candidate.AudioEndpointVolume;
            _endpointVolume.OnVolumeNotification += HandleEndpointVolumeChanged;
            _sessionManager = candidate.AudioSessionManager;
            _sessionManager.OnSessionCreated += HandleSessionCreated;
            ReconcileSessions();
            _nextFullSessionRescanUtc = timeProvider.GetUtcNow() + FullSessionRescanInterval;

            logger.LogInformation(
                "Monitoring Windows audio output device {AudioDeviceName}",
                candidate.FriendlyName);
        }
        finally
        {
            if (!transferred)
            {
                candidate.Dispose();
            }
        }
    }

    private void ReconcileSessions()
    {
        if (_sessionManager is null)
        {
            return;
        }

        _sessionManager.RefreshSessions();
        var collection = _sessionManager.Sessions;
        var liveSessionIds = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < collection.Count; index++)
        {
            AudioSessionControl? control = null;

            try
            {
                control = collection[index];
                var sessionId = CreateSessionKey(control, index);
                while (!liveSessionIds.Add(sessionId))
                {
                    sessionId = $"{sessionId}#{index}";
                }

                if (_sessions.ContainsKey(sessionId))
                {
                    control.Dispose();
                    continue;
                }

                var handle = new SessionHandle(
                    sessionId,
                    control,
                    RequestRefresh,
                    logger);
                control = null;
                _sessions.Add(sessionId, handle);
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "An audio session could not be registered");
                control?.Dispose();
            }
        }

        foreach (var staleSessionId in _sessions.Keys
                     .Where(sessionId => !liveSessionIds.Contains(sessionId))
                     .ToArray())
        {
            RemoveSession(staleSessionId);
        }
    }

    private static string CreateSessionKey(AudioSessionControl control, int index)
    {
        var instanceId = TryRead(() => control.GetSessionInstanceIdentifier);
        if (!string.IsNullOrWhiteSpace(instanceId))
        {
            return instanceId.Trim();
        }

        var sessionId = TryRead(() => control.GetSessionIdentifier);
        var processId = TryRead(() => control.GetProcessID);
        return !string.IsNullOrWhiteSpace(sessionId)
            ? $"{sessionId.Trim()}:{processId}"
            : $"unidentified:{processId}:{index}";
    }

    private async Task PublishUnavailableIfChangedAsync(
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken)
    {
        var unavailable = AudioState.Unavailable(observedAtUtc);
        if (!stateStore.TryUpdate(unavailable, out var changedState))
        {
            return;
        }

        try
        {
            await broadcaster.BroadcastAsync(changedState, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host shutdown owns cancellation.
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Could not broadcast unavailable audio state");
        }
    }

    private void HandleEndpointVolumeChanged(AudioVolumeNotificationData _) =>
        RequestRefresh(RefreshFlags.State);

    private void HandleSessionCreated(object _, IAudioSessionControl __) =>
        RequestRefresh(RefreshFlags.Sessions | RefreshFlags.State);

    private void RequestRefresh(RefreshFlags flags)
    {
        Interlocked.Or(ref _pendingRefreshFlags, (int)flags);
        try
        {
            _refreshSignal.Release();
        }
        catch (SemaphoreFullException)
        {
            // A pending wake-up already covers these atomically combined flags.
        }
    }

    private void RemoveSession(string sessionId)
    {
        if (!_sessions.Remove(sessionId, out var session))
        {
            return;
        }

        session.Dispose();
    }

    private void DisposeOutputDevice()
    {
        foreach (var session in _sessions.Values)
        {
            session.Dispose();
        }

        _sessions.Clear();

        if (_sessionManager is not null)
        {
            _sessionManager.OnSessionCreated -= HandleSessionCreated;
        }

        if (_endpointVolume is not null)
        {
            _endpointVolume.OnVolumeNotification -= HandleEndpointVolumeChanged;
        }

        try
        {
            _outputDevice?.Dispose();
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Windows audio output resources did not close cleanly");
        }

        _sessionManager = null;
        _endpointVolume = null;
        _outputDevice = null;
    }

    private void DisposeCoreAudioResources()
    {
        DisposeOutputDevice();

        try
        {
            _deviceEnumerator?.Dispose();
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Windows audio device enumeration did not close cleanly");
        }

        _deviceEnumerator = null;
    }

    private static T? TryRead<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return default;
        }
    }

    [Flags]
    private enum RefreshFlags
    {
        None = 0,
        Device = 1,
        Sessions = 2,
        State = 4,
        All = Device | Sessions | State
    }

    private sealed class SessionHandle : IDisposable
    {
        private readonly AudioSessionControl _control;
        private readonly SessionEventSink _eventSink;
        private readonly ILogger _logger;
        private bool _registered;

        public SessionHandle(
            string sessionInstanceId,
            AudioSessionControl control,
            Action<RefreshFlags> requestRefresh,
            ILogger logger)
        {
            SessionInstanceId = sessionInstanceId;
            _control = control;
            _eventSink = new SessionEventSink(requestRefresh);
            _logger = logger;

            try
            {
                _control.RegisterEventClient(_eventSink);
                _registered = true;
            }
            catch (Exception exception)
            {
                logger.LogDebug(
                    exception,
                    "An audio session will use fallback polling because notifications were unavailable");
            }
        }

        public string SessionInstanceId { get; }

        public AudioSessionSnapshot ReadSnapshot()
        {
            var processId = ReadProcessId();
            return new AudioSessionSnapshot(
                SessionInstanceId,
                TryRead(() => _control.GetSessionIdentifier),
                TryRead(() => _control.GetGroupingParam()),
                processId,
                ReadProcessName(processId),
                TryRead(() => _control.DisplayName),
                TryRead(() => _control.SimpleAudioVolume.Volume),
                TryRead(() => _control.SimpleAudioVolume.Mute),
                MapSessionState(TryRead(() => _control.State)),
                TryRead(() => _control.IsSystemSoundsSession));
        }

        public void Dispose()
        {
            try
            {
                if (_registered)
                {
                    _control.UnRegisterEventClient(_eventSink);
                    _registered = false;
                }

                _control.Dispose();
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "An audio session resource did not close cleanly");
            }
        }

        private int? ReadProcessId()
        {
            var nativeProcessId = TryRead(() => _control.GetProcessID);
            return nativeProcessId is > 0 and <= int.MaxValue
                ? (int)nativeProcessId
                : null;
        }

        private static string? ReadProcessName(int? processId)
        {
            if (processId is null)
            {
                return null;
            }

            try
            {
                using var process = Process.GetProcessById(processId.Value);
                return process.ProcessName;
            }
            catch (Exception exception) when (exception is
                ArgumentException or
                InvalidOperationException or
                System.ComponentModel.Win32Exception or
                NotSupportedException)
            {
                return null;
            }
        }

        private static AudioSessionStatus MapSessionState(AudioSessionState state) => state switch
        {
            AudioSessionState.AudioSessionStateActive => AudioSessionStatus.Active,
            AudioSessionState.AudioSessionStateInactive => AudioSessionStatus.Inactive,
            AudioSessionState.AudioSessionStateExpired => AudioSessionStatus.Expired,
            _ => AudioSessionStatus.Unknown
        };
    }

    private sealed class SessionEventSink(Action<RefreshFlags> requestRefresh)
        : IAudioSessionEventsHandler
    {
        public void OnVolumeChanged(float volume, bool isMuted) =>
            requestRefresh(RefreshFlags.State);

        public void OnDisplayNameChanged(string displayName) =>
            requestRefresh(RefreshFlags.State);

        public void OnIconPathChanged(string iconPath)
        {
        }

        public void OnChannelVolumeChanged(
            uint channelCount,
            nint newVolumes,
            uint channelIndex) =>
            requestRefresh(RefreshFlags.State);

        public void OnGroupingParamChanged(ref Guid groupingId) =>
            requestRefresh(RefreshFlags.State);

        public void OnStateChanged(AudioSessionState state) =>
            requestRefresh(RefreshFlags.State);

        public void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason) =>
            requestRefresh(RefreshFlags.Sessions | RefreshFlags.State);
    }
}
