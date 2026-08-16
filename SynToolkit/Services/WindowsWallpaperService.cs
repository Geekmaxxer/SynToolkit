#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace SynToolkit.Services;

internal sealed record WallpaperApplyResult(bool Success, string Message);

internal sealed record WallpaperImportResult(bool Success, string Message, string? ImportedPath);

internal static class WindowsWallpaperService
{
    private static readonly Lazy<string> BundledWallpaperDirectory = new(ResolveBundledWallpaperDirectory);

    private static string WallpaperDirectory => BundledWallpaperDirectory.Value;

    private const string DefaultWallpaperFileName = "SynergyOS Wallpaper v3.8 silver main.png";
    private const long MaxCustomWallpaperBytes = 25L * 1024 * 1024;

    private const uint SpiSetDesktopWallpaper = 0x0014;
    private const uint SpiGetDesktopWallpaper = 0x0073;
    private const uint SpifUpdateIniFile = 0x0001;
    private const uint SpifSendWinIniChange = 0x0002;

    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp" };

    /// <summary>
    /// Per-user custom wallpapers. Lives next to other SynToolkit user data
    /// (%LocalAppData%\SynToolkit) so app updates never wipe user-added files.
    /// </summary>
    public static string CustomWallpapersDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SynToolkit",
        "CustomWallpapers");

    private static readonly string PreviousWallpaperStatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SynToolkit",
        "previous-wallpaper.txt");

    public static void EnsureCustomWallpapersDirectory() =>
        Directory.CreateDirectory(CustomWallpapersDirectory);

    /// <summary>
    /// Live-scans the bundled Assets\Wallpapers folder. Called on section load and Refresh.
    /// </summary>
    public static IReadOnlyList<string> GetAvailableWallpapers() =>
        EnumerateImageFiles(WallpaperDirectory);

    /// <summary>
    /// Live-scans %LocalAppData%\SynToolkit\CustomWallpapers, including files
    /// dropped in via Explorer. Called on section load and Refresh.
    /// Newest imports first so the section reads as a reusable history.
    /// </summary>
    public static IReadOnlyList<string> GetCustomWallpapers()
    {
        EnsureCustomWallpapersDirectory();
        return EnumerateImageFiles(CustomWallpapersDirectory, newestFirst: true);
    }

    public static string? GetCurrentWallpaper()
    {
        var buffer = new StringBuilder(32768);
        return SystemParametersInfo(
            SpiGetDesktopWallpaper,
            (uint)buffer.Capacity,
            buffer,
            0)
            ? buffer.ToString()
            : null;
    }

    /// <summary>
    /// Returns the known bundled or custom wallpaper whose normalized path
    /// matches <paramref name="path"/>, or null if the wallpaper was set
    /// outside SynToolkit (Windows cache, Spotlight, another app, etc.).
    /// Matching the current desktop wallpaper may consult both libraries.
    /// This is identity lookup only — it must never feed a merged UI list.
    /// </summary>
    public static string? FindKnownWallpaperPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }

        foreach (string candidate in GetAvailableWallpapers().Concat(GetCustomWallpapers()))
        {
            if (PathsEqual(candidate, fullPath))
                return candidate;
        }

        return null;
    }

    public static bool IsCustomWallpaperPath(string filePath) =>
        IsPathInside(filePath, CustomWallpapersDirectory);

    public static WallpaperApplyResult Apply(string filePath)
    {
        try
        {
            var fullPath = Path.GetFullPath(filePath);
            if (!IsAllowedApplyPath(fullPath))
            {
                return new WallpaperApplyResult(false, "The selected wallpaper is unavailable.");
            }

            string? currentWallpaper = GetCurrentWallpaper();

            var applied = SystemParametersInfo(
                SpiSetDesktopWallpaper,
                0,
                fullPath,
                SpifUpdateIniFile | SpifSendWinIniChange);

            if (!applied)
            {
                return new WallpaperApplyResult(false, "Windows rejected the wallpaper change.");
            }

            if (!string.IsNullOrWhiteSpace(currentWallpaper) && !PathsEqual(currentWallpaper, fullPath))
            {
                WritePreviousWallpaperPath(currentWallpaper);
            }

            return new WallpaperApplyResult(true, $"{GetDisplayName(fullPath)} is now your desktop wallpaper.");
        }
        catch (Exception ex)
        {
            return new WallpaperApplyResult(false, ex.Message);
        }
    }

    public static string? GetPreviousWallpaperPath()
    {
        string? path = ReadPreviousWallpaperPath();
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    public static bool CanRestorePreviousWallpaper(string? currentWallpaperPath)
    {
        string? previous = GetPreviousWallpaperPath();
        if (string.IsNullOrWhiteSpace(previous) || !File.Exists(previous) || !HasValidImageHeader(previous))
            return false;

        if (string.IsNullOrWhiteSpace(currentWallpaperPath))
            return true;

        if (PathsEqual(previous, currentWallpaperPath))
            return false;

        string? knownCurrent = FindKnownWallpaperPath(currentWallpaperPath);
        string? knownPrevious = FindKnownWallpaperPath(previous);
        if (knownCurrent is not null &&
            knownPrevious is not null &&
            PathsEqual(knownCurrent, knownPrevious))
        {
            return false;
        }

        return true;
    }

    public static WallpaperImportResult ImportCustomWallpaper(string sourcePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                return new WallpaperImportResult(false, "The selected file could not be found.", null);
            }

            var extension = Path.GetExtension(sourcePath);
            if (!SupportedExtensions.Contains(extension))
            {
                return new WallpaperImportResult(false, "Choose a JPG, JPEG, PNG, or BMP image.", null);
            }

            var sourceInfo = new FileInfo(sourcePath);
            if (sourceInfo.Length <= 0)
            {
                return new WallpaperImportResult(false, "The selected file is empty.", null);
            }

            if (sourceInfo.Length > MaxCustomWallpaperBytes)
            {
                return new WallpaperImportResult(
                    false,
                    "That image is larger than 25 MB. Choose a smaller file.",
                    null);
            }

            if (!HasValidImageHeader(sourcePath))
            {
                return new WallpaperImportResult(
                    false,
                    "The selected file is not a valid image.",
                    null);
            }

            EnsureCustomWallpapersDirectory();

            var sourceFullPath = Path.GetFullPath(sourcePath);
            if (IsPathInside(sourceFullPath, CustomWallpapersDirectory))
            {
                return new WallpaperImportResult(
                    true,
                    $"{GetDisplayName(sourceFullPath)} is already in Your Wallpapers.",
                    sourceFullPath);
            }

            var destinationPath = GetUniqueDestinationPath(sourcePath);
            File.Copy(sourcePath, destinationPath, overwrite: false);
            try
            {
                File.SetCreationTimeUtc(destinationPath, DateTime.UtcNow);
            }
            catch
            {
                // Timestamp is only used for history ordering.
            }

            return new WallpaperImportResult(
                true,
                $"{GetDisplayName(destinationPath)} was added to Your Wallpapers.",
                destinationPath);
        }
        catch (Exception ex)
        {
            return new WallpaperImportResult(false, ex.Message, null);
        }
    }

    public static WallpaperApplyResult DeleteCustomWallpaper(string filePath)
    {
        try
        {
            var fullPath = Path.GetFullPath(filePath);
            if (!IsPathInside(fullPath, CustomWallpapersDirectory) ||
                !File.Exists(fullPath) ||
                !SupportedExtensions.Contains(Path.GetExtension(fullPath)))
            {
                return new WallpaperApplyResult(false, "That wallpaper is not in Your Wallpapers.");
            }

            File.Delete(fullPath);
            return new WallpaperApplyResult(true, $"{GetDisplayName(fullPath)} was removed from Your Wallpapers.");
        }
        catch (Exception ex)
        {
            return new WallpaperApplyResult(false, ex.Message);
        }
    }

    public static string GetDisplayName(string filePath)
    {
        if (IsPathInside(filePath, CustomWallpapersDirectory))
            return GetCustomDisplayName(filePath);

        var name = Path.GetFileNameWithoutExtension(filePath);

        // Remove common prefixes like "SynergyOS Wallpaper" or "SynergyOS wallpaper"
        name = System.Text.RegularExpressions.Regex.Replace(
            name,
            @"^SynergyOS\s+Wallpaper\s*",
            "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Extract version number before removing it (for potential use)
        var versionMatch = System.Text.RegularExpressions.Regex.Match(
            name,
            @"^v?(\d+\.?\d*)\s*",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        string version = versionMatch.Success ? versionMatch.Groups[1].Value : "";

        // Remove version prefix like "v3.5 " or "4.4 "
        name = System.Text.RegularExpressions.Regex.Replace(
            name,
            @"^v?\d+\.?\d*\s*",
            "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Clean up remaining text
        name = name.Replace('-', ' ').Replace('_', ' ').Trim();

        // If name is empty after cleanup, use version as fallback
        if (string.IsNullOrWhiteSpace(name))
        {
            name = !string.IsNullOrEmpty(version) ? $"Version {version}" : Path.GetFileNameWithoutExtension(filePath);
        }
        // If we have a version and a generic/common name, append version to differentiate
        else if (!string.IsNullOrEmpty(version) && IsGenericName(name))
        {
            name = $"{name} ({version})";
        }

        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name.ToLowerInvariant());
    }

    public static string GetCurrentWallpaperTitle(string? currentPath)
    {
        if (string.IsNullOrWhiteSpace(currentPath))
            return "No wallpaper set";

        string? knownPath = FindKnownWallpaperPath(currentPath);
        return knownPath is null
            ? "Set outside SynToolkit"
            : GetDisplayName(knownPath);
    }

    public static string GetCurrentWallpaperSubtitle(string? currentPath)
    {
        if (string.IsNullOrWhiteSpace(currentPath))
            return "Windows has no desktop wallpaper recorded";

        string? knownPath = FindKnownWallpaperPath(currentPath);
        if (knownPath is null)
            return "Applied from Windows or another app";

        return IsCustomWallpaperPath(knownPath)
            ? "Your wallpaper"
            : "SynToolkit wallpaper";
    }

    public static string? GetDefaultWallpaperPath()
    {
        var defaultPath = Path.Combine(WallpaperDirectory, DefaultWallpaperFileName);
        if (File.Exists(defaultPath))
            return defaultPath;

        // Fallback to first bundled wallpaper if the named default is missing.
        // Never fall back to a custom wallpaper.
        var wallpapers = GetAvailableWallpapers();
        return wallpapers.Count > 0 ? wallpapers[0] : null;
    }

    private static string GetCustomDisplayName(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath)
            .Replace('-', ' ')
            .Replace('_', ' ')
            .Trim();

        if (string.IsNullOrWhiteSpace(name) || !name.Any(char.IsLetter))
            return "Custom wallpaper";

        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name.ToLowerInvariant());
    }

    private static bool IsGenericName(string name)
    {
        var genericNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "mixed text only",
            "text only",
            "variant",
            "varient",
            "default",
            "main"
        };
        return genericNames.Contains(name.Trim());
    }

    private static IReadOnlyList<string> EnumerateImageFiles(string directory, bool newestFirst = false)
    {
        if (!Directory.Exists(directory))
            return [];

        var results = new List<string>();
        foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
        {
            try
            {
                string extension = Path.GetExtension(path);
                if (!SupportedExtensions.Contains(extension))
                    continue;

                if (!HasValidImageHeader(path))
                {
                    App.logger.Warn($"[Wallpaper] Skipping file that is not a readable image: {path}");
                    continue;
                }

                results.Add(path);
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "[Wallpaper] Skipping unreadable wallpaper file: {0}", path);
            }
        }

        if (newestFirst)
        {
            return results
                .OrderByDescending(GetImportTimestampUtc)
                .ThenBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return results
            .OrderBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static DateTime GetImportTimestampUtc(string path)
    {
        var info = new FileInfo(path);
        return info.CreationTimeUtc >= info.LastWriteTimeUtc
            ? info.CreationTimeUtc
            : info.LastWriteTimeUtc;
    }

    private static bool IsAllowedApplyPath(string fullPath)
    {
        if (IsAllowedWallpaperPath(fullPath))
            return true;

        // Restore Previous may re-apply a Windows-managed file we captured
        // from GetCurrentWallpaper() immediately before the last apply.
        string? previous = ReadPreviousWallpaperPath();
        return !string.IsNullOrWhiteSpace(previous) &&
               PathsEqual(previous, fullPath) &&
               File.Exists(fullPath) &&
               HasValidImageHeader(fullPath);
    }

    private static string? ReadPreviousWallpaperPath()
    {
        try
        {
            if (!File.Exists(PreviousWallpaperStatePath))
                return null;

            string path = File.ReadAllText(PreviousWallpaperStatePath).Trim();
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        catch (Exception exception)
        {
            App.logger.Warn(exception, "[Wallpaper] Unable to read previous wallpaper path.");
            return null;
        }
    }

    private static void WritePreviousWallpaperPath(string path)
    {
        try
        {
            string directory = Path.GetDirectoryName(PreviousWallpaperStatePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(PreviousWallpaperStatePath, path);
        }
        catch (Exception exception)
        {
            App.logger.Warn(exception, "[Wallpaper] Unable to save previous wallpaper path.");
        }
    }

    private static bool IsAllowedWallpaperPath(string fullPath) =>
        File.Exists(fullPath) &&
        SupportedExtensions.Contains(Path.GetExtension(fullPath)) &&
        (IsPathInside(fullPath, WallpaperDirectory) || IsPathInside(fullPath, CustomWallpapersDirectory));

    private static bool IsPathInside(string filePath, string directory)
    {
        try
        {
            var fullPath = Path.GetFullPath(filePath);
            var root = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string GetUniqueDestinationPath(string sourcePath)
    {
        var fileName = Path.GetFileName(sourcePath);
        foreach (char invalid in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(invalid, '_');

        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "Custom wallpaper.png";

        var destination = Path.Combine(CustomWallpapersDirectory, fileName);
        if (!File.Exists(destination))
            return destination;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (int i = 1; i < 10_000; i++)
        {
            destination = Path.Combine(CustomWallpapersDirectory, $"{stem} ({i}){extension}");
            if (!File.Exists(destination))
                return destination;
        }

        throw new IOException("Too many wallpapers already use this file name.");
    }

    private static string ResolveBundledWallpaperDirectory()
    {
        string baseDirectory = AppContext.BaseDirectory;
        string[] candidates =
        [
            Path.Combine(baseDirectory, "Assets", "Wallpapers"),
            Path.Combine(baseDirectory, "assets", "Wallpapers")
        ];

        foreach (string candidate in candidates)
        {
            string fullPath = Path.GetFullPath(candidate);
            if (Directory.Exists(fullPath))
            {
                int fileCount = 0;
                try
                {
                    fileCount = Directory.EnumerateFiles(fullPath, "*", SearchOption.TopDirectoryOnly).Count();
                }
                catch
                {
                    // Count is only for diagnostics.
                }

                App.logger.Info($"[Wallpaper] Bundled directory: {fullPath} (files={fileCount})");
                App.logger.Info($"[Wallpaper] Custom directory: {CustomWallpapersDirectory}");
                return fullPath;
            }
        }

        App.logger.Warn($"[Wallpaper] Bundled directory not found. BaseDirectory={baseDirectory}");
        App.logger.Info($"[Wallpaper] Custom directory: {CustomWallpapersDirectory}");
        return Path.GetFullPath(candidates[0]);
    }

    private static bool HasValidImageHeader(string path)
    {
        try
        {
            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            byte[] header = new byte[12];
            int read = stream.Read(header, 0, header.Length);
            if (read < 2)
                return false;

            // Identify by content, not extension. Several shipped wallpapers are
            // JPEG or WebP files that were saved with a .png extension.
            if (header[0] == 0xFF && header[1] == 0xD8)
                return true;

            if (header[0] == 0x42 && header[1] == 0x4D)
                return true;

            if (read >= 6 &&
                header[0] == (byte)'G' && header[1] == (byte)'I' && header[2] == (byte)'F' &&
                header[3] == (byte)'8' && (header[4] == (byte)'7' || header[4] == (byte)'9') &&
                header[5] == (byte)'a')
            {
                return true;
            }

            if (read >= 8 &&
                header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
                header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
            {
                return true;
            }

            if (read >= 12 &&
                header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F' &&
                header[8] == (byte)'W' && header[9] == (byte)'E' && header[10] == (byte)'B' && header[11] == (byte)'P')
            {
                return true;
            }

            return false;
        }
        catch (Exception exception)
        {
            App.logger.Warn(exception, $"[Wallpaper] Unable to read image header: {path}");
            return false;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(
        uint uiAction,
        uint uiParam,
        StringBuilder pvParam,
        uint fWinIni);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(
        uint uiAction,
        uint uiParam,
        string pvParam,
        uint fWinIni);
}
