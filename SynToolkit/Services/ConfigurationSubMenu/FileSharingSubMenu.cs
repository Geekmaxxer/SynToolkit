using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SynToolkit.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace SynToolkit.Services.ConfigurationSubMenu
{
    public class FileSharingSubMenu : IConfigurationSubMenu
    {
        private readonly ConfigurationStoreSubMenu _configurationStoreSubMenu;
        public FileSharingSubMenu(
            [FromKeyedServices("FileSharingSubMenu")] ConfigurationStoreSubMenu configurationStoreSubMenu)
        {
            _configurationStoreSubMenu = configurationStoreSubMenu;
        }
    }
}
