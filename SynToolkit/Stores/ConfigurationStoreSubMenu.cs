using SynToolkit.Services.ConfigurationServices;
using System.Collections.Generic;


namespace SynToolkit.Stores
{
    public class ConfigurationStoreSubMenu
    {
        private List<string> _configurationServices;

        public List<string> ConfigurationStores
        {
            get
            {
                return _configurationServices;
            }
            set
            {
                _configurationServices = value;
            }
        }
    }
}
