using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
