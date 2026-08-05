using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace PiCommandStrip.App.ForegroundWindows;

public sealed class WindowsForegroundWindowProvider(
    TimeProvider timeProvider,
    ILogger<WindowsForegroundWindowProvider> logger) : IForegroundWindowProvider
{
    private int _unsupportedPlatformWasLogged;

    public ValueTask<ForegroundWindowState> ObserveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var observedAtUtc = timeProvider.GetUtcNow();

        if (!OperatingSystem.IsWindows())
        {
            if (Interlocked.Exchange(ref _unsupportedPlatformWasLogged, 1) == 0)
            {
                logger.LogWarning("Foreground-window detection is unavailable because the host is not Windows");
            }

            return ValueTask.FromResult(ForegroundWindowState.Unavailable(observedAtUtc));
        }

        var windowHandle = NativeMethods.GetForegroundWindow();
        if (windowHandle == nint.Zero)
        {
            return ValueTask.FromResult(ForegroundWindowState.Unavailable(observedAtUtc));
        }

        var threadId = NativeMethods.GetWindowThreadProcessId(windowHandle, out var nativeProcessId);
        if (threadId == 0 || nativeProcessId == 0 || nativeProcessId > int.MaxValue)
        {
            return ValueTask.FromResult(ForegroundWindowState.Unavailable(observedAtUtc));
        }

        var processId = (int)nativeProcessId;

        try
        {
            using var process = Process.GetProcessById(processId);
            var processName = process.ProcessName;
            var windowTitle = ReadWindowTitle(windowHandle);

            return ValueTask.FromResult(new ForegroundWindowState(
                true,
                processName,
                processId,
                windowTitle,
                observedAtUtc));
        }
        catch (ArgumentException exception)
        {
            logger.LogDebug(exception, "Foreground process {ProcessId} exited during inspection", processId);
        }
        catch (InvalidOperationException exception)
        {
            logger.LogDebug(exception, "Foreground process {ProcessId} became unavailable during inspection", processId);
        }
        catch (Win32Exception exception)
        {
            logger.LogDebug(exception, "Foreground process {ProcessId} could not be inspected", processId);
        }
        catch (NotSupportedException exception)
        {
            logger.LogDebug(exception, "Foreground process {ProcessId} does not expose process information", processId);
        }

        return ValueTask.FromResult(ForegroundWindowState.Unavailable(observedAtUtc));
    }

    private static string ReadWindowTitle(nint windowHandle)
    {
        var titleLength = NativeMethods.GetWindowTextLength(windowHandle);
        if (titleLength <= 0)
        {
            return string.Empty;
        }

        var title = new StringBuilder(titleLength + 1);
        var copiedCharacterCount = NativeMethods.GetWindowText(windowHandle, title, title.Capacity);

        return copiedCharacterCount > 0 ? title.ToString() : string.Empty;
    }
}
