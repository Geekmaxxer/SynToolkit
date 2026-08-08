#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SynToolkit.Services.RadeonSlimmer
{
    /// <summary>
    /// Enumerates and removes individual display-driver components bundled with an extracted
    /// Radeon Software installer. Ported from GSDragoon/RadeonSoftwareSlimmer's
    /// DisplayComponentListModel (https://github.com/GSDragoon/RadeonSoftwareSlimmer, GPL-3.0
    /// License) — same license as SynToolkit itself.
    /// </summary>
    public static class RadeonDisplayComponentService
    {
        private const string ComponentRelativePath = @"Packages\Drivers\Display\WT6A_INF";
        private const string BackupRelativePath = @"RSS_Backup\DisplayComponents";

        public static List<RadeonDisplayComponent> LoadDisplayComponents(string extractionFolderPath)
        {
            string componentBaseDirectory = Path.Combine(extractionFolderPath, ComponentRelativePath);
            List<RadeonDisplayComponent> components = new();

            if (!Directory.Exists(componentBaseDirectory))
            {
                return components;
            }

            foreach (string componentDirectory in Directory.EnumerateDirectories(componentBaseDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                if (Directory.EnumerateFiles(componentDirectory, "*.inf", SearchOption.TopDirectoryOnly).Count() == 1)
                {
                    components.Add(new RadeonDisplayComponent
                    {
                        DirectoryPath = componentDirectory,
                        Name = Path.GetFileName(componentDirectory),
                    });
                }
            }

            return components.OrderBy(component => component.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static void RemoveComponentsNotKeeping(string extractionFolderPath, IEnumerable<RadeonDisplayComponent> components)
        {
            string backupDirectory = Path.Combine(extractionFolderPath, BackupRelativePath);
            Directory.CreateDirectory(backupDirectory);

            foreach (RadeonDisplayComponent component in components.Where(component => !component.Keep))
            {
                Directory.Move(component.DirectoryPath, Path.Combine(backupDirectory, component.Name));
            }
        }

        public static void RestoreToDefault(string extractionFolderPath)
        {
            string backupDirectory = Path.Combine(extractionFolderPath, BackupRelativePath);
            string componentBaseDirectory = Path.Combine(extractionFolderPath, ComponentRelativePath);

            if (!Directory.Exists(backupDirectory))
            {
                return;
            }

            foreach (string backedUpComponentDirectory in Directory.EnumerateDirectories(backupDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                Directory.Move(backedUpComponentDirectory, Path.Combine(componentBaseDirectory, Path.GetFileName(backedUpComponentDirectory)));
            }
        }
    }
}
