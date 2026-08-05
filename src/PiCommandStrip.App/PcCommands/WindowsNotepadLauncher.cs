using System.Diagnostics;

namespace PiCommandStrip.App.PcCommands;

public sealed class WindowsNotepadLauncher : INotepadLauncher
{
    public void Launch()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Notepad can only be opened on Windows.");
        }

        var notepadPath = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        var startInfo = new ProcessStartInfo
        {
            FileName = notepadPath,
            UseShellExecute = false
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows did not start Notepad.");
    }
}
