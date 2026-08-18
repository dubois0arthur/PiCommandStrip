using System.Runtime.InteropServices;

namespace PiCommandStrip.App.AudioMixer;

public sealed class WindowsDefaultAudioOutputDeviceSwitcher
    : IDefaultAudioOutputDeviceSwitcher
{
    public void SetDefaultOutputDevice(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Default audio output selection is available only on Windows.");
        }

        var policyConfig = (IPolicyConfig)(object)new PolicyConfigClient();

        try
        {
            // Windows has separate default roles. PiCommandStrip changes ordinary
            // desktop/system playback but deliberately leaves Communications alone.
            Marshal.ThrowExceptionForHR(policyConfig.SetDefaultEndpoint(
                deviceId,
                AudioDeviceRole.Console));
            Marshal.ThrowExceptionForHR(policyConfig.SetDefaultEndpoint(
                deviceId,
                AudioDeviceRole.Multimedia));
        }
        finally
        {
            if (Marshal.IsComObject(policyConfig))
            {
                Marshal.FinalReleaseComObject(policyConfig);
            }
        }
    }

    private enum AudioDeviceRole
    {
        Console,
        Multimedia,
        Communications
    }

    // Windows provides public Core Audio APIs for discovering endpoints and
    // observing defaults, but not for changing the system default endpoint.
    // PolicyConfig is the private desktop policy interface used by established
    // device switchers. Keeping its vtable here prevents that unsupported detail
    // from leaking into the mixer, protocol, or UI layers.
    [ComImport]
    [Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig]
        int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, nint format);

        [PreserveSig]
        int GetDeviceFormat(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            [MarshalAs(UnmanagedType.Bool)] bool defaultFormat,
            nint format);

        [PreserveSig]
        int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

        [PreserveSig]
        int SetDeviceFormat(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            nint endpointFormat,
            nint mixFormat);

        [PreserveSig]
        int GetProcessingPeriod(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            [MarshalAs(UnmanagedType.Bool)] bool defaultPeriod,
            nint defaultProcessingPeriod,
            nint minimumProcessingPeriod);

        [PreserveSig]
        int SetProcessingPeriod(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            nint processingPeriod);

        [PreserveSig]
        int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, nint mode);

        [PreserveSig]
        int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, nint mode);

        [PreserveSig]
        int GetPropertyValue(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            nint key,
            nint value);

        [PreserveSig]
        int SetPropertyValue(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            nint key,
            nint value);

        [PreserveSig]
        int SetDefaultEndpoint(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            AudioDeviceRole role);

        [PreserveSig]
        int SetEndpointVisibility(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            [MarshalAs(UnmanagedType.Bool)] bool visible);
    }

    [ComImport]
    [Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    private sealed class PolicyConfigClient
    {
    }
}
