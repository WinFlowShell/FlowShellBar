using Microsoft.UI.Xaml.Media;

namespace FlowShellBar.App.Application.ViewModels;

public sealed class BarStatusSectionViewModel : BindableBase
{
    private string _runtimeModeLabel = string.Empty;
    private string _connectionStateLabel = string.Empty;
    private bool _isNetworkAvailable;
    private bool _isAudioAvailable;
    private bool _hasNotifications;
    private Brush? _connectionStateBackground;
    private Brush? _connectionStateBorderBrush;
    private Brush? _connectionStateForeground;

    public string RuntimeModeLabel
    {
        get => _runtimeModeLabel;
        set
        {
            if (SetProperty(ref _runtimeModeLabel, value))
            {
                OnPropertyChanged(nameof(RuntimeModeShortLabel));
            }
        }
    }

    public string RuntimeModeShortLabel => RuntimeModeLabel switch
    {
        "SESSION" => "S",
        "STANDALONE" => "O",
        _ => RuntimeModeLabel.Length > 0 ? RuntimeModeLabel[..1] : string.Empty,
    };

    public string ConnectionStateLabel
    {
        get => _connectionStateLabel;
        set => SetProperty(ref _connectionStateLabel, value);
    }

    public bool IsNetworkAvailable
    {
        get => _isNetworkAvailable;
        set => SetProperty(ref _isNetworkAvailable, value);
    }

    public bool IsAudioAvailable
    {
        get => _isAudioAvailable;
        set => SetProperty(ref _isAudioAvailable, value);
    }

    public bool HasNotifications
    {
        get => _hasNotifications;
        set => SetProperty(ref _hasNotifications, value);
    }

    public Brush? ConnectionStateBackground
    {
        get => _connectionStateBackground;
        set => SetProperty(ref _connectionStateBackground, value);
    }

    public Brush? ConnectionStateBorderBrush
    {
        get => _connectionStateBorderBrush;
        set => SetProperty(ref _connectionStateBorderBrush, value);
    }

    public Brush? ConnectionStateForeground
    {
        get => _connectionStateForeground;
        set => SetProperty(ref _connectionStateForeground, value);
    }
}
