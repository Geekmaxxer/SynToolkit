using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;

namespace SynToolkit.Utils
{
    public static class SettingsBehaviorHelper
    {
        private static bool _isUpdatingBackgroundSetting;

        /// <summary>
        /// Changes close-to-tray behavior from the Settings page.
        /// </summary>
        public static void KeepBackground_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingBackgroundSetting || sender is not ToggleSwitch toggleSwitch)
            {
                return;
            }

            bool requestedState = toggleSwitch.IsOn;
            try
            {
                if (toggleSwitch.IsOn)
                {
                    RegistryHelper.SetValue("HKLM\\SOFTWARE\\SynToolkit", "KeepInBackground", 1);
                }
                else
                {
                    RegistryHelper.DeleteValue("HKLM\\SOFTWARE\\SynToolkit", "KeepInBackground");
                }

                if (!App.SetCloseToTrayEnabled(requestedState))
                {
                    throw new System.InvalidOperationException(
                        requestedState
                            ? "The system-tray icon is unavailable."
                            : "The system-tray icon could not be disabled.");
                }
            }
            catch (System.Exception exception)
            {
                App.logger.Error(exception, "Unable to change close-to-system-tray behavior.");
                try
                {
                    if (requestedState)
                    {
                        RegistryHelper.DeleteValue("HKLM\\SOFTWARE\\SynToolkit", "KeepInBackground");
                    }
                    else
                    {
                        RegistryHelper.SetValue("HKLM\\SOFTWARE\\SynToolkit", "KeepInBackground", 1);
                    }

                    App.SetCloseToTrayEnabled(!requestedState);
                }
                catch (System.Exception rollbackException)
                {
                    App.logger.Warn(rollbackException, "Unable to restore the previous close-to-tray preference.");
                }

                _isUpdatingBackgroundSetting = true;
                toggleSwitch.IsOn = !requestedState;
                _isUpdatingBackgroundSetting = false;
            }
        }
    }
}
