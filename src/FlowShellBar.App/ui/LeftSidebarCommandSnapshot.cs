namespace FlowShellBar.App.Ui;

internal sealed record LeftSidebarCommandSnapshot(
    string Mode,
    bool IsOpen,
    bool IsAttached,
    bool IsDetached,
    bool IsPinned);
