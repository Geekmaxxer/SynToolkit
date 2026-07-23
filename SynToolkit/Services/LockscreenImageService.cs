#nullable enable

using System;
using System.IO;
using Microsoft.Win32;
using SynToolkit.Utils;

namespace SynToolkit.Services
{
    /// <summary>
    /// Replaces the Windows lock-screen image. Windows caches the lock screen at a
    /// well-documented fixed path (%WINDIR%\Web\Screen\img100.jpg) and separately caches
    /// rendered copies under ProgramData\Microsoft\Windows\SystemData that must be cleared for
    /// a replacement to actually take effect.
    /// </summary>
    public static class LockscreenImageService
    {
        public static void SetLockscreenImage(string sourceImagePath, string userSid, bool removeAcrylicBlur)
        {
            try
            {
                RegistryHelper.SetValue(
                    @"HKLM\SOFTWARE\Policies\Microsoft\Windows\System",
                    "DisableAcrylicBackgroundOnLogon",
                    removeAcrylicBlur ? 1 : 0,
                    RegistryValueKind.DWord);
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[Adjustments] Unable to set the lock-screen acrylic-blur policy.");
            }

            RegistryHelper.SetValue(
                @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\LogonUI\Creative\" + userSid,
                "RotatingLockScreenEnabled",
                0,
                RegistryValueKind.DWord);
            RegistryHelper.SetValue(
                $@"HKU\{userSid}\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
                "RotatingLockScreenEnabled",
                0,
                RegistryValueKind.DWord);

            string targetPath = Environment.ExpandEnvironmentVariables(@"%WINDIR%\Web\Screen\img100.jpg");
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
            File.Copy(sourceImagePath, targetPath);

            string systemDataPath = Environment.ExpandEnvironmentVariables(@"%PROGRAMDATA%\Microsoft\Windows\SystemData");
            if (!Directory.Exists(systemDataPath))
            {
                return;
            }

            foreach (string dataDirectory in Directory.EnumerateDirectories(systemDataPath))
            {
                string readOnlyDirectory = Path.Combine(dataDirectory, "ReadOnly");
                if (!Directory.Exists(readOnlyDirectory))
                {
                    continue;
                }

                foreach (string cachedLockscreenDirectory in Directory.GetDirectories(readOnlyDirectory, "Lockscreen_*"))
                {
                    try
                    {
                        Directory.Delete(cachedLockscreenDirectory, true);
                    }
                    catch (Exception exception)
                    {
                        App.logger.Debug(exception, "[Adjustments] Unable to clear cached lock-screen directory {Directory}.", cachedLockscreenDirectory);
                    }
                }
            }
        }
    }
}
