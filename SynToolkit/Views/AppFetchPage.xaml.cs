using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SynToolkit.Services;
using SynToolkit.ViewModels;

namespace SynToolkit.Views
{
    public sealed partial class AppFetchPage : Page
    {
        private readonly AppFetchPageViewModel _viewModel;
        private bool _hasLoadedInstallerStates;

        public AppFetchPage()
        {
            InitializeComponent();
            _viewModel = App._host.Services.GetRequiredService<AppFetchPageViewModel>();
            DataContext = _viewModel;
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (_hasLoadedInstallerStates)
            {
                return;
            }

            _hasLoadedInstallerStates = true;
            await _viewModel.RefreshInstallerStatesAsync();
        }

        private async void InstallerPrimaryAction_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element ||
                element.DataContext is not FeaturedInstallerViewModel installer)
            {
                return;
            }

            try
            {
                await _viewModel.InstallSingleAsync(installer);
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[Installers] The quick action failed for {AppName}.", installer.Name);
            }
        }

        private async void InstallerUninstall_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element ||
                element.DataContext is not FeaturedInstallerViewModel installer ||
                !installer.CanUninstall)
            {
                return;
            }

            ContentDialog confirmationDialog = new()
            {
                Title = $"Uninstall {installer.Name}?",
                Content = "Close the app before continuing. Its publisher's uninstaller controls whether personal settings and data are kept.",
                PrimaryButtonText = "Uninstall",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };
            if (await confirmationDialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            WingetInstallResult result = await _viewModel.UninstallSingleAsync(installer);
            if (result.Succeeded)
            {
                return;
            }

            ContentDialog errorDialog = new()
            {
                Title = $"Could not uninstall {installer.Name}",
                Content = string.IsNullOrWhiteSpace(result.Output)
                    ? "The uninstaller did not complete. Close the app and try again."
                    : result.Output,
                CloseButtonText = "Close",
                XamlRoot = this.XamlRoot
            };
            await errorDialog.ShowAsync();
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e) => _ = RunSearchAsync(SearchBox.Text);

        private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) =>
            _ = RunSearchAsync(args.QueryText);

        private async Task RunSearchAsync(string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                return;
            }

            try
            {
                Task searchTask = _viewModel.SearchAsync(searchTerm);
                ResultsSection.StartBringIntoView(new BringIntoViewOptions
                {
                    AnimationDesired = true,
                    VerticalAlignmentRatio = 0
                });
                await searchTask;
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[AppFetch] Unhandled search failure for term \"{SearchTerm}\".", searchTerm);
            }
        }
    }
}
