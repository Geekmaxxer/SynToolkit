#nullable enable

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32;
using Microsoft.Graphics.Canvas;
using SynToolkit.Utils;
using Windows.Storage;
using Windows.System.UserProfile;
using Windows.Foundation;
using Windows.UI;

namespace SynToolkit.Services
{
    /// <summary>
    /// Sets the signed-in user's lock-screen image through the per-user Windows API.
    /// </summary>
    public static class LockscreenImageService
    {
        internal static async Task SetLockscreenImageAsync(
            string sourceImagePath,
            bool removeAcrylicBlur,
            WallpaperFitMode fitMode)
        {
            if (string.IsNullOrWhiteSpace(sourceImagePath) || !File.Exists(sourceImagePath))
            {
                throw new FileNotFoundException("The selected lock-screen image could not be found.", sourceImagePath);
            }

            try
            {
                RegistryHelper.SetValue(
                    @"HKLM\SOFTWARE\Policies\Microsoft\Windows\System",
                    "DisableAcrylicBackgroundOnLogon",
                    removeAcrylicBlur ? 1 : 0,
                    RegistryValueKind.DWord);
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[Adjustments] Unable to set the lock-screen acrylic-blur policy.");
            }

            string renderedDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SynToolkit",
                "LockscreenImages");
            Directory.CreateDirectory(renderedDirectory);

            string renderedPath = Path.Combine(
                renderedDirectory,
                $"Lockscreen-{Guid.NewGuid():N}.png");

            try
            {
                await RenderImageAsync(sourceImagePath, renderedPath, fitMode);
                StorageFile imageFile = await StorageFile.GetFileFromPathAsync(renderedPath);
                await LockScreen.SetImageFileAsync(imageFile);
                RemoveOldRenderedImages(renderedDirectory, renderedPath);
            }
            catch
            {
                if (File.Exists(renderedPath))
                {
                    File.Delete(renderedPath);
                }

                throw;
            }
        }

        private static void RemoveOldRenderedImages(string directory, string activePath)
        {
            foreach (string path in Directory.EnumerateFiles(directory, "Lockscreen-*.png"))
            {
                if (string.Equals(path, activePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    File.Delete(path);
                }
                catch (IOException exception)
                {
                    App.logger.Debug(exception, "[Adjustments] Unable to remove an old rendered lock-screen image.");
                }
            }
        }

        private static async Task RenderImageAsync(string sourceImagePath, string outputPath, WallpaperFitMode fitMode)
        {
            CanvasDevice device = CanvasDevice.GetSharedDevice();
            StorageFile sourceFile = await StorageFile.GetFileFromPathAsync(sourceImagePath);
            CanvasBitmap source = await CanvasBitmap.LoadAsync(device, sourceFile.Path);

            const int outputWidth = 1920;
            const int outputHeight = 1080;
            StorageFolder tempFolder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(outputPath)!);
            StorageFile outputFile = await tempFolder.CreateFileAsync(
                Path.GetFileName(outputPath),
                CreationCollisionOption.ReplaceExisting);

            using (CanvasRenderTarget target = new(device, outputWidth, outputHeight, 96))
            {
                using (CanvasDrawingSession drawingSession = target.CreateDrawingSession())
                {
                    drawingSession.Clear(new Color { A = 255, R = 0, G = 0, B = 0 });

                    float sourceWidth = source.SizeInPixels.Width;
                    float sourceHeight = source.SizeInPixels.Height;
                    Rect destination = GetDestinationRect(sourceWidth, sourceHeight, outputWidth, outputHeight, fitMode);

                    if (fitMode == WallpaperFitMode.Tile)
                    {
                        for (double y = 0; y < outputHeight; y += sourceHeight)
                        {
                            for (double x = 0; x < outputWidth; x += sourceWidth)
                            {
                                drawingSession.DrawImage(source, new Rect(x, y, sourceWidth, sourceHeight));
                            }
                        }
                    }
                    else
                    {
                        drawingSession.DrawImage(source, destination);
                    }
                }

                await target.SaveAsync(outputFile.Path, CanvasBitmapFileFormat.Png);
            }
        }

        private static Rect GetDestinationRect(float sourceWidth, float sourceHeight, int outputWidth, int outputHeight, WallpaperFitMode fitMode)
        {
            if (fitMode == WallpaperFitMode.Stretch)
            {
                return new Rect(0, 0, outputWidth, outputHeight);
            }

            double scale = fitMode == WallpaperFitMode.Fit
                ? Math.Min(outputWidth / sourceWidth, outputHeight / sourceHeight)
                : Math.Max(outputWidth / sourceWidth, outputHeight / sourceHeight);

            if (fitMode == WallpaperFitMode.Center)
            {
                scale = 1;
            }

            double width = sourceWidth * scale;
            double height = sourceHeight * scale;
            return new Rect(
                (outputWidth - width) / 2,
                (outputHeight - height) / 2,
                width,
                height);
        }
    }
}