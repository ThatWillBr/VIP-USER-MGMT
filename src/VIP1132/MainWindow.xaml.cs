using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media.Effects;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using VIP1132.Models;
using VIP1132.Services;

namespace VIP1132;

public partial class MainWindow : Window
{
    private readonly StateService _stateService = new();
    private readonly WindowsUserService _users = new();
    private readonly ZoomService _zoom = new();
    private readonly UpdateService _updates = new();
    private SetupWorkflow _workflow = null!;
    private AppState _state = new();
    private bool _busy;
    private Border? _updateBanner;
    private TextBlock? _updateMessage;
    private Button? _updateInstallButton;
    private Button? _updateLaterButton;
    private readonly DispatcherTimer _progressTimer;
    private double _displayedProgress;
    private double _targetProgress;
    private string? _lastProgressLogStep;
    private double _lastProgressLogPercent = -100;
    private DateTime _lastProgressLogUtc = DateTime.MinValue;

    public MainWindow()
    {
        InitializeComponent();
        FitStartupSizeToWorkArea();
        _progressTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        _progressTimer.Tick += (_, _) => AdvanceProgressDisplay();
        _workflow = new SetupWorkflow(_stateService, _users, _zoom);
        MachineText.Text = Environment.MachineName.ToUpperInvariant();
        AdminStatusText.Text = App.IsAdministrator() ? "Administrator" : "Standard access";
        SourceInitialized += (_, _) => EnableDarkTitleBar();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _state = await _workflow.InitializeStateAsync();
            RefreshState();
            Log("System ready. Recovered workflow state and checked local users.", LogLevel.Success);
            Log("The full setup will only report success after Zoom is visible under the new user's session.", LogLevel.Detail);
        }
        catch (Exception ex)
        {
            Log("Startup check failed: " + ex.Message, LogLevel.Error);
        }

        _ = CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var currentVersion = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            var manifest = await _updates.CheckAsync(currentVersion);
            if (manifest != null)
                ShowUpdateBanner(manifest);
        }
        catch
        {
            // Update checks are helpful, but they should never interrupt setup work.
        }
    }

    private void ShowUpdateBanner(UpdateManifest manifest)
    {
        if (_updateBanner != null)
            return;

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var signal = new System.Windows.Shapes.Ellipse
        {
            Width = 11,
            Height = 11,
            Fill = new SolidColorBrush(Color.FromRgb(82, 246, 255)),
            Margin = new Thickness(0, 0, 13, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect { Color = Color.FromRgb(82, 246, 255), BlurRadius = 15, ShadowDepth = 0, Opacity = 1 }
        };
        Grid.SetColumn(signal, 0);
        content.Children.Add(signal);

        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(manifest.Title) ? $"VIP 1132 v{manifest.Version} is available" : manifest.Title,
            Foreground = Brushes.White,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold
        });
        _updateMessage = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(manifest.Notes) ? "A new update is ready to install." : manifest.Notes,
            Foreground = new SolidColorBrush(Color.FromRgb(163, 188, 211)),
            FontSize = 12,
            Margin = new Thickness(0, 3, 16, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 560
        };
        copy.Children.Add(_updateMessage);
        Grid.SetColumn(copy, 1);
        content.Children.Add(copy);

        _updateInstallButton = CreateUpdateButton(_updates.IsInstalledCopy ? "UPDATE NOW" : "GET UPDATE", true);
        _updateInstallButton.Click += async (_, _) => await InstallAvailableUpdateAsync(manifest);
        Grid.SetColumn(_updateInstallButton, 2);
        content.Children.Add(_updateInstallButton);

        _updateLaterButton = CreateUpdateButton("LATER", false);
        _updateLaterButton.Click += (_, _) =>
        {
            if (_updateBanner != null)
                RootSurface.Children.Remove(_updateBanner);
            _updateBanner = null;
        };
        Grid.SetColumn(_updateLaterButton, 3);
        content.Children.Add(_updateLaterButton);

        _updateBanner = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(248, 6, 13, 26)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(82, 246, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(17, 13, 13, 13),
            Margin = new Thickness(30, 0, 30, 24),
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = content,
            Effect = new DropShadowEffect { Color = Color.FromRgb(82, 246, 255), BlurRadius = 28, ShadowDepth = 0, Opacity = 0.32 }
        };
        Panel.SetZIndex(_updateBanner, 10000);
        RootSurface.Children.Add(_updateBanner);
    }

    private static Button CreateUpdateButton(string text, bool primary)
    {
        return new Button
        {
            Content = text,
            Foreground = primary ? new SolidColorBrush(Color.FromRgb(82, 246, 255)) : new SolidColorBrush(Color.FromRgb(158, 179, 199)),
            Background = new SolidColorBrush(primary ? Color.FromArgb(95, 0, 97, 123) : Color.FromArgb(65, 28, 39, 54)),
            BorderBrush = new SolidColorBrush(primary ? Color.FromRgb(82, 246, 255) : Color.FromRgb(72, 95, 118)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(15, 8, 15, 8),
            Margin = new Thickness(8, 0, 0, 0),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand,
            Effect = primary ? new DropShadowEffect { Color = Color.FromRgb(82, 246, 255), BlurRadius = 14, ShadowDepth = 0, Opacity = 0.35 } : null
        };
    }

    private async Task InstallAvailableUpdateAsync(UpdateManifest manifest)
    {
        if (!_updates.IsInstalledCopy)
        {
            _updates.OpenPortableDownload(manifest);
            return;
        }

        if (_updateInstallButton == null || _updateMessage == null)
            return;

        _updateInstallButton.IsEnabled = false;
        if (_updateLaterButton != null)
            _updateLaterButton.IsEnabled = false;

        try
        {
            var progress = new Progress<int>(percent => _updateMessage.Text = $"Downloading update... {percent}%");
            var installer = await _updates.DownloadInstallerAsync(manifest, progress);
            _updateMessage.Text = "Update ready. Starting installer...";
            Process.Start(new ProcessStartInfo(installer)
            {
                UseShellExecute = true,
                Verb = "runas"
            });
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            _updateMessage.Text = "Update could not start: " + ex.Message;
            _updateInstallButton.IsEnabled = true;
            if (_updateLaterButton != null)
                _updateLaterButton.IsEnabled = true;
        }
    }

    private async void RunAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var highest = await _users.HighestNumericUserAsync() ?? 0;
        var next = Math.Max(highest, _state.CurrentUserNumber ?? 0) + 1;
        var old = _state.CurrentUsername ?? "none";
        var answer = MessageBox.Show(
            $"This will close and clean Zoom, delete managed user {old}, create user {next} (password {next}), " +
            "install Zoom, apply dark mode only, and open Zoom as the new user.\n\nContinue?",
            "Run full VIP 1132 setup",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        ShowDeploymentOverlay(true);
        try
        {
            await RunBusyAsync(async () =>
            {
                ResetProgressDisplay();
                var progress = new Progress<ProgressUpdate>(update =>
                {
                    SetProgressTarget(update.Percent);
                    ProgressText.Text = $"{update.Step} — {update.Message}";
                    DeploymentStepText.Text = update.Step;
                    ActivityStateText.Text = update.Step;
                    LogProgressUpdate(update);
                });

                var outcome = await _workflow.RunFullSetupAsync(_state, progress);
                _state = outcome.State;
                RefreshState();
                ShowDeploymentOverlay(false);
                if (outcome.Result.Success)
                {
                    var suffix = outcome.Result.HasWarnings
                        ? " Zoom is open, but review the yellow warnings in Activity."
                        : " Zoom is open and dark mode was verified.";
                    MessageBox.Show(
                        $"User: {_state.CurrentUsername}\nPassword: {_state.CurrentUsername}\n\n{suffix}",
                        outcome.Result.HasWarnings ? "Setup completed with warnings" : "Setup complete",
                        MessageBoxButton.OK,
                        outcome.Result.HasWarnings ? MessageBoxImage.Warning : MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(outcome.Result.Message, "Setup stopped", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
        }
        finally
        {
            ShowDeploymentOverlay(false);
        }
    }

    private async void CreateUserButton_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            var highest = await _users.HighestNumericUserAsync() ?? 0;
            var next = Math.Max(highest, _state.CurrentUserNumber ?? 0) + 1;
            if (MessageBox.Show($"Create user {next} with password {next}?", "Create next user",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            Log($"Creating Windows user {next}…", LogLevel.Info);
            var result = await _users.CreateAsync(next.ToString(), next.ToString());
            if (!result.Success) throw new InvalidOperationException(result.BestMessage);
            _state.CurrentUserNumber = next;
            _state.LastAttemptUserNumber = next;
            _state.LastSetupStatus = "User created manually";
            await _stateService.SaveAsync(_state);
            RefreshState();
            Log($"User {next} created and added to Administrators.", LogLevel.Success);
        });
    }

    private async void DeleteUserButton_Click(object sender, RoutedEventArgs e)
    {
        if (_state.CurrentUsername is not { } username)
        {
            MessageBox.Show("There is no active managed user to delete.", "Delete user");
            return;
        }
        if (MessageBox.Show($"Delete Windows user {username}?", "Delete active user",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        await RunBusyAsync(async () =>
        {
            await _zoom.StopZoomAsync();
            var result = await _users.DeleteAsync(username);
            if (!result.Success) throw new InvalidOperationException(result.BestMessage);
            _state.CurrentUserNumber = null;
            _state.LastSetupStatus = $"User {username} deleted";
            await _stateService.SaveAsync(_state);
            RefreshState();
            Log($"Windows user {username} deleted.", LogLevel.Success);
        });
    }

    private async void ListUsersButton_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            var users = await _users.ListUsersAsync();
            var text = string.Join(Environment.NewLine, users.OrderBy(x => x));
            Log($"Found {users.Count} local Windows users.", LogLevel.Success);
            MessageBox.Show(text, $"Local users on {Environment.MachineName}", MessageBoxButton.OK, MessageBoxImage.Information);
        });
    }

    private async void DownloadZoomButton_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            ResetProgressDisplay();
            var progress = new Progress<double>(value =>
            {
                SetProgressTarget(value);
                ProgressText.Text = $"Downloading Zoom… {value:0.0}%";
            });
            Log("Downloading the latest 64-bit Zoom MSI from Zoom…", LogLevel.Info);
            await _zoom.DownloadInstallerAsync(progress);
            SetProgressTarget(100);
            Log("Zoom installer saved to Public Downloads.", LogLevel.Success);
        });
    }

    private async void UninstallZoomButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Close Zoom and run Zoom's official CleanZoom removal tool?", "Clean uninstall",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await RunBusyAsync(async () =>
        {
            Log("Closing Zoom processes…", LogLevel.Info);
            await _zoom.StopZoomAsync();
            var result = await _zoom.UninstallCleanlyAsync();
            if (!result.Success) throw new InvalidOperationException(result.Message);
            Log(result.Message, LogLevel.Success);
        });
    }

    private void OpenDownloadsButton_Click(object sender, RoutedEventArgs e) => _zoom.OpenDownloads();

    private void InstructionsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new InstructionsWindow
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private void ProfileDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "General\n• Dark mode only\n\n" +
            "VIP 1132 does not modify Zoom audio, video, meeting, or advanced settings.",
            "Zoom dark mode profile",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            ShowDeploymentOverlay(false);
            Log(ex.Message, LogLevel.Error);
            MessageBox.Show(ex.Message, "VIP 1132", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        RunAllButton.IsEnabled = !busy;
        ActionsPanel.IsEnabled = !busy;
        ActivityStateText.Text = busy ? "Working…" : "Ready";
        SetupProgress.Tag = busy ? "Active" : null;
        DeploymentProgress.Tag = busy && DeploymentOverlay.Visibility == Visibility.Visible ? "Active" : null;
        if (busy && !_progressTimer.IsEnabled)
            _progressTimer.Start();
        else if (!busy && Math.Abs(_targetProgress - _displayedProgress) < 0.01)
            _progressTimer.Stop();
        if (!busy && SetupProgress.Value >= 100)
            ProgressText.Text = "Ready for the next command";
    }

    private void ResetProgressDisplay()
    {
        _displayedProgress = 0;
        _targetProgress = 0;
        ApplyProgressDisplay();
        if (!_progressTimer.IsEnabled)
            _progressTimer.Start();
    }

    private void SetProgressTarget(double percent)
    {
        _targetProgress = Math.Clamp(percent, 0, 100);
        if (_targetProgress < _displayedProgress)
            _displayedProgress = _targetProgress;

        if (_targetProgress >= 100)
        {
            _displayedProgress = 100;
            ApplyProgressDisplay();
            return;
        }

        if (!_progressTimer.IsEnabled)
            _progressTimer.Start();
    }

    private void AdvanceProgressDisplay()
    {
        var remaining = _targetProgress - _displayedProgress;
        if (remaining <= 0.005)
        {
            _displayedProgress = _targetProgress;
            ApplyProgressDisplay();
            if (!_busy)
                _progressTimer.Stop();
            return;
        }

        var step = Math.Clamp(remaining * 0.18, 0.03, 1.25);
        _displayedProgress = Math.Min(_targetProgress, _displayedProgress + step);
        ApplyProgressDisplay();
    }

    private void ApplyProgressDisplay()
    {
        SetupProgress.Value = _displayedProgress;
        DeploymentProgress.Value = _displayedProgress;
        DeploymentPercentText.Text = $"{_displayedProgress:0.0}%";
    }

    private void LogProgressUpdate(ProgressUpdate update)
    {
        var now = DateTime.UtcNow;
        var isDetailedPercentage = update.Level == LogLevel.Detail && update.Message.Contains('%');
        var shouldLog = !isDetailedPercentage
                        || !string.Equals(update.Step, _lastProgressLogStep, StringComparison.Ordinal)
                        || update.Percent - _lastProgressLogPercent >= 5
                        || now - _lastProgressLogUtc >= TimeSpan.FromSeconds(2);
        if (!shouldLog)
            return;

        _lastProgressLogStep = update.Step;
        _lastProgressLogPercent = update.Percent;
        _lastProgressLogUtc = now;
        Log(update.Message, update.Level);
    }

    private void RefreshState()
    {
        ActiveUserText.Text = _state.CurrentUsername is { } user ? $"User {user}" : "None yet";
        LastSetupText.Text = _state.LastSetupStatus;
        SequenceSummaryText.Text = _state.CurrentUsername is { } current
            ? $"Replaces user {current} with the next numeric profile, installs Zoom, applies dark mode, and opens it visibly."
            : "Creates the next numeric profile, installs Zoom, applies dark mode, and opens it visibly.";
    }

    private void Log(string message, LogLevel level)
    {
        var color = level switch
        {
            LogLevel.Success => (Color)ColorConverter.ConvertFromString("#66E0B1"),
            LogLevel.Warning => (Color)ColorConverter.ConvertFromString("#FBCB58"),
            LogLevel.Error => (Color)ColorConverter.ConvertFromString("#FB8B8B"),
            LogLevel.Info => (Color)ColorConverter.ConvertFromString("#76A8FF"),
            _ => (Color)ColorConverter.ConvertFromString("#B7C5D9")
        };
        var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 4) };
        paragraph.Inlines.Add(new Run($"[{DateTime.Now:HH:mm:ss}] ") { Foreground = Brushes.SlateGray });
        paragraph.Inlines.Add(new Run(message) { Foreground = new SolidColorBrush(color) });
        OutputLog.Document.Blocks.Add(paragraph);
        OutputLog.ScrollToEnd();
    }

    private void ShowDeploymentOverlay(bool show)
    {
        if (show)
        {
            ResetProgressDisplay();
            DeploymentStepText.Text = "Initializing systems…";
            DeploymentOverlay.Visibility = Visibility.Visible;
            DeploymentProgress.Tag = "Active";

            var videoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "setup-loop.mp4");
            if (File.Exists(videoPath))
            {
                SetupVideo.Source = new System.Uri(videoPath, System.UriKind.Absolute);
                SetupVideo.Position = TimeSpan.Zero;
                SetupVideo.Play();
            }
        }
        else
        {
            SetupVideo.Stop();
            DeploymentProgress.Tag = null;
            DeploymentOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void SetupVideo_MediaEnded(object sender, RoutedEventArgs e)
    {
        SetupVideo.Position = TimeSpan.Zero;
        SetupVideo.Play();
    }

    private void SetupVideo_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        Log("The deployment animation could not be played; setup will continue normally.", LogLevel.Warning);
    }

    protected override void OnClosed(EventArgs e)
    {
        _progressTimer.Stop();
        SetupVideo.Stop();
        base.OnClosed(e);
    }

    private void EnableDarkTitleBar()
    {
        var enabled = 1;
        DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref enabled, sizeof(int));
    }

    private void FitStartupSizeToWorkArea()
    {
        const double margin = 48;
        var workArea = SystemParameters.WorkArea;
        var maxWidth = Math.Max(MinWidth, workArea.Width - margin);
        var maxHeight = Math.Max(MinHeight, workArea.Height - margin);

        MaxWidth = maxWidth;
        MaxHeight = maxHeight;
        Width = Math.Min(Width, maxWidth);
        Height = Math.Min(Height, maxHeight);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);
}
