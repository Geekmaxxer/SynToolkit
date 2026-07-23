using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using SynToolkit.ViewModels;

namespace SynToolkit.Views
{
    public sealed partial class AppFetchPage : Page
    {
        private readonly AppFetchPageViewModel _viewModel;
        private bool _hasSearched;

        public AppFetchPage()
        {
            InitializeComponent();
            _viewModel = App._host.Services.GetRequiredService<AppFetchPageViewModel>();
            DataContext = _viewModel;
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
                // Animate to top on first search
                if (!_hasSearched)
                {
                    _hasSearched = true;
                    HintText.Visibility = Visibility.Collapsed;
                    ResultsPanel.Visibility = Visibility.Visible;
                    
                    var moveAnimation = (Storyboard)Resources["MoveToTopAnimation"];
                    moveAnimation?.Begin();
                }

                await _viewModel.SearchAsync(searchTerm);
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[AppFetch] Unhandled search failure for term \"{SearchTerm}\".", searchTerm);
            }
        }
    }
}
