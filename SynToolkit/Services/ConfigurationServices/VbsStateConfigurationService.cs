using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using SynToolkit.Stores;
using SynToolkit.Utils;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// Virtualization-based Security toggle matching SynergyOS Disable VBS / Enable VBS scripts.
    /// A restart is required for changes to take effect.
    /// </summary>
    internal class VbsStateConfigurationService : IConfigurationService
    {
        private const string DeviceGuardKey =
            @"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard";
        private const string HvciKey =
            @"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity";
        private const string KernelShadowStacksKey =
            @"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\KernelShadowStacks";
        private const string CredentialGuardKey =
            @"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\CredentialGuard";
        private const string LsaKey =
            @"HKLM\SYSTEM\CurrentControlSet\Control\Lsa";

        private const string EnableVbsValueName = "EnableVirtualizationBasedSecurity";
        private const string EnabledValueName = "Enabled";
        private const string RunAsPplValueName = "RunAsPPL";

        private readonly ConfigurationStore _configurationStore;

        public VbsStateConfigurationService(
            [FromKeyedServices("VbsState")] ConfigurationStore configurationStore)
        {
            _configurationStore = configurationStore;
        }

        public void Enable()
        {
            // SynergyOS 2. Configuration\VBS\Enable VBS.bat
            RegistryHelper.SetValue(HvciKey, EnabledValueName, 1, RegistryValueKind.DWord);
            RegistryHelper.SetValue(DeviceGuardKey, EnableVbsValueName, 1, RegistryValueKind.DWord);
            RegistryHelper.SetValue(KernelShadowStacksKey, EnabledValueName, 1, RegistryValueKind.DWord);
            RegistryHelper.SetValue(LsaKey, RunAsPplValueName, 1, RegistryValueKind.DWord);
            RegistryHelper.SetValue(CredentialGuardKey, EnabledValueName, 1, RegistryValueKind.DWord);
            App.ContentDialogCaller("restart");
            _configurationStore.CurrentSetting = IsEnabled();
        }

        public void Disable()
        {
            // SynergyOS 2. Configuration\VBS\Disable VBS (default).bat
            RegistryHelper.SetValue(HvciKey, EnabledValueName, 0, RegistryValueKind.DWord);
            RegistryHelper.SetValue(DeviceGuardKey, EnableVbsValueName, 0, RegistryValueKind.DWord);
            RegistryHelper.SetValue(KernelShadowStacksKey, EnabledValueName, 0, RegistryValueKind.DWord);
            RegistryHelper.SetValue(LsaKey, RunAsPplValueName, 0, RegistryValueKind.DWord);
            RegistryHelper.SetValue(CredentialGuardKey, EnabledValueName, 0, RegistryValueKind.DWord);
            App.ContentDialogCaller("restart");
            _configurationStore.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            // SynergyOS Enable VBS sets EnableVirtualizationBasedSecurity=1;
            // Disable VBS (default) sets it to 0.
            return RegistryHelper.IsMatch(DeviceGuardKey, EnableVbsValueName, 1);
        }
    }
}
