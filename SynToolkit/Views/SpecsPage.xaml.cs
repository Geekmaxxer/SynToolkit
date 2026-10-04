#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SynToolkit.ViewModels;

namespace SynToolkit.Views
{
    public sealed partial class SpecsPage : Page
    {
        private readonly SpecsPageViewModel _viewModel;
        private readonly DispatcherTimer _cpuMonitoringTimer;
        private CancellationTokenSource _lifetimeCancellation = new();
        private bool _isCpuDetailsExpanded;
        private CancellationTokenSource? _cpuMonitoringCancellation;

        public SpecsPage()
        {
            InitializeComponent();
            _viewModel = App._host.Services.GetRequiredService<SpecsPageViewModel>();
            DataContext = _viewModel;

            _cpuMonitoringTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1250) };
            _cpuMonitoringTimer.Tick += CpuMonitoringTimer_Tick;
            Loaded += SpecsPage_Loaded;
            Unloaded += SpecsPage_Unloaded;

        }

        private void CpuDetailsExpander_Expanded(object sender, EventArgs args)
        {
            _isCpuDetailsExpanded = true;
            StartCpuMonitoring();
        }

        private void CpuDetailsExpander_Collapsed(object sender, EventArgs args)
        {
            _isCpuDetailsExpanded = false;
            StopCpuMonitoring();
        }
        private void MotherboardDetailsExpander_Expanded(object sender, EventArgs args)
        {
            _ = _viewModel.LoadMotherboardDetailsAsync(_lifetimeCancellation.Token);
        }
        private void SpecsPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (_lifetimeCancellation.IsCancellationRequested)
            {
                _lifetimeCancellation.Dispose();
                _lifetimeCancellation = new CancellationTokenSource();
            }

            _ = LoadSpecsAsync(_lifetimeCancellation.Token);
            if (_isCpuDetailsExpanded)
            {
                StartCpuMonitoring();
            }
        }

        private void SpecsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            StopCpuMonitoring();
            _lifetimeCancellation.Cancel();
        }

        private async Task LoadSpecsAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _viewModel.LoadAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void StartCpuMonitoring()
        {
            StopCpuMonitoring();
            _cpuMonitoringCancellation = new CancellationTokenSource();
            _ = _viewModel.RefreshCpuLiveMetricsAsync(_cpuMonitoringCancellation.Token);
            _cpuMonitoringTimer.Start();
        }

        private void StopCpuMonitoring()
        {
            _cpuMonitoringTimer.Stop();
            _cpuMonitoringCancellation?.Cancel();
            _cpuMonitoringCancellation?.Dispose();
            _cpuMonitoringCancellation = null;
        }

        private void CpuMonitoringTimer_Tick(object? sender, object e)
        {
            if (_cpuMonitoringCancellation is { } source)
                _ = _viewModel.RefreshCpuLiveMetricsAsync(source.Token);
        }
    }
}
