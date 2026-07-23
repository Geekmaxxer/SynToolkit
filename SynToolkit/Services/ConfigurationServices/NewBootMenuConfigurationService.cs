using SynToolkit.Stores;
using SynToolkit.Services.Bcd;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace SynToolkit.Services.ConfigurationServices
{
    public class NewBootMenuConfigurationService : IConfigurationService
    {
        private readonly ConfigurationStore _newBootMenuConfigurationStore;
        private readonly IBcdService _bcdService;

        public NewBootMenuConfigurationService(
            [FromKeyedServices("NewBootMenu")] ConfigurationStore newBootMenuConfigurationStore,
            IBcdService bcdService)
        {
            _newBootMenuConfigurationStore = newBootMenuConfigurationStore;
            _bcdService = bcdService;
        }

        public void Disable()
        {
            _bcdService.SetIntegerElement(WellKnownObjectIdentifiers.Default, WellKnownElementTypes.BootMenuPolicyWinload, 0UL);

            _newBootMenuConfigurationStore.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            _bcdService.SetIntegerElement(WellKnownObjectIdentifiers.Default, WellKnownElementTypes.BootMenuPolicyWinload, 1UL);

            _newBootMenuConfigurationStore.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            object value = _bcdService.GetElementValue(
                WellKnownObjectIdentifiers.Default,
                WellKnownElementTypes.BootMenuPolicyWinload);

            return value is null || TryConvertToUInt64(value, out ulong policy) && policy == 1UL;
        }

        private static bool TryConvertToUInt64(object value, out ulong result)
        {
            try
            {
                result = Convert.ToUInt64(value);
                return true;
            }
            catch (Exception)
            {
                result = 0;
                return false;
            }
        }
    }
}
