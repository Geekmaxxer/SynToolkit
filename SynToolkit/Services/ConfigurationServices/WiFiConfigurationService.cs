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
    /// <summary>
    /// Wi-Fi / WLAN services toggle matching SynergyOS Disable WIFI / Enable WI-FI scripts.
    /// </summary>
    public sealed class WiFiConfigurationService : IConfigurationService
    {
        private const string SynToolkitStoreKey = @"HKLM\SOFTWARE\SynToolkit\Services\WiFi";

        // SynergyOS 2. Configuration\Wi-Fi\*.bat
        private static readonly string[] ServiceNames =
        {
            "WlanSvc",
            "WwanSvc",
            "wcncsvc",
            "lmhosts"
        };

        private readonly ConfigurationStore _configurationStore;

        public WiFiConfigurationService(
            [FromKeyedServices("WiFi")] ConfigurationStore configurationStore)
        {
            _configurationStore = configurationStore;
        }

        public void Disable()
        {
            ApplyDisabledState();
        }

        public void Enable()
        {
            ApplyEnabledState();
        }

        public bool IsEnabled()
        {
            IReadOnlyList<string> installedServices = GetInstalledServices();
            EnsureAnyServiceIsInstalled(installedServices);
            return installedServices.All(serviceName =>
                ServiceHelper.TryGetStartupType(serviceName, out ServiceStartMode startMode) &&
                startMode != ServiceStartMode.Disabled);
        }

        private void ApplyDisabledState()
        {
            IReadOnlyList<string> installedServices = GetInstalledServices();
            EnsureAnyServiceIsInstalled(installedServices);

            foreach (string serviceName in installedServices)
            {
                CaptureOriginalState(serviceName);
            }

            foreach (string serviceName in installedServices)
            {
                ServiceHelper.SetStartupType(serviceName, ServiceStartMode.Disabled);
                ServiceHelper.StopService(serviceName, TimeSpan.FromSeconds(15));
            }

            _configurationStore.CurrentSetting = IsEnabled();
        }

        private void ApplyEnabledState()
        {
            IReadOnlyList<string> installedServices = GetInstalledServices();
            EnsureAnyServiceIsInstalled(installedServices);

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
            }

            foreach (string serviceName in installedServices)
            {
                ClearSnapshot(serviceName);
            }

            _configurationStore.CurrentSetting = IsEnabled();
        }

        private static IReadOnlyList<string> GetInstalledServices() =>
            ServiceNames.Where(ServiceHelper.ServiceExists).ToArray();

        private static void EnsureAnyServiceIsInstalled(IReadOnlyCollection<string> installedServices)
        {
            if (installedServices.Count == 0)
            {
                throw new InvalidOperationException(
                    "No supported Windows Wi-Fi services are installed on this computer.");
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
                // SynergyOS Enable WI-FI sets Start=2 (Automatic).
                return new ServiceSnapshot(ServiceStartMode.Automatic, false, false);
            }

            object storedMode = RegistryHelper.GetValue(
                SynToolkitStoreKey,
                SnapshotStartModeValue(serviceName));
            if (storedMode is not int modeValue || !Enum.IsDefined(typeof(ServiceStartMode), modeValue))
            {
                throw new InvalidOperationException(
                    $"The saved state for Wi-Fi service '{serviceName}' is invalid. No services were restored.");
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
