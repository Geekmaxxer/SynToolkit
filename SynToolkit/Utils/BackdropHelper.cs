using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace SynToolkit.Utils
{
    /// <summary>
    /// Applies a solid-color fallback background when MicaBackdrop composition is
    /// unsupported. A shortcut launch triggers full UAC elevation (SynToolkit's
    /// app.manifest requests requireAdministrator), and DWM can fail to composite Mica
    /// across that medium-to-high integrity boundary; without a fallback the window's
    /// swapchain paints solid black instead of falling back to a themed background.
    /// </summary>
    public static class BackdropHelper
    {
        public static void ApplySafeMicaFallback(Window window, Panel rootPanel)
        {
            if (MicaController.IsSupported())
            {
                return;
            }

            window.SystemBackdrop = null;
            rootPanel.Background = Application.Current.Resources["ApplicationPageBackgroundThemeBrush"] as Brush
                ?? new SolidColorBrush(Colors.Black);
        }
    }
}
