#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using SynToolkit.ViewModels;

namespace SynToolkit.Views
{
    public sealed partial class SpecsPage : Page
    {
        private readonly SpecsPageViewModel _viewModel;

        public SpecsPage()
        {
            InitializeComponent();
            _viewModel = App._host.Services.GetRequiredService<SpecsPageViewModel>();
            DataContext = _viewModel;
            _ = _viewModel.LoadAsync();
        }
    }
}
