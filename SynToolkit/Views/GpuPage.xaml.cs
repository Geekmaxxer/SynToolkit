#nullable enable

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SynToolkit.Services.NvidiaProfileInspector;
using SynToolkit.Services.RadeonSlimmer;
using SynToolkit.Utils;
using SynToolkit.ViewModels;

namespace SynToolkit.Views
{
    public sealed partial class GpuPage : Page
    {
        private const string InstallerFileFilter =
            "Radeon Software installers|*radeon*.exe;*adrenalin*.exe;*amd-software-pro-edition*.exe;*vanguard*.exe|Executables (*.exe)|*.exe|All files (*.*)|*.*";

        private const string NipFileFilter = "NVIDIA Profile Inspector profiles (*.nip)|*.nip|All files (*.*)|*.*";

        private readonly GpuPageViewModel _viewModel;

        public GpuPage()
        {
            InitializeComponent();
            _viewModel = App._host.Services.GetRequiredService<GpuPageViewModel>();
            DataContext = _viewModel;
            _viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(GpuPageViewModel.CurrentStep))
                {
                    UpdateStepVisibility();
                }
            };
            UpdateStepVisibility();
            _ = _viewModel.DetectGpusAsync();
        }

        private void UpdateStepVisibility()
        {
            SelectInstallerPanel.Visibility = _viewModel.CurrentStep == GpuWizardStep.SelectInstaller ? Visibility.Visible : Visibility.Collapsed;
            ExtractingPanel.Visibility = _viewModel.CurrentStep == GpuWizardStep.Extracting ? Visibility.Visible : Visibility.Collapsed;
            CustomizePanel.Visibility = _viewModel.CurrentStep == GpuWizardStep.Customize ? Visibility.Visible : Visibility.Collapsed;
            DonePanel.Visibility = _viewModel.CurrentStep == GpuWizardStep.Done ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BrowseInstallerButton_Click(object sender, RoutedEventArgs e)
        {
            IntPtr windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
            string? filePath = NativeFileDialogHelper.ShowOpenFileDialog(windowHandle, InstallerFileFilter);
            if (filePath is not null)
            {
                _viewModel.SelectInstaller(filePath);
            }
        }

        private void BrowseExtractionFolderButton_Click(object sender, RoutedEventArgs e)
        {
            using System.Windows.Forms.FolderBrowserDialog dialog = new() { ShowNewFolderButton = true };
            if (System.IO.Directory.Exists(_viewModel.ExtractionFolderPath))
            {
                dialog.SelectedPath = _viewModel.ExtractionFolderPath;
            }

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _viewModel.ExtractionFolderPath = dialog.SelectedPath;
            }
        }

        private async void ExtractButton_Click(object sender, RoutedEventArgs e) => await _viewModel.ExtractAndLoadAsync();

        private void SkipExtractionButton_Click(object sender, RoutedEventArgs e) => _viewModel.LoadFromAlreadyExtracted();

        private void KeepAllPackages_Click(object sender, RoutedEventArgs e) => _viewModel.SetAllPackages(true);

        private void RemoveAllPackages_Click(object sender, RoutedEventArgs e) => _viewModel.SetAllPackages(false);

        private void EnableAllScheduledTasks_Click(object sender, RoutedEventArgs e) => _viewModel.SetAllScheduledTasks(true);

        private void DisableAllScheduledTasks_Click(object sender, RoutedEventArgs e) => _viewModel.SetAllScheduledTasks(false);

        private void KeepAllDisplayComponents_Click(object sender, RoutedEventArgs e) => _viewModel.SetAllDisplayComponents(true);

        private void RemoveAllDisplayComponents_Click(object sender, RoutedEventArgs e) => _viewModel.SetAllDisplayComponents(false);

        private async void ApplyAndInstallButton_Click(object sender, RoutedEventArgs e) => await _viewModel.ApplyAndInstallAsync();

        private async void ResetToDefaultsButton_Click(object sender, RoutedEventArgs e) => await _viewModel.ResetToDefaultsAsync();

        private void StartOverButton_Click(object sender, RoutedEventArgs e) => _viewModel.StartOver();

        private async void BrowseNipFileButton_Click(object sender, RoutedEventArgs e)
        {
            IntPtr windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
            string? filePath = NativeFileDialogHelper.ShowOpenFileDialog(windowHandle, NipFileFilter);
            if (filePath is not null)
            {
                await _viewModel.LoadNipFileAsync(filePath);
            }
        }

        private async void ApplyNvidiaProfilesButton_Click(object sender, RoutedEventArgs e)
        {
            ContentDialog confirmation = new()
            {
                XamlRoot = XamlRoot,
                Style = Microsoft.UI.Xaml.Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                Title = "Apply profiles to the NVIDIA driver?",
                Content = "This writes the profiles above directly to your NVIDIA driver settings, creating any profiles that don't already exist. Existing profiles with the same name are updated, not replaced. This is the same live driver-settings API NVIDIA Profile Inspector itself uses.",
                PrimaryButtonText = "Apply",
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
                App.logger.Error(exception, "The apply-NVIDIA-profiles confirmation could not be displayed.");
                return;
            }

            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            await _viewModel.ApplyNvidiaProfilesAsync();
        }

        private async void LoadBundledProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (BundledProfilesComboBox.SelectedItem is BundledNvidiaProfileFile selected)
            {
                await _viewModel.LoadBundledProfileAsync(selected);
            }
        }

        private void AddNewSettingButton_Click(object sender, RoutedEventArgs e) => _viewModel.AddNewSettingRow();

        private void RemoveNewSettingButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: NvidiaProfileSetting setting })
            {
                _viewModel.RemoveNewSettingRow(setting);
            }
        }

        private async void ExportNewProfileButton_Click(object sender, RoutedEventArgs e)
        {
            IntPtr windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
            string? filePath = NativeFileDialogHelper.ShowSaveFileDialog(windowHandle, NipFileFilter, "New Profile.nip");
            if (filePath is not null)
            {
                await _viewModel.ExportNewProfileAsync(filePath);
            }
        }
    }
}
