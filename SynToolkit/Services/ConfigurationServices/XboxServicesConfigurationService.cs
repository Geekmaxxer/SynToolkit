using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using SynToolkit.Stores;
using SynToolkit.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;

namespace SynToolkit.Services.ConfigurationServices
{
    public sealed class XboxServicesConfigurationService : IConfigurationService
    {
        private const string SynToolkitStoreKey = @"HKLM\SOFTWARE\SynToolkit\Services\XboxServices";

        private static readonly string[] ServiceNames =
        {
            "XboxGipSvc",
            "XblAuthManager",
            "XblGameSave",
            "XboxNetApiSvc"
        };

        private readonly ConfigurationStore _configurationStore;

        public XboxServicesConfigurationService(
            [FromKeyedServices("XboxServices")] ConfigurationStore configurationStore)
        {
            _configurationStore = configurationStore;
        }

        public void Disable()
        {
            IReadOnlyList<string> installedServices = GetInstalledServices();
            EnsureAnyServiceIsInstalled(installedServices);

            // Capture every available service before changing any of them so a
            // later failure never leaves the earlier services without a revert.
            foreach (string serviceName in installedServices)
            {
                CaptureOriginalState(serviceName);
            }

            foreach (string serviceName in installedServices)
            {
                ServiceHelper.SetStartupType(serviceName, ServiceStartMode.Disabled);
                ServiceHelper.StopService(serviceName, TimeSpan.FromSeconds(15));
            }

            foreach (string serviceName in installedServices)
            {
                if (!ServiceHelper.IsStartupTypeMatch(serviceName, ServiceStartMode.Disabled) ||
                    !ServiceHelper.TryGetStatus(serviceName, out ServiceControllerStatus status) ||
                    status != ServiceControllerStatus.Stopped)
                {
                    throw new InvalidOperationException(
                        $"Windows did not fully disable Xbox service '{serviceName}'. Saved states were retained for revert.");
                }
            }

            _configurationStore.CurrentSetting = false;
        }

        public void Enable()
        {
            IReadOnlyList<string> installedServices = GetInstalledServices();
            EnsureAnyServiceIsInstalled(installedServices);

            // Validate every saved record before restoring the first service.
            Dictionary<string, ServiceSnapshot> restoreStates = installedServices.ToDictionary(
                serviceName => serviceName,
                ReadSnapshotOrDefault,
                StringComparer.OrdinalIgnoreCase);

            foreach ((string serviceName, ServiceSnapshot snapshot) in restoreStates)
            {
                ServiceHelper.SetStartupType(serviceName, snapshot.StartMode);
                if (snapshot.StartMode == ServiceStartMode.Automatic)
                {
                    ServiceHelper.SetDelayedAutoStart(serviceName, snapshot.DelayedAutoStart);
                }
                if (snapshot.WasRunning && snapshot.StartMode != ServiceStartMode.Disabled)
                {
                    ServiceHelper.StartService(serviceName, TimeSpan.FromSeconds(15));
                }
                else
                {
                    ServiceHelper.StopService(serviceName, TimeSpan.FromSeconds(15));
                }
            }

            foreach ((string serviceName, ServiceSnapshot snapshot) in restoreStates)
            {
                if (!ServiceHelper.IsStartupTypeMatch(serviceName, snapshot.StartMode) ||
                    (snapshot.StartMode == ServiceStartMode.Automatic &&
                     ServiceHelper.GetDelayedAutoStart(serviceName) != snapshot.DelayedAutoStart) ||
                    !ServiceHelper.TryGetStatus(serviceName, out ServiceControllerStatus status) ||
                    status != (snapshot.WasRunning
                        ? ServiceControllerStatus.Running
                        : ServiceControllerStatus.Stopped))
                {
                    throw new InvalidOperationException(
                        $"Windows did not fully restore Xbox service '{serviceName}'. Saved states were retained so the revert can be retried.");
                }
            }

            // Clear only services that still exist and were verified. A snapshot
            // for a temporarily missing service is deliberately retained.
            foreach (string serviceName in installedServices)
            {
                ClearSnapshot(serviceName);
            }

            _configurationStore.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            IReadOnlyList<string> installedServices = GetInstalledServices();
            EnsureAnyServiceIsInstalled(installedServices);
            return installedServices.All(serviceName =>
                ServiceHelper.TryGetStartupType(serviceName, out ServiceStartMode startMode) &&
                startMode != ServiceStartMode.Disabled);
        }

        private static IReadOnlyList<string> GetInstalledServices() =>
            ServiceNames.Where(ServiceHelper.ServiceExists).ToArray();

        private static void EnsureAnyServiceIsInstalled(IReadOnlyCollection<string> installedServices)
        {
            if (installedServices.Count == 0)
            {
                throw new InvalidOperationException(
                    "No supported Windows Xbox services are installed on this computer.");
            }
        }

        private static void CaptureOriginalState(string serviceName)
        {
            if (HasSnapshot(serviceName))
            {
                return;
            }

            ServiceStartMode startMode = ServiceHelper.GetStartupType(serviceName);
            bool delayedAutoStart = startMode == ServiceStartMode.Automatic &&
                ServiceHelper.GetDelayedAutoStart(serviceName);
            bool wasRunning = ServiceHelper.TryGetStatus(serviceName, out ServiceControllerStatus status) &&
                status == ServiceControllerStatus.Running;

            RegistryHelper.SetValue(
                SynToolkitStoreKey,
                SnapshotStartModeValue(serviceName),
                (int)startMode,
                RegistryValueKind.DWord);
            RegistryHelper.SetValue(
                SynToolkitStoreKey,
                SnapshotWasRunningValue(serviceName),
                wasRunning ? 1 : 0,
                RegistryValueKind.DWord);
            RegistryHelper.SetValue(
                SynToolkitStoreKey,
                SnapshotDelayedAutoStartValue(serviceName),
                delayedAutoStart ? 1 : 0,
                RegistryValueKind.DWord);
            RegistryHelper.SetValue(
                SynToolkitStoreKey,
                SnapshotPresentValue(serviceName),
                1,
                RegistryValueKind.DWord);
        }

        private static ServiceSnapshot ReadSnapshotOrDefault(string serviceName)
        {
            if (!HasSnapshot(serviceName))
            {
                // Manual is the safe Windows 11 fallback when the service was
                // not previously changed by SynToolkit.
                return new ServiceSnapshot(ServiceStartMode.Manual, false, false);
            }

            object storedMode = RegistryHelper.GetValue(
                SynToolkitStoreKey,
                SnapshotStartModeValue(serviceName));
            if (storedMode is not int modeValue || !Enum.IsDefined(typeof(ServiceStartMode), modeValue))
            {
                throw new InvalidOperationException(
                    $"The saved state for Xbox service '{serviceName}' is invalid. No services were restored.");
            }

            return new ServiceSnapshot(
                (ServiceStartMode)modeValue,
                RegistryHelper.IsMatch(
                    SynToolkitStoreKey,
                    SnapshotWasRunningValue(serviceName),
                    1),
                (ServiceStartMode)modeValue == ServiceStartMode.Automatic &&
                    RegistryHelper.IsMatch(
                        SynToolkitStoreKey,
                        SnapshotDelayedAutoStartValue(serviceName),
                        1));
        }

        private static bool HasSnapshot(string serviceName) =>
            RegistryHelper.IsMatch(
                SynToolkitStoreKey,
                SnapshotPresentValue(serviceName),
                1);

        private static void ClearSnapshot(string serviceName)
        {
            RegistryHelper.DeleteValue(SynToolkitStoreKey, SnapshotPresentValue(serviceName));
            RegistryHelper.DeleteValue(SynToolkitStoreKey, SnapshotStartModeValue(serviceName));
            RegistryHelper.DeleteValue(SynToolkitStoreKey, SnapshotWasRunningValue(serviceName));
            RegistryHelper.DeleteValue(SynToolkitStoreKey, SnapshotDelayedAutoStartValue(serviceName));
        }

        private static string SnapshotPresentValue(string serviceName) => $"{serviceName}_SnapshotPresent";
        private static string SnapshotStartModeValue(string serviceName) => $"{serviceName}_StartMode";
        private static string SnapshotWasRunningValue(string serviceName) => $"{serviceName}_WasRunning";
        private static string SnapshotDelayedAutoStartValue(string serviceName) => $"{serviceName}_DelayedAutoStart";

        private sealed record ServiceSnapshot(
            ServiceStartMode StartMode,
            bool WasRunning,
            bool DelayedAutoStart);
    }
}
