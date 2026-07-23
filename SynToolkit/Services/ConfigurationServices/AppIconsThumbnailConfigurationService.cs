using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace SynToolkit.Services.ConfigurationServices
{
    public class AppIconsThumbnailConfigurationService : IConfigurationService
    {
        private const string SYNTOOLKIT_STORE_KEY_NAME = @"HKLM\SOFTWARE\SynToolkit\Services\AppIconsThumbnail";
        private const string STATE_VALUE_NAME = "state";

        private const string ADVANCED_KEY_NAME = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

        private const string SHOW_TYPE_OVERLAY_KEY_NAME = "ShowTypeOverlay";

        private readonly ConfigurationStore _appIconsThumbnailConfigurationService;

        public AppIconsThumbnailConfigurationService(
            [FromKeyedServices("AppIconsThumbnail")] ConfigurationStore appIconsThumbnailConfigurationService)
        {
            _appIconsThumbnailConfigurationService = appIconsThumbnailConfigurationService;
        }

        public void Disable()
        {
            RegistryHelper.SetValue(ADVANCED_KEY_NAME, SHOW_TYPE_OVERLAY_KEY_NAME, 0, Microsoft.Win32.RegistryValueKind.DWord);
            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 0);

            _appIconsThumbnailConfigurationService.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            RegistryHelper.SetValue(ADVANCED_KEY_NAME, SHOW_TYPE_OVERLAY_KEY_NAME, 1, Microsoft.Win32.RegistryValueKind.DWord);
            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 1);

            _appIconsThumbnailConfigurationService.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            try
            {
                object value = RegistryHelper.GetValue(ADVANCED_KEY_NAME, SHOW_TYPE_OVERLAY_KEY_NAME);
                return value is null || value is int enabled && enabled != 0;
            }
            catch (Exception exception)
            {
                App.logger.Warn($"Unable to detect thumbnail app-icon state: {exception.Message}");
                return false;
            }
        }
    }
}
