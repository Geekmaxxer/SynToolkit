using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// Disables Multi-Plane Overlay (MPO), a common fix for DWM flickering/black-flash
    /// issues on some GPU driver combinations.
    /// </summary>
    internal class MultiPlaneOverlayConfigurationService : IConfigurationService
    {
        private const string SYNTOOLKIT_STORE_KEY_NAME = @"HKLM\SOFTWARE\SynToolkit\Services\MultiPlaneOverlay";
        private const string STATE_VALUE_NAME = "state";

        private const string DWM_KEY_NAME = @"HKLM\SOFTWARE\Microsoft\Windows\Dwm";
        private const string OVERLAY_TEST_MODE_VALUE_NAME = "OverlayTestMode";
        private const int DISABLE_MPO_VALUE = 5;

        private readonly ConfigurationStore _multiPlaneOverlayConfigurationStore;

        public MultiPlaneOverlayConfigurationService(
            [FromKeyedServices("MultiPlaneOverlay")] ConfigurationStore multiPlaneOverlayConfigurationStore)
        {
            _multiPlaneOverlayConfigurationStore = multiPlaneOverlayConfigurationStore;
        }

        public void Disable()
        {
            RegistryHelper.SetValue(DWM_KEY_NAME, OVERLAY_TEST_MODE_VALUE_NAME, DISABLE_MPO_VALUE, RegistryValueKind.DWord);
            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 0);

            App.ContentDialogCaller("logoff");

            _multiPlaneOverlayConfigurationStore.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            RegistryHelper.DeleteValue(DWM_KEY_NAME, OVERLAY_TEST_MODE_VALUE_NAME);
            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 1);

            App.ContentDialogCaller("logoff");

            _multiPlaneOverlayConfigurationStore.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            return !RegistryHelper.IsMatch(DWM_KEY_NAME, OVERLAY_TEST_MODE_VALUE_NAME, DISABLE_MPO_VALUE);
        }
    }
}
