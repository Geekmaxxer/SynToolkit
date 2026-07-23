using SynToolkit.Stores;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynToolkit.Services.ConfigurationSubMenu
{
    public class BootConfigurationSubMenu : IConfigurationSubMenu
    {
        private readonly ConfigurationStoreSubMenu _bootConfigurationSubMenu;


        public BootConfigurationSubMenu(
            [FromKeyedServices("BootConfigurationSubMenu")] ConfigurationStoreSubMenu bootConfigurationSubMenu)
        {
            _bootConfigurationSubMenu = bootConfigurationSubMenu;
        }
    }
}
