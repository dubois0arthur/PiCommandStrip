using PiCommandStrip.App.ForegroundWindows;

namespace PiCommandStrip.App.Contexts;

public sealed record ContextSignals(ForegroundWindowState ForegroundWindow);

public sealed record ContextResolution(
    ContextProfile Profile,
    string Source,
    string Trigger);

public interface IContextResolver
{
    ContextResolution Resolve(ContextSignals signals);
}
