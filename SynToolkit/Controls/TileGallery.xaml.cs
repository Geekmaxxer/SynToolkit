using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SynToolkit.Utils;
using System;

// Taken from https://github.com/microsoft/WinUI-Gallery/blob/main/WinUIGallery/Controls/TileGallery.xaml.cs
namespace SynToolkit.Controls
{
    public sealed partial class TileGallery : UserControl
    {
        // HeaderTile width (200) + StackPanel Spacing (12).
        private const double TileScrollStep = 212;

        private bool _isShowingUpdateNotes;

        public TileGallery()
        {
            this.InitializeComponent();
            SetText();
            ApplyFixedLinks();
            Unloaded += TileGallery_Unloaded;
            Loaded += TileGallery_Loaded;
        }

        private void TileGallery_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateScrollButtonsVisibility();
        }

        private void SetText()
        {
            UpdateNotesTile.Title = App.GetValueFromItemList("Tile_UpdateNotesTitle");
            UpdateNotesTile.Description = App.GetValueFromItemList("Tile_UpdateNotesDescription");
            DocumentationTile.Title = App.GetValueFromItemList("Tile_DocumentationTitle");
            DocumentationTile.Description = App.GetValueFromItemList("Tile_DocumentationDescription");
            SynergyOsTile.Title = App.GetValueFromItemList("Tile_SynergyOsTitle");
            SynergyOsTile.Description = App.GetValueFromItemList("Tile_SynergyOsDescription");
            SynToolkitRepoTile.Title = App.GetValueFromItemList("Tile_SynToolkitTitle");
            SynToolkitRepoTile.Description = App.GetValueFromItemList("Tile_SynToolkitDescription");
            ReportBugTile.Title = App.GetValueFromItemList("Tile_ReportBugTitle");
            ReportBugTile.Description = App.GetValueFromItemList("Tile_ReportBugDescription");
            DiscordTile.Title = App.GetValueFromItemList("Tile_DiscordTitle");
            DiscordTile.Description = App.GetValueFromItemList("Tile_DiscordDescription");
        }

        private void ApplyFixedLinks()
        {
            DocumentationTile.Link = CommunityLinks.KwanteksYouTubeUrl;
            SynergyOsTile.Link = CommunityLinks.SynergyOsRepoUrl;
            SynToolkitRepoTile.Link = CommunityLinks.SynToolkitRepoUrl;
            DiscordTile.Link = CommunityLinks.DiscordInviteUrl;
        }

        private async void UpdateNotesTile_Click(object sender, RoutedEventArgs e)
        {
            if (_isShowingUpdateNotes || XamlRoot is null)
            {
                return;
            }

            try
            {
                _isShowingUpdateNotes = true;
                UpdateNotesDialog.XamlRoot = XamlRoot;
                await UpdateNotesDialog.ShowAsync();
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "The local update-notes dialog could not be displayed.");
            }
            finally
            {
                _isShowingUpdateNotes = false;
            }
        }

        private void ReportBugTile_Click(object sender, RoutedEventArgs e)
        {
            MenuFlyout flyout = new MenuFlyout();

            MenuFlyoutItem synToolkitItem = new MenuFlyoutItem
            {
                Text = App.GetValueFromItemList("Tile_ReportBug_SynToolkit")
            };
            synToolkitItem.Click += async (_, _) =>
                await CommunityLinks.LaunchUriAsync(CommunityLinks.SynToolkitBugTemplateUrl);

            MenuFlyoutItem synergyOsItem = new MenuFlyoutItem
            {
                Text = App.GetValueFromItemList("Tile_ReportBug_SynergyOs")
            };
            synergyOsItem.Click += async (_, _) =>
                await CommunityLinks.LaunchUriAsync(CommunityLinks.SynergyOsBugTemplateUrl);

            flyout.Items.Add(synToolkitItem);
            flyout.Items.Add(synergyOsItem);
            flyout.ShowAt(ReportBugTile);
        }

        private void TileGallery_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_isShowingUpdateNotes)
            {
                UpdateNotesDialog.Hide();
            }
        }

        private void scroller_ViewChanging(object sender, ScrollViewerViewChangingEventArgs e)
        {
            UpdateScrollButtonsForOffset(e.FinalView.HorizontalOffset);
        }

        private void scroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            UpdateScrollButtonsVisibility();
        }

        private void ScrollBackBtn_Click(object sender, RoutedEventArgs e)
        {
            double target = Math.Max(0, scroller.HorizontalOffset - TileScrollStep);
            scroller.ChangeView(target, null, null, false);
            if (ScrollForwardBtn.Visibility == Visibility.Visible)
            {
                ScrollForwardBtn.Focus(FocusState.Programmatic);
            }
        }

        private void ScrollForwardBtn_Click(object sender, RoutedEventArgs e)
        {
            double target = Math.Min(scroller.ScrollableWidth, scroller.HorizontalOffset + TileScrollStep);
            scroller.ChangeView(target, null, null, false);
            if (ScrollBackBtn.Visibility == Visibility.Visible)
            {
                ScrollBackBtn.Focus(FocusState.Programmatic);
            }
        }

        private void scroller_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateScrollButtonsVisibility();
        }

        private void scroller_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            if (scroller.ScrollableWidth <= 0)
            {
                return;
            }

            int delta = e.GetCurrentPoint(scroller).Properties.MouseWheelDelta;
            if (delta == 0)
            {
                return;
            }

            double target = Math.Clamp(
                scroller.HorizontalOffset - delta,
                0,
                scroller.ScrollableWidth);
            scroller.ChangeView(target, null, null, true);
            e.Handled = true;
        }

        private void scroller_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (scroller.ScrollableWidth <= 0)
            {
                return;
            }

            if (e.Key == Windows.System.VirtualKey.Left)
            {
                ScrollBackBtn_Click(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Right)
            {
                ScrollForwardBtn_Click(sender, e);
                e.Handled = true;
            }
        }

        private void UpdateScrollButtonsVisibility()
        {
            if (scroller.ScrollableWidth <= 0)
            {
                ScrollBackBtn.Visibility = Visibility.Collapsed;
                ScrollForwardBtn.Visibility = Visibility.Collapsed;
                ScrollBackBtn.IsEnabled = false;
                ScrollForwardBtn.IsEnabled = false;
                return;
            }

            UpdateScrollButtonsForOffset(scroller.HorizontalOffset);
        }

        private void UpdateScrollButtonsForOffset(double horizontalOffset)
        {
            if (scroller.ScrollableWidth <= 0)
            {
                ScrollBackBtn.Visibility = Visibility.Collapsed;
                ScrollForwardBtn.Visibility = Visibility.Collapsed;
                ScrollBackBtn.IsEnabled = false;
                ScrollForwardBtn.IsEnabled = false;
                return;
            }

            bool canScrollBack = horizontalOffset > 1;
            bool canScrollForward = horizontalOffset < scroller.ScrollableWidth - 1;

            ScrollBackBtn.Visibility = canScrollBack ? Visibility.Visible : Visibility.Collapsed;
            ScrollForwardBtn.Visibility = canScrollForward ? Visibility.Visible : Visibility.Collapsed;
            ScrollBackBtn.IsEnabled = canScrollBack;
            ScrollForwardBtn.IsEnabled = canScrollForward;
        }
    }
}
