using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SynToolkit.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace SynToolkit.Services.ConfigurationSubMenu
{
    public class TroubleshootingNetworkSubMenu : IConfigurationSubMenu
    {
        private readonly ConfigurationStore _troubleshootingNetworkSubMenu;
        public TroubleshootingNetworkSubMenu(
            [FromKeyedServices("TroubleshootingNetwork")] ConfigurationStore troubleshootingNetworkSubMenu)
        {
            _troubleshootingNetworkSubMenu = troubleshootingNetworkSubMenu;
        }
    }
}
