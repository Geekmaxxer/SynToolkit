#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading.Tasks;
using Microsoft.Graphics.Canvas;
using Microsoft.Win32;
using SynToolkit.Utils;

namespace SynToolkit.Services
{
    /// <summary>
    /// Sets the signed-in Windows user's account picture. Windows keys account pictures by
    /// SID under HKLM\...\AccountPicture\Users\&lt;SID&gt; and expects several square JPEGs at
    /// fixed resolutions in %PUBLIC%\AccountPictures\&lt;SID&gt;, plus a copy under the user's
    /// own AppData\Local\Microsoft\Windows\AccountPicture folder — both well-documented,
    /// independently-known Windows account-picture internals.
    /// </summary>
    public static class ProfileImageService
    {
        private static readonly int[] Resolutions = { 32, 40, 48, 64, 96, 192, 208, 240, 424, 448, 1080 };

        public static async Task SetProfilePictureAsync(string sourceImagePath, string userSid, string userProfileFolder)
        {
            string pictureDirectory = Path.Combine(
                Environment.ExpandEnvironmentVariables("%PUBLIC%\\AccountPictures"),
                userSid);

            ResetExistingPictureDirectory(pictureDirectory);
            Directory.CreateDirectory(pictureDirectory);

            CanvasDevice device = CanvasDevice.GetSharedDevice();
            using CanvasBitmap sourceBitmap = await CanvasBitmap.LoadAsync(device, sourceImagePath);

            Guid pictureId = Guid.NewGuid();
            string pfpKey = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\AccountPicture\Users\" + userSid;
            RegistryHelper.DeleteKey(pfpKey);

            string? path448 = null;
            foreach (int resolution in Resolutions)
            {
                using CanvasRenderTarget renderTarget = new(device, resolution, resolution, 96);
                using (CanvasDrawingSession drawingSession = renderTarget.CreateDrawingSession())
                {
                    drawingSession.Clear(Windows.UI.Color.FromArgb(255, 255, 255, 255));
                    drawingSession.DrawImage(
                        sourceBitmap,
                        new Windows.Foundation.Rect(0, 0, resolution, resolution),
                        new Windows.Foundation.Rect(0, 0, sourceBitmap.SizeInPixels.Width, sourceBitmap.SizeInPixels.Height));
                }

                string savePath = Path.Combine(pictureDirectory, $"{{{pictureId.ToString().ToUpperInvariant()}}}-Image{resolution}.jpg");
                await renderTarget.SaveAsync(savePath, CanvasBitmapFileFormat.Jpeg);

                RegistryHelper.SetValue(pfpKey, "Image" + resolution, savePath, RegistryValueKind.String);

                if (resolution == 448)
                {
                    path448 = savePath;
                }

                if (resolution == 1080)
                {
                    string userPictureDirectory = Path.Combine(userProfileFolder, @"AppData\Local\Microsoft\Windows\AccountPicture");
                    Directory.CreateDirectory(userPictureDirectory);
                    string userSavePath = Path.Combine(userPictureDirectory, "UserImage.jpg");
                    await renderTarget.SaveAsync(userSavePath, CanvasBitmapFileFormat.Jpeg);
                    File.SetAttributes(userSavePath, FileAttributes.System | FileAttributes.Hidden | FileAttributes.Archive);
                }
            }

            RegistryHelper.SetValue(pfpKey, "UserPicturePath", path448 ?? string.Empty, RegistryValueKind.String);

            TryRefreshGroupPolicy();
        }

        private static void ResetExistingPictureDirectory(string pictureDirectory)
        {
            if (!Directory.Exists(pictureDirectory))
            {
                return;
            }

            try
            {
                Directory.Delete(pictureDirectory, true);
                return;
            }
            catch (UnauthorizedAccessException)
            {
                // Fall through to the ACL reset below. SynToolkit already runs elevated, so
                // granting the current (Administrator) identity full control is normally
                // enough without a separate SeTakeOwnershipPrivilege escalation.
            }

            DirectoryInfo directoryInfo = new(pictureDirectory);
            DirectorySecurity security = directoryInfo.GetAccessControl();
            security.SetOwner(WindowsIdentity.GetCurrent().User!);
            directoryInfo.SetAccessControl(security);

            security = new DirectorySecurity();
            security.AddAccessRule(new FileSystemAccessRule(
                WindowsIdentity.GetCurrent().User!,
                FileSystemRights.FullControl,
                InheritanceFlags.ObjectInherit | InheritanceFlags.ContainerInherit,
                PropagationFlags.NoPropagateInherit,
                AccessControlType.Allow));
            directoryInfo.SetAccessControl(security);

            foreach (FileSystemInfo info in directoryInfo.GetFileSystemInfos("*", SearchOption.AllDirectories))
            {
                info.Attributes = FileAttributes.Normal;
            }

            Directory.Delete(pictureDirectory, true);
        }

        private static void TryRefreshGroupPolicy()
        {
            try
            {
                using Process process = Process.Start(new ProcessStartInfo("gpupdate.exe", "/force")
                {
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false,
                    CreateNoWindow = true
                })!;
                process.WaitForExit(20_000);
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[Adjustments] gpupdate /force failed after setting the profile picture.");
            }
        }
    }
}
