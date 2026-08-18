using System.Diagnostics;
using System.Threading.Channels;
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
    private const string AudioUnavailableMessage = "Windows audio is currently unavailable.";
    private const string ApplicationUnavailableMessage =
        "That audio application is no longer available.";

    private readonly Dictionary<string, SessionHandle> _sessions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _refreshSignal = new(0, 1);
    private readonly Channel<AudioControlRequest> _controlRequests =
        Channel.CreateBounded<AudioControlRequest>(new BoundedChannelOptions(64)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    private MMDeviceEnumerator? _deviceEnumerator;
    private MMDevice? _outputDevice;
    private AudioEndpointVolume? _endpointVolume;
    private AudioSessionManager? _sessionManager;
    private int _pendingRefreshFlags = (int)RefreshFlags.All;
    private int _acceptsCommands;
    private DateTimeOffset _nextFullSessionRescanUtc = DateTimeOffset.MinValue;

    public AudioState Current => stateStore.Current;

    public Task<AudioMixerCommandResult> SetMasterVolumeAsync(
        float volume,
        CancellationToken cancellationToken) =>
        QueueControlAsync(
            new AudioControlOperation(AudioControlKind.MasterVolume, null, volume, null),
            cancellationToken);

    public Task<AudioMixerCommandResult> SetMasterMuteAsync(
        bool isMuted,
        CancellationToken cancellationToken) =>
        QueueControlAsync(
            new AudioControlOperation(AudioControlKind.MasterMute, null, null, isMuted),
            cancellationToken);

    public Task<AudioMixerCommandResult> SetApplicationVolumeAsync(
        string applicationId,
        float volume,
        CancellationToken cancellationToken) =>
        QueueControlAsync(
            new AudioControlOperation(
                AudioControlKind.ApplicationVolume,
                applicationId,
                volume,
                null),
            cancellationToken);

    public Task<AudioMixerCommandResult> SetApplicationMuteAsync(
        string applicationId,
        bool isMuted,
        CancellationToken cancellationToken) =>
        QueueControlAsync(
            new AudioControlOperation(
                AudioControlKind.ApplicationMute,
                applicationId,
                null,
                isMuted),
            cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            logger.LogWarning("Audio mixer monitoring is unavailable because the host is not Windows");
            return;
        }

        // ASP.NET's worker threads use the MTA required by Core Audio callbacks.
        await Task.Yield();
        Volatile.Write(ref _acceptsCommands, 1);

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
                    if (ProcessPendingControlRequests(normalized))
                    {
                        snapshot = Observe(RefreshFlags.State);
                        normalized = normalizer.Normalize(snapshot, timeProvider.GetUtcNow());
                    }

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
                    FailPendingControlRequests();
                    await PublishUnavailableIfChangedAsync(now, stoppingToken);
                }
            }
        }
        finally
        {
            Volatile.Write(ref _acceptsCommands, 0);
            FailPendingControlRequests();
            DisposeCoreAudioResources();
        }
    }

    private async Task<AudioMixerCommandResult> QueueControlAsync(
        AudioControlOperation operation,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _acceptsCommands) == 0)
        {
            return AudioMixerCommandResult.Failure(AudioUnavailableMessage);
        }

        var request = new AudioControlRequest(operation, cancellationToken);
        if (!_controlRequests.Writer.TryWrite(request))
        {
            return AudioMixerCommandResult.Failure(AudioUnavailableMessage);
        }

        RequestRefresh(RefreshFlags.State);
        return await request.Completion.Task.WaitAsync(cancellationToken);
    }

    private bool ProcessPendingControlRequests(AudioState observedState)
    {
        var processedAny = false;

        while (_controlRequests.Reader.TryRead(out var request))
        {
            processedAny = true;

            if (request.CancellationToken.IsCancellationRequested)
            {
                request.Completion.TrySetCanceled(request.CancellationToken);
                continue;
            }

            try
            {
                request.Completion.TrySetResult(ExecuteControlRequest(
                    request.Operation,
                    observedState));
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Windows rejected an allowlisted audio mixer operation");
                request.Completion.TrySetResult(
                    AudioMixerCommandResult.Failure("The audio change could not be completed."));
            }
        }

        return processedAny;
    }

    private AudioMixerCommandResult ExecuteControlRequest(
        AudioControlOperation operation,
        AudioState observedState)
    {
        if (_endpointVolume is null || !observedState.IsAvailable)
        {
            return AudioMixerCommandResult.Failure(AudioUnavailableMessage);
        }

        return operation.Kind switch
        {
            AudioControlKind.MasterVolume when operation.Volume is { } volume =>
                SetMasterVolume(volume),
            AudioControlKind.MasterMute when operation.IsMuted is { } isMuted =>
                SetMasterMute(isMuted),
            AudioControlKind.ApplicationVolume when
                operation.ApplicationId is { } applicationId &&
                operation.Volume is { } volume =>
                    SetApplicationControl(
                        observedState,
                        applicationId,
                        handle => handle.SetVolume(volume),
                        "Application volume updated."),
            AudioControlKind.ApplicationMute when
                operation.ApplicationId is { } applicationId &&
                operation.IsMuted is { } isMuted =>
                    SetApplicationControl(
                        observedState,
                        applicationId,
                        handle => handle.SetMute(isMuted),
                        isMuted ? "Application muted." : "Application unmuted."),
            _ => AudioMixerCommandResult.Failure("The audio change is invalid.")
        };
    }

    private AudioMixerCommandResult SetMasterVolume(float volume)
    {
        if (!float.IsFinite(volume) || volume is < 0 or > 1 || _endpointVolume is null)
        {
            return AudioMixerCommandResult.Failure("The requested volume is invalid.");
        }

        _endpointVolume.MasterVolumeLevelScalar = volume;
        return AudioMixerCommandResult.Success("Master volume updated.");
    }

    private AudioMixerCommandResult SetMasterMute(bool isMuted)
    {
        if (_endpointVolume is null)
        {
            return AudioMixerCommandResult.Failure(AudioUnavailableMessage);
        }

        _endpointVolume.Mute = isMuted;
        return AudioMixerCommandResult.Success(isMuted
            ? "Master output muted."
            : "Master output unmuted.");
    }

    private AudioMixerCommandResult SetApplicationControl(
        AudioState observedState,
        string applicationId,
        Action<SessionHandle> apply,
        string successMessage)
    {
        var application = AudioMixerTargetResolver.ResolveApplication(
            observedState,
            applicationId);
        if (application is null)
        {
            return AudioMixerCommandResult.Failure(ApplicationUnavailableMessage);
        }

        var updatedSessionCount = 0;
        foreach (var sessionInstanceId in application.SessionInstanceIds)
        {
            if (!_sessions.TryGetValue(sessionInstanceId, out var session))
            {
                continue;
            }

            try
            {
                apply(session);
                updatedSessionCount++;
            }
            catch (Exception exception)
            {
                logger.LogDebug(
                    exception,
                    "An audio session disappeared while applying a mixer operation");
            }
        }

        return updatedSessionCount > 0
            ? AudioMixerCommandResult.Success(successMessage)
            : AudioMixerCommandResult.Failure(ApplicationUnavailableMessage);
    }

    private void FailPendingControlRequests()
    {
        while (_controlRequests.Reader.TryRead(out var request))
        {
            request.Completion.TrySetResult(
                AudioMixerCommandResult.Failure(AudioUnavailableMessage));
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

    private enum AudioControlKind
    {
        MasterVolume,
        MasterMute,
        ApplicationVolume,
        ApplicationMute
    }

    private sealed record AudioControlOperation(
        AudioControlKind Kind,
        string? ApplicationId,
        float? Volume,
        bool? IsMuted);

    private sealed class AudioControlRequest(
        AudioControlOperation operation,
        CancellationToken cancellationToken)
    {
        public AudioControlOperation Operation { get; } = operation;

        public CancellationToken CancellationToken { get; } = cancellationToken;

        public TaskCompletionSource<AudioMixerCommandResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
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

        public void SetVolume(float volume) =>
            _control.SimpleAudioVolume.Volume = volume;

        public void SetMute(bool isMuted) =>
            _control.SimpleAudioVolume.Mute = isMuted;

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
