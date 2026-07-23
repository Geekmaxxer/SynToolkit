#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SynToolkit.Services.NvidiaProfileInspector
{
    public sealed record BundledNvidiaProfileFile(string DisplayName, string FullPath);

    /// <summary>
    /// Lists bundled .nip files under assets/NvidiaProfiles so they can be picked from a
    /// gallery instead of always browsing for a file. SynToolkit ships this folder empty —
    /// fabricating "known-good" NVIDIA setting IDs without the ability to verify them against
    /// real hardware would risk silently writing incorrect driver settings, which conflicts
    /// with this feature's own correctness bar. Users (or a future update) can drop trusted
    /// .nip files into the folder to populate the gallery; see assets/NvidiaProfiles/README.txt.
    /// </summary>
    public static class NvidiaProfileGalleryService
    {
        public static IReadOnlyList<BundledNvidiaProfileFile> GetBundledProfiles()
        {
            string galleryDirectory = Path.Combine(AppContext.BaseDirectory, "assets", "NvidiaProfiles");
            if (!Directory.Exists(galleryDirectory))
            {
                return Array.Empty<BundledNvidiaProfileFile>();
            }

            return Directory.EnumerateFiles(galleryDirectory, "*.nip", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => new BundledNvidiaProfileFile(Path.GetFileNameWithoutExtension(path), path))
                .ToList();
        }
    }
}
