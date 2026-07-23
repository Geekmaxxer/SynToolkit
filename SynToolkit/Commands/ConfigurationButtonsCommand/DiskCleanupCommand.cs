using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using SynToolkit.Views;

namespace SynToolkit.Commands.ConfigurationButtonsCommand
{
    public class DiskCleanupCommand : AsyncCommandBase
    {
        protected override async Task ExecuteAsync(object parameter)
        {
            await Task.CompletedTask;
            
            // Navigate to the Disk Cleanup tab
            if (App.m_window?.Content is Grid rootGrid)
            {
                var navView = FindNavigationView(rootGrid);
                if (navView != null)
                {
                    foreach (var item in navView.MenuItems)
                    {
                        if (item is NavigationViewItem navItem && 
                            navItem.Tag?.ToString() == "SynToolkit.Views.CleanerPage")
                        {
                            navView.SelectedItem = navItem;
                            break;
                        }
                    }
                }
            }
        }

        private static NavigationView FindNavigationView(Microsoft.UI.Xaml.DependencyObject parent)
        {
            int childCount = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is NavigationView nav)
                    return nav;
                var result = FindNavigationView(child);
                if (result != null)
                    return result;
            }
            return null;
        }
    }
}
