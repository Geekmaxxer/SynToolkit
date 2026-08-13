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

internal static class WindowsWallpaperService
{
    private static readonly string WallpaperDirectory = Path.Combine(
        AppContext.BaseDirectory, "Assets", "Wallpapers");
    
    private const string DefaultWallpaperFileName = "SynergyOS Wallpaper v3.8 silver main.png";

    private const uint SpiSetDesktopWallpaper = 0x0014;
    private const uint SpiGetDesktopWallpaper = 0x0073;
    private const uint SpifUpdateIniFile = 0x0001;
    private const uint SpifSendWinIniChange = 0x0002;

    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp" };

    public static IReadOnlyList<string> GetAvailableWallpapers()
    {
        if (!Directory.Exists(WallpaperDirectory))
            return [];

        return Directory
            .EnumerateFiles(WallpaperDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    public static WallpaperApplyResult Apply(string filePath)
    {
        try
        {
            var fullPath = Path.GetFullPath(filePath);
            var wallpaperRoot = Path.GetFullPath(WallpaperDirectory)
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            if (!fullPath.StartsWith(wallpaperRoot, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(fullPath) ||
                !SupportedExtensions.Contains(Path.GetExtension(fullPath)))
            {
                return new WallpaperApplyResult(false, "The selected wallpaper is unavailable.");
            }

            var applied = SystemParametersInfo(
                SpiSetDesktopWallpaper,
                0,
                fullPath,
                SpifUpdateIniFile | SpifSendWinIniChange);

            return applied
                ? new WallpaperApplyResult(true, $"{GetDisplayName(fullPath)} is now your desktop wallpaper.")
                : new WallpaperApplyResult(false, "Windows rejected the wallpaper change.");
        }
        catch (Exception ex)
        {
            return new WallpaperApplyResult(false, ex.Message);
        }
    }

    public static string GetDisplayName(string filePath)
    {
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
    
    public static string? GetDefaultWallpaperPath()
    {
        var defaultPath = Path.Combine(WallpaperDirectory, DefaultWallpaperFileName);
        if (File.Exists(defaultPath))
            return defaultPath;
        
        // Fallback to first available if default file is missing
        var wallpapers = GetAvailableWallpapers();
        return wallpapers.Count > 0 ? wallpapers[0] : null;
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
