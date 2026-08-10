using VIP1132.Models;

namespace VIP1132.Services;

public sealed class SetupWorkflow
{
    private readonly StateService _stateService;
    private readonly WindowsUserService _users;
    private readonly ZoomService _zoom;

    public SetupWorkflow(StateService stateService, WindowsUserService users, ZoomService zoom)
    {
        _stateService = stateService;
        _users = users;
        _zoom = zoom;
    }

    public async Task<AppState> InitializeStateAsync(CancellationToken cancellationToken = default)
    {
        var state = await _stateService.LoadAsync();
        if (state.CurrentUserNumber is null)
        {
            state.CurrentUserNumber = await _users.HighestNumericUserAsync(cancellationToken);
            if (state.CurrentUserNumber is not null)
            {
                state.LastSetupStatus = "Recovered from existing Windows users";
                await _stateService.SaveAsync(state);
            }
        }
        return state;
    }

    public async Task<(AppState State, ZoomProfileReport? Report, OperationResult Result)> RunFullSetupAsync(
        AppState state,
        IProgress<ProgressUpdate> progress,
        CancellationToken cancellationToken = default)
    {
        ZoomProfileReport? report = null;
        Task? installerDownloadTask = null;
        CancellationTokenSource? installerDownloadCts = null;
        var progressSync = new object();
        var lastPercent = 0d;

        void Report(double percent, string step, string message, LogLevel level = LogLevel.Detail)
        {
            lock (progressSync)
            {
                percent = Math.Max(lastPercent, Math.Clamp(percent, 0, 100));
                lastPercent = percent;
            }
            progress.Report(new ProgressUpdate(percent, step, message, level));
        }

        try
        {
            var highest = await _users.HighestNumericUserAsync(cancellationToken) ?? 0;
            var baseline = Math.Max(highest, state.CurrentUserNumber ?? 0);
            var newNumber = baseline + 1;
            while (await _users.ExistsAsync(newNumber.ToString(), cancellationToken))
                newNumber++;
            var username = newNumber.ToString();
            var password = username;

            Report(2.5, "Preflight", $"Preparing Windows user {username}.", LogLevel.Info);
            Report(5.0, "Stop Zoom", "Closing all Zoom processes safely.");
            await _zoom.StopZoomAsync(cancellationToken);

            Report(8.0, "Remove Zoom", "Preparing Zoom's official CleanZoom utility.");
            var cleanProgress = new Progress<double>(p => Report(
                8.0 + p * 0.06,
                "Remove Zoom",
                p >= 100
                    ? "CleanZoom is ready; removing the existing Zoom installation."
                    : $"Downloading CleanZoom… {p:0.0}%"));
            var clean = await _zoom.UninstallCleanlyAsync(cleanProgress, cancellationToken);
            if (!clean.Success) throw new InvalidOperationException(clean.Message);
            Report(23.0, "Remove Zoom", clean.Message, LogLevel.Success);

            var downloadSync = new object();
            var latestDownloadPercent = 0d;
            var publishDownloadProgress = false;
            installerDownloadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var downloadProgress = new Progress<double>(p =>
            {
                var publish = false;
                lock (downloadSync)
                {
                    latestDownloadPercent = p;
                    publish = publishDownloadProgress;
                }

                if (publish)
                    Report(47.0 + p * 0.20, "Download Zoom", $"Preparing Zoom Workplace… {p:0.0}%");
            });
            installerDownloadTask = _zoom.DownloadInstallerAsync(downloadProgress, installerDownloadCts.Token);

            if (state.CurrentUsername is { } oldUsername && await _users.ExistsAsync(oldUsername, cancellationToken))
            {
                Report(26.0, "Old user", $"Deleting Windows account {oldUsername}.");
                var deleted = await _users.DeleteAsync(oldUsername, cancellationToken);
                if (!deleted.Success)
                    Report(33.0, "Old user", "Account deletion warning: " + deleted.BestMessage, LogLevel.Warning);
                else
                    Report(33.0, "Old user", $"Windows account {oldUsername} deleted.", LogLevel.Success);
            }
            else
            {
                Report(33.0, "Old user", "No previous managed account was present.");
            }

            Report(36.0, "New user", $"Creating local administrator {username}.");
            var created = await _users.CreateAsync(username, password, cancellationToken);
            if (!created.Success) throw new InvalidOperationException("Could not create the new Windows user: " + created.BestMessage);

            state.CurrentUserNumber = newNumber;
            state.LastAttemptUserNumber = newNumber;
            state.LastSetupStatus = "User created; Zoom setup in progress";
            await _stateService.SaveAsync(state);
            Report(45.0, "New user", $"User {username} created and added to Administrators.", LogLevel.Success);

            double currentDownloadPercent;
            lock (downloadSync)
            {
                publishDownloadProgress = true;
                currentDownloadPercent = latestDownloadPercent;
            }
            Report(
                47.0 + currentDownloadPercent * 0.20,
                "Download Zoom",
                $"Preparing Zoom Workplace… {currentDownloadPercent:0.0}%");
            await installerDownloadTask;
            Report(68.0, "Download Zoom", "Current 64-bit Zoom MSI is ready.", LogLevel.Success);

            Report(71.0, "Install Zoom", "Installing Zoom for all Windows users.");
            var installed = await _zoom.InstallAsync(cancellationToken);
            if (!installed.Success) throw new InvalidOperationException(installed.Message);
            Report(84.0, "Install Zoom", installed.Message, LogLevel.Success);

            Report(87.0, "Apply dark mode", "Opening Zoom interactively as the new user and applying dark mode only.");
            var reportPath = _stateService.NewHelperReportPath();
            var configured = await _zoom.ConfigureAndLaunchAsUserAsync(username, password, reportPath, cancellationToken);
            report = configured.Report;
            if (!configured.Result.Success) throw new InvalidOperationException(configured.Result.Message);

            if (report is not null)
            {
                foreach (var warning in report.Settings.Where(x =>
                             x.Status is ZoomSettingStatus.Unavailable or ZoomSettingStatus.Failed))
                {
                    Report(97.0, "Apply dark mode",
                        $"{warning.Setting}: {warning.Detail ?? warning.Status.ToString()}", LogLevel.Warning);
                }
            }

            state.LastSetupStatus = configured.Result.HasWarnings ? "Completed with settings warnings" : "Completed";
            await _stateService.SaveAsync(state);
            Report(100.0, "Complete", configured.Result.Message,
                configured.Result.HasWarnings ? LogLevel.Warning : LogLevel.Success);
            return (state, report, configured.Result);
        }
        catch (Exception ex)
        {
            if (installerDownloadCts is not null)
                await installerDownloadCts.CancelAsync();
            if (installerDownloadTask is not null)
            {
                try { await installerDownloadTask; }
                catch { }
            }

            state.LastSetupStatus = "Failed: " + ex.Message;
            await _stateService.SaveAsync(state);
            Report(lastPercent, "Stopped", ex.Message, LogLevel.Error);
            return (state, report, new OperationResult(false, ex.Message));
        }
        finally
        {
            installerDownloadCts?.Dispose();
        }
    }
}
