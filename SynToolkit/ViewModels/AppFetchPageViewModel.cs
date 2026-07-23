#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SynToolkit.Services;
using SynToolkit.Services.ConfigurationServices;

namespace SynToolkit.ViewModels
{
    /// <summary>
    /// Drives the App Fetch page's search box and results list. Ported from AME.AppFetch's
    /// Handler (https://github.com/Ameliorated-LLC/appfetch, MIT License, Copyright (c)
    /// Ameliorated LLC).
    /// </summary>
    public partial class AppFetchPageViewModel : ObservableObject
    {
        private readonly AppFetchService _service;
        private readonly IConfigurationService _xboxServicesConfigurationService;

        public ObservableCollection<AppFetchItemViewModel> Results { get; } = new();

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private bool _hasError;

        [ObservableProperty]
        private string _errorMessage = string.Empty;

        /// <summary>
        /// True when SynToolkit's Xbox-services debloat tweak is currently applied,
        /// which can prevent Xbox/Gaming-related Store apps from installing or running.
        /// Drives visibility of the disclosure banner and its revert button.
        /// </summary>
        [ObservableProperty]
        private bool _isXboxServicesTweakApplied;

        [ObservableProperty]
        private bool _isRevertingXboxServicesTweak;

        public bool IsRevertXboxServicesButtonEnabled => !IsRevertingXboxServicesTweak;

        partial void OnIsRevertingXboxServicesTweakChanged(bool value) =>
            OnPropertyChanged(nameof(IsRevertXboxServicesButtonEnabled));

        private Task? _installedPackagesTask;

        public AppFetchPageViewModel(
            AppFetchService service,
            [Microsoft.Extensions.DependencyInjection.FromKeyedServices("XboxServices")] IConfigurationService xboxServicesConfigurationService)
        {
            _service = service;
            _xboxServicesConfigurationService = xboxServicesConfigurationService;
            RefreshXboxServicesTweakState();
        }

        private void RefreshXboxServicesTweakState()
        {
            try
            {
                IsXboxServicesTweakApplied = !_xboxServicesConfigurationService.IsEnabled();
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[AppFetch] Unable to read the Xbox-services tweak state.");
                IsXboxServicesTweakApplied = false;
            }
        }

        [RelayCommand]
        private async Task RevertXboxServicesTweakAsync()
        {
            IsRevertingXboxServicesTweak = true;
            try
            {
                await Task.Run(() => _xboxServicesConfigurationService.Enable());
                RefreshXboxServicesTweakState();
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[AppFetch] Unable to revert the Xbox-services tweak.");
                ErrorMessage = $"Unable to revert the Xbox services tweak: {exception.Message}";
                HasError = true;
            }
            finally
            {
                IsRevertingXboxServicesTweak = false;
            }
        }

        public async Task SearchAsync(string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                return;
            }

            Results.Clear();
            IsLoading = true;
            HasError = false;

            _installedPackagesTask ??= _service.PrepareDataAsync();

            try
            {
                List<AppFetchService.StoreProductListDto> list = await _service.SearchProductsAsync(searchTerm);

                try
                {
                    await _installedPackagesTask;
                }
                catch (Exception exception)
                {
                    App.logger.Debug(exception, "[AppFetch] Unable to load installed-package state.");
                }

                foreach (AppFetchService.StoreProductListDto result in list)
                {
                    AppFetchItemViewModel item = new(_service, result);
                    item.OperationFailed += Item_OperationFailed;
                    Results.Add(item);
                    _ = item.RefineInstalledStateAsync();
                }
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[AppFetch] Search failed for term \"{SearchTerm}\".", searchTerm);
                ErrorMessage = "Network request failed. Ensure you have a stable internet connection.";
                HasError = true;
            }

            IsLoading = false;
        }

        private void Item_OperationFailed(object? sender, string message)
        {
            ErrorMessage = message;
            HasError = true;
        }
    }
}
