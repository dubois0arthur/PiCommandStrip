namespace PiCommandStrip.App.AudioMixer;

public interface IDefaultAudioOutputDeviceSwitcher
{
    void SetDefaultOutputDevice(string deviceId);
}
