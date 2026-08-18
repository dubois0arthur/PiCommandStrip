using PiCommandStrip.App.AudioMixer;

namespace PiCommandStrip.App.PcCommands;

public sealed class AudioCommandHandler(
    string commandId,
    IAudioMixerService audioMixerService) : IPcCommandHandler
{
    public string CommandId { get; } = commandId;

    public static bool IsVolumeCommand(string commandId) => commandId is
        PcCommandIds.AudioSetMasterVolume or
        PcCommandIds.AudioSetApplicationVolume;

    public async Task<PcCommandExecutionResult> ExecuteAsync(
        PcCommandInvocation invocation,
        CancellationToken cancellationToken)
    {
        var result = CommandId switch
        {
            PcCommandIds.AudioSetMasterVolume =>
                await SetMasterVolumeAsync(invocation.Volume, cancellationToken),
            PcCommandIds.AudioSetMasterMute =>
                await SetMasterMuteAsync(invocation.IsMuted, cancellationToken),
            PcCommandIds.AudioSetApplicationVolume =>
                await SetApplicationVolumeAsync(
                    invocation.ApplicationId,
                    invocation.Volume,
                    cancellationToken),
            PcCommandIds.AudioSetApplicationMute =>
                await SetApplicationMuteAsync(
                    invocation.ApplicationId,
                    invocation.IsMuted,
                    cancellationToken),
            PcCommandIds.AudioSetOutputDevice =>
                await SetOutputDeviceAsync(invocation.DeviceId, cancellationToken),
            _ => AudioMixerCommandResult.Failure("The audio command is not supported.")
        };

        return new PcCommandExecutionResult(result.Succeeded, result.Message);
    }

    private Task<AudioMixerCommandResult> SetMasterVolumeAsync(
        float? volume,
        CancellationToken cancellationToken) =>
        IsValidVolume(volume)
            ? audioMixerService.SetMasterVolumeAsync(volume!.Value, cancellationToken)
            : Task.FromResult(AudioMixerCommandResult.Failure(
                "The requested volume must be between 0 and 1."));

    private Task<AudioMixerCommandResult> SetMasterMuteAsync(
        bool? isMuted,
        CancellationToken cancellationToken) =>
        isMuted is not null
            ? audioMixerService.SetMasterMuteAsync(isMuted.Value, cancellationToken)
            : Task.FromResult(AudioMixerCommandResult.Failure(
                "The requested mute state is invalid."));

    private Task<AudioMixerCommandResult> SetApplicationVolumeAsync(
        string? applicationId,
        float? volume,
        CancellationToken cancellationToken)
    {
        if (!IsValidApplicationId(applicationId))
        {
            return Task.FromResult(AudioMixerCommandResult.Failure(
                "The requested audio application is invalid."));
        }

        return IsValidVolume(volume)
            ? audioMixerService.SetApplicationVolumeAsync(
                applicationId!,
                volume!.Value,
                cancellationToken)
            : Task.FromResult(AudioMixerCommandResult.Failure(
                "The requested volume must be between 0 and 1."));
    }

    private Task<AudioMixerCommandResult> SetApplicationMuteAsync(
        string? applicationId,
        bool? isMuted,
        CancellationToken cancellationToken)
    {
        if (!IsValidApplicationId(applicationId))
        {
            return Task.FromResult(AudioMixerCommandResult.Failure(
                "The requested audio application is invalid."));
        }

        return isMuted is not null
            ? audioMixerService.SetApplicationMuteAsync(
                applicationId!,
                isMuted.Value,
                cancellationToken)
            : Task.FromResult(AudioMixerCommandResult.Failure(
                "The requested mute state is invalid."));
    }

    private static bool IsValidVolume(float? volume) =>
        volume is { } value && float.IsFinite(value) && value is >= 0 and <= 1;

    private static bool IsValidApplicationId(string? applicationId) =>
        AudioMixerTargetResolver.IsValidApplicationId(applicationId);

    private Task<AudioMixerCommandResult> SetOutputDeviceAsync(
        string? deviceId,
        CancellationToken cancellationToken) =>
        AudioOutputDeviceTargetResolver.IsValidDeviceIdShape(deviceId)
            ? audioMixerService.SetOutputDeviceAsync(deviceId!, cancellationToken)
            : Task.FromResult(AudioMixerCommandResult.Failure(
                "The requested output device is invalid."));
}
