using FlowShellBar.App.Application.Actions;
using FlowShellBar.App.Application.ViewModels;
using FlowShellBar.App.Diagnostics;

namespace FlowShellBar.App.Host;

public sealed record BootstrapContext(
    BarViewModel ViewModel,
    IBarActionDispatcher ActionDispatcher,
    IAppLogger Logger);
