using SynToolkit.Stores;
using SynToolkit.Services.Bcd;
using Microsoft.Extensions.DependencyInjection;

namespace SynToolkit.Services.ConfigurationServices
{
    public class BootLogoConfigurationService : IConfigurationService
    {
        private readonly ConfigurationStore _configurationStore;
        private readonly IBcdService _bcdService;

        public BootLogoConfigurationService(
            [FromKeyedServices("BootLogo")] ConfigurationStore configurationStore,
            IBcdService bcdService)
        {
            _configurationStore = configurationStore;
            _bcdService = bcdService;
        }

        public void Disable()
        {
            _bcdService.SetBooleanElement(WellKnownObjectIdentifiers.GlobalSettings, WellKnownElementTypes.NoBootUxLogo, true);

            _configurationStore.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            _bcdService.DeleteElement(WellKnownObjectIdentifiers.GlobalSettings, WellKnownElementTypes.NoBootUxLogo);

            _configurationStore.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            object value = _bcdService.GetElementValue(
                WellKnownObjectIdentifiers.GlobalSettings,
                WellKnownElementTypes.NoBootUxLogo);

            return value is null or false;
        }
    }
}
