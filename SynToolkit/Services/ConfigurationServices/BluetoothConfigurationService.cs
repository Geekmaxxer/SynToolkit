using Microsoft.Extensions.DependencyInjection;
using SynToolkit.Stores;
using SynToolkit.Utils;
using System;
using System.ServiceProcess;

namespace SynToolkit.Services.ConfigurationServices
{
    public class BluetoothConfigurationService : IConfigurationService
    {
        private const string BLUETOOTH_SUPPORT_SERVICE_NAME = "bthserv";
        private const string SYNTOOLKIT_STORE_KEY_NAME = @"HKLM\SOFTWARE\SynToolkit\Services\Bluetooth";
        private const string SNAPSHOT_PRESENT_VALUE_NAME = "SnapshotPresent";
        private const string PREVIOUS_START_MODE_VALUE_NAME = "PreviousStartMode";
        private const string PREVIOUS_RUNNING_VALUE_NAME = "PreviousRunning";
        private const string PREVIOUS_DELAYED_AUTO_START_VALUE_NAME = "PreviousDelayedAutoStart";

        private readonly ConfigurationStore _bluetoothConfigurationStore;

        public BluetoothConfigurationService(
            [FromKeyedServices("Bluetooth")] ConfigurationStore bluetoothConfigurationStore)
        {
            _bluetoothConfigurationStore = bluetoothConfigurationStore;
        }

        public void Disable()
        {
            CaptureOriginalState();
            ServiceHelper.SetStartupType(BLUETOOTH_SUPPORT_SERVICE_NAME, ServiceStartMode.Disabled);
            StopServiceIfRunning(BLUETOOTH_SUPPORT_SERVICE_NAME);
            UpdateDetectedState(expectedState: false);
        }

        public void Enable()
        {
            ServiceStartMode restoreMode = ServiceStartMode.Manual;
            bool restoreRunning = false;
            bool hasSnapshot = RegistryHelper.IsMatch(
                SYNTOOLKIT_STORE_KEY_NAME,
                SNAPSHOT_PRESENT_VALUE_NAME,
                1);

            if (hasSnapshot)
            {
                object storedMode = RegistryHelper.GetValue(
                    SYNTOOLKIT_STORE_KEY_NAME,
                    PREVIOUS_START_MODE_VALUE_NAME);
                if (storedMode is not int modeValue || !Enum.IsDefined(typeof(ServiceStartMode), modeValue))
                {
                    throw new InvalidOperationException(
                        "The saved Bluetooth service state is invalid. No changes were made.");
                }

                restoreMode = (ServiceStartMode)modeValue;
                restoreRunning = RegistryHelper.IsMatch(
                    SYNTOOLKIT_STORE_KEY_NAME,
                    PREVIOUS_RUNNING_VALUE_NAME,
                    1);
            }

            bool restoreDelayedAutoStart = restoreMode == ServiceStartMode.Automatic &&
                RegistryHelper.IsMatch(
                    SYNTOOLKIT_STORE_KEY_NAME,
                    PREVIOUS_DELAYED_AUTO_START_VALUE_NAME,
                    1);

            // Manual is the Windows 11 fallback when no SynToolkit snapshot exists.
            ServiceHelper.SetStartupType(BLUETOOTH_SUPPORT_SERVICE_NAME, restoreMode);
            if (restoreMode == ServiceStartMode.Automatic)
            {
                ServiceHelper.SetDelayedAutoStart(BLUETOOTH_SUPPORT_SERVICE_NAME, restoreDelayedAutoStart);
            }
            if (restoreRunning && restoreMode != ServiceStartMode.Disabled)
            {
                ServiceHelper.StartService(BLUETOOTH_SUPPORT_SERVICE_NAME, TimeSpan.FromSeconds(15));
            }

            UpdateDetectedState(expectedState: restoreMode != ServiceStartMode.Disabled);
            if (hasSnapshot)
            {
                ClearOriginalState();
            }
        }

        public bool IsEnabled()
        {
            return ServiceHelper.GetStartupType(BLUETOOTH_SUPPORT_SERVICE_NAME) != ServiceStartMode.Disabled;
        }

        private void UpdateDetectedState(bool expectedState)
        {
            bool detectedState = IsEnabled();
            _bluetoothConfigurationStore.CurrentSetting = detectedState;

            if (detectedState != expectedState)
            {
                throw new InvalidOperationException("Windows did not accept the requested Bluetooth service state.");
            }
        }

        private static void CaptureOriginalState()
        {
            if (RegistryHelper.IsMatch(
                SYNTOOLKIT_STORE_KEY_NAME,
                SNAPSHOT_PRESENT_VALUE_NAME,
                1))
            {
                return;
            }

            ServiceStartMode startupType = ServiceHelper.GetStartupType(BLUETOOTH_SUPPORT_SERVICE_NAME);
            bool delayedAutoStart = startupType == ServiceStartMode.Automatic &&
                ServiceHelper.GetDelayedAutoStart(BLUETOOTH_SUPPORT_SERVICE_NAME);
            bool wasRunning = ServiceHelper.TryGetStatus(
                BLUETOOTH_SUPPORT_SERVICE_NAME,
                out ServiceControllerStatus status) &&
                status == ServiceControllerStatus.Running;

            RegistryHelper.SetValue(
                SYNTOOLKIT_STORE_KEY_NAME,
                PREVIOUS_START_MODE_VALUE_NAME,
                (int)startupType,
                Microsoft.Win32.RegistryValueKind.DWord);
            RegistryHelper.SetValue(
                SYNTOOLKIT_STORE_KEY_NAME,
                PREVIOUS_RUNNING_VALUE_NAME,
                wasRunning ? 1 : 0,
                Microsoft.Win32.RegistryValueKind.DWord);
            RegistryHelper.SetValue(
                SYNTOOLKIT_STORE_KEY_NAME,
                PREVIOUS_DELAYED_AUTO_START_VALUE_NAME,
                delayedAutoStart ? 1 : 0,
                Microsoft.Win32.RegistryValueKind.DWord);
            RegistryHelper.SetValue(
                SYNTOOLKIT_STORE_KEY_NAME,
                SNAPSHOT_PRESENT_VALUE_NAME,
                1,
                Microsoft.Win32.RegistryValueKind.DWord);
        }

        private static void ClearOriginalState()
        {
            RegistryHelper.DeleteValue(SYNTOOLKIT_STORE_KEY_NAME, SNAPSHOT_PRESENT_VALUE_NAME);
            RegistryHelper.DeleteValue(SYNTOOLKIT_STORE_KEY_NAME, PREVIOUS_START_MODE_VALUE_NAME);
            RegistryHelper.DeleteValue(SYNTOOLKIT_STORE_KEY_NAME, PREVIOUS_RUNNING_VALUE_NAME);
            RegistryHelper.DeleteValue(SYNTOOLKIT_STORE_KEY_NAME, PREVIOUS_DELAYED_AUTO_START_VALUE_NAME);
        }

        private static void StopServiceIfRunning(string serviceName)
        {
            using ServiceController controller = new(serviceName);
            controller.Refresh();

            if (controller.Status is ServiceControllerStatus.Stopped)
            {
                return;
            }

            if (controller.Status is not ServiceControllerStatus.StopPending)
            {
                controller.Stop();
            }

            controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
        }
    }
}
