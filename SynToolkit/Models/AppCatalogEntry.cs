#nullable enable

using System;
using System.Collections.Generic;

namespace SynToolkit.Models
{
    /// <summary>
    /// Represents a single app/runtime in the installer catalog.
    /// </summary>
    public sealed class AppCatalogEntry
    {
        public required string Id { get; init; }
        public required string DisplayName { get; init; }
        public required string ShortDescription { get; init; }
        public required AppCategory Category { get; init; }
        public required AppDetectionRule DetectionRule { get; init; }
        
        /// <summary>
        /// Placeholder for future icon path. Currently null - will be populated when real icons are added.
        /// </summary>
        public string? IconPath { get; init; }
        
        /// <summary>
        /// Placeholder for future download/install URL. Currently null - will be populated in follow-up pass.
        /// </summary>
        public string? InstallUrl { get; init; }
        
        /// <summary>
        /// Optional subcategory for grouping within a category (e.g., "x86" vs "x64" for runtimes).
        /// </summary>
        public string? Subcategory { get; init; }
        
        /// <summary>
        /// Whether this entry is flagged for review before finalizing (uncertain inclusion, naming, etc.).
        /// </summary>
        public bool IsFlaggedForReview { get; init; }
        
        /// <summary>
        /// Review notes explaining why this entry is flagged.
        /// </summary>
        public string? ReviewNotes { get; init; }
    }

    /// <summary>
    /// Categories for organizing apps in the installer page.
    /// </summary>
    public enum AppCategory
    {
        GameLaunchers,
        GamingCreatorApps,
        Browsers,
        MediaCommunication,
        DevUtility,
        Runtimes,
        GpuDrivers
    }

    /// <summary>
    /// Defines how to detect if an app is installed.
    /// </summary>
    public sealed class AppDetectionRule
    {
        /// <summary>
        /// Registry DisplayName patterns to match (case-insensitive, partial match).
        /// Checked in HKLM and HKCU uninstall keys.
        /// </summary>
        public IReadOnlyList<string> RegistryNamePatterns { get; init; } = Array.Empty<string>();
        
        /// <summary>
        /// Known installation folder paths to check for existence.
        /// Supports environment variables like %ProgramFiles%.
        /// </summary>
        public IReadOnlyList<string> KnownFolderPaths { get; init; } = Array.Empty<string>();
        
        /// <summary>
        /// Known executable names to check on PATH.
        /// </summary>
        public IReadOnlyList<string> PathExecutables { get; init; } = Array.Empty<string>();
        
        /// <summary>
        /// Specific registry GUIDs to check (used for VC++ Redistributables).
        /// </summary>
        public IReadOnlyList<string> RegistryGuids { get; init; } = Array.Empty<string>();
        
        /// <summary>
        /// If true, detection is not yet implemented and always returns false.
        /// </summary>
        public bool IsStub { get; init; }
    }

    /// <summary>
    /// View model wrapper for displaying an app entry with installation state.
    /// </summary>
    public sealed class AppCatalogEntryViewModel
    {
        public required AppCatalogEntry Entry { get; init; }
        public bool IsInstalled { get; set; }
        public bool IsSelected { get; set; }
        
        public string DisplayName => Entry.DisplayName;
        public string ShortDescription => Entry.ShortDescription;
        public AppCategory Category => Entry.Category;
        public string? Subcategory => Entry.Subcategory;
        
        /// <summary>
        /// Gets the monogram (first letter) for placeholder icon display.
        /// </summary>
        public string Monogram => string.IsNullOrEmpty(Entry.DisplayName) 
            ? "?" 
            : Entry.DisplayName[0].ToString().ToUpperInvariant();
        
        /// <summary>
        /// Whether this entry has a real icon (not placeholder).
        /// </summary>
        public bool HasRealIcon => !string.IsNullOrEmpty(Entry.IconPath);
        
        /// <summary>
        /// Icon path or null for placeholder.
        /// </summary>
        public string? IconPath => Entry.IconPath;
    }
}
