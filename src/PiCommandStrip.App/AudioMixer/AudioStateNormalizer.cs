using System.Security.Cryptography;
using System.Text;

namespace PiCommandStrip.App.AudioMixer;

public sealed class AudioStateNormalizer
{
    private const float MixedVolumeTolerance = 0.005f;

    public AudioState Normalize(
        AudioMixerSnapshot? snapshot,
        DateTimeOffset lastUpdatedUtc)
    {
        if (snapshot?.OutputDevice is null ||
            string.IsNullOrWhiteSpace(snapshot.OutputDevice.DeviceId))
        {
            return AudioState.Unavailable(lastUpdatedUtc);
        }

        var output = new AudioOutputDeviceState(
            snapshot.OutputDevice.DeviceId.Trim(),
            NormalizeText(snapshot.OutputDevice.FriendlyName) ?? "Unknown output device",
            NormalizeVolume(snapshot.OutputDevice.Volume),
            snapshot.OutputDevice.IsMuted);

        var applications = snapshot.Sessions
            .Select((session, index) => new IndexedSession(session, index))
            .Where(item =>
                !item.Session.IsSystemSoundsSession &&
                item.Session.State is not AudioSessionStatus.Expired)
            .GroupBy(item => CreateGroupKey(item.Session, item.Index), StringComparer.Ordinal)
            .Select(group => NormalizeGroup(group.Key, group.Select(item => item.Session).ToArray()))
            .OrderBy(application => application.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(application => application.ApplicationId, StringComparer.Ordinal)
            .ToArray();

        return new AudioState(true, output, applications, 0, lastUpdatedUtc);
    }

    private static ApplicationAudioState NormalizeGroup(
        string groupKey,
        IReadOnlyList<AudioSessionSnapshot> sessions)
    {
        var processIds = sessions
            .Where(session => session.ProcessId is > 0)
            .Select(session => session.ProcessId!.Value)
            .Distinct()
            .Order()
            .ToArray();
        var processNames = sessions
            .Select(session => NormalizeProcessName(session.ProcessName))
            .Where(name => name is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var processName = processNames.Length == 1 ? processNames[0] : null;
        var displayName = sessions
            .Select(session => NormalizeText(session.DisplayName))
            .Where(name => name is not null)
            .OrderBy(name => name!.Length)
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()
            ?? HumanizeProcessName(processName)
            ?? "Unknown audio session";
        var volumes = sessions
            .Select(session => NormalizeVolume(session.Volume))
            .ToArray();
        var minimumVolume = volumes.Min();
        var maximumVolume = volumes.Max();
        var mutedCount = sessions.Count(session => session.IsMuted);
        var sessionInstanceIds = sessions
            .Select((session, index) =>
                NormalizeText(session.SessionInstanceId) ?? $"unidentified:{index}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        return new ApplicationAudioState(
            CreateApplicationId(groupKey),
            processIds,
            processName,
            displayName,
            maximumVolume,
            mutedCount == sessions.Count,
            NormalizeState(sessions),
            sessions.Count,
            maximumVolume - minimumVolume > MixedVolumeTolerance,
            mutedCount > 0 && mutedCount < sessions.Count,
            sessionInstanceIds);
    }

    private static string CreateGroupKey(AudioSessionSnapshot session, int index)
    {
        var processName = NormalizeProcessName(session.ProcessName);
        if (processName is not null)
        {
            return $"process:{processName.ToLowerInvariant()}";
        }

        if (session.GroupingId is { } groupingId && groupingId != Guid.Empty)
        {
            return $"group:{groupingId:D}";
        }

        var sessionIdentifier = NormalizeText(session.SessionIdentifier);
        if (sessionIdentifier is not null)
        {
            return $"session:{sessionIdentifier}";
        }

        var instanceIdentifier = NormalizeText(session.SessionInstanceId);
        return instanceIdentifier is not null
            ? $"instance:{instanceIdentifier}"
            : $"unidentified:{index}";
    }

    private static string CreateApplicationId(string groupKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(groupKey)))
            .ToLowerInvariant();

    private static string NormalizeState(IReadOnlyList<AudioSessionSnapshot> sessions)
    {
        if (sessions.Any(session => session.State is AudioSessionStatus.Active))
        {
            return AudioSessionStates.Active;
        }

        return sessions.Any(session => session.State is AudioSessionStatus.Inactive)
            ? AudioSessionStates.Inactive
            : AudioSessionStates.Unknown;
    }

    private static float NormalizeVolume(float volume)
    {
        if (!float.IsFinite(volume))
        {
            return 0;
        }

        return MathF.Round(Math.Clamp(volume, 0, 1), 3);
    }

    private static string? NormalizeProcessName(string? value)
    {
        var normalized = NormalizeText(value);
        if (normalized is null)
        {
            return null;
        }

        if (normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        return normalized.Any(char.IsLetterOrDigit)
            ? normalized.ToLowerInvariant()
            : null;
    }

    private static string? HumanizeProcessName(string? processName)
    {
        if (processName is null)
        {
            return null;
        }

        return processName.Length == 1
            ? processName.ToUpperInvariant()
            : $"{char.ToUpperInvariant(processName[0])}{processName[1..]}";
    }

    private static string? NormalizeText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record IndexedSession(AudioSessionSnapshot Session, int Index);
}
