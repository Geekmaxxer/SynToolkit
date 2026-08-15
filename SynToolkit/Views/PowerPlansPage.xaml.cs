#nullable enable

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SynToolkit.Services;
using SynToolkit.Utils;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SynToolkit.Views
{
    public sealed partial class PowerPlansPage : Page
    {
        private const string TutorialVideoUrl = "https://www.youtube.com/watch?v=GJ3omzm-CYU";
        
        private readonly PowerPlanService _powerPlanService = new();
        private CancellationTokenSource _lifetimeCancellation = new();
        private PowerPlanSnapshot? _snapshot;
        private bool _isBusy = true;
        private bool _isPageLoaded;
        private int _lifetimeVersion;
        private bool _isGridView;

        public PowerPlansPage()
        {
            InitializeComponent();
            LoadBundledPlans();
            UpdateViewModeButtons();
        }

        private void LoadBundledPlans()
        {
            var bundledPlans = _powerPlanService.GetBundledPlans();
            if (bundledPlans.Count > 0)
            {
                BundledPlansListView.ItemsSource = bundledPlans;
                BundledPlansGridView.ItemsSource = bundledPlans;
                UpdateViewModeVisibility();
                BundledPlansEmptyState.Visibility = Visibility.Collapsed;
            }
            else
            {
                BundledPlansListView.Visibility = Visibility.Collapsed;
                BundledPlansGridView.Visibility = Visibility.Collapsed;
                BundledPlansEmptyState.Visibility = Visibility.Visible;
            }
        }
        
        private void UpdateViewModeVisibility()
        {
            BundledPlansListView.Visibility = _isGridView ? Visibility.Collapsed : Visibility.Visible;
            BundledPlansGridView.Visibility = _isGridView ? Visibility.Visible : Visibility.Collapsed;
        }
        
        private void UpdateViewModeButtons()
        {
            ListViewButton.Style = _isGridView 
                ? null 
                : Microsoft.UI.Xaml.Application.Current.Resources["AccentButtonStyle"] as Style;
            GridViewButton.Style = _isGridView 
                ? Microsoft.UI.Xaml.Application.Current.Resources["AccentButtonStyle"] as Style 
                : null;
        }
        
        private void ListViewButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isGridView)
            {
                _isGridView = false;
                UpdateViewModeVisibility();
                UpdateViewModeButtons();
            }
        }
        
        private void GridViewButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isGridView)
            {
                _isGridView = true;
                UpdateViewModeVisibility();
                UpdateViewModeButtons();
            }
        }
        
        private async void WatchTutorialButton_Click(object sender, RoutedEventArgs e)
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri(TutorialVideoUrl));
        }
        
        private void RefreshBundledPlansButton_Click(object sender, RoutedEventArgs e)
        {
            LoadBundledPlans();
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            _isPageLoaded = true;
            ElevationInfoBar.IsOpen = !_powerPlanService.CanMutatePowerPlans;
            _lifetimeCancellation.Cancel();
            _lifetimeCancellation = new CancellationTokenSource();
            int lifetimeVersion = ++_lifetimeVersion;
            CancellationToken cancellationToken = _lifetimeCancellation.Token;

            SetBusy(true);
            try
            {
                await RefreshStatusAsync(cancellationToken, lifetimeVersion, showErrors: true);
            }
            finally
            {
                if (IsCurrentLifetime(lifetimeVersion, cancellationToken))
                {
                    SetBusy(false);
                }
            }
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _isPageLoaded = false;
            _lifetimeVersion++;
            _lifetimeCancellation.Cancel();
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            int lifetimeVersion = _lifetimeVersion;
            CancellationToken cancellationToken = _lifetimeCancellation.Token;
            SetBusy(true);
            try
            {
                await RefreshStatusAsync(cancellationToken, lifetimeVersion, showErrors: true);
            }
            finally
            {
                if (IsCurrentLifetime(lifetimeVersion, cancellationToken))
                {
                    SetBusy(false);
                }
            }
        }

        private async void ImportBuiltInButton_Click(object sender, RoutedEventArgs e) =>
            await RunOperationAsync(
                token => _powerPlanService.ImportBuiltInPlanAsync(token),
                "SynToolkit SOS Performance was imported and activated.");

        private async void ActivateBuiltInButton_Click(object sender, RoutedEventArgs e) =>
            await RunOperationAsync(
                token => _powerPlanService.ActivateSynToolkitPlanAsync(token),
                "SynToolkit SOS Performance is now active.");

        private async void RestorePreviousButton_Click(object sender, RoutedEventArgs e) =>
            await RunOperationAsync(
                token => _powerPlanService.RestorePreviousPlanAsync(token),
                "The previous power plan was restored.");

        private async void ActivateBalancedButton_Click(object sender, RoutedEventArgs e) =>
            await RunOperationAsync(
                token => _powerPlanService.ActivateBalancedPlanAsync(token),
                "Windows Balanced is now active.");

        private async void RemoveBuiltInButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy)
            {
                return;
            }

            ContentDialog confirmation = new()
            {
                XamlRoot = XamlRoot,
                Style = Microsoft.UI.Xaml.Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                Title = "Remove SynToolkit SOS Performance?",
                Content = "If the plan is active, SynToolkit will restore your previous plan first. No other power plan will be removed.",
                PrimaryButtonText = "Remove",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };

            ContentDialogResult result;
            try
            {
                result = await confirmation.ShowAsync();
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "The remove-power-plan confirmation could not be displayed.");
                if (_isPageLoaded)
                {
                    ShowResult("Confirmation unavailable", exception.Message, InfoBarSeverity.Error);
                }
                return;
            }

            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            if (!_isPageLoaded)
            {
                return;
            }

            await RunOperationAsync(
                token => _powerPlanService.RemoveSynToolkitPlanAsync(token),
                "The SynToolkit power plan was removed.");
        }

        private async void RestoreDefaultSchemesButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy)
            {
                return;
            }

            ContentDialog confirmation = new()
            {
                XamlRoot = XamlRoot,
                Style = Microsoft.UI.Xaml.Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                Title = "Restore default power plans?",
                Content = "This resets every Windows power plan to its default configuration, including SynToolkit's SOS Performance plan and any other custom plans you've created. This cannot be undone.",
                PrimaryButtonText = "Restore defaults",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };

            ContentDialogResult result;
            try
            {
                result = await confirmation.ShowAsync();
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "The restore-default-power-plans confirmation could not be displayed.");
                if (_isPageLoaded)
                {
                    ShowResult("Confirmation unavailable", exception.Message, InfoBarSeverity.Error);
                }
                return;
            }

            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            if (!_isPageLoaded)
            {
                return;
            }

            await RunOperationAsync(
                token => _powerPlanService.RestoreDefaultSchemesAsync(token),
                "Windows power plans were restored to their defaults.");
        }

        private async void ImportCustomButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy)
            {
                return;
            }

            string? immutablePlanPath = null;
            int lifetimeVersion = _lifetimeVersion;
            CancellationToken cancellationToken = _lifetimeCancellation.Token;
            SetBusy(true);
            OperationInfoBar.IsOpen = false;
            try
            {
                string? selectedFilePath = ShowPowFilePicker();
                if (selectedFilePath is null)
                {
                    return;
                }

                immutablePlanPath = await SnapshotSelectedPlanAsync(selectedFilePath, cancellationToken);
                PowerPlanImportResult importResult = await _powerPlanService.ImportCustomPlanAsync(
                    immutablePlanPath,
                    cancellationToken);
                if (IsCurrentLifetime(lifetimeVersion, cancellationToken))
                {
                    ShowResult(
                        "Power plan imported",
                        $"{importResult.SchemeName} was imported and activated. ID: {importResult.SchemeId:D}",
                        InfoBarSeverity.Success);
                }
            }
            catch (OperationCanceledException)
            {
                // Navigating away cancels pre-import work. A started Windows
                // power-plan transaction still finishes or rolls back safely.
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "Custom .pow import failed.");
                if (IsCurrentLifetime(lifetimeVersion, cancellationToken))
                {
                    ShowResult("Power-plan import failed", exception.Message, InfoBarSeverity.Error);
                }
            }
            finally
            {
                if (immutablePlanPath is not null)
                {
                    TryDeleteTemporaryPlan(immutablePlanPath);
                }

                if (IsCurrentLifetime(lifetimeVersion, cancellationToken))
                {
                    await RefreshStatusAsync(cancellationToken, lifetimeVersion, showErrors: false);
                    if (IsCurrentLifetime(lifetimeVersion, cancellationToken))
                    {
                        SetBusy(false);
                    }
                }
            }
        }

        private async void ImportBundledPlanButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy)
            {
                return;
            }

            if (sender is not Button button || button.Tag is not BundledPowerPlan plan)
            {
                return;
            }

            int lifetimeVersion = _lifetimeVersion;
            CancellationToken cancellationToken = _lifetimeCancellation.Token;
            SetBusy(true);
            OperationInfoBar.IsOpen = false;
            try
            {
                PowerPlanImportResult importResult = await _powerPlanService.ImportCustomPlanAsync(
                    plan.FilePath,
                    cancellationToken);
                if (IsCurrentLifetime(lifetimeVersion, cancellationToken))
                {
                    ShowResult(
                        "Power plan imported",
                        $"{plan.DisplayName} was imported and activated. ID: {importResult.SchemeId:D}",
                        InfoBarSeverity.Success);
                }
            }
            catch (OperationCanceledException)
            {
                // Navigating away cancels pre-import work.
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "Bundled .pow import failed for {PlanName}.", plan.DisplayName);
                if (IsCurrentLifetime(lifetimeVersion, cancellationToken))
                {
                    ShowResult("Power-plan import failed", exception.Message, InfoBarSeverity.Error);
                }
            }
            finally
            {
                if (IsCurrentLifetime(lifetimeVersion, cancellationToken))
                {
                    await RefreshStatusAsync(cancellationToken, lifetimeVersion, showErrors: false);
                    if (IsCurrentLifetime(lifetimeVersion, cancellationToken))
                    {
                        SetBusy(false);
                    }
                }
            }
        }

        private async void OpenPowerSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:powersleep"));
        }

        private async Task RunOperationAsync(
            Func<CancellationToken, Task> operation,
            string successMessage)
        {
            if (_isBusy)
            {
                return;
            }

            int lifetimeVersion = _lifetimeVersion;
            CancellationToken cancellationToken = _lifetimeCancellation.Token;
            SetBusy(true);
            OperationInfoBar.IsOpen = false;
            try
            {
                await operation(cancellationToken);
                if (IsCurrentLifetime(lifetimeVersion, cancellationToken))
                {
                    ShowResult("Power plan updated", successMessage, InfoBarSeverity.Success);
                }
            }
            catch (OperationCanceledException)
            {
                // Navigating away cancels the operation without showing an error.
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "A power-plan operation failed.");
                if (IsCurrentLifetime(lifetimeVersion, cancellationToken))
                {
                    ShowResult("Power-plan operation failed", exception.Message, InfoBarSeverity.Error);
                }
            }
            finally
            {
                if (IsCurrentLifetime(lifetimeVersion, cancellationToken))
                {
                    await RefreshStatusAsync(cancellationToken, lifetimeVersion, showErrors: false);
                    if (IsCurrentLifetime(lifetimeVersion, cancellationToken))
                    {
                        SetBusy(false);
                    }
                }
            }
        }

        private async Task RefreshStatusAsync(
            CancellationToken cancellationToken,
            int lifetimeVersion,
            bool showErrors)
        {
            if (!IsCurrentLifetime(lifetimeVersion, cancellationToken))
            {
                return;
            }

            StatusProgressRing.IsActive = true;
            StatusProgressRing.Visibility = Visibility.Visible;
            try
            {
                PowerPlanSnapshot snapshot = await _powerPlanService.GetStateAsync(cancellationToken);
                if (!IsCurrentLifetime(lifetimeVersion, cancellationToken))
                {
                    return;
                }

                _snapshot = snapshot;
                CurrentPlanName.Text = _snapshot.ActiveSchemeName;
                CurrentPlanId.Text = _snapshot.ActiveSchemeId?.ToString("D") ?? "Active plan ID unavailable";

                BuiltInPlanState.Text = _snapshot.HasSynToolkitSchemeConflict
                    ? "Reserved ID conflict — not managed"
                    : _snapshot.IsSynToolkitPlanActive
                        ? "Active"
                        : _snapshot.IsSynToolkitPlanInstalled
                            ? "Installed"
                            : "Not imported";

                PreviousPlanState.Text = _snapshot.PreviousSchemeId is Guid previousSchemeId
                    ? $"Previous plan: {_snapshot.PreviousSchemeName ?? previousSchemeId.ToString("D")}" 
                    : "No previous plan has been recorded.";
            }
            catch (OperationCanceledException)
            {
                // The page is no longer current.
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "Unable to read the current power-plan state.");
                if (IsCurrentLifetime(lifetimeVersion, cancellationToken))
                {
                    _snapshot = null;
                    CurrentPlanName.Text = "Power-plan status unavailable";
                    CurrentPlanId.Text = string.Empty;
                    if (showErrors)
                    {
                        ShowResult("Status unavailable", exception.Message, InfoBarSeverity.Error);
                    }
                }
            }
            finally
            {
                if (IsCurrentLifetime(lifetimeVersion, cancellationToken))
                {
                    StatusProgressRing.IsActive = _isBusy;
                    StatusProgressRing.Visibility = _isBusy ? Visibility.Visible : Visibility.Collapsed;
                    UpdateButtonStates();
                }
            }
        }

        private bool IsCurrentLifetime(int lifetimeVersion, CancellationToken cancellationToken) =>
            _isPageLoaded &&
            lifetimeVersion == _lifetimeVersion &&
            !cancellationToken.IsCancellationRequested;

        private void SetBusy(bool isBusy)
        {
            _isBusy = isBusy;
            StatusProgressRing.IsActive = isBusy;
            StatusProgressRing.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
            UpdateButtonStates();
        }

        private void UpdateButtonStates()
        {
            bool stateReady = _snapshot is not null;
            bool hasConflict = _snapshot?.HasSynToolkitSchemeConflict == true;
            bool canMutate = !_isBusy && stateReady && _powerPlanService.CanMutatePowerPlans;
            RefreshButton.IsEnabled = !_isBusy;
            RefreshBundledPlansButton.IsEnabled = !_isBusy;
            ImportBuiltInButton.IsEnabled = canMutate && !hasConflict;
            ImportCustomButton.IsEnabled = canMutate;
            ActivateBuiltInButton.IsEnabled = canMutate && !hasConflict && _snapshot?.IsSynToolkitPlanInstalled == true && _snapshot.IsSynToolkitPlanActive == false;
            RemoveBuiltInButton.IsEnabled = canMutate && !hasConflict && _snapshot?.IsSynToolkitPlanInstalled == true;
            RestorePreviousButton.IsEnabled = canMutate && _snapshot?.PreviousSchemeId is not null;
            ActivateBalancedButton.IsEnabled = canMutate && _snapshot?.ActiveSchemeId is Guid activeSchemeId && activeSchemeId != PowerPlanService.BalancedSchemeId;
            RestoreDefaultSchemesButton.IsEnabled = canMutate;
            BundledPlansListView.IsEnabled = canMutate;
            BundledPlansGridView.IsEnabled = canMutate;
        }

        /// <summary>
        private static string? ShowPowFilePicker()
        {
            IntPtr windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
            return NativeFileDialogHelper.ShowOpenFileDialog(windowHandle, "Windows power plan (*.pow)|*.pow");
        }

        private static async Task<string> SnapshotSelectedPlanAsync(
            string sourceFilePath,
            CancellationToken cancellationToken)
        {
            if (!string.Equals(Path.GetExtension(sourceFilePath), ".pow", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Only Windows .pow power-plan files can be imported.");
            }

            FileInfo sourceInfo = new(sourceFilePath);
            long maximumBytes = PowerPlanService.MaximumPlanFileBytes;
            if (!sourceInfo.Exists || sourceInfo.Length == 0 || sourceInfo.Length > maximumBytes)
            {
                throw new InvalidDataException("The selected .pow file is empty or larger than 64 MB.");
            }

            string destinationPath = Path.Combine(
                Path.GetTempPath(),
                $"SynToolkit-Picker-{Guid.NewGuid():N}.pow");

            try
            {
                await using FileStream source = new(
                    sourceFilePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using FileStream destination = new(
                    destinationPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await PowerPlanService.CopyBoundedAsync(source, destination, cancellationToken);
                await destination.FlushAsync(cancellationToken);
                return destinationPath;
            }
            catch
            {
                TryDeleteTemporaryPlan(destinationPath);
                throw;
            }
        }

        private static void TryDeleteTemporaryPlan(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "Unable to remove temporary selected power-plan file {Path}.", path);
            }
        }

        private void ShowResult(string title, string message, InfoBarSeverity severity)
        {
            OperationInfoBar.Title = title;
            OperationInfoBar.Message = string.IsNullOrWhiteSpace(message)
                ? "Windows did not return any additional details. Check the SynToolkit log, then try again as administrator."
                : message.Trim();
            OperationInfoBar.Severity = severity;
            OperationInfoBar.IsOpen = true;
        }
    }
}
