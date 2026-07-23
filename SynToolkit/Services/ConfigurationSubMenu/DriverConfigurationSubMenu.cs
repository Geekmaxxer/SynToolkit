using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SynToolkit.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace SynToolkit.Services.ConfigurationSubMenu
{
    public class DriverConfigurationSubMenu : IConfigurationSubMenu
    {
        private readonly ConfigurationStoreSubMenu _driverConfigurationSubMenu;

        public DriverConfigurationSubMenu(
            [FromKeyedServices("DriverConfigurationSubMenu")] ConfigurationStoreSubMenu driverConfigurationSubMenu)
        {
            _driverConfigurationSubMenu = driverConfigurationSubMenu;
        }
    }
}
