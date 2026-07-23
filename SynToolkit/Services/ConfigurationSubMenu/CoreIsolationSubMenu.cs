using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SynToolkit.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace SynToolkit.Services.ConfigurationSubMenu
{
    public class CoreIsolationSubMenu : IConfigurationSubMenu
    {
        private readonly ConfigurationStore _coreIsolationSubMenu;
        public CoreIsolationSubMenu(
            [FromKeyedServices("CoreIsolationSubMenu")] ConfigurationStore coreIsolationSubMenu)
        {
            _coreIsolationSubMenu = coreIsolationSubMenu;
        }
    }
}
