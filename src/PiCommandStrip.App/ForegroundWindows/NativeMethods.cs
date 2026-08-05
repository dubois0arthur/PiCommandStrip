using System.Runtime.InteropServices;
using System.Text;

namespace PiCommandStrip.App.ForegroundWindows;

internal static class NativeMethods
{
    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetWindowTextLength(nint windowHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetWindowText(
        nint windowHandle,
        [Out] StringBuilder text,
        int maximumCharacterCount);
}
