using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynToolkit.Models.CustomViewsModel
{
    public enum ContextMenuType 
    {
        All, // Main location file entries: HKEY_CLASSES_ROOT\*\shell
        Directory, // Folder: HKEY_CLASSES_ROOT\Directory\shell
        DirectoryBackground, // Desktop background: HKEY_CLASSES_ROOT\Directory\Background\shell
        Drive, // HKEY_CLASSES_ROOT\Drive\shell
        Program, // HKEY_CLASSES_ROOT\{program_name}\shell OR HKEY_CLASSES_ROOT\program name\shellex\ContextMenuHandlers
        CustomCommandStore, // HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\CommandStore\shell

    }
    public class ContextMenuEntry
    {
        public string ParentRegKey { get; set; }
        public string Name { get; set; }
        public string ContextMenuType { get; set; }
        public KeyValuePair<string, string> Icon { get; set; }
        public string Command { get; set; }
        public List<KeyValuePair<string, string>> Parameters { get; set; }

        /// <summary>
        /// Simple context menu entry
        /// </summary>
        /// <param name="parentRegKey"></param>
        /// <param name="name"></param>
        /// <param name="contextMenuType"></param>
        /// <param name="icon"></param>
        /// <param name="command"></param>
        public ContextMenuEntry(string parentRegKey, string name, string contextMenuType, KeyValuePair<string, string> icon, string command)
        {
            ParentRegKey = parentRegKey;
            Name = name;
            ContextMenuType = contextMenuType;
            Icon = icon;
            Command = command;
        }

        /// <summary>
        /// Complex context menu entry with more parameters to use
        /// </summary>
        /// <param name="parentRegKey"></param>
        /// <param name="name"></param>
        /// <param name="contextMenuType"></param>
        /// <param name="icon"></param>
        /// <param name="command"></param>
        /// <param name="parameters"></param>
        public ContextMenuEntry(string parentRegKey, string name, string contextMenuType, KeyValuePair<string, string> icon, string command, List<KeyValuePair<string, string>> parameters)
        {
            ParentRegKey = parentRegKey;
            Name = name;
            ContextMenuType = contextMenuType;
            Icon = icon;
            Command = command;
            Parameters = parameters;
        }

        /// <summary>
        /// Add many parameters at once
        /// </summary>
        /// <param name="toAdd"></param>
        public void AddParameter(List<KeyValuePair<string, string>> toAdd) 
            => this.Parameters.AddRange(toAdd);

        /// <summary>
        /// Add a single parameter
        /// </summary>
        /// <param name="toAdd"></param>
        public void AddParameter(KeyValuePair<string, string> toAdd) 
            => this.Parameters.Add(toAdd);
    }
}
