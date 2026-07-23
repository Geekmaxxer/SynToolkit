using Microsoft.Extensions.DependencyInjection;
using SynToolkit.Stores;
using SynToolkit.Utils;
using System;
using Microsoft.Win32;

namespace SynToolkit.Services.ConfigurationServices
{
    internal class SleepConfigurationService : IConfigurationService
    {
        // GUID_SLEEP_SUBGROUP and GUID_STANDBY_TIMEOUT from the Windows power policy API.
        private static readonly Guid SleepSubgroup = new("238c9fa8-0aad-41ed-83f4-97be242c8f20");
        private static readonly Guid StandbyTimeout = new("29f6c1db-86da-48c5-9fdb-f2b67b1f44da");

        private const uint DEFAULT_AC_TIMEOUT_SECONDS = 30 * 60;
        private const uint DEFAULT_DC_TIMEOUT_SECONDS = 15 * 60;
        private const string SYNTOOLKIT_STORE_KEY_NAME = @"HKLM\SOFTWARE\SynToolkit\Services\Sleep";
        private const string PREVIOUS_AC_TIMEOUT_VALUE_NAME = "PreviousAcTimeoutSeconds";
        private const string PREVIOUS_DC_TIMEOUT_VALUE_NAME = "PreviousDcTimeoutSeconds";

        private readonly ConfigurationStore _sleepConfigurationStore;

        public SleepConfigurationService(
            [FromKeyedServices("Sleep")] ConfigurationStore sleepConfigurationStore)
        {
            _sleepConfigurationStore = sleepConfigurationStore;
        }

        public void Disable()
        {
            (uint acValue, uint dcValue) = PowerSettingsHelper.ReadCurrentValues(SleepSubgroup, StandbyTimeout);
            PreserveNonzeroTimeout(PREVIOUS_AC_TIMEOUT_VALUE_NAME, acValue);
            PreserveNonzeroTimeout(PREVIOUS_DC_TIMEOUT_VALUE_NAME, dcValue);

            PowerSettingsHelper.WriteCurrentValues(SleepSubgroup, StandbyTimeout, 0, 0);
            UpdateDetectedState(expectedState: false);
        }

        public void Enable()
        {
            (uint acValue, uint dcValue) = PowerSettingsHelper.ReadCurrentValues(SleepSubgroup, StandbyTimeout);

            PowerSettingsHelper.WriteCurrentValues(
                SleepSubgroup,
                StandbyTimeout,
                acValue > 0
                    ? acValue
                    : ReadPreservedTimeout(PREVIOUS_AC_TIMEOUT_VALUE_NAME, DEFAULT_AC_TIMEOUT_SECONDS),
                dcValue > 0
                    ? dcValue
                    : ReadPreservedTimeout(PREVIOUS_DC_TIMEOUT_VALUE_NAME, DEFAULT_DC_TIMEOUT_SECONDS));

            UpdateDetectedState(expectedState: true);
        }

        public bool IsEnabled()
        {
            (uint acValue, uint dcValue) = PowerSettingsHelper.ReadCurrentValues(SleepSubgroup, StandbyTimeout);
            return acValue > 0 && dcValue > 0;
        }

        private static void PreserveNonzeroTimeout(string valueName, uint value)
        {
            if (value > 0)
            {
                RegistryHelper.SetValue(
                    SYNTOOLKIT_STORE_KEY_NAME,
                    valueName,
                    value,
                    RegistryValueKind.DWord);
            }
        }

        private static uint ReadPreservedTimeout(string valueName, uint fallback)
        {
            object value = RegistryHelper.GetValue(SYNTOOLKIT_STORE_KEY_NAME, valueName);

            try
            {
                uint timeout = Convert.ToUInt32(value);
                return timeout > 0 ? timeout : fallback;
            }
            catch (Exception exception) when (exception is FormatException
                or InvalidCastException
                or OverflowException)
            {
                return fallback;
            }
        }

        private void UpdateDetectedState(bool expectedState)
        {
            bool detectedState = IsEnabled();
            _sleepConfigurationStore.CurrentSetting = detectedState;

            if (detectedState != expectedState)
            {
                throw new InvalidOperationException("Windows did not accept the requested automatic sleep state.");
            }
        }
    }
}
