using System.Collections.ObjectModel;
using System.Windows.Input;

using FlowShellBar.App.Application.Actions;
using FlowShellBar.App.Application.Models;
using FlowShellBar.App.Diagnostics;
using FlowShellBar.App.Integrations;

using Microsoft.UI.Dispatching;

namespace FlowShellBar.App.Application.ViewModels;

public sealed class BarViewModel : BindableBase
{
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly IBarActionDispatcher _actionDispatcher;
    private readonly IAppLogger _logger;

    private string _activeWindowAppName = string.Empty;
    private string _activeWindowTitle = string.Empty;
    private string _currentTime = string.Empty;
    private string _currentDate = string.Empty;
    private string _runtimeModeLabel = string.Empty;
    private bool _isNetworkAvailable;
    private bool _isAudioAvailable;
    private bool _hasNotifications;

    public BarViewModel(
        DispatcherQueue dispatcherQueue,
        IBarModelProvider barModelProvider,
        IBarActionDispatcher actionDispatcher,
        IAppLogger logger)
    {
        _dispatcherQueue = dispatcherQueue;
        _actionDispatcher = actionDispatcher;
        _logger = logger;

        Workspaces = [];

        OpenLauncherCommand = new AsyncDelegateCommand(_ => _actionDispatcher.DispatchAsync(new BarActionRequest(BarActionKind.OpenLauncher)));
        OpenStatusPanelCommand = new AsyncDelegateCommand(_ => _actionDispatcher.DispatchAsync(new BarActionRequest(BarActionKind.OpenStatusPanel)));
        ToggleOverviewCommand = new AsyncDelegateCommand(_ => _actionDispatcher.DispatchAsync(new BarActionRequest(BarActionKind.ToggleOverview)));
        SwitchWorkspaceCommand = new AsyncDelegateCommand(async parameter =>
        {
            if (TryGetWorkspaceId(parameter, out var workspaceId))
            {
                await _actionDispatcher.DispatchAsync(new BarActionRequest(BarActionKind.SwitchWorkspace, WorkspaceId: workspaceId));
            }
        });

        ApplyModel(barModelProvider.GetCurrentModel());
        barModelProvider.ModelChanged += OnModelChanged;
    }

    public ObservableCollection<WorkspaceItemViewModel> Workspaces { get; }

    public ICommand OpenLauncherCommand { get; }

    public ICommand OpenStatusPanelCommand { get; }

    public ICommand ToggleOverviewCommand { get; }

    public ICommand SwitchWorkspaceCommand { get; }

    public string ActiveWindowAppName
    {
        get => _activeWindowAppName;
        private set => SetProperty(ref _activeWindowAppName, value);
    }

    public string ActiveWindowTitle
    {
        get => _activeWindowTitle;
        private set => SetProperty(ref _activeWindowTitle, value);
    }

    public string CurrentTime
    {
        get => _currentTime;
        private set => SetProperty(ref _currentTime, value);
    }

    public string CurrentDate
    {
        get => _currentDate;
        private set => SetProperty(ref _currentDate, value);
    }

    public string RuntimeModeLabel
    {
        get => _runtimeModeLabel;
        private set => SetProperty(ref _runtimeModeLabel, value);
    }

    public bool IsNetworkAvailable
    {
        get => _isNetworkAvailable;
        private set => SetProperty(ref _isNetworkAvailable, value);
    }

    public bool IsAudioAvailable
    {
        get => _isAudioAvailable;
        private set => SetProperty(ref _isAudioAvailable, value);
    }

    public bool HasNotifications
    {
        get => _hasNotifications;
        private set => SetProperty(ref _hasNotifications, value);
    }

    private void OnModelChanged(object? sender, BarModel model)
    {
        if (_dispatcherQueue.HasThreadAccess)
        {
            ApplyModel(model);
            return;
        }

        _dispatcherQueue.TryEnqueue(() => ApplyModel(model));
    }

    private void ApplyModel(BarModel model)
    {
        RuntimeModeLabel = model.RuntimeMode.ToString().ToUpperInvariant();
        ActiveWindowAppName = model.ActiveWindowAppName;
        ActiveWindowTitle = model.ActiveWindowTitle;
        CurrentTime = model.CurrentTime;
        CurrentDate = model.CurrentDate;
        IsNetworkAvailable = model.StatusCluster.IsNetworkAvailable;
        IsAudioAvailable = model.StatusCluster.IsAudioAvailable;
        HasNotifications = model.StatusCluster.HasNotifications;

        SyncWorkspaces(model.Workspaces);
        _logger.Info($"Bar model applied. Active workspace: {model.Workspaces.FirstOrDefault(x => x.IsActive)?.Id ?? -1}.");
    }

    private void SyncWorkspaces(IReadOnlyList<WorkspaceModel> workspaces)
    {
        while (Workspaces.Count < workspaces.Count)
        {
            Workspaces.Add(new WorkspaceItemViewModel());
        }

        while (Workspaces.Count > workspaces.Count)
        {
            Workspaces.RemoveAt(Workspaces.Count - 1);
        }

        for (var index = 0; index < workspaces.Count; index++)
        {
            var source = workspaces[index];
            var target = Workspaces[index];

            target.Id = source.Id;
            target.Label = source.Label;
            target.IsActive = source.IsActive;
            target.IsOccupied = source.IsOccupied;
        }
    }

    private static bool TryGetWorkspaceId(object? parameter, out int workspaceId)
    {
        switch (parameter)
        {
            case int value:
                workspaceId = value;
                return true;

            case string text when int.TryParse(text, out var parsed):
                workspaceId = parsed;
                return true;

            default:
                workspaceId = 0;
                return false;
        }
    }
}
