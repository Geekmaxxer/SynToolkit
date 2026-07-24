using Microsoft.UI.Xaml;
using SynToolkit.Utils;
using System;
using System.Runtime.InteropServices;
using WinUIEx;

namespace SynToolkit
{
    public enum IncompatibleVersionReason
    {
        SynergyOs,
        Windows
    }

    public sealed partial class IncompatibleVersionWindow : Window
    {
        public IncompatibleVersionWindow(IncompatibleVersionReason reason = IncompatibleVersionReason.SynergyOs)
        {
            WindowManager.Get(this).Width = 1250;
            WindowManager.Get(this).Height = 850;
            CenterWindowOnScreen();
            ExtendsContentIntoTitleBar = true;
            InitializeComponent();
            LoadText(reason);
        }

        private void LoadText(IncompatibleVersionReason reason)
        {
            if (reason == IncompatibleVersionReason.Windows)
            {
                IncompatibleVer.Text =
                    "This version of Windows is not supported by SynToolkit.\r\nSynToolkit requires 64-bit Windows 10 version 1809 (build 17763) or newer.";
                ReleasesLink.Content = CompatibilityHelper.SynergyOsReleasesUrl;
                ReleasesLink.NavigateUri = new Uri(CompatibilityHelper.SynergyOsReleasesUrl);
                return;
            }

            IncompatibleVer.Text = App.GetValueFromItemList("IncompatibleVer");
            ReleasesLink.Content = CompatibilityHelper.SynergyOsReleasesUrl;
            ReleasesLink.NavigateUri = new Uri(CompatibilityHelper.SynergyOsReleasesUrl);
        }

        private void CenterWindowOnScreen()
        {
            var screenWidth = GetSystemMetrics(SM_CXSCREEN);
            var screenHeight = GetSystemMetrics(SM_CYSCREEN);

            double centerX = (screenWidth - Bounds.Width) / 2;
            double centerY = (screenHeight - Bounds.Height) / 2;

            MoveAndResize(centerX, centerY, Bounds.Width, Bounds.Height);
        }

        private void MoveAndResize(double x, double y, double width, double height)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            SetWindowPos(hwnd, IntPtr.Zero, (int)x, (int)y, (int)width, (int)height, SWP_NOZORDER | SWP_NOACTIVATE);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
    }
}
