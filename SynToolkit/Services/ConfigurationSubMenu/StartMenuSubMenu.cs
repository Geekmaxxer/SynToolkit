using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SynToolkit.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace SynToolkit.Services.ConfigurationSubMenu
{
    public class StartMenuSubMenu : IConfigurationSubMenu
    {

        private readonly ConfigurationStoreSubMenu _startMenuSubMenu;

        public StartMenuSubMenu(
            [FromKeyedServices("StartMenuSubMenu")] ConfigurationStoreSubMenu startMenuSubMenu)
        {
            _startMenuSubMenu = startMenuSubMenu;
        }
    }
}
