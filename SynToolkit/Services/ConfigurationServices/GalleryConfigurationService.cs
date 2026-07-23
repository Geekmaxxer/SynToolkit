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
    public class GalleryConfigurationService : IConfigurationService
    {
        private const string SYNTOOLKIT_STORE_KEY_NAME = @"HKLM\SOFTWARE\SynToolkit\Services\Gallery";
        private const string STATE_VALUE_NAME = "state";

        private const string LONG_STRING_KEY_NAME = @"HKCU\Software\Classes\CLSID\{e88865ea-0e1c-4e20-9aa6-edcd0212c87c}";
        private const string MACHINE_GALLERY_CLSID_KEY_NAME = @"HKLM\SOFTWARE\Classes\CLSID\{e88865ea-0e1c-4e20-9aa6-edcd0212c87c}";

        private const string IS_PINNED_TO_NAME_SPACE_TREE_VALUE_NAME = "System.IsPinnedToNameSpaceTree";

        private readonly ConfigurationStore _galleryConfigurationService;

        public GalleryConfigurationService(
            [FromKeyedServices("Gallery")] ConfigurationStore galleryConfigurationService)
        {
            _galleryConfigurationService = galleryConfigurationService;
        }


        public void Disable()
        {
            RegistryHelper.SetValue(LONG_STRING_KEY_NAME, IS_PINNED_TO_NAME_SPACE_TREE_VALUE_NAME, 0, Microsoft.Win32.RegistryValueKind.DWord);
            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 0);

            _galleryConfigurationService.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            RegistryHelper.SetValue(LONG_STRING_KEY_NAME, IS_PINNED_TO_NAME_SPACE_TREE_VALUE_NAME, 1, Microsoft.Win32.RegistryValueKind.DWord);
            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 1);

            _galleryConfigurationService.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            try
            {
                object value = RegistryHelper.GetValue(
                    LONG_STRING_KEY_NAME, IS_PINNED_TO_NAME_SPACE_TREE_VALUE_NAME);
                // This key is a per-user override. When absent, Explorer uses the
                // built-in pinned state on Windows builds that provide Gallery.
                return RegistryHelper.KeyExists(MACHINE_GALLERY_CLSID_KEY_NAME)
                    && (value is null || value is int pinned && pinned != 0);
            }
            catch (Exception exception)
            {
                App.logger.Warn($"Unable to detect File Explorer Gallery state: {exception.Message}");
                return false;
            }
        }
    }
}
