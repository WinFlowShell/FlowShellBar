namespace FlowShellBar.App.Application.Models;

public enum BarSurfacePlacementSource
{
    Fallback = 0,
    FlowtileWm = 1,
}

public sealed record BarSurfacePlacementModel(
    BarSurfacePlacementSource Source,
    ulong? MonitorId,
    string? MonitorBinding,
    int WorkAreaX,
    int WorkAreaY,
    int WorkAreaWidth,
    int WorkAreaHeight)
{
    public static BarSurfacePlacementModel Fallback { get; } = new(
        BarSurfacePlacementSource.Fallback,
        null,
        null,
        0,
        0,
        0,
        0);

    public bool IsFlowtileBound => Source == BarSurfacePlacementSource.FlowtileWm;
}
