namespace FlowShellBar.App.Integrations;

public sealed record FlowtileBarProjection(
    ulong MonitorId,
    string? MonitorBinding,
    FlowtileRectProjection? WorkArea,
    IReadOnlyList<FlowtileWorkspaceProjection> Workspaces,
    int ActiveWorkspaceId,
    string ActiveWindowAppName,
    string ActiveWindowTitle,
    bool IsOverviewOpen);

public sealed record FlowtileRectProjection(
    int X,
    int Y,
    int Width,
    int Height);

public sealed record FlowtileWorkspaceProjection(
    int WorkspaceId,
    int VerticalIndex,
    string Label,
    bool IsActive,
    bool IsOccupied);
