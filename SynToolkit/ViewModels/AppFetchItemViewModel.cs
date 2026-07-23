#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SynToolkit.Services;

namespace SynToolkit.ViewModels
{
    /// <summary>
    /// Represents a single Microsoft Store search result and its install/uninstall/update state.
    /// Install-flow logic ported from AME.AppFetch's StoreItem
    /// (https://github.com/Ameliorated-LLC/appfetch, MIT License, Copyright (c) Ameliorated LLC).
    /// </summary>
    public partial class AppFetchItemViewModel : ObservableObject
    {
        private readonly AppFetchService _service;

        public AppFetchService.StoreProductListDto Package { get; }

        public string Name => Package.Title ?? "Unknown";
        public string? Description { get; }

        // BitmapIcon.UriSource is typed System.Uri; classic {Binding} does not implicitly
        // convert a bound string to Uri (unlike a literal XAML attribute value), so this
        // exposes a pre-parsed Uri for the view to bind to directly.
        public Uri? IconUri => Uri.TryCreate(Package.IconUrl, UriKind.Absolute, out Uri? uri) ? uri : null;

        [ObservableProperty]
        private string _buttonText = "Install";

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private bool _isButtonEnabled = true;

        [ObservableProperty]
        private double _progress;

        [ObservableProperty]
        private bool _isCancelable;

        private CancellationTokenSource? _cancellationTokenSource;

        public event EventHandler<string>? OperationFailed;

        public AppFetchItemViewModel(AppFetchService service, AppFetchService.StoreProductListDto package)
        {
            _service = service;
            Package = package;

            if (!string.IsNullOrWhiteSpace(package.Description))
            {
                string[] lines = package.Description.Split('\n');
                string firstLine = lines.First();
                if (firstLine.Length >= 100)
                {
                    string firstSentence = package.Description.Split('.').First() + ".";
                    Description = firstSentence.Length < 100 || firstSentence.Length < lines.Length
                        ? firstSentence.Trim()
                        : firstLine.Trim();
                }
                else
                {
                    Description = firstLine.Trim();
                }
            }
        }

        /// <summary>
        /// Compares this item against the currently installed packages and updates the button
        /// state to Install/Uninstall/Update/Installed. Call after AppFetchService.PrepareDataAsync.
        /// </summary>
        public async Task RefineInstalledStateAsync()
        {
            AppFetchService.InstalledPackage? installedMatch =
                _service.InstalledPackages.FirstOrDefault(x => (x.PublisherName == Package.PublisherName && x.Title == Package.Title) || x.ProductID == Package.ProductId) ??
                _service.InstalledPackages.FirstOrDefault(x => x.PublisherName == Package.PublisherName && x.ApplicationTitles.Any(title => title.Equals(Package.Title)));

            if (installedMatch == null)
            {
                return;
            }

            IsBusy = true;

            if (_service.InstalledPackages.Any(x => x.ProductID == Package.ProductId))
            {
                bool matchFound = !string.IsNullOrEmpty(_service.InstalledPackages.First(x => x.ProductID == Package.ProductId).FullName);
                ButtonText = !matchFound ? "Installed" : "Uninstall";
                IsButtonEnabled = matchFound;
                IsBusy = false;
                return;
            }

            IsButtonEnabled = false;
            try
            {
                System.Collections.Generic.List<AppFetchService.StorePackageDto> installPackages = await _service.GetPackages(Package.ProductId!, false);

                AppFetchService.StorePackageDto? match = installPackages.FirstOrDefault(x =>
                    x.Name!.Split('_').First() == installedMatch.FullName.Split('_').First() &&
                    x.Name!.Split('_').Last() == installedMatch.FullName.Split('_').Last());

                if (match == null)
                {
                    throw new Exception("Match not found");
                }

                ButtonText = match.Name!.Split('_')[1] != installedMatch.FullName.Split('_')[1] ? "Update" : "Uninstall";
            }
            catch
            {
                ButtonText = "Install";
            }

            IsBusy = false;
            IsButtonEnabled = ButtonText != "Installed";
        }

        [RelayCommand]
        public async Task InstallOrUninstallAsync()
        {
            IsButtonEnabled = false;
            IsBusy = true;

            try
            {
                if (ButtonText == "Uninstall")
                {
                    AppFetchService.InstalledPackage toUninstall =
                        _service.InstalledPackages.FirstOrDefault(x => x.PublisherName == Package.PublisherName && x.Title == Package.Title) ??
                        _service.InstalledPackages.First(x => x.PublisherName == Package.PublisherName && x.ApplicationTitles.Any(title => title.Equals(Package.Title)));
                    await _service.UninstallApp(toUninstall.FullName);
                    ButtonText = "Install";
                }
                else
                {
                    System.Collections.Generic.List<AppFetchService.StorePackageDto> installPackages = await _service.GetPackages(Package.ProductId!, true);

                    Progress = 0;
                    _cancellationTokenSource = new CancellationTokenSource();
                    IsCancelable = true;
                    try
                    {
                        await _service.DownloadAndInstallPackagesAsync(
                            installPackages,
                            new Progress<double>(value => Progress = value),
                            _cancellationTokenSource.Token);
                    }
                    finally
                    {
                        IsCancelable = false;
                        _cancellationTokenSource.Dispose();
                        _cancellationTokenSource = null;
                    }

                    try
                    {
                        await _service.PrepareDataAsync();
                    }
                    catch (Exception exception)
                    {
                        App.logger.Debug(exception, "[AppFetch] Unable to refresh installed-package state after install.");
                    }

                    await Task.Delay(2900);

                    AppFetchService.InstalledPackage? installedMatch =
                        _service.InstalledPackages.FirstOrDefault(x => x.PublisherName == Package.PublisherName && x.Title == Package.Title) ??
                        _service.InstalledPackages.FirstOrDefault(x => x.PublisherName == Package.PublisherName && x.ApplicationTitles.Any(title => title.Equals(Package.Title)));

                    if (installedMatch != null)
                    {
                        installedMatch.ProductID = Package.ProductId;
                        ButtonText = "Uninstall";
                    }
                    else
                    {
                        _service.InstalledPackages.Add(new AppFetchService.InstalledPackage { ProductID = Package.ProductId });
                        ButtonText = "Installed";
                    }
                }
            }
            catch (OperationCanceledException)
            {
                App.logger.Info("[AppFetch] Install of {Name} was canceled.", Name);
                ButtonText = "Install";
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[AppFetch] Failed to {Action} {Name}.", ButtonText == "Uninstall" ? "uninstall" : "install", Name);
                OperationFailed?.Invoke(this, exception.Message == "Microsoft Edge is required"
                    ? $"Microsoft Edge is required to install {Name}."
                    : $"An unexpected error occurred while attempting to {(ButtonText == "Uninstall" ? "uninstall" : "install")} {Name}.");
                ButtonText = "Install";
            }

            IsBusy = false;
            IsButtonEnabled = ButtonText != "Installed";
        }

        [RelayCommand]
        private void Cancel() => _cancellationTokenSource?.Cancel();
    }
}
