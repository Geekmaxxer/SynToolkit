using Microsoft.UI.Xaml;
using WinUIEx;

namespace SynToolkit.Utils
{
    public static class AppBehaviorHelper
    {
        /// <summary>
        /// Hides the main window only when a working tray icon is available.
        /// Otherwise, closing the window always performs a full process shutdown.
        /// </summary>
        public static void HandleMainWindowClosed(object sender, WindowEventArgs e)
        {
            if (!App.IsShuttingDown && App.TryHideMainWindowToTray())
            {
                e.Handled = true;
                return;
            }

            SaveWindowSize();
            App.ShutdownApplication();
        }

        private static void SaveWindowSize()
        {
            try
            {
                if (App.m_window is not MainWindow mainWindow || mainWindow.IsFullscreen())
                {
                    return;
                }

                mainWindow.GetWindowSize(out int width, out int height);
                if (!mainWindow.IsPersistableWindowSize(width, height))
                {
                    return;
                }

                RegistryHelper.SetValue(@"HKLM\SOFTWARE\SynToolkit", "AppWidth", width, Microsoft.Win32.RegistryValueKind.String);
                RegistryHelper.SetValue(@"HKLM\SOFTWARE\SynToolkit", "AppHeight", height, Microsoft.Win32.RegistryValueKind.String);
            }
            catch (System.Exception exception)
            {
                App.logger.Warn(exception, "Unable to save the SynToolkit window size during shutdown.");
            }
        }
    }
}
