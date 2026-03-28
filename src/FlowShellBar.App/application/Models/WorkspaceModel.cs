namespace FlowShellBar.App.Application.Models;

public sealed record WorkspaceModel(
    int Id,
    string Label,
    bool IsActive,
    bool IsOccupied);
