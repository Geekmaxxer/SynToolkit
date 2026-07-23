using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SynToolkit.Services.ConfigurationServices;
using SynToolkit.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace SynToolkit.Services.ConfigurationSubMenu
{
    public class MitigationsSubMenu : IConfigurationSubMenu
    {
        private readonly ConfigurationStoreSubMenu _mitigationsSubMenu;

        public MitigationsSubMenu(
            [FromKeyedServices("MitigationsSubMenu")] ConfigurationStoreSubMenu mitigationsSubMenu)
        {
            _mitigationsSubMenu = mitigationsSubMenu;
        }
    }
}
