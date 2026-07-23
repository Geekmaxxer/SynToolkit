using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System;
using System.ServiceProcess;

namespace SynToolkit.Services.ConfigurationServices
{
    public class NvidiaDispayContainerConfigurationService : IConfigurationService
    {

        private const string NVIDIA_DISPLAY_CONTAINER_SERVICE_NAME = "NVDisplay.ContainerLocalSystem";
        private const string SNAPSHOT_KEY_NAME = @"HKLM\SOFTWARE\SynToolkit\Services\NvidiaDisplayContainer";
        private const string SNAPSHOT_STARTUP_VALUE_NAME = "PreviousStartupType";
        private const string SNAPSHOT_RUNNING_VALUE_NAME = "PreviousWasRunning";

        private readonly ConfigurationStore _nvidiaDispayContainerConfigurationService;

        public NvidiaDispayContainerConfigurationService(
            [FromKeyedServices("NvidiaDispayContainer")]  ConfigurationStore nvidiaDispayContainerConfigurationService)
        {
            _nvidiaDispayContainerConfigurationService = nvidiaDispayContainerConfigurationService;
        }
        public void Disable()
        {
            ServiceSnapshot originalState = CaptureCurrentState();
            SaveSnapshotIfMissing(originalState);

            try
            {
                ServiceHelper.StopService(NVIDIA_DISPLAY_CONTAINER_SERVICE_NAME);
                ServiceHelper.SetStartupType(NVIDIA_DISPLAY_CONTAINER_SERVICE_NAME, ServiceStartMode.Disabled);
                VerifyState(new ServiceSnapshot(ServiceStartMode.Disabled, false));
            }
            catch
            {
                TryRestoreState(originalState, "rolling back a failed NVIDIA Display Container disable");
                throw;
            }

            _nvidiaDispayContainerConfigurationService.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            ServiceSnapshot originalState = CaptureCurrentState();
            ServiceSnapshot targetState = TryReadSnapshot(out ServiceSnapshot savedState)
                ? savedState
                : new ServiceSnapshot(ServiceStartMode.Automatic, true);

            try
            {
                RestoreState(targetState);
                VerifyState(targetState);
                TryClearSnapshot();
            }
            catch
            {
                TryRestoreState(originalState, "rolling back a failed NVIDIA Display Container restore");
                throw;
            }

            _nvidiaDispayContainerConfigurationService.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            if (!GpuDetectionService.HasNvidiaGpu())
            {
                throw new InvalidOperationException("No NVIDIA GPU was detected on this system.");
            }

            if (!ServiceHelper.TryGetStartupType(
                    NVIDIA_DISPLAY_CONTAINER_SERVICE_NAME,
                    out ServiceStartMode startupType))
            {
                throw new InvalidOperationException(
                    $"The NVIDIA Display Container service '{NVIDIA_DISPLAY_CONTAINER_SERVICE_NAME}' is not installed or its startup type cannot be read.");
            }

            if (!ServiceHelper.TryGetStatus(
                    NVIDIA_DISPLAY_CONTAINER_SERVICE_NAME,
                    out ServiceControllerStatus status))
            {
                throw new InvalidOperationException(
                    $"The NVIDIA Display Container service '{NVIDIA_DISPLAY_CONTAINER_SERVICE_NAME}' status cannot be read.");
            }

            return startupType != ServiceStartMode.Disabled
                && status == ServiceControllerStatus.Running;
        }

        private static ServiceSnapshot CaptureCurrentState()
        {
            if (!ServiceHelper.TryGetStartupType(
                    NVIDIA_DISPLAY_CONTAINER_SERVICE_NAME,
                    out ServiceStartMode startupType)
                || !ServiceHelper.TryGetStatus(
                    NVIDIA_DISPLAY_CONTAINER_SERVICE_NAME,
                    out ServiceControllerStatus status))
            {
                throw new InvalidOperationException(
                    $"The NVIDIA Display Container service '{NVIDIA_DISPLAY_CONTAINER_SERVICE_NAME}' state cannot be read.");
            }

            return new ServiceSnapshot(
                startupType,
                status == ServiceControllerStatus.Running);
        }

        private static void SaveSnapshotIfMissing(ServiceSnapshot state)
        {
            if (TryReadSnapshot(out _))
            {
                return;
            }

            RegistryHelper.SetValue(
                SNAPSHOT_KEY_NAME,
                SNAPSHOT_STARTUP_VALUE_NAME,
                (int)state.StartupType,
                RegistryValueKind.DWord);
            RegistryHelper.SetValue(
                SNAPSHOT_KEY_NAME,
                SNAPSHOT_RUNNING_VALUE_NAME,
                state.WasRunning ? 1 : 0,
                RegistryValueKind.DWord);
        }

        private static bool TryReadSnapshot(out ServiceSnapshot state)
        {
            state = default;
            try
            {
                object startupValue = RegistryHelper.GetValue(SNAPSHOT_KEY_NAME, SNAPSHOT_STARTUP_VALUE_NAME);
                object runningValue = RegistryHelper.GetValue(SNAPSHOT_KEY_NAME, SNAPSHOT_RUNNING_VALUE_NAME);
                if (startupValue is null || runningValue is null)
                {
                    return false;
                }

                int startupNumber = Convert.ToInt32(startupValue);
                int runningNumber = Convert.ToInt32(runningValue);
                if (!Enum.IsDefined(typeof(ServiceStartMode), startupNumber)
                    || runningNumber is not (0 or 1))
                {
                    return false;
                }

                state = new ServiceSnapshot((ServiceStartMode)startupNumber, runningNumber == 1);
                return true;
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "Unable to read the saved NVIDIA Display Container service state.");
                return false;
            }
        }

        private static void TryClearSnapshot()
        {
            try
            {
                RegistryHelper.DeleteValue(SNAPSHOT_KEY_NAME, SNAPSHOT_STARTUP_VALUE_NAME);
                RegistryHelper.DeleteValue(SNAPSHOT_KEY_NAME, SNAPSHOT_RUNNING_VALUE_NAME);
            }
            catch (Exception exception)
            {
                // The service has already been restored successfully. Do not undo
                // that success merely because stale recovery metadata could not be removed.
                App.logger.Warn(exception, "Unable to clear the restored NVIDIA Display Container service snapshot.");
            }
        }

        private static void RestoreState(ServiceSnapshot state)
        {
            if (state.WasRunning)
            {
                // A disabled service cannot be started. Use Manual temporarily for
                // the unusual but valid case where it was running while disabled.
                ServiceStartMode startupBeforeStart = state.StartupType == ServiceStartMode.Disabled
                    ? ServiceStartMode.Manual
                    : state.StartupType;
                ServiceHelper.SetStartupType(NVIDIA_DISPLAY_CONTAINER_SERVICE_NAME, startupBeforeStart);
                ServiceHelper.StartService(NVIDIA_DISPLAY_CONTAINER_SERVICE_NAME);
                if (startupBeforeStart != state.StartupType)
                {
                    ServiceHelper.SetStartupType(NVIDIA_DISPLAY_CONTAINER_SERVICE_NAME, state.StartupType);
                }
            }
            else
            {
                ServiceHelper.StopService(NVIDIA_DISPLAY_CONTAINER_SERVICE_NAME);
                ServiceHelper.SetStartupType(NVIDIA_DISPLAY_CONTAINER_SERVICE_NAME, state.StartupType);
            }
        }

        private static void VerifyState(ServiceSnapshot expected)
        {
            ServiceSnapshot actual = CaptureCurrentState();
            if (actual != expected)
            {
                throw new InvalidOperationException(
                    $"The NVIDIA Display Container service did not retain its requested startup/running state.");
            }
        }

        private static void TryRestoreState(ServiceSnapshot state, string operation)
        {
            try
            {
                RestoreState(state);
                VerifyState(state);
            }
            catch (Exception rollbackException)
            {
                App.logger.Error(rollbackException, $"Unable to restore the NVIDIA Display Container service while {operation}.");
            }
        }

        private readonly record struct ServiceSnapshot(
            ServiceStartMode StartupType,
            bool WasRunning);
    }
}
