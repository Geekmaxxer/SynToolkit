using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynToolkit.Models
{
    public class Language
    {
        public string Name { get; set; }
        public string Key { get; set; }

        public Language(string name, string key)
        {
            Name = name;
            Key = key;
        }
    }
}
