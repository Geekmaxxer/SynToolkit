#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace SynToolkit.Services.RadeonSlimmer
{
    /// <summary>
    /// Configuration for the "Optimize Driver" preset. This defines which packages, scheduled
    /// tasks, and display components to keep or disable when applying the recommended slim
    /// configuration. Update this configuration if AMD's package contents change in future
    /// driver releases.
    /// </summary>
    public static class RadeonOptimizationConfig
    {
        /// <summary>
        /// Packages to keep (by ProductName). Everything else will be unchecked.
        /// These are also used as "anchor" entries to determine if the packages tab matches.
        /// </summary>
        public static readonly string[] PackagesToKeep =
        {
            "AMD Display Driver",
            "AMD Settings",
        };

        /// <summary>
        /// Display component folder names to keep. Supports wildcards (*) for prefix matching.
        /// Everything else will be unchecked.
        /// </summary>
        public static readonly string[] DisplayComponentsToKeep =
        {
            "amdocl",           // AMD-OpenCL User Mode Driver
            "amdogl",           // OpenGL driver
            "amdpcibridge*",    // AMD PCI Bridge Device Extension (may have suffix)
            "amdvlk",           // Vulkan driver
            "amdwin*",          // AMD-Windows Support Components (may have version suffix like amdwin-u0196284)
            "amdxe",            // AMD Controller Emulation
        };

        /// <summary>
        /// Scheduled tasks: disable ALL tasks. For matching purposes, a tab is considered
        /// "matched" as long as at least one scheduled task exists.
        /// </summary>
        public const bool ScheduledTasksDefaultEnabled = false;
    }

    /// <summary>
    /// Result of applying the recommended optimization preset.
    /// </summary>
    public sealed class OptimizationResult
    {
        public TabMatchResult ScheduledTasksResult { get; init; } = new();
        public TabMatchResult PackagesResult { get; init; } = new();
        public TabMatchResult DisplayComponentsResult { get; init; } = new();

        public bool AllTabsFullyMatched =>
            ScheduledTasksResult.MatchType == TabMatchType.FullMatch &&
            PackagesResult.MatchType == TabMatchType.FullMatch &&
            DisplayComponentsResult.MatchType == TabMatchType.FullMatch;

        public bool AnyTabPartiallyMatched =>
            ScheduledTasksResult.MatchType == TabMatchType.PartialMatch ||
            PackagesResult.MatchType == TabMatchType.PartialMatch ||
            DisplayComponentsResult.MatchType == TabMatchType.PartialMatch;

        public bool AnyTabSkipped =>
            ScheduledTasksResult.MatchType == TabMatchType.NoMatch ||
            PackagesResult.MatchType == TabMatchType.NoMatch ||
            DisplayComponentsResult.MatchType == TabMatchType.NoMatch;

        public bool NoTabsMatched =>
            ScheduledTasksResult.MatchType == TabMatchType.NoMatch &&
            PackagesResult.MatchType == TabMatchType.NoMatch &&
            DisplayComponentsResult.MatchType == TabMatchType.NoMatch;

        public IEnumerable<string> SkippedTabNames
        {
            get
            {
                if (ScheduledTasksResult.MatchType == TabMatchType.NoMatch)
                    yield return "Scheduled Tasks";
                if (PackagesResult.MatchType == TabMatchType.NoMatch)
                    yield return "Packages";
                if (DisplayComponentsResult.MatchType == TabMatchType.NoMatch)
                    yield return "Display Driver Components";
            }
        }
    }

    public enum TabMatchType
    {
        FullMatch,
        PartialMatch,
        NoMatch,
    }

    public sealed class TabMatchResult
    {
        public TabMatchType MatchType { get; init; } = TabMatchType.NoMatch;
        public int TotalItems { get; init; }
        public int MatchedItems { get; init; }
        public int ChangedItems { get; init; }
    }

    /// <summary>
    /// Service to apply the recommended optimization preset to the Radeon Slimmer data.
    /// </summary>
    public static class RadeonOptimizationService
    {
        /// <summary>
        /// Applies the recommended optimization preset to all three tabs.
        /// </summary>
        public static OptimizationResult ApplyRecommendedOptimization(
            IEnumerable<RadeonPackage> packages,
            IEnumerable<RadeonScheduledTask> scheduledTasks,
            IEnumerable<RadeonDisplayComponent> displayComponents)
        {
            return new OptimizationResult
            {
                ScheduledTasksResult = ApplyScheduledTasksOptimization(scheduledTasks),
                PackagesResult = ApplyPackagesOptimization(packages),
                DisplayComponentsResult = ApplyDisplayComponentsOptimization(displayComponents),
            };
        }

        private static TabMatchResult ApplyScheduledTasksOptimization(IEnumerable<RadeonScheduledTask> tasks)
        {
            List<RadeonScheduledTask> taskList = tasks.ToList();

            // For scheduled tasks, we disable all. Consider it a "full match" if any tasks exist.
            if (taskList.Count == 0)
            {
                return new TabMatchResult { MatchType = TabMatchType.NoMatch, TotalItems = 0, MatchedItems = 0, ChangedItems = 0 };
            }

            int changedCount = 0;
            foreach (RadeonScheduledTask task in taskList)
            {
                if (task.Enabled != RadeonOptimizationConfig.ScheduledTasksDefaultEnabled)
                {
                    task.Enabled = RadeonOptimizationConfig.ScheduledTasksDefaultEnabled;
                    changedCount++;
                }
            }

            return new TabMatchResult
            {
                MatchType = TabMatchType.FullMatch,
                TotalItems = taskList.Count,
                MatchedItems = taskList.Count,
                ChangedItems = changedCount,
            };
        }

        private static TabMatchResult ApplyPackagesOptimization(IEnumerable<RadeonPackage> packages)
        {
            List<RadeonPackage> packageList = packages.ToList();

            if (packageList.Count == 0)
            {
                return new TabMatchResult { MatchType = TabMatchType.NoMatch, TotalItems = 0, MatchedItems = 0, ChangedItems = 0 };
            }

            // Check how many anchor packages are found
            int anchorsFound = RadeonOptimizationConfig.PackagesToKeep
                .Count(keepName => packageList.Any(p =>
                    string.Equals(p.ProductName, keepName, StringComparison.OrdinalIgnoreCase)));

            if (anchorsFound == 0)
            {
                // None of the key packages found - skip this tab entirely
                return new TabMatchResult { MatchType = TabMatchType.NoMatch, TotalItems = packageList.Count, MatchedItems = 0, ChangedItems = 0 };
            }

            int changedCount = 0;
            foreach (RadeonPackage package in packageList)
            {
                bool shouldKeep = RadeonOptimizationConfig.PackagesToKeep
                    .Any(keepName => string.Equals(package.ProductName, keepName, StringComparison.OrdinalIgnoreCase));

                if (package.Keep != shouldKeep)
                {
                    package.Keep = shouldKeep;
                    changedCount++;
                }
            }

            TabMatchType matchType = anchorsFound == RadeonOptimizationConfig.PackagesToKeep.Length
                ? TabMatchType.FullMatch
                : TabMatchType.PartialMatch;

            return new TabMatchResult
            {
                MatchType = matchType,
                TotalItems = packageList.Count,
                MatchedItems = anchorsFound,
                ChangedItems = changedCount,
            };
        }

        private static TabMatchResult ApplyDisplayComponentsOptimization(IEnumerable<RadeonDisplayComponent> components)
        {
            List<RadeonDisplayComponent> componentList = components.ToList();

            if (componentList.Count == 0)
            {
                return new TabMatchResult { MatchType = TabMatchType.NoMatch, TotalItems = 0, MatchedItems = 0, ChangedItems = 0 };
            }

            // Check how many keep-patterns match at least one component
            int patternsMatched = RadeonOptimizationConfig.DisplayComponentsToKeep
                .Count(pattern => componentList.Any(c => MatchesPattern(c.Name, pattern)));

            if (patternsMatched == 0)
            {
                // None of the expected components found - skip this tab entirely
                return new TabMatchResult { MatchType = TabMatchType.NoMatch, TotalItems = componentList.Count, MatchedItems = 0, ChangedItems = 0 };
            }

            int changedCount = 0;
            foreach (RadeonDisplayComponent component in componentList)
            {
                bool shouldKeep = RadeonOptimizationConfig.DisplayComponentsToKeep
                    .Any(pattern => MatchesPattern(component.Name, pattern));

                if (component.Keep != shouldKeep)
                {
                    component.Keep = shouldKeep;
                    changedCount++;
                }
            }

            TabMatchType matchType = patternsMatched == RadeonOptimizationConfig.DisplayComponentsToKeep.Length
                ? TabMatchType.FullMatch
                : TabMatchType.PartialMatch;

            return new TabMatchResult
            {
                MatchType = matchType,
                TotalItems = componentList.Count,
                MatchedItems = patternsMatched,
                ChangedItems = changedCount,
            };
        }

        /// <summary>
        /// Matches a component name against a pattern. Supports * as a wildcard at the end.
        /// </summary>
        private static bool MatchesPattern(string name, string pattern)
        {
            if (pattern.EndsWith("*"))
            {
                string prefix = pattern[..^1];
                return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(name, pattern, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Generates a user-friendly summary message based on the optimization result.
        /// </summary>
        public static string GetResultMessage(OptimizationResult result)
        {
            if (result.NoTabsMatched)
            {
                return "This import doesn't match a recognized AMD package layout, so no optimization was applied. You can still configure settings manually.";
            }

            if (result.AllTabsFullyMatched)
            {
                return "Recommended optimization applied.";
            }

            if (result.AnyTabSkipped)
            {
                string skippedTabs = string.Join(", ", result.SkippedTabNames);
                return $"Optimization applied where possible. {skippedTabs} didn't match this package and were skipped.";
            }

            if (result.AnyTabPartiallyMatched)
            {
                return "Recommended optimization applied. Some items in your import didn't match the standard AMD layout and were left unchanged — review the tabs if needed.";
            }

            return "Optimization applied.";
        }
    }
}
