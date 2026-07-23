using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SynToolkit.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace SynToolkit.Services.ConfigurationSubMenu
{
    internal class BootConfigBehavior : IConfigurationSubMenu
    {
        private readonly ConfigurationStoreSubMenu _bootConfigBehavior;


        public BootConfigBehavior(
            [FromKeyedServices("BootConfigBehavior")] ConfigurationStoreSubMenu bootConfigBehavior)
        {
            _bootConfigBehavior = bootConfigBehavior;
        }
    }
}
