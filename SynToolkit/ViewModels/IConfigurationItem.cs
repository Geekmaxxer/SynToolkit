using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SynToolkit.Enums;

namespace SynToolkit.ViewModels
{
    public interface IConfigurationItem
    {
        string Name { get; }
        string Key { get; }
        ConfigurationType Type { get; }
    }
}
