using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using SynToolkit.Stores;
using SynToolkit.Utils;
using System;

namespace SynToolkit.Services.ConfigurationServices
{
    public class FsoAndGameBarConfigurationService : IConfigurationService
    {
        private const string SynToolkitStoreKey = @"HKLM\SOFTWARE\SynToolkit\Services\FSOGameBar";
        private const string GameConfigStoreKey = @"HKCU\System\GameConfigStore";
        private const string GameDvrKey = @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR";
        private const string GameDvrPolicyKey = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR";
        private const string StateValue = "state";
        private const string BackupCapturedValue = "BackupCaptured";

        private readonly ConfigurationStore _configurationStore;

        public FsoAndGameBarConfigurationService(
            [FromKeyedServices("FsoAndGameBar")] ConfigurationStore configurationStore)
        {
            _configurationStore = configurationStore;
        }

        public void Disable()
        {
            CaptureOriginalSettings();
            RegistryHelper.SetValue(GameConfigStoreKey, "GameDVR_Enabled", 0, RegistryValueKind.DWord);
            RegistryHelper.SetValue(GameConfigStoreKey, "GameDVR_FSEBehaviorMode", 2, RegistryValueKind.DWord);
            RegistryHelper.SetValue(GameDvrKey, "AppCaptureEnabled", 0, RegistryValueKind.DWord);
            RegistryHelper.SetValue(GameDvrPolicyKey, "AllowGameDVR", 0, RegistryValueKind.DWord);
            RegistryHelper.SetValue(SynToolkitStoreKey, StateValue, 0, RegistryValueKind.DWord);
            bool detectedState = IsEnabled();
            _configurationStore.CurrentSetting = detectedState;
            if (detectedState)
            {
                throw new InvalidOperationException(
                    "Windows did not accept all requested Game Bar/FSO changes. The original settings remain available for revert.");
            }
        }

        public void Enable()
        {
            bool hasSnapshot = RegistryHelper.IsMatch(SynToolkitStoreKey, BackupCapturedValue, 1);
            if (hasSnapshot)
            {
                DwordSnapshot gameDvrEnabled = ReadSnapshot("GameDvrEnabled");
                DwordSnapshot fsoBehavior = ReadSnapshot("FsoBehavior");
                DwordSnapshot appCaptureEnabled = ReadSnapshot("AppCaptureEnabled");
                DwordSnapshot allowGameDvr = ReadSnapshot("AllowGameDvr");

                RestoreSnapshot(GameConfigStoreKey, "GameDVR_Enabled", gameDvrEnabled);
                RestoreSnapshot(GameConfigStoreKey, "GameDVR_FSEBehaviorMode", fsoBehavior);
                RestoreSnapshot(GameDvrKey, "AppCaptureEnabled", appCaptureEnabled);
                RestoreSnapshot(GameDvrPolicyKey, "AllowGameDVR", allowGameDvr);
            }
            else
            {
                // Missing values use the Windows 11 defaults when no SynToolkit
                // snapshot is available from an earlier disable operation.
                RegistryHelper.DeleteValue(GameConfigStoreKey, "GameDVR_Enabled");
                RegistryHelper.DeleteValue(GameConfigStoreKey, "GameDVR_FSEBehaviorMode");
                RegistryHelper.DeleteValue(GameDvrKey, "AppCaptureEnabled");
                RegistryHelper.DeleteValue(GameDvrPolicyKey, "AllowGameDVR");
            }

            RegistryHelper.SetValue(SynToolkitStoreKey, StateValue, 1, RegistryValueKind.DWord);
            bool detectedState = IsEnabled();
            _configurationStore.CurrentSetting = detectedState;
            if (!detectedState)
            {
                throw new InvalidOperationException(
                    "Windows did not restore all Game Bar/FSO settings. The saved state was retained so the revert can be retried.");
            }

            if (hasSnapshot)
            {
                ClearSnapshots();
            }
        }

        private static void CaptureOriginalSettings()
        {
            if (RegistryHelper.IsMatch(SynToolkitStoreKey, BackupCapturedValue, 1))
            {
                return;
            }

            CaptureDword(GameConfigStoreKey, "GameDVR_Enabled", "GameDvrEnabled");
            CaptureDword(GameConfigStoreKey, "GameDVR_FSEBehaviorMode", "FsoBehavior");
            CaptureDword(GameDvrKey, "AppCaptureEnabled", "AppCaptureEnabled");
            CaptureDword(GameDvrPolicyKey, "AllowGameDVR", "AllowGameDvr");
            RegistryHelper.SetValue(
                SynToolkitStoreKey,
                BackupCapturedValue,
                1,
                RegistryValueKind.DWord);
        }

        private static void CaptureDword(string keyPath, string valueName, string backupName)
        {
            object value = RegistryHelper.GetValue(keyPath, valueName);
            RegistryHelper.SetValue(
                SynToolkitStoreKey,
                $"Backup{backupName}Exists",
                value is null ? 0 : 1,
                RegistryValueKind.DWord);

            if (value is null)
            {
                RegistryHelper.DeleteValue(SynToolkitStoreKey, $"Backup{backupName}Value");
                return;
            }

            if (value is not int dwordValue)
            {
                throw new InvalidOperationException(
                    $"The existing {valueName} setting is not a DWORD. SynToolkit left it unchanged.");
            }

            RegistryHelper.SetValue(
                SynToolkitStoreKey,
                $"Backup{backupName}Value",
                dwordValue,
                RegistryValueKind.DWord);
        }

        private static DwordSnapshot ReadSnapshot(string backupName)
        {
            bool existed = RegistryHelper.IsMatch(
                SynToolkitStoreKey,
                $"Backup{backupName}Exists",
                1);
            if (!existed)
            {
                return new DwordSnapshot(false, 0);
            }

            object value = RegistryHelper.GetValue(
                SynToolkitStoreKey,
                $"Backup{backupName}Value");
            return value is int dwordValue
                ? new DwordSnapshot(true, dwordValue)
                : throw new InvalidOperationException(
                    "A saved Game Bar/FSO setting is invalid. No settings were restored.");
        }

        private static void RestoreSnapshot(
            string keyPath,
            string valueName,
            DwordSnapshot snapshot)
        {
            if (snapshot.Existed)
            {
                RegistryHelper.SetValue(keyPath, valueName, snapshot.Value, RegistryValueKind.DWord);
            }
            else
            {
                RegistryHelper.DeleteValue(keyPath, valueName);
            }
        }

        private static void ClearSnapshots()
        {
            foreach (string backupName in new[]
            {
                "GameDvrEnabled",
                "FsoBehavior",
                "AppCaptureEnabled",
                "AllowGameDvr"
            })
            {
                RegistryHelper.DeleteValue(SynToolkitStoreKey, $"Backup{backupName}Exists");
                RegistryHelper.DeleteValue(SynToolkitStoreKey, $"Backup{backupName}Value");
            }

            RegistryHelper.DeleteValue(SynToolkitStoreKey, BackupCapturedValue);
        }

        public bool IsEnabled()
        {
            try
            {
                object gameDvrEnabled = RegistryHelper.GetValue(GameConfigStoreKey, "GameDVR_Enabled");
                object fsoBehavior = RegistryHelper.GetValue(GameConfigStoreKey, "GameDVR_FSEBehaviorMode");
                object appCaptureEnabled = RegistryHelper.GetValue(GameDvrKey, "AppCaptureEnabled");
                object gameDvrPolicy = RegistryHelper.GetValue(GameDvrPolicyKey, "AllowGameDVR");

                // Missing values retain Windows defaults. Explicit zero values
                // disable Game DVR/capture, and FSEBehaviorMode=2 disables FSO.
                return !(gameDvrEnabled is int gameDvr && gameDvr == 0)
                    && !(fsoBehavior is int fso && fso == 2)
                    && !(appCaptureEnabled is int capture && capture == 0)
                    && !(gameDvrPolicy is int policy && policy == 0);
            }
            catch (System.Exception exception)
            {
                App.logger.Warn($"Unable to detect FSO/Game Bar state: {exception.Message}");
                return false;
            }
        }

        private sealed record DwordSnapshot(bool Existed, int Value);
    }
}
