#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SynToolkit.Services;
using SynToolkit.Utils;

namespace SynToolkit.Views
{
    internal sealed class WallpaperItem
    {
        public required string FilePath { get; init; }
        public required string DisplayName { get; init; }
        public string ThumbnailPath => FilePath;
        public bool IsSelected { get; set; }
        public Microsoft.UI.Xaml.Media.Brush BorderBrush => IsSelected
            ? (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["AccentFillColorDefaultBrush"]
            : (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["ControlStrokeColorDefaultBrush"];
        public Thickness BorderThickness => IsSelected ? new Thickness(2) : new Thickness(1);
    }

    public sealed partial class AdjustmentsPage : Page
    {
        private const string ImageFileFilter = "Image files (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp";

        private bool _isPageLoaded;
        private string? _currentWallpaperPath;

        public AdjustmentsPage()
        {
            InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            _isPageLoaded = true;
            bool isElevated = IsCurrentProcessElevated();
            ElevationInfoBar.IsOpen = !isElevated;
            SetActionsEnabled(isElevated);
            LoadWallpapers();
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e) => _isPageLoaded = false;

        private void SetActionsEnabled(bool enabled)
        {
            ChangePasswordButton.IsEnabled = enabled;
            ChangeDisplayNameButton.IsEnabled = enabled;
            ChangeAdminPasswordButton.IsEnabled = enabled;
            ChangeProfilePictureButton.IsEnabled = enabled;
            ChangeLockscreenImageButton.IsEnabled = enabled;
            AddKeyboardLanguageButton.IsEnabled = enabled;
            RemoveKeyboardLanguageButton.IsEnabled = enabled;
        }

        private static bool IsCurrentProcessElevated()
        {
            try
            {
                using WindowsIdentity identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[Adjustments] Unable to determine whether SynToolkit is running elevated.");
                return false;
            }
        }

        private async void ChangePasswordButton_Click(object sender, RoutedEventArgs e)
        {
            string[]? values = await ShowFormDialogAsync("Change password", "Change", ("New password", true));
            if (values is null)
            {
                return;
            }

            await RunActionAsync(
                () => UserIdentityService.ChangePassword(values[0]),
                "Password changed.");
        }

        private async void ChangeDisplayNameButton_Click(object sender, RoutedEventArgs e)
        {
            string[]? values = await ShowFormDialogAsync("Change display name", "Change", ("New display name", false));
            if (values is null)
            {
                return;
            }

            await RunActionAsync(
                () => UserIdentityService.ChangeDisplayName(values[0]),
                "Display name changed.");
        }

        private async void ChangeAdminPasswordButton_Click(object sender, RoutedEventArgs e)
        {
            string[]? values = await ShowFormDialogAsync("Change Administrator password", "Change", ("New Administrator password", true));
            if (values is null)
            {
                return;
            }

            await RunActionAsync(
                () => UserIdentityService.ChangeAdministratorPassword(values[0]),
                "Administrator password changed.");
        }

        private async void ChangeProfilePictureButton_Click(object sender, RoutedEventArgs e)
        {
            string? filePath = ShowImagePicker();
            if (filePath is null)
            {
                return;
            }

            await RunActionAsync(
                async () =>
                {
                    string? sid = WindowsIdentity.GetCurrent().User?.Value;
                    if (string.IsNullOrEmpty(sid))
                    {
                        throw new InvalidOperationException("Unable to resolve the signed-in user's SID.");
                    }

                    string profileFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    await ProfileImageService.SetProfilePictureAsync(filePath, sid, profileFolder);
                },
                "Profile picture changed.");
        }

        private async void ChangeLockscreenImageButton_Click(object sender, RoutedEventArgs e)
        {
            string? filePath = ShowImagePicker();
            if (filePath is null)
            {
                return;
            }

            ContentDialog blurDialog = new()
            {
                XamlRoot = XamlRoot,
                Style = Microsoft.UI.Xaml.Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                Title = "Remove lock screen blur?",
                Content = "Windows applies an acrylic blur effect over the lock screen image by default.",
                PrimaryButtonText = "Remove blur",
                SecondaryButtonText = "Keep blur",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };
            ContentDialogResult blurResult = await blurDialog.ShowAsync();
            if (blurResult != ContentDialogResult.Primary && blurResult != ContentDialogResult.Secondary)
            {
                return;
            }
            bool removeBlur = blurResult == ContentDialogResult.Primary;

            await RunActionAsync(
                () =>
                {
                    string? sid = WindowsIdentity.GetCurrent().User?.Value;
                    if (string.IsNullOrEmpty(sid))
                    {
                        throw new InvalidOperationException("Unable to resolve the signed-in user's SID.");
                    }

                    LockscreenImageService.SetLockscreenImage(filePath, sid, removeBlur);
                },
                "Lock screen image changed.");
        }

        private async void AddKeyboardLanguageButton_Click(object sender, RoutedEventArgs e)
        {
            string[]? values = await ShowFormDialogAsync(
                "Add keyboard language",
                "Add",
                ("Language tag:keyboard ID, e.g. en-US:00000409", false));
            if (values is null)
            {
                return;
            }

            ContentDialog defaultDialog = new()
            {
                XamlRoot = XamlRoot,
                Style = Microsoft.UI.Xaml.Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                Title = "Set as default input method?",
                PrimaryButtonText = "Set as default",
                SecondaryButtonText = "Don't set as default",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Secondary
            };
            ContentDialogResult defaultResult = await defaultDialog.ShowAsync();
            if (defaultResult != ContentDialogResult.Primary && defaultResult != ContentDialogResult.Secondary)
            {
                return;
            }
            bool setAsDefault = defaultResult == ContentDialogResult.Primary;

            await RunActionAsync(
                () => KeyboardLanguageService.AddKeyboardLanguage(values[0].Trim(), setAsDefault),
                "Keyboard language added.");
        }

        private async void RemoveKeyboardLanguageButton_Click(object sender, RoutedEventArgs e)
        {
            string[]? values = await ShowFormDialogAsync(
                "Remove keyboard language",
                "Remove",
                ("Language tag:keyboard ID, e.g. en-US:00000409", false));
            if (values is null)
            {
                return;
            }

            await RunActionAsync(
                () => KeyboardLanguageService.RemoveKeyboardLanguage(values[0].Trim()),
                "Keyboard language removed.");
        }

        private string? ShowImagePicker()
        {
            IntPtr windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
            return NativeFileDialogHelper.ShowOpenFileDialog(windowHandle, ImageFileFilter);
        }

        private async Task<string[]?> ShowFormDialogAsync(string title, string primaryButtonText, params (string Label, bool IsPassword)[] fields)
        {
            StackPanel panel = new() { Spacing = 10 };
            Control[] inputs = new Control[fields.Length];
            for (int i = 0; i < fields.Length; i++)
            {
                panel.Children.Add(new TextBlock { Text = fields[i].Label, TextWrapping = TextWrapping.Wrap });
                if (fields[i].IsPassword)
                {
                    PasswordBox box = new();
                    inputs[i] = box;
                    panel.Children.Add(box);
                }
                else
                {
                    TextBox box = new();
                    inputs[i] = box;
                    panel.Children.Add(box);
                }
            }

            ContentDialog dialog = new()
            {
                XamlRoot = XamlRoot,
                Style = Microsoft.UI.Xaml.Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                Title = title,
                Content = panel,
                PrimaryButtonText = primaryButtonText,
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };

            ContentDialogResult result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return null;
            }

            return inputs.Select(control => control is PasswordBox passwordBox ? passwordBox.Password : ((TextBox)control).Text).ToArray();
        }

        private async Task RunActionAsync(Action action, string successMessage) =>
            await RunActionAsync(() => { action(); return Task.CompletedTask; }, successMessage);

        private async Task RunActionAsync(Func<Task> action, string successMessage)
        {
            SetActionsEnabled(false);
            OperationInfoBar.IsOpen = false;
            try
            {
                await Task.Run(action);
                if (_isPageLoaded)
                {
                    ShowResult("Done", successMessage, InfoBarSeverity.Success);
                }
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[Adjustments] An action failed.");
                if (_isPageLoaded)
                {
                    ShowResult("Action failed", exception.Message, InfoBarSeverity.Error);
                }
            }
            finally
            {
                if (_isPageLoaded)
                {
                    SetActionsEnabled(IsCurrentProcessElevated());
                }
            }
        }

        private void ShowResult(string title, string message, InfoBarSeverity severity)
        {
            OperationInfoBar.Title = title;
            OperationInfoBar.Message = message;
            OperationInfoBar.Severity = severity;
            OperationInfoBar.IsOpen = true;
        }

        private void LoadWallpapers()
        {
            _currentWallpaperPath = WindowsWallpaperService.GetCurrentWallpaper();
            UpdateCurrentWallpaperPreview();

            IReadOnlyList<string> availableWallpapers = WindowsWallpaperService.GetAvailableWallpapers();
            List<WallpaperItem> wallpaperItems = availableWallpapers
                .Select(path => new WallpaperItem
                {
                    FilePath = path,
                    DisplayName = WindowsWallpaperService.GetDisplayName(path),
                    IsSelected = IsCurrentWallpaper(path)
                })
                .ToList();

            WallpaperGridView.ItemsSource = wallpaperItems;
            WallpaperEmptyMessage.Visibility = wallpaperItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            WallpaperGridView.Visibility = wallpaperItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            
            UpdateRestoreDefaultButtonState(wallpaperItems);
        }

        private void UpdateCurrentWallpaperPreview()
        {
            if (string.IsNullOrEmpty(_currentWallpaperPath) || !File.Exists(_currentWallpaperPath))
            {
                CurrentWallpaperName.Text = "Custom wallpaper";
                CurrentWallpaperPath.Text = _currentWallpaperPath ?? "No wallpaper set";
                CurrentWallpaperPreview.ImageSource = null;
                return;
            }

            CurrentWallpaperName.Text = WindowsWallpaperService.GetDisplayName(_currentWallpaperPath);
            CurrentWallpaperPath.Text = _currentWallpaperPath;
            
            try
            {
                CurrentWallpaperPreview.ImageSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(_currentWallpaperPath));
            }
            catch
            {
                CurrentWallpaperPreview.ImageSource = null;
            }
        }

        private void UpdateRestoreDefaultButtonState(List<WallpaperItem>? items = null)
        {
            items ??= WallpaperGridView.ItemsSource as List<WallpaperItem>;
            string? defaultPath = WindowsWallpaperService.GetDefaultWallpaperPath();
            
            bool isDefaultApplied = !string.IsNullOrEmpty(defaultPath) && 
                                    !string.IsNullOrEmpty(_currentWallpaperPath) &&
                                    string.Equals(Path.GetFullPath(defaultPath), Path.GetFullPath(_currentWallpaperPath), StringComparison.OrdinalIgnoreCase);
            
            RestoreDefaultButton.IsEnabled = !isDefaultApplied && defaultPath != null;
        }

        private bool IsCurrentWallpaper(string path)
        {
            if (string.IsNullOrEmpty(_currentWallpaperPath))
                return false;

            try
            {
                return string.Equals(
                    Path.GetFullPath(path),
                    Path.GetFullPath(_currentWallpaperPath),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private void RefreshWallpapersButton_Click(object sender, RoutedEventArgs e)
        {
            LoadWallpapers();
        }

        private async void RestoreDefaultButton_Click(object sender, RoutedEventArgs e)
        {
            string? defaultPath = WindowsWallpaperService.GetDefaultWallpaperPath();
            if (string.IsNullOrEmpty(defaultPath))
            {
                ShowResult("Restore failed", "No default wallpaper available.", InfoBarSeverity.Error);
                return;
            }

            await ApplyWallpaperAsync(defaultPath);
        }

        private async void WallpaperGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not WallpaperItem item)
                return;

            await ApplyWallpaperAsync(item.FilePath);
        }

        private async Task ApplyWallpaperAsync(string wallpaperPath)
        {
            SetActionsEnabled(false);
            RestoreDefaultButton.IsEnabled = false;
            OperationInfoBar.IsOpen = false;

            try
            {
                WallpaperApplyResult result = await Task.Run(() => WindowsWallpaperService.Apply(wallpaperPath));

                if (!_isPageLoaded)
                    return;

                if (result.Success)
                {
                    _currentWallpaperPath = wallpaperPath;
                    UpdateCurrentWallpaperPreview();
                    UpdateWallpaperActiveStates();
                    UpdateRestoreDefaultButtonState();
                    ShowResult("Done", result.Message, InfoBarSeverity.Success);
                }
                else
                {
                    ShowResult("Wallpaper change failed", result.Message, InfoBarSeverity.Error);
                }
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[Adjustments] Wallpaper apply failed.");
                if (_isPageLoaded)
                {
                    ShowResult("Wallpaper change failed", exception.Message, InfoBarSeverity.Error);
                }
            }
            finally
            {
                if (_isPageLoaded)
                {
                    SetActionsEnabled(IsCurrentProcessElevated());
                    UpdateRestoreDefaultButtonState();
                }
            }
        }

        private void UpdateWallpaperActiveStates()
        {
            if (WallpaperGridView.ItemsSource is not List<WallpaperItem> items)
                return;

            foreach (WallpaperItem item in items)
            {
                item.IsSelected = IsCurrentWallpaper(item.FilePath);
            }

            WallpaperGridView.ItemsSource = null;
            WallpaperGridView.ItemsSource = items;
        }
    }
}
