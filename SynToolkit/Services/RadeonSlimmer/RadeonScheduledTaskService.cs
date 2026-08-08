#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace SynToolkit.Services.RadeonSlimmer
{
    /// <summary>
    /// Reads and updates the Task Scheduler XML exports bundled with an extracted Radeon
    /// Software installer. Ported from GSDragoon/RadeonSoftwareSlimmer's
    /// ScheduledTaskXmlListModel (https://github.com/GSDragoon/RadeonSoftwareSlimmer, GPL-3.0
    /// License) — same license as SynToolkit itself.
    /// </summary>
    public static class RadeonScheduledTaskService
    {
        public static List<RadeonScheduledTask> LoadScheduledTasks(string extractionFolderPath)
        {
            string configDirectory = Path.Combine(extractionFolderPath, "Config");
            List<RadeonScheduledTask> tasks = new();

            if (!Directory.Exists(configDirectory))
            {
                return tasks;
            }

            foreach (string file in Directory.EnumerateFiles(configDirectory, "*.xml", SearchOption.TopDirectoryOnly))
            {
                // Monet*.xml files in this folder are not Task Scheduler exports.
                if (Path.GetFileName(file).StartsWith("Monet", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!TryLoadTaskDocument(file, out XDocument? document) || document is null)
                {
                    continue;
                }

                XNamespace ns = document.Root!.GetDefaultNamespace();
                XElement? registrationInfo = document.Root.Element(ns + "RegistrationInfo");
                XElement? settings = document.Root.Element(ns + "Settings");
                XElement? execAction = document.Root.Element(ns + "Actions")?.Element(ns + "Exec");

                if (registrationInfo?.Element(ns + "Description") is null || settings?.Element(ns + "Enabled") is null || execAction is null)
                {
                    continue;
                }

                tasks.Add(new RadeonScheduledTask
                {
                    SourceFile = file,
                    Uri = registrationInfo.Element(ns + "URI")?.Value,
                    Description = registrationInfo.Element(ns + "Description")!.Value,
                    Command = $"{execAction.Element(ns + "Command")?.Value} {execAction.Element(ns + "Arguments")?.Value}".Trim(),
                    Enabled = bool.Parse(settings.Element(ns + "Enabled")!.Value),
                });
            }

            return tasks;
        }

        public static void SetScheduledTaskStatus(RadeonScheduledTask task)
        {
            if (!TryLoadTaskDocument(task.SourceFile, out XDocument? document) || document is null)
            {
                throw new IOException($"Unable to read scheduled task file {task.SourceFile}.");
            }

            XNamespace ns = document.Root!.GetDefaultNamespace();
            XElement settings = document.Root.Element(ns + "Settings")!;
            settings.Element(ns + "Enabled")!.Value = XmlConvert.ToString(task.Enabled);
            // Unhide the task alongside any state change so it's visible in Task Scheduler.
            settings.Element(ns + "Hidden")!.Value = XmlConvert.ToString(false);

            document.Save(task.SourceFile);
        }

        public static void RestoreToDefault(IEnumerable<RadeonScheduledTask> tasks)
        {
            foreach (RadeonScheduledTask task in tasks)
            {
                task.Enabled = true;
                SetScheduledTaskStatus(task);
            }
        }

        private static bool TryLoadTaskDocument(string filePath, out XDocument? document)
        {
            document = null;

            try
            {
                document = XDocument.Load(filePath);
            }
            catch (XmlException)
            {
                // Some AMD-exported task XML files declare an incorrect encoding; retry as plain text.
                try
                {
                    document = XDocument.Parse(File.ReadAllText(filePath));
                }
                catch (XmlException)
                {
                    return false;
                }
            }

            return string.Equals(document.Root?.Name.LocalName, "Task", StringComparison.Ordinal);
        }
    }
}
