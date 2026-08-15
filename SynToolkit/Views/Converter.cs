using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SynToolkit.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;

namespace SynToolkit.Views
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return value is bool boolValue && boolValue ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return value is Visibility visibility && visibility == Visibility.Visible;
        }
    }

    public class BoolToVisibilityInverseConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return value is bool boolValue && boolValue ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return value is Visibility visibility && visibility == Visibility.Collapsed;
        }
    }

    public class BoolToSeverityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return value is bool isWarning && isWarning ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    internal class FontIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            string iconValue = value as string;
            if (string.IsNullOrEmpty(iconValue))
            {
                return new FontIcon { Glyph = "\uE897" };
            }

            // If it's an image path, create an ImageIcon
            if (iconValue.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                iconValue.StartsWith("ms-appx:", StringComparison.OrdinalIgnoreCase))
            {
                return new ImageIcon { Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(iconValue)) };
            }

            // Otherwise, treat it as a FontIcon glyph
            return new FontIcon { Glyph = iconValue };
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    internal class ImageSourceConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string path &&
                !string.IsNullOrWhiteSpace(path) &&
                Uri.TryCreate(path, UriKind.Absolute, out Uri uri))
            {
                return new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(uri);
            }

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public class ConfigItemDataTemplateSelector : DataTemplateSelector
    {
        public DataTemplate ConfigurationItem { get; set; }
        public DataTemplate MultiOptionConfigurationItem { get; set; }
        public DataTemplate ConfigurationSubMenu { get; set; }
        public DataTemplate ConfigurationButton { get; set; }
        public DataTemplate ConfiguartionLink { get; set; }
        protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        {
            if (item is ConfigurationItemViewModel)
            {
                return ConfigurationItem;
            }
            if (item is MultiOptionConfigurationItemViewModel)
            {
                return MultiOptionConfigurationItem;
            }
            if (item is ConfigurationSubMenuViewModel)
            {
                return ConfigurationSubMenu;
            }
            if (item is LinksViewModel)
            {
                return ConfiguartionLink;
            }
            if (item is ConfigurationButtonViewModel)
            {
                return ConfigurationButton;
            }

            return base.SelectTemplateCore(item, container);
        }
    }

    public class FavoriteItemDataTemplateSelector : DataTemplateSelector
    {
        public DataTemplate ConfigurationItem { get; set; }
        public DataTemplate MultiOptionConfigurationItem { get; set; }
        public DataTemplate ConfigurationButton { get; set; }
        public DataTemplate ConfiguartionLink { get; set; }
        protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        {
            if (item is ConfigurationItemViewModel)
            {
                return ConfigurationItem;
            }
            if (item is MultiOptionConfigurationItemViewModel)
            {
                return MultiOptionConfigurationItem;
            }
            if (item is LinksViewModel)
            {
                return ConfiguartionLink;
            }
            if (item is ConfigurationButtonViewModel)
            {
                return ConfigurationButton;
            }

            return base.SelectTemplateCore(item, container);
        }
    }
}
