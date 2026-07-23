using System;
using System.Threading;
using System.Threading.Tasks;
using SynToolkit.Utils;
using SynToolkit.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SynToolkit.Commands.ConfigurationButtonsCommand
{
    public class SetUpdateDeferralConfigurationButton : AsyncCommandBase
    {
        protected override async Task ExecuteAsync(object parameter)
        {
            UpdateDeferralPage page = new UpdateDeferralPage();
            await Task.Run(() =>
            {
                // stupid hack idk why microsoft doesn't allow us to do a DispatcherQueue.EnqueueAsync
                // in things other than pages and whatnot
                page.ShowUpdateDeferralPrompt();
            });
        }

        public static void SetUpdateDeferral(int featureDays, int qualityDays)
        {
            const string WINDOWS_UPDATE_KEY = "HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate";
            
            RegistryHelper.SetValue(WINDOWS_UPDATE_KEY, "DeferFeatureUpdates", 1, Microsoft.Win32.RegistryValueKind.DWord);
            RegistryHelper.SetValue(WINDOWS_UPDATE_KEY, "DeferFeatureUpdatesPeriodInDays", featureDays, Microsoft.Win32.RegistryValueKind.DWord);

            RegistryHelper.SetValue(WINDOWS_UPDATE_KEY, "DeferQualityUpdates", 1, Microsoft.Win32.RegistryValueKind.DWord);
            RegistryHelper.SetValue(WINDOWS_UPDATE_KEY, "DeferQualityUpdatesPeriodInDays", qualityDays, Microsoft.Win32.RegistryValueKind.DWord);

            RegistryHelper.SetValue("HKLM\\SOFTWARE\\SynToolkit\\FeatureUpdateDeferrals", "state", 1, Microsoft.Win32.RegistryValueKind.DWord);
            RegistryHelper.SetValue("HKLM\\SOFTWARE\\SynToolkit\\FeatureUpdateDeferrals", "value", featureDays, Microsoft.Win32.RegistryValueKind.DWord);
                                                                                        
            RegistryHelper.SetValue("HKLM\\SOFTWARE\\SynToolkit\\QualityUpdateDeferrals", "state", 1, Microsoft.Win32.RegistryValueKind.DWord);
            RegistryHelper.SetValue("HKLM\\SOFTWARE\\SynToolkit\\QualityUpdateDeferrals", "value", qualityDays, Microsoft.Win32.RegistryValueKind.DWord);
        }
    }
}
