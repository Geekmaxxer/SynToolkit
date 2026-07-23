using SynToolkit.Services.ConfigurationServices;
using SynToolkit.Stores;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;

namespace SynToolkit.Services.ConfigurationSubMenu
{
    internal class ServicesSubMenu : IConfigurationSubMenu
    {

        private readonly ConfigurationStoreSubMenu _servicesSubMenu;

        public ServicesSubMenu(
            [FromKeyedServices("ServicesSubMenu")] ConfigurationStoreSubMenu servicesSubMenu)
        {
            _servicesSubMenu = servicesSubMenu;
        }
    }
}
