#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SynToolkit.Services.RadeonSlimmer
{
    /// <summary>
    /// Reads, removes, and restores installable packages listed in an extracted Radeon Software
    /// installer's manifests. Ported from GSDragoon/RadeonSoftwareSlimmer's PackageListModel
    /// (https://github.com/GSDragoon/RadeonSoftwareSlimmer, GPL-3.0 License) — same license as
    /// SynToolkit itself. The manifest paths and JSON shape are AMD's own installer format, not
    /// upstream's creative expression.
    /// </summary>
    public static class RadeonPackageService
    {
        private static readonly string[] PackageManifestFiles =
        {
            @"Bin64\cccmanifest_64.json",
            @"Config\InstallManifest.json"
        };

        public static List<RadeonPackage> LoadPackages(string extractionFolderPath)
        {
            BackupIfNotAlready(extractionFolderPath);

            List<RadeonPackage> packages = new();
            foreach (string relativePath in PackageManifestFiles)
            {
                string manifestPath = Path.Combine(extractionFolderPath, relativePath);
                if (!File.Exists(manifestPath))
                {
                    continue;
                }

                JObject manifest = JObject.Parse(File.ReadAllText(manifestPath));
                JToken? packageTokens = manifest.SelectToken("Packages.Package");
                if (packageTokens == null)
                {
                    continue;
                }

                foreach (JToken token in packageTokens.Children())
                {
                    packages.Add(new RadeonPackage
                    {
                        SourceFile = manifestPath,
                        Description = token.SelectToken("Info.Description")?.ToString() ?? string.Empty,
                        ProductName = token.SelectToken("Info.productName")?.ToString() ?? string.Empty,
                        Url = token.SelectToken("Info.url")?.ToString() ?? string.Empty,
                        Type = token.SelectToken("Info.ptype")?.ToString() ?? string.Empty,
                    });
                }
            }

            return packages.OrderBy(package => package.ProductName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static void RemovePackage(RadeonPackage packageToRemove)
        {
            JObject manifest;
            using (StreamReader streamReader = new(packageToRemove.SourceFile))
            using (JsonTextReader jsonReader = new(streamReader))
            {
                manifest = (JObject)JToken.ReadFrom(jsonReader);
                JToken? packageTokens = manifest.SelectToken("Packages.Package");
                foreach (JToken token in packageTokens?.Children().ToList() ?? Enumerable.Empty<JToken>())
                {
                    bool isMatch =
                        token.SelectToken("Info.Description")?.ToString() == packageToRemove.Description &&
                        token.SelectToken("Info.productName")?.ToString() == packageToRemove.ProductName &&
                        token.SelectToken("Info.url")?.ToString() == packageToRemove.Url &&
                        token.SelectToken("Info.ptype")?.ToString() == packageToRemove.Type;

                    if (isMatch)
                    {
                        token.Remove();
                        break;
                    }
                }
            }

            using StreamWriter streamWriter = new(packageToRemove.SourceFile, append: false);
            using JsonTextWriter jsonWriter = new(streamWriter) { Formatting = Formatting.Indented };
            manifest.WriteTo(jsonWriter);
        }

        public static void RestoreToDefault(string extractionFolderPath)
        {
            string backupDirectory = Path.Combine(extractionFolderPath, "RSS_Backup", "Packages");
            if (!Directory.Exists(backupDirectory))
            {
                return;
            }

            foreach (string relativePath in PackageManifestFiles)
            {
                string backupFile = Path.Combine(backupDirectory, relativePath);
                if (File.Exists(backupFile))
                {
                    File.Copy(backupFile, Path.Combine(extractionFolderPath, relativePath), overwrite: true);
                }
            }
        }

        private static void BackupIfNotAlready(string extractionFolderPath)
        {
            string backupDirectory = Path.Combine(extractionFolderPath, "RSS_Backup", "Packages");

            foreach (string relativePath in PackageManifestFiles)
            {
                string backupFile = Path.Combine(backupDirectory, relativePath);
                if (File.Exists(backupFile))
                {
                    continue;
                }

                string sourceFile = Path.Combine(extractionFolderPath, relativePath);
                if (!File.Exists(sourceFile))
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(backupFile)!);
                File.Copy(sourceFile, backupFile);
            }
        }
    }
}
