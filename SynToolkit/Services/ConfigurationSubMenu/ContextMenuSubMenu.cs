using SynToolkit.Services.ConfigurationServices;
using SynToolkit.Stores;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;

namespace SynToolkit.Services.ConfigurationSubMenu
{
    public class ContextMenuSubMenu : IConfigurationSubMenu
    {
        private readonly ConfigurationStoreSubMenu _contextMenuConfigurationSubMenu;

        public ContextMenuSubMenu(
            [FromKeyedServices("ContextMenuSubMenu")] ConfigurationStoreSubMenu contextMenuSubMenu)
        {
            _contextMenuConfigurationSubMenu = contextMenuSubMenu;
        }
    }
}
