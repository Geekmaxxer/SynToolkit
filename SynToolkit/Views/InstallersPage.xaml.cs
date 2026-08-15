#nullable enable

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SynToolkit.Models;
using SynToolkit.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SynToolkit.Views
{
    public sealed partial class InstallersPage : Page
    {
        private readonly InstalledAppsDetectionService _detectionService = new();
        private CancellationTokenSource _lifetimeCancellation = new();
        private IReadOnlyList<AppCatalogEntryViewModel>? _allApps;
        private IReadOnlyList<AppCatalogEntryViewModel>? _filteredApps;
        private bool _isPageLoaded;
        private int _lifetimeVersion;
        private bool _isGridView;
        private string _selectedCategory = "All";
        private string _searchQuery = string.Empty;

        public InstallersPage()
        {
            InitializeComponent();
            UpdateViewModeButtons();
            UpdateCategoryButtons();
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            _isPageLoaded = true;
            _lifetimeCancellation.Cancel();
            _lifetimeCancellation = new CancellationTokenSource();
            int lifetimeVersion = ++_lifetimeVersion;
            CancellationToken cancellationToken = _lifetimeCancellation.Token;

            await LoadAppsAsync(cancellationToken, lifetimeVersion);
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _isPageLoaded = false;
            _lifetimeVersion++;
            _lifetimeCancellation.Cancel();
        }

        private async Task LoadAppsAsync(CancellationToken cancellationToken, int lifetimeVersion)
        {
            if (!IsCurrentLifetime(lifetimeVersion, cancellationToken))
            {
                return;
            }

            SetLoadingState(true);

            try
            {
                _allApps = await _detectionService.GetCatalogWithInstallStateAsync(cancellationToken);

                if (!IsCurrentLifetime(lifetimeVersion, cancellationToken))
                {
                    return;
                }

                ApplyFilters();
                UpdateHeaderDescription();
            }
            catch (OperationCanceledException)
            {
                // Page navigated away
            }
            catch (Exception ex)
            {
                App.logger.Error(ex, "Failed to load app catalog");
                if (IsCurrentLifetime(lifetimeVersion, cancellationToken))
                {
                    ShowResult("Failed to load apps", ex.Message, InfoBarSeverity.Error);
                }
            }
            finally
            {
                if (IsCurrentLifetime(lifetimeVersion, cancellationToken))
                {
                    SetLoadingState(false);
                }
            }
        }

        private void ApplyFilters()
        {
            if (_allApps is null)
            {
                _filteredApps = null;
                UpdateSectionVisibility();
                return;
            }

            IEnumerable<AppCatalogEntryViewModel> filtered = _allApps;

            // Apply search filter
            if (!string.IsNullOrWhiteSpace(_searchQuery))
            {
                var query = _searchQuery.ToLowerInvariant();
                filtered = filtered.Where(app =>
                    app.DisplayName.ToLowerInvariant().Contains(query) ||
                    app.ShortDescription.ToLowerInvariant().Contains(query));
            }

            // Apply category filter
            filtered = _selectedCategory switch
            {
                "Apps" => filtered.Where(app =>
                    app.Category is AppCategory.GameLaunchers or
                    AppCategory.GamingCreatorApps or
                    AppCategory.Browsers or
                    AppCategory.MediaCommunication or
                    AppCategory.DevUtility),
                "Runtimes" => filtered.Where(app => app.Category == AppCategory.Runtimes),
                "Drivers" => filtered.Where(app => app.Category == AppCategory.GpuDrivers),
                _ => filtered // "All"
            };

            _filteredApps = filtered.ToList();
            UpdateSectionVisibility();
        }

        private void UpdateSectionVisibility()
        {
            if (_filteredApps is null || _filteredApps.Count == 0)
            {
                EmptyState.Visibility = Visibility.Visible;
                ContentArea.Visibility = Visibility.Collapsed;
                return;
            }

            EmptyState.Visibility = Visibility.Collapsed;
            ContentArea.Visibility = Visibility.Visible;

            // Group by category
            var gameLaunchers = _filteredApps.Where(a => a.Category == AppCategory.GameLaunchers).ToList();
            var gamingCreator = _filteredApps.Where(a => a.Category == AppCategory.GamingCreatorApps).ToList();
            var browsers = _filteredApps.Where(a => a.Category == AppCategory.Browsers).ToList();
            var media = _filteredApps.Where(a => a.Category == AppCategory.MediaCommunication).ToList();
            var devUtility = _filteredApps.Where(a => a.Category == AppCategory.DevUtility).ToList();
            var runtimes = _filteredApps.Where(a => a.Category == AppCategory.Runtimes).ToList();
            var drivers = _filteredApps.Where(a => a.Category == AppCategory.GpuDrivers).ToList();

            // Update section visibility and data
            UpdateSection(GameLaunchersSection, GameLaunchersListView, GameLaunchersGridView, gameLaunchers);
            UpdateSection(GamingCreatorSection, GamingCreatorListView, GamingCreatorGridView, gamingCreator);
            UpdateSection(BrowsersSection, BrowsersListView, BrowsersGridView, browsers);
            UpdateSection(MediaSection, MediaListView, MediaGridView, media);
            UpdateSection(DevUtilitySection, DevUtilityListView, DevUtilityGridView, devUtility);
            UpdateSection(RuntimesSection, RuntimesListView, RuntimesGridView, runtimes);

            // Drivers section is special (stub)
            DriversSection.Visibility = drivers.Count > 0 || _selectedCategory == "Drivers" || _selectedCategory == "All"
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void UpdateSection(
            StackPanel section,
            ItemsControl listView,
            ItemsControl gridView,
            IReadOnlyList<AppCatalogEntryViewModel> items)
        {
            if (items.Count == 0)
            {
                section.Visibility = Visibility.Collapsed;
                return;
            }

            section.Visibility = Visibility.Visible;
            listView.ItemsSource = items;
            gridView.ItemsSource = items;

            listView.Visibility = _isGridView ? Visibility.Collapsed : Visibility.Visible;
            gridView.Visibility = _isGridView ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateHeaderDescription()
        {
            if (_allApps is null)
            {
                HeaderDescription.Text = "Scanning for installed apps...";
                return;
            }

            int installedCount = _allApps.Count(a => a.IsInstalled);
            int totalCount = _allApps.Count;

            HeaderDescription.Text = $"{installedCount} of {totalCount} apps already installed on your machine.";
        }

        private void SetLoadingState(bool isLoading)
        {
            LoadingState.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            ContentArea.Visibility = isLoading ? Visibility.Collapsed : Visibility.Visible;
            RescanButton.IsEnabled = !isLoading;

            if (isLoading)
            {
                EmptyState.Visibility = Visibility.Collapsed;
            }
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

        private void UpdateCategoryButtons()
        {
            AllCategoryButton.Style = _selectedCategory == "All"
                ? Microsoft.UI.Xaml.Application.Current.Resources["AccentButtonStyle"] as Style
                : null;
            AppsCategoryButton.Style = _selectedCategory == "Apps"
                ? Microsoft.UI.Xaml.Application.Current.Resources["AccentButtonStyle"] as Style
                : null;
            RuntimesCategoryButton.Style = _selectedCategory == "Runtimes"
                ? Microsoft.UI.Xaml.Application.Current.Resources["AccentButtonStyle"] as Style
                : null;
            DriversCategoryButton.Style = _selectedCategory == "Drivers"
                ? Microsoft.UI.Xaml.Application.Current.Resources["AccentButtonStyle"] as Style
                : null;
        }

        private void ListViewButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isGridView)
            {
                _isGridView = false;
                UpdateViewModeButtons();
                ApplyFilters();
            }
        }

        private void GridViewButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isGridView)
            {
                _isGridView = true;
                UpdateViewModeButtons();
                ApplyFilters();
            }
        }

        private void CategoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string category)
            {
                _selectedCategory = category;
                UpdateCategoryButtons();
                ApplyFilters();
            }
        }

        private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                _searchQuery = sender.Text ?? string.Empty;
                ApplyFilters();
            }
        }

        private async void RescanButton_Click(object sender, RoutedEventArgs e)
        {
            _detectionService.InvalidateCatalogCache();
            int lifetimeVersion = _lifetimeVersion;
            CancellationToken cancellationToken = _lifetimeCancellation.Token;

            await LoadAppsAsync(cancellationToken, lifetimeVersion);
        }

        private void InstallSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            // Stubbed action - show "Coming soon" message
            ShowResult(
                "Coming soon",
                "App installation will be available in a future update. Download URLs have not been added yet.",
                InfoBarSeverity.Informational);
        }

        private bool IsCurrentLifetime(int lifetimeVersion, CancellationToken cancellationToken) =>
            _isPageLoaded &&
            lifetimeVersion == _lifetimeVersion &&
            !cancellationToken.IsCancellationRequested;

        private void ShowResult(string title, string message, InfoBarSeverity severity)
        {
            OperationInfoBar.Title = title;
            OperationInfoBar.Message = message;
            OperationInfoBar.Severity = severity;
            OperationInfoBar.IsOpen = true;
        }
    }
}
