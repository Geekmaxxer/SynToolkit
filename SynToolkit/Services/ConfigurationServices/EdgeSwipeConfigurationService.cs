using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace SynToolkit.Services.ConfigurationServices
{
    public class EdgeSwipeConfigurationService : IConfigurationService
    {
        private const string SYNTOOLKIT_STORE_KEY_NAME = @"HKLM\SOFTWARE\SynToolkit\Services\EdgeSwipe";
        private const string STATE_VALUE_NAME = "state";

        private const string EDGE_UI_KEY_NAME = @"HKLM\Software\Policies\Microsoft\Windows\EdgeUI";

        private const string ALLOW_EDGE_SWIPE_VALUE_NAME = "AllowEdgeSwipe";


        private readonly ConfigurationStore _edgeSwipeConfigurationService;
        
        public EdgeSwipeConfigurationService(
            [FromKeyedServices("EdgeSwipe")] ConfigurationStore edgeSwipeConfigurationService)
        {
            _edgeSwipeConfigurationService = edgeSwipeConfigurationService;
        }
        public void Disable()
        {
            RegistryHelper.SetValue(EDGE_UI_KEY_NAME, ALLOW_EDGE_SWIPE_VALUE_NAME, 0, Microsoft.Win32.RegistryValueKind.DWord);
            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 0);

            _edgeSwipeConfigurationService.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            RegistryHelper.DeleteValue(EDGE_UI_KEY_NAME, ALLOW_EDGE_SWIPE_VALUE_NAME);
            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 1);

            _edgeSwipeConfigurationService.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            try
            {
                object value = RegistryHelper.GetValue(EDGE_UI_KEY_NAME, ALLOW_EDGE_SWIPE_VALUE_NAME);
                return value is null || value is int policy && policy != 0;
            }
            catch (Exception exception)
            {
                App.logger.Warn($"Unable to detect edge-swipe policy: {exception.Message}");
                return false;
            }
        }
    }
}
