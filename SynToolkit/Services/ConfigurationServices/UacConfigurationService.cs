using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// User Account Control (UAC) toggle matching SynergyOS playbook scripts.
    /// Disabled state sets EnableLUA/PromptOnSecureDesktop/ConsentPromptBehaviorAdmin
    /// to the SynergyOS "Disable UAC" values; enabled restores stock Windows defaults.
    /// A restart is required for changes to take effect.
    /// </summary>
    internal class UacConfigurationService : IConfigurationService
    {
        private const string POLICY_KEY_NAME = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
        private const string ENABLE_LUA_VALUE_NAME = "EnableLUA";
        private const string PROMPT_ON_SECURE_DESKTOP_VALUE_NAME = "PromptOnSecureDesktop";
        private const string CONSENT_PROMPT_BEHAVIOR_ADMIN_VALUE_NAME = "ConsentPromptBehaviorAdmin";
        private const string VALIDATE_ADMIN_CODE_SIGNATURES_VALUE_NAME = "ValidateAdminCodeSignatures";

        private readonly ConfigurationStore _uacConfigurationStore;

        public UacConfigurationService(
            [FromKeyedServices("UAC")] ConfigurationStore uacConfigurationStore)
        {
            _uacConfigurationStore = uacConfigurationStore;
        }

        public void Enable()
        {
            // Stock Windows UAC defaults (SynergyOS "Enable UAC (default).bat").
            RegistryHelper.SetValue(POLICY_KEY_NAME, ENABLE_LUA_VALUE_NAME, 1, RegistryValueKind.DWord);
            RegistryHelper.SetValue(POLICY_KEY_NAME, PROMPT_ON_SECURE_DESKTOP_VALUE_NAME, 1, RegistryValueKind.DWord);
            RegistryHelper.SetValue(POLICY_KEY_NAME, CONSENT_PROMPT_BEHAVIOR_ADMIN_VALUE_NAME, 5, RegistryValueKind.DWord);
            RegistryHelper.SetValue(POLICY_KEY_NAME, VALIDATE_ADMIN_CODE_SIGNATURES_VALUE_NAME, 0, RegistryValueKind.DWord);
            App.ContentDialogCaller("restart");
            _uacConfigurationStore.CurrentSetting = IsEnabled();
        }

        public void Disable()
        {
            // SynergyOS playbook disable-uac option ("Disable UAC.bat").
            RegistryHelper.SetValue(POLICY_KEY_NAME, ENABLE_LUA_VALUE_NAME, 0, RegistryValueKind.DWord);
            RegistryHelper.SetValue(POLICY_KEY_NAME, PROMPT_ON_SECURE_DESKTOP_VALUE_NAME, 0, RegistryValueKind.DWord);
            RegistryHelper.SetValue(POLICY_KEY_NAME, CONSENT_PROMPT_BEHAVIOR_ADMIN_VALUE_NAME, 0, RegistryValueKind.DWord);
            RegistryHelper.SetValue(POLICY_KEY_NAME, VALIDATE_ADMIN_CODE_SIGNATURES_VALUE_NAME, 0, RegistryValueKind.DWord);
            App.ContentDialogCaller("restart");
            _uacConfigurationStore.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            // SynergyOS marks UAC off by setting EnableLUA=0. Any other state
            // (1, or absent / Windows default) means UAC is on.
            return !RegistryHelper.IsMatch(POLICY_KEY_NAME, ENABLE_LUA_VALUE_NAME, 0);
        }
    }
}
