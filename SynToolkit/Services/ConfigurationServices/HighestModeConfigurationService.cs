using SynToolkit.Stores;
using SynToolkit.Services.Bcd;
using Microsoft.Extensions.DependencyInjection;

namespace SynToolkit.Services.ConfigurationServices
{
    public class HighestModeConfigurationService : IConfigurationService
    {
        private readonly ConfigurationStore _highestModeConfigurationStore;
        private readonly IBcdService _bcdService;

        public HighestModeConfigurationService(
            [FromKeyedServices("HighestMode")] ConfigurationStore highestModeConfigurationStore,
            IBcdService bcdService)
        {
            _highestModeConfigurationStore = highestModeConfigurationStore;
            _bcdService = bcdService;
        }

        public void Disable()
        {
            _bcdService.DeleteElement(WellKnownObjectIdentifiers.GlobalSettings, WellKnownElementTypes.HighestMode);

            _highestModeConfigurationStore.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            _bcdService.SetBooleanElement(WellKnownObjectIdentifiers.GlobalSettings, WellKnownElementTypes.HighestMode, true);

            _highestModeConfigurationStore.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            return _bcdService.GetElementValue(
                WellKnownObjectIdentifiers.GlobalSettings,
                WellKnownElementTypes.HighestMode) is true;
        }
    }
}
