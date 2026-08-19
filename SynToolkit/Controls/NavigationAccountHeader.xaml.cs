#nullable enable

using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SynToolkit.Models;
using System;
using System.IO;

namespace SynToolkit.Controls
{
    public sealed partial class NavigationAccountHeader : UserControl
    {
        private static readonly Uri PlaceholderAvatarUri =
            new("ms-appx:///assets/Icons/UserAccount.png");

        public NavigationAccountHeader()
        {
            InitializeComponent();
            SetAccountInfo(new UserAccountInfo(string.Empty, null, null));
        }

        public void SetAccountInfo(UserAccountInfo info)
        {
            DisplayNameText.Text = info.DisplayName;

            if (string.IsNullOrWhiteSpace(info.AccountTypeLabel))
            {
                AccountTypeText.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            }
            else
            {
                AccountTypeText.Text = info.AccountTypeLabel;
                AccountTypeText.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            }

            AvatarImage.Source = CreateAvatarSource(info.ProfilePicturePath);
        }

        private static ImageSource CreateAvatarSource(string? profilePicturePath)
        {
            if (!string.IsNullOrWhiteSpace(profilePicturePath) &&
                File.Exists(profilePicturePath) &&
                Uri.TryCreate(Path.GetFullPath(profilePicturePath), UriKind.Absolute, out Uri? fileUri))
            {
                return new BitmapImage(fileUri);
            }

            return new BitmapImage(PlaceholderAvatarUri);
        }
    }
}
