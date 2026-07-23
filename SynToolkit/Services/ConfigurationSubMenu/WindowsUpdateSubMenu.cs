using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SynToolkit.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace SynToolkit.Services.ConfigurationSubMenu
{
    public class WindowsUpdateSubMenu : IConfigurationSubMenu
    {
        private readonly ConfigurationStore _windowsUpdateSubMenu;
        public WindowsUpdateSubMenu(
            [FromKeyedServices("WindowsUpdate")] ConfigurationStore windowsUpdateSubMenu)
        {
            _windowsUpdateSubMenu = windowsUpdateSubMenu;
        }
    }
}
