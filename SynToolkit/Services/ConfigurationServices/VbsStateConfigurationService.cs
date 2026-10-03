using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using SynToolkit.Stores;
using SynToolkit.Utils;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// Virtualization-based Security / Memory Integrity toggle matching
    /// Installer\Synergy\Scripts\VBS\Disable VBS (default).bat and Enable VBS.bat.
    /// Does not touch bcdedit, Hyper-V/WSL/Sandbox features, or Credential Guard (LsaCfgFlags).
    /// A restart is required for changes to take effect.
    /// </summary>
    internal class VbsStateConfigurationService : IConfigurationService
    {
        private const string DeviceGuardKey =
            @"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard";
        private const string HvciKey =
            @"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity";
        private const string KernelKey =
            @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\kernel";

        private const string EnableVbsValueName = "EnableVirtualizationBasedSecurity";
        private const string RequirePlatformSecurityFeaturesValueName = "RequirePlatformSecurityFeatures";
        private const string DeviceGuardHvciValueName = "HypervisorEnforcedCodeIntegrity";
        private const string EnabledValueName = "Enabled";

        // Exploit-mitigation knobs kept for parity with the SynergyOS VBS scripts.
        // Separated from the DeviceGuard block so they can move to the mitigations toggle later.
        private const string DisableCfgExportSuppressionValueName = "DisableControlFlowGuardExportSuppression";
        private const string DisableCfgXfgValueName = "DisableControlFlowGuardXFG";
        private const string DisableExceptionChainValidationValueName = "DisableExceptionChainValidation";

        private readonly ConfigurationStore _configurationStore;

        public VbsStateConfigurationService(
            [FromKeyedServices("VbsState")] ConfigurationStore configurationStore)
        {
            _configurationStore = configurationStore;
        }

        public void Enable()
        {
            // Installer\Synergy\Scripts\VBS\Enable VBS.bat
            ApplyDeviceGuardValues(enabled: true);
            ApplyKernelMitigationValues(disableMitigations: false);
            App.ContentDialogCaller("restart");
            _configurationStore.CurrentSetting = IsEnabled();
        }

        public void Disable()
        {
            // Installer\Synergy\Scripts\VBS\Disable VBS (default).bat
            ApplyDeviceGuardValues(enabled: false);
            ApplyKernelMitigationValues(disableMitigations: true);
            App.ContentDialogCaller("restart");
            _configurationStore.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            // Disabled when EnableVirtualizationBasedSecurity == 0; otherwise treat as enabled.
            return !RegistryHelper.IsMatch(DeviceGuardKey, EnableVbsValueName, 0);
        }

        private static void ApplyDeviceGuardValues(bool enabled)
        {
            int value = enabled ? 1 : 0;
            RegistryHelper.SetValue(DeviceGuardKey, EnableVbsValueName, value, RegistryValueKind.DWord);
            RegistryHelper.SetValue(DeviceGuardKey, RequirePlatformSecurityFeaturesValueName, value, RegistryValueKind.DWord);
            RegistryHelper.SetValue(DeviceGuardKey, DeviceGuardHvciValueName, value, RegistryValueKind.DWord);
            RegistryHelper.SetValue(HvciKey, EnabledValueName, value, RegistryValueKind.DWord);
        }

        private static void ApplyKernelMitigationValues(bool disableMitigations)
        {
            int value = disableMitigations ? 1 : 0;
            RegistryHelper.SetValue(KernelKey, DisableCfgExportSuppressionValueName, value, RegistryValueKind.DWord);
            RegistryHelper.SetValue(KernelKey, DisableCfgXfgValueName, value, RegistryValueKind.DWord);
            RegistryHelper.SetValue(KernelKey, DisableExceptionChainValidationValueName, value, RegistryValueKind.DWord);
        }
    }
}
