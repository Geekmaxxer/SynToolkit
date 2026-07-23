#nullable enable

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SynToolkit.Services;
using SynToolkit.Services.NvidiaProfileInspector;
using SynToolkit.Services.RadeonSlimmer;

namespace SynToolkit.ViewModels
{
    public enum GpuWizardStep
    {
        SelectInstaller,
        Extracting,
        Customize,
        Done,
    }

    /// <summary>
    /// Drives the GPU tab: an AMD Radeon driver slimmer wizard (select an AMD Radeon Software
    /// installer, extract it, exclude packages/scheduled tasks/display-driver components, then
    /// launch the modified Setup.exe — ported from GSDragoon/RadeonSoftwareSlimmer's
    /// PreInstallViewModel, https://github.com/GSDragoon/RadeonSoftwareSlimmer, GPL-3.0, same
    /// license as SynToolkit) and an NVIDIA .nip profile importer (reads a profile, then applies
    /// it to the live NVIDIA driver through NvAPIWrapper.Net, https://github.com/falahati/NvAPIWrapper,
    /// LGPL-3.0; .nip model shape from https://github.com/Orbmu2k/nvidiaProfileInspector, MIT
    /// License). Both sections are disabled when the corresponding vendor's GPU isn't detected.
    /// Only the Radeon Slimmer's PreInstall phase is ported (no PostInstall cleanup).
    /// </summary>
    public partial class GpuPageViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _installerFilePath = string.Empty;

        [ObservableProperty]
        private string _extractionFolderPath = string.Empty;

        [ObservableProperty]
        private GpuWizardStep _currentStep = GpuWizardStep.SelectInstaller;

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private bool _hasError;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private bool _hasAmdGpu;

        [ObservableProperty]
        private bool _hasNvidiaGpu;

        public bool NoAmdGpuDetected => !HasAmdGpu;
        public bool NoNvidiaGpuDetected => !HasNvidiaGpu;

        partial void OnHasAmdGpuChanged(bool value) => OnPropertyChanged(nameof(NoAmdGpuDetected));
        partial void OnHasNvidiaGpuChanged(bool value) => OnPropertyChanged(nameof(NoNvidiaGpuDetected));

        [ObservableProperty]
        private string _detectedGpuSummary = "Detecting installed GPUs...";

        [ObservableProperty]
        private string _nipFilePath = string.Empty;

        [ObservableProperty]
        private bool _isApplyingNvidiaProfiles;

        [ObservableProperty]
        private string _nvidiaApplyResultSummary = string.Empty;

        public bool HasNvidiaApplyResult => !string.IsNullOrEmpty(NvidiaApplyResultSummary);

        partial void OnNvidiaApplyResultSummaryChanged(string value) => OnPropertyChanged(nameof(HasNvidiaApplyResult));

        [ObservableProperty]
        private string _newProfileName = string.Empty;

        [ObservableProperty]
        private string _newProfileExecutablesText = string.Empty;

        public ObservableCollection<RadeonPackage> Packages { get; } = new();
        public ObservableCollection<RadeonScheduledTask> ScheduledTasks { get; } = new();
        public ObservableCollection<RadeonDisplayComponent> DisplayComponents { get; } = new();
        public ObservableCollection<NvidiaProfile> NvidiaProfiles { get; } = new();
        public ObservableCollection<BundledNvidiaProfileFile> BundledProfiles { get; } = new();
        public ObservableCollection<NvidiaProfileSetting> NewProfileSettings { get; } = new();

        public GpuPageViewModel()
        {
            foreach (BundledNvidiaProfileFile bundledProfile in NvidiaProfileGalleryService.GetBundledProfiles())
            {
                BundledProfiles.Add(bundledProfile);
            }
        }

        public async Task LoadBundledProfileAsync(BundledNvidiaProfileFile bundledProfile) =>
            await LoadNipFileAsync(bundledProfile.FullPath);

        public void AddNewSettingRow() => NewProfileSettings.Add(new NvidiaProfileSetting());

        public void RemoveNewSettingRow(NvidiaProfileSetting setting) => NewProfileSettings.Remove(setting);

        public async Task ExportNewProfileAsync(string exportFilePath)
        {
            HasError = false;
            try
            {
                NvidiaProfile profile = new()
                {
                    ProfileName = string.IsNullOrWhiteSpace(NewProfileName) ? "New Profile" : NewProfileName.Trim(),
                    Executeables = NewProfileExecutablesText
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToList(),
                    Settings = NewProfileSettings.ToList(),
                };

                await Task.Run(() => NvidiaProfilePreviewService.SaveProfiles(new System.Collections.Generic.List<NvidiaProfile> { profile }, exportFilePath));
                StatusMessage = $"Exported '{profile.ProfileName}' to {exportFilePath}.";
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[GPU] Exporting a new .nip profile failed.");
                StatusMessage = exception.Message;
                HasError = true;
            }
        }

        public async Task DetectGpusAsync()
        {
            System.Collections.Generic.IReadOnlyList<DetectedGpu> gpus = await Task.Run(GpuDetectionService.GetDetectedGpus);

            HasAmdGpu = GpuDetectionService.HasAmdGpu(gpus);
            HasNvidiaGpu = GpuDetectionService.HasNvidiaGpu(gpus);
            DetectedGpuSummary = gpus.Count == 0
                ? "No GPU could be detected."
                : $"Detected: {string.Join(", ", gpus.Select(gpu => gpu.Name))}";
        }

        public async Task LoadNipFileAsync(string nipFilePath)
        {
            HasError = false;
            NipFilePath = nipFilePath;
            try
            {
                System.Collections.Generic.List<NvidiaProfile> profiles = await Task.Run(() => NvidiaProfilePreviewService.LoadProfiles(nipFilePath));
                NvidiaProfiles.Clear();
                foreach (NvidiaProfile profile in profiles)
                {
                    NvidiaProfiles.Add(profile);
                }
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[GPU] Reading a .nip profile file failed.");
                StatusMessage = exception.Message;
                HasError = true;
            }
        }

        public async Task ApplyNvidiaProfilesAsync()
        {
            HasError = false;
            NvidiaApplyResultSummary = string.Empty;
            IsApplyingNvidiaProfiles = true;
            try
            {
                System.Collections.Generic.List<NvidiaProfile> profiles = NvidiaProfiles.ToList();
                System.Collections.Generic.List<NvidiaProfileApplyResult> results = await Task.Run(() => NvidiaProfileApplyService.Apply(profiles));

                int settingsApplied = results.Sum(profile => profile.Settings.Count(setting => setting.Applied));
                int settingsSkipped = results.Sum(profile => profile.Settings.Count(setting => !setting.Applied));
                int profilesCreated = results.Count(profile => profile.ProfileCreated);

                NvidiaApplyResultSummary = settingsSkipped == 0
                    ? $"Applied {settingsApplied} setting(s) across {results.Count} profile(s) ({profilesCreated} newly created)."
                    : $"Applied {settingsApplied} setting(s) across {results.Count} profile(s) ({profilesCreated} newly created). {settingsSkipped} setting(s) were skipped — see the log for details.";

                if (settingsSkipped > 0)
                {
                    foreach (NvidiaProfileApplyResult profile in results)
                    {
                        foreach (NvidiaSettingApplyResult setting in profile.Settings.Where(setting => !setting.Applied))
                        {
                            App.logger.Warn("[GPU] Skipped NVIDIA setting '{0}' on profile '{1}': {2}", setting.SettingName, profile.ProfileName, setting.SkipReason);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[GPU] Applying NVIDIA profiles to the driver failed.");
                StatusMessage = exception.Message;
                HasError = true;
            }
            finally
            {
                IsApplyingNvidiaProfiles = false;
            }
        }

        public void SelectInstaller(string installerFilePath)
        {
            InstallerFilePath = installerFilePath;
            ExtractionFolderPath = RadeonInstallerExtractionService.DefaultExtractionFolderFor(installerFilePath);
        }

        public async Task ExtractAndLoadAsync()
        {
            HasError = false;
            IsBusy = true;
            CurrentStep = GpuWizardStep.Extracting;
            try
            {
                await Task.Run(() =>
                {
                    RadeonInstallerExtractionService.ValidateInstallerFile(InstallerFilePath);
                    RadeonInstallerExtractionService.ValidatePreExtractLocation(ExtractionFolderPath);
                    RadeonInstallerExtractionService.ExtractInstallerFiles(InstallerFilePath, ExtractionFolderPath);
                    RadeonInstallerExtractionService.ValidateExtractedLocation(ExtractionFolderPath);
                });

                LoadCustomizationLists();
                CurrentStep = GpuWizardStep.Customize;
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[GPU] Extracting the Radeon Software installer failed.");
                StatusMessage = exception.Message;
                HasError = true;
                CurrentStep = GpuWizardStep.SelectInstaller;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void LoadFromAlreadyExtracted()
        {
            HasError = false;
            try
            {
                RadeonInstallerExtractionService.ValidateExtractedLocation(ExtractionFolderPath);
                LoadCustomizationLists();
                CurrentStep = GpuWizardStep.Customize;
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[GPU] Reading an already-extracted Radeon Software installer failed.");
                StatusMessage = exception.Message;
                HasError = true;
            }
        }

        private void LoadCustomizationLists()
        {
            Packages.Clear();
            foreach (RadeonPackage package in RadeonPackageService.LoadPackages(ExtractionFolderPath))
            {
                Packages.Add(package);
            }

            ScheduledTasks.Clear();
            foreach (RadeonScheduledTask task in RadeonScheduledTaskService.LoadScheduledTasks(ExtractionFolderPath))
            {
                ScheduledTasks.Add(task);
            }

            DisplayComponents.Clear();
            foreach (RadeonDisplayComponent component in RadeonDisplayComponentService.LoadDisplayComponents(ExtractionFolderPath))
            {
                DisplayComponents.Add(component);
            }
        }

        public void SetAllPackages(bool keep)
        {
            foreach (RadeonPackage package in Packages)
            {
                package.Keep = keep;
            }
        }

        public void SetAllScheduledTasks(bool enabled)
        {
            foreach (RadeonScheduledTask task in ScheduledTasks)
            {
                task.Enabled = enabled;
            }
        }

        public void SetAllDisplayComponents(bool keep)
        {
            foreach (RadeonDisplayComponent component in DisplayComponents)
            {
                component.Keep = keep;
            }
        }

        public async Task ApplyAndInstallAsync()
        {
            HasError = false;
            IsBusy = true;
            try
            {
                await Task.Run(() =>
                {
                    foreach (RadeonPackage package in Packages.Where(package => !package.Keep).ToList())
                    {
                        RadeonPackageService.RemovePackage(package);
                    }

                    foreach (RadeonScheduledTask task in ScheduledTasks)
                    {
                        RadeonScheduledTaskService.SetScheduledTaskStatus(task);
                    }

                    RadeonDisplayComponentService.RemoveComponentsNotKeeping(ExtractionFolderPath, DisplayComponents);
                });

                LoadCustomizationLists();
                RadeonInstallerExtractionService.RunSetup(ExtractionFolderPath);
                CurrentStep = GpuWizardStep.Done;
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[GPU] Applying changes to the Radeon Software installer failed.");
                StatusMessage = exception.Message;
                HasError = true;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task ResetToDefaultsAsync()
        {
            HasError = false;
            IsBusy = true;
            try
            {
                await Task.Run(() =>
                {
                    RadeonPackageService.RestoreToDefault(ExtractionFolderPath);
                    RadeonScheduledTaskService.RestoreToDefault(ScheduledTasks);
                    RadeonDisplayComponentService.RestoreToDefault(ExtractionFolderPath);
                });

                LoadCustomizationLists();
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[GPU] Resetting the Radeon Software installer to defaults failed.");
                StatusMessage = exception.Message;
                HasError = true;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void StartOver()
        {
            InstallerFilePath = string.Empty;
            ExtractionFolderPath = string.Empty;
            HasError = false;
            Packages.Clear();
            ScheduledTasks.Clear();
            DisplayComponents.Clear();
            CurrentStep = GpuWizardStep.SelectInstaller;
        }
    }
}
