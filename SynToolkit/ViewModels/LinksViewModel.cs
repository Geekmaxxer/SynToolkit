using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SynToolkit.Enums;
using SynToolkit.Models;
using Microsoft.UI.Xaml.Controls;

namespace SynToolkit.ViewModels
{
    public class LinksViewModel : IConfigurationItem
    {
        private Links link { get; set; }
        public string Name => link.name ?? "N/A";
        public string Link => link.link;
        public string FontIcon => link.Icon;
        public string Key => link.name.ToLower().Replace(" ", "") ?? "N/A";
        public ConfigurationType Type => link.configurationType;

        public LinksViewModel(Links link)
        {
            this.link = link;
        }
    }
}
