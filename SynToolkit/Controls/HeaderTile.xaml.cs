using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System;

//Taken from https://github.com/microsoft/WinUI-Gallery/blob/main/WinUIGallery/Controls/HeaderTile.xaml.cs

namespace SynToolkit.Controls
{
    public sealed partial class HeaderTile : UserControl
    {
        public event RoutedEventHandler Click;

        public string Title
        {
            get { return (string)GetValue(TitleProperty); }
            set { SetValue(TitleProperty, value); }
        }

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(HeaderTile), new PropertyMetadata(null));

        public string Description
        {
            get { return (string)GetValue(DescriptionProperty); }
            set { SetValue(DescriptionProperty, value); }
        }

        public static readonly DependencyProperty DescriptionProperty =
            DependencyProperty.Register("Description", typeof(string), typeof(HeaderTile), new PropertyMetadata(null));

        public object Source
        {
            get { return (object)GetValue(SourceProperty); }
            set { SetValue(SourceProperty, value); }
        }

        public static readonly DependencyProperty SourceProperty =
            DependencyProperty.Register("Source", typeof(object), typeof(HeaderTile), new PropertyMetadata(null));

        public string Link
        {
            get { return (string)GetValue(LinkProperty); }
            set { SetValue(LinkProperty, value); }
        }

        public static readonly DependencyProperty LinkProperty =
            DependencyProperty.Register(
                "Link",
                typeof(string),
                typeof(HeaderTile),
                new PropertyMetadata(null, OnLinkChanged));

        public HeaderTile()
        {
            this.InitializeComponent();
            UpdateActionSemantics();
        }

        private async void TileButton_Click(object sender, RoutedEventArgs e)
        {
            Click?.Invoke(this, e);

            if (string.IsNullOrWhiteSpace(Link))
            {
                return;
            }

            if (!Uri.TryCreate(Link, UriKind.Absolute, out Uri linkUri))
            {
                App.logger.Warn("Header tile contains an invalid link: {Link}", Link);
                return;
            }

            try
            {
                await Windows.System.Launcher.LaunchUriAsync(linkUri);
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "Header tile link could not be opened.");
            }
        }

        private static void OnLinkChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
        {
            if (dependencyObject is HeaderTile tile)
            {
                tile.UpdateActionSemantics();
            }
        }

        private void UpdateActionSemantics()
        {
            if (TileButton is null || ActionIcon is null)
            {
                return;
            }

            bool opensExternalLink = !string.IsNullOrWhiteSpace(Link);
            AutomationProperties.SetLocalizedControlType(TileButton, opensExternalLink ? "link" : "button");
            ActionIcon.Glyph = opensExternalLink ? "\uE8A7" : "\uE946";
        }
    }
}
