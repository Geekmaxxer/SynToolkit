#nullable enable

using Microsoft.Win32;
using SynToolkit.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SynToolkit.Services
{
    /// <summary>
    /// Detects which apps from the catalog are installed on the current machine.
    /// Uses registry uninstall keys, known folder paths, and PATH executables.
    /// </summary>
    public sealed class InstalledAppsDetectionService
    {
        private static readonly string CatalogDirectory = Path.Combine(
            AppContext.BaseDirectory, "Assets", "Installers");
        private static readonly string CatalogManifestPath = Path.Combine(
            CatalogDirectory, "manifest.json");

        private static readonly string[] UninstallKeyPaths =
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };

        private List<AppCatalogEntry>? _cachedCatalog;
        private readonly object _catalogLock = new();

        /// <summary>
        /// Loads the app catalog from the manifest file.
        /// </summary>
        public IReadOnlyList<AppCatalogEntry> GetCatalog()
        {
            lock (_catalogLock)
            {
                if (_cachedCatalog is not null)
                {
                    return _cachedCatalog;
                }

                _cachedCatalog = LoadCatalogFromManifest();
                return _cachedCatalog;
            }
        }

        /// <summary>
        /// Clears the cached catalog, forcing a reload on next access.
        /// </summary>
        public void InvalidateCatalogCache()
        {
            lock (_catalogLock)
            {
                _cachedCatalog = null;
            }
        }

        /// <summary>
        /// Scans for installed apps and returns a set of catalog IDs that are confirmed installed.
        /// </summary>
        public async Task<HashSet<string>> ScanInstalledAppsAsync(CancellationToken cancellationToken = default)
        {
            var catalog = GetCatalog();
            var installedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Run detection on a background thread to avoid blocking UI
            await Task.Run(() =>
            {
                // Pre-load registry display names for efficiency
                var registryDisplayNames = LoadAllRegistryDisplayNames();
                var pathDirectories = GetPathDirectories();

                foreach (var entry in catalog)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (IsAppInstalled(entry, registryDisplayNames, pathDirectories))
                    {
                        installedIds.Add(entry.Id);
                    }
                }
            }, cancellationToken);

            return installedIds;
        }

        /// <summary>
        /// Creates view models for all catalog entries with installation state.
        /// </summary>
        public async Task<IReadOnlyList<AppCatalogEntryViewModel>> GetCatalogWithInstallStateAsync(
            CancellationToken cancellationToken = default)
        {
            var catalog = GetCatalog();
            var installedIds = await ScanInstalledAppsAsync(cancellationToken);

            return catalog.Select(entry => new AppCatalogEntryViewModel
            {
                Entry = entry,
                IsInstalled = installedIds.Contains(entry.Id),
                IsSelected = false
            }).ToList();
        }

        private bool IsAppInstalled(
            AppCatalogEntry entry,
            Dictionary<string, List<string>> registryDisplayNames,
            string[] pathDirectories)
        {
            var rule = entry.DetectionRule;

            // Stub entries always return false
            if (rule.IsStub)
            {
                return false;
            }

            // Check specific registry GUIDs first (for VC++ Redistributables)
            if (rule.RegistryGuids.Count > 0)
            {
                foreach (var guid in rule.RegistryGuids)
                {
                    if (IsRegistryGuidPresent(guid))
                    {
                        return true;
                    }
                }
            }

            // Check registry display name patterns
            if (rule.RegistryNamePatterns.Count > 0)
            {
                foreach (var pattern in rule.RegistryNamePatterns)
                {
                    if (IsRegistryNameMatch(pattern, registryDisplayNames))
                    {
                        return true;
                    }
                }
            }

            // Check known folder paths
            if (rule.KnownFolderPaths.Count > 0)
            {
                foreach (var folderPath in rule.KnownFolderPaths)
                {
                    var expandedPath = Environment.ExpandEnvironmentVariables(folderPath);
                    if (Directory.Exists(expandedPath) || File.Exists(expandedPath))
                    {
                        return true;
                    }
                }
            }

            // Check PATH executables
            if (rule.PathExecutables.Count > 0)
            {
                foreach (var exeName in rule.PathExecutables)
                {
                    if (IsExecutableOnPath(exeName, pathDirectories))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private Dictionary<string, List<string>> LoadAllRegistryDisplayNames()
        {
            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            void ScanHive(RegistryKey hive, string hiveName)
            {
                foreach (var keyPath in UninstallKeyPaths)
                {
                    try
                    {
                        using var uninstallKey = hive.OpenSubKey(keyPath);
                        if (uninstallKey is null) continue;

                        foreach (var subKeyName in uninstallKey.GetSubKeyNames())
                        {
                            try
                            {
                                using var appKey = uninstallKey.OpenSubKey(subKeyName);
                                var displayName = appKey?.GetValue("DisplayName") as string;
                                if (!string.IsNullOrWhiteSpace(displayName))
                                {
                                    var fullPath = $"{hiveName}\\{keyPath}\\{subKeyName}";
                                    if (!result.TryGetValue(displayName, out var list))
                                    {
                                        list = new List<string>();
                                        result[displayName] = list;
                                    }
                                    list.Add(fullPath);
                                }
                            }
                            catch
                            {
                                // Skip inaccessible subkeys
                            }
                        }
                    }
                    catch
                    {
                        // Skip inaccessible registry paths
                    }
                }
            }

            ScanHive(Registry.LocalMachine, "HKLM");
            ScanHive(Registry.CurrentUser, "HKCU");

            return result;
        }

        private bool IsRegistryNameMatch(string pattern, Dictionary<string, List<string>> displayNames)
        {
            // Case-insensitive partial match
            var patternLower = pattern.ToLowerInvariant();
            return displayNames.Keys.Any(name => 
                name.ToLowerInvariant().Contains(patternLower));
        }

        private bool IsRegistryGuidPresent(string guid)
        {
            // Check for specific GUID in uninstall keys
            foreach (var keyPath in UninstallKeyPaths)
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey($@"{keyPath}\{guid}");
                    if (key is not null)
                    {
                        return true;
                    }
                }
                catch
                {
                    // Skip inaccessible paths
                }

                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey($@"{keyPath}\{guid}");
                    if (key is not null)
                    {
                        return true;
                    }
                }
                catch
                {
                    // Skip inaccessible paths
                }
            }

            return false;
        }

        private string[] GetPathDirectories()
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            return pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        }

        private bool IsExecutableOnPath(string exeName, string[] pathDirectories)
        {
            // Ensure .exe extension
            var fullExeName = exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) 
                ? exeName 
                : exeName + ".exe";

            foreach (var dir in pathDirectories)
            {
                try
                {
                    var fullPath = Path.Combine(dir, fullExeName);
                    if (File.Exists(fullPath))
                    {
                        return true;
                    }
                }
                catch
                {
                    // Skip invalid paths
                }
            }

            return false;
        }

        private List<AppCatalogEntry> LoadCatalogFromManifest()
        {
            if (!File.Exists(CatalogManifestPath))
            {
                App.logger.Warn("Installer catalog manifest not found at {Path}", CatalogManifestPath);
                return GetFallbackCatalog();
            }

            try
            {
                var json = File.ReadAllText(CatalogManifestPath);
                using var doc = JsonDocument.Parse(json);
                
                if (!doc.RootElement.TryGetProperty("apps", out var appsArray))
                {
                    App.logger.Warn("Installer catalog manifest missing 'apps' property");
                    return GetFallbackCatalog();
                }

                var entries = new List<AppCatalogEntry>();
                foreach (var appElement in appsArray.EnumerateArray())
                {
                    try
                    {
                        var entry = ParseAppEntry(appElement);
                        if (entry is not null)
                        {
                            entries.Add(entry);
                        }
                    }
                    catch (Exception ex)
                    {
                        App.logger.Debug(ex, "Failed to parse app entry from manifest");
                    }
                }

                return entries;
            }
            catch (Exception ex)
            {
                App.logger.Error(ex, "Failed to load installer catalog manifest");
                return GetFallbackCatalog();
            }
        }

        private AppCatalogEntry? ParseAppEntry(JsonElement element)
        {
            var id = element.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
            var displayName = element.TryGetProperty("displayName", out var nameProp) ? nameProp.GetString() : null;
            var description = element.TryGetProperty("shortDescription", out var descProp) ? descProp.GetString() : null;
            var categoryStr = element.TryGetProperty("category", out var catProp) ? catProp.GetString() : null;

            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(displayName) || string.IsNullOrEmpty(categoryStr))
            {
                return null;
            }

            if (!Enum.TryParse<AppCategory>(categoryStr, ignoreCase: true, out var category))
            {
                return null;
            }

            var detectionRule = ParseDetectionRule(element);

            return new AppCatalogEntry
            {
                Id = id,
                DisplayName = displayName,
                ShortDescription = description ?? "No description available.",
                Category = category,
                DetectionRule = detectionRule,
                IconPath = element.TryGetProperty("iconPath", out var iconProp) ? iconProp.GetString() : null,
                InstallUrl = element.TryGetProperty("installUrl", out var urlProp) ? urlProp.GetString() : null,
                Subcategory = element.TryGetProperty("subcategory", out var subProp) ? subProp.GetString() : null,
                IsFlaggedForReview = element.TryGetProperty("flaggedForReview", out var flagProp) && flagProp.GetBoolean(),
                ReviewNotes = element.TryGetProperty("reviewNotes", out var notesProp) ? notesProp.GetString() : null
            };
        }

        private AppDetectionRule ParseDetectionRule(JsonElement element)
        {
            if (!element.TryGetProperty("detectionRule", out var ruleElement))
            {
                return new AppDetectionRule { IsStub = true };
            }

            return new AppDetectionRule
            {
                RegistryNamePatterns = ParseStringArray(ruleElement, "registryNamePatterns"),
                KnownFolderPaths = ParseStringArray(ruleElement, "knownFolderPaths"),
                PathExecutables = ParseStringArray(ruleElement, "pathExecutables"),
                RegistryGuids = ParseStringArray(ruleElement, "registryGuids"),
                IsStub = ruleElement.TryGetProperty("isStub", out var stubProp) && stubProp.GetBoolean()
            };
        }

        private IReadOnlyList<string> ParseStringArray(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var arrayProp) || arrayProp.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            var result = new List<string>();
            foreach (var item in arrayProp.EnumerateArray())
            {
                var str = item.GetString();
                if (!string.IsNullOrEmpty(str))
                {
                    result.Add(str);
                }
            }
            return result;
        }

        private List<AppCatalogEntry> GetFallbackCatalog()
        {
            // Return empty catalog if manifest is unavailable
            return new List<AppCatalogEntry>();
        }
    }
}
