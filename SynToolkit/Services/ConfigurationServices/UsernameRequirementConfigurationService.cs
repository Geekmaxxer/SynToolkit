using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// Controls whether Windows requires typing a username at sign-in (vs. showing the last
    /// signed-in user), via the documented dontdisplaylastusername policy value.
    /// </summary>
    internal class UsernameRequirementConfigurationService : IConfigurationService
    {
        private const string POLICY_KEY_NAME = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
        private const string POLICY_VALUE_NAME = "dontdisplaylastusername";

        private readonly ConfigurationStore _usernameRequirementConfigurationStore;

        public UsernameRequirementConfigurationService(
            [FromKeyedServices("UsernameRequirement")] ConfigurationStore usernameRequirementConfigurationStore)
        {
            _usernameRequirementConfigurationStore = usernameRequirementConfigurationStore;
        }

        public void Enable()
        {
            RegistryHelper.SetValue(POLICY_KEY_NAME, POLICY_VALUE_NAME, 1, RegistryValueKind.DWord);
            _usernameRequirementConfigurationStore.CurrentSetting = IsEnabled();
        }

        public void Disable()
        {
            RegistryHelper.DeleteValue(POLICY_KEY_NAME, POLICY_VALUE_NAME);
            _usernameRequirementConfigurationStore.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            return RegistryHelper.IsMatch(POLICY_KEY_NAME, POLICY_VALUE_NAME, 1);
        }
    }
}
