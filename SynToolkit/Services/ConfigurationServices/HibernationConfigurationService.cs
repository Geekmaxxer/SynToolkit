using Microsoft.Extensions.DependencyInjection;
using SynToolkit.Stores;
using SynToolkit.Utils;
using System;

namespace SynToolkit.Services.ConfigurationServices
{
    internal class HibernationConfigurationService : IConfigurationService
    {
        private const string POWER_KEY_NAME = @"HKLM\SYSTEM\CurrentControlSet\Control\Power";
        private const string HIBERNATE_ENABLED_VALUE_NAME = "HibernateEnabled";
        private const string HIBERFILE_TYPE_VALUE_NAME = "HiberFileType";
        private const int HIBERFILE_TYPE_FULL = 2;
        private const string HIBERNATION_BUTTON_OPTION_KEY_NAME =
            @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\FlyoutMenuSettings";
        private const string SHOW_HIBERNATION_BUTTON_VALUE_NAME = "ShowHibernateButton";

        private readonly ConfigurationStore _hibernationConfigurationStore;

        public HibernationConfigurationService(
            [FromKeyedServices("Hibernation")] ConfigurationStore hibernationConfigurationStore)
        {
            _hibernationConfigurationStore = hibernationConfigurationStore;
        }

        public void Disable()
        {
            CommandResult result = CommandPromptHelper.RunProcessResult(
                "powercfg.exe",
                ["/hibernate", "off"]);

            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"Windows could not disable hibernation: {result.CombinedOutput}");
            }

            RegistryHelper.SetValue(
                HIBERNATION_BUTTON_OPTION_KEY_NAME,
                SHOW_HIBERNATION_BUTTON_VALUE_NAME,
                0);

            UpdateDetectedState(expectedState: false);
        }

        public void Enable()
        {
            CommandResult result = CommandPromptHelper.RunProcessResult(
                "powercfg.exe",
                ["/hibernate", "/type", "full"]);

            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"Windows could not enable hibernation: {result.CombinedOutput}");
            }

            RegistryHelper.SetValue(
                HIBERNATION_BUTTON_OPTION_KEY_NAME,
                SHOW_HIBERNATION_BUTTON_VALUE_NAME,
                1);

            UpdateDetectedState(expectedState: true);
        }

        public bool IsEnabled()
        {
            object enabledValue = RegistryHelper.GetValue(POWER_KEY_NAME, HIBERNATE_ENABLED_VALUE_NAME);
            object hiberFileTypeValue = RegistryHelper.GetValue(POWER_KEY_NAME, HIBERFILE_TYPE_VALUE_NAME);

            if (enabledValue is null || hiberFileTypeValue is null)
            {
                throw new InvalidOperationException("Windows hibernation state is unavailable on this installation.");
            }

            return Convert.ToInt32(enabledValue) != 0
                && Convert.ToInt32(hiberFileTypeValue) == HIBERFILE_TYPE_FULL;
        }

        private void UpdateDetectedState(bool expectedState)
        {
            bool detectedState = IsEnabled();
            _hibernationConfigurationStore.CurrentSetting = detectedState;

            if (detectedState != expectedState)
            {
                throw new InvalidOperationException("Windows did not accept the requested hibernation state.");
            }
        }
    }
}
