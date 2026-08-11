using System.Diagnostics;
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

    public async Task<(AppState State, OperationResult Result)> RunFullSetupAsync(
        AppState state,
        IProgress<ProgressUpdate> progress,
        CancellationToken cancellationToken = default)
    {
        Task? installerDownloadTask = null;
        CancellationTokenSource? installerDownloadCts = null;
        var progressSync = new object();
        var lastPercent = 0d;
        var totalTimer = Stopwatch.StartNew();

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
            var localUsers = await _users.ListUsersAsync(cancellationToken);
            var knownUsers = localUsers.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var highest = localUsers
                .Select(user => int.TryParse(user, out var number) ? number : 0)
                .DefaultIfEmpty()
                .Max();
            var baseline = Math.Max(highest, state.CurrentUserNumber ?? 0);
            var newNumber = baseline + 1;
            while (knownUsers.Contains(newNumber.ToString()))
                newNumber++;
            var username = newNumber.ToString();
            var password = username;

            Report(2.5, "Preflight", $"Preparing Windows user {username}.", LogLevel.Info);

            var downloadSync = new object();
            var latestDownloadPercent = 0d;
            var publishDownloadProgress = false;
            var downloadTimer = Stopwatch.StartNew();
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
                    Report(50.0 + p * 0.20, "Download Zoom", $"Preparing Zoom Workplace… {p:0.0}%");
            });
            installerDownloadTask = _zoom.DownloadInstallerAsync(downloadProgress, installerDownloadCts.Token);

            var phaseTimer = Stopwatch.StartNew();
            Report(5.0, "Stop Zoom", "Closing all Zoom processes safely.");
            await _zoom.StopZoomAsync(cancellationToken);
            Report(7.0, "Stop Zoom", $"Zoom processes closed in {FormatDuration(phaseTimer.Elapsed)}.", LogLevel.Success);

            phaseTimer.Restart();
            Report(8.0, "Remove Zoom", "Preparing Zoom's official CleanZoom utility.");
            var cleanProgress = new Progress<double>(p => Report(
                8.0 + p * 0.12,
                "Remove Zoom",
                p >= 100
                    ? "CleanZoom is ready; removing the existing Zoom installation."
                    : $"Downloading CleanZoom… {p:0.0}%"));
            var clean = await _zoom.UninstallCleanlyAsync(cleanProgress, cancellationToken);
            if (!clean.Success) throw new InvalidOperationException(clean.Message);
            Report(25.0, "Remove Zoom", $"{clean.Message} ({FormatDuration(phaseTimer.Elapsed)})", LogLevel.Success);

            // Surface an early background download failure before rotating Windows accounts.
            if (installerDownloadTask.IsCompleted)
                await installerDownloadTask;

            if (state.CurrentUsername is { } oldUsername && knownUsers.Contains(oldUsername))
            {
                phaseTimer.Restart();
                Report(28.0, "Old user", $"Deleting Windows account {oldUsername}.");
                var deleted = await _users.DeleteAsync(oldUsername, cancellationToken);
                if (!deleted.Success)
                    Report(36.0, "Old user", "Account deletion warning: " + deleted.BestMessage, LogLevel.Warning);
                else
                    Report(36.0, "Old user", $"Windows account {oldUsername} deleted in {FormatDuration(phaseTimer.Elapsed)}.", LogLevel.Success);
            }
            else
            {
                Report(36.0, "Old user", "No previous managed account was present.");
            }

            phaseTimer.Restart();
            Report(40.0, "New user", $"Creating local administrator {username}.");
            var created = await _users.CreateAsync(username, password, cancellationToken);
            if (!created.Success) throw new InvalidOperationException("Could not create the new Windows user: " + created.BestMessage);

            state.CurrentUserNumber = newNumber;
            state.LastAttemptUserNumber = newNumber;
            state.LastSetupStatus = "User created; Zoom setup in progress";
            await _stateService.SaveAsync(state);
            Report(48.0, "New user", $"User {username} created and added to Administrators in {FormatDuration(phaseTimer.Elapsed)}.", LogLevel.Success);

            double currentDownloadPercent;
            lock (downloadSync)
            {
                publishDownloadProgress = true;
                currentDownloadPercent = latestDownloadPercent;
            }
            Report(
                50.0 + currentDownloadPercent * 0.20,
                "Download Zoom",
                $"Preparing Zoom Workplace… {currentDownloadPercent:0.0}%");
            phaseTimer.Restart();
            await installerDownloadTask;
            Report(70.0, "Download Zoom",
                $"Current 64-bit Zoom MSI is ready after {FormatDuration(downloadTimer.Elapsed)}; final wait was {FormatDuration(phaseTimer.Elapsed)}.",
                LogLevel.Success);

            phaseTimer.Restart();
            Report(73.0, "Install Zoom", "Installing Zoom for all Windows users.");
            var installed = await _zoom.InstallAsync(cancellationToken);
            if (!installed.Success) throw new InvalidOperationException(installed.Message);
            Report(88.0, "Install Zoom", $"{installed.Message} ({FormatDuration(phaseTimer.Elapsed)})", LogLevel.Success);

            phaseTimer.Restart();
            Report(91.0, "Launch Zoom", $"Opening Zoom as {Environment.MachineName}\\{username}.");
            var launched = await _zoom.LaunchAsUserAsync(username, password, cancellationToken);
            if (!launched.Success) throw new InvalidOperationException(launched.Message);

            state.LastSetupStatus = "Completed";
            await _stateService.SaveAsync(state);
            var completion = $"{launched.Message} Setup completed in {FormatDuration(totalTimer.Elapsed)}.";
            Report(100.0, "Complete", completion, LogLevel.Success);
            return (state, new OperationResult(true, completion));
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
            Report(lastPercent, "Stopped", $"{ex.Message} Stopped after {FormatDuration(totalTimer.Elapsed)}.", LogLevel.Error);
            return (state, new OperationResult(false, ex.Message));
        }
        finally
        {
            installerDownloadCts?.Dispose();
        }
    }

    private static string FormatDuration(TimeSpan elapsed)
    {
        return elapsed.TotalMinutes >= 1
            ? $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds:00}s"
            : $"{elapsed.TotalSeconds:0.0}s";
    }
}
