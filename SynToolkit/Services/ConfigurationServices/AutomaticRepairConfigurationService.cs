using SynToolkit.Stores;
using SynToolkit.Services.Bcd;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace SynToolkit.Services.ConfigurationServices
{
    public class AutomaticRepairConfigurationService : IConfigurationService
    {
        private readonly ConfigurationStore _automaticRepairConfigurationStore;
        private readonly IBcdService _bcdService;

        public AutomaticRepairConfigurationService(
            [FromKeyedServices("AutomaticRepair")] ConfigurationStore automaticRepairConfigurationStore,
            IBcdService bcdService)
        {
            _automaticRepairConfigurationStore = automaticRepairConfigurationStore;
            _bcdService = bcdService;
        }

        public void Disable()
        {
            _bcdService.SetIntegerElement(WellKnownObjectIdentifiers.Current, WellKnownElementTypes.BootStatusPolicy, 1UL);

            _automaticRepairConfigurationStore.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            _bcdService.SetIntegerElement(WellKnownObjectIdentifiers.Current, WellKnownElementTypes.BootStatusPolicy, 0UL);

            _automaticRepairConfigurationStore.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            object value = _bcdService.GetElementValue(
                WellKnownObjectIdentifiers.Current,
                WellKnownElementTypes.BootStatusPolicy);

            return value is null || TryConvertToUInt64(value, out ulong policy) && policy == 0UL;
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
