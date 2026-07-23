using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using SynToolkit.Stores;
using SynToolkit.Utils;
using System;

namespace SynToolkit.Services.ConfigurationServices
{
    public class CpuIdleContextMenuConfigurationService : IConfigurationService
    {
        // HKLM\Software\Classes is the machine-owned half of the merged HKCR view.
        private const string MENU_KEY_NAME =
            @"HKLM\SOFTWARE\Classes\DesktopBackground\Shell\SynToolkit.CpuIdle";
        private const string PROCESSOR_IDLE_DISABLE_GUID = "5d76a2ca-e8c0-402f-a133-2158492d58ad";

        private static readonly string DisableIdleCommand = CreatePowerCommand(1);
        private static readonly string EnableIdleCommand = CreatePowerCommand(0);

        private readonly ConfigurationStore _cpuIdleContextMenuConfigurationStore;

        public CpuIdleContextMenuConfigurationService(
            [FromKeyedServices("CpuIdleContextMenu")] ConfigurationStore cpuIdleContextMenuConfigurationStore)
        {
            _cpuIdleContextMenuConfigurationStore = cpuIdleContextMenuConfigurationStore;
        }

        public void Disable()
        {
            RegistryHelper.DeleteKey(MENU_KEY_NAME);
            UpdateDetectedState(expectedState: false);
        }

        public void Enable()
        {
            RegistryHelper.SetValue(MENU_KEY_NAME, "Icon", "powercpl.dll");
            RegistryHelper.SetValue(MENU_KEY_NAME, "MUIVerb", "CPU Idle");
            RegistryHelper.SetValue(MENU_KEY_NAME, "Position", "Bottom");
            RegistryHelper.SetValue(MENU_KEY_NAME, "SubCommands", string.Empty);

            string disableIdleKeyName = $@"{MENU_KEY_NAME}\Shell\DisableIdle";
            RegistryHelper.SetValue(disableIdleKeyName, "MUIVerb", "Disable CPU idle");
            RegistryHelper.SetValue(disableIdleKeyName, "Icon", "powercpl.dll");
            RegistryHelper.SetValue(
                $@"{disableIdleKeyName}\Command",
                null,
                DisableIdleCommand,
                RegistryValueKind.String);

            string enableIdleKeyName = $@"{MENU_KEY_NAME}\Shell\EnableIdle";
            RegistryHelper.SetValue(enableIdleKeyName, "MUIVerb", "Enable CPU idle");
            RegistryHelper.SetValue(enableIdleKeyName, "Icon", "powercpl.dll");
            RegistryHelper.SetValue(
                $@"{enableIdleKeyName}\Command",
                null,
                EnableIdleCommand,
                RegistryValueKind.String);

            UpdateDetectedState(expectedState: true);
        }

        public bool IsEnabled()
        {
            return RegistryHelper.IsMatch(MENU_KEY_NAME, "MUIVerb", "CPU Idle")
                && RegistryHelper.IsMatch(
                    $@"{MENU_KEY_NAME}\Shell\DisableIdle\Command",
                    null,
                    DisableIdleCommand)
                && RegistryHelper.IsMatch(
                    $@"{MENU_KEY_NAME}\Shell\EnableIdle\Command",
                    null,
                    EnableIdleCommand);
        }

        private void UpdateDetectedState(bool expectedState)
        {
            bool detectedState = IsEnabled();
            _cpuIdleContextMenuConfigurationStore.CurrentSetting = detectedState;

            if (detectedState != expectedState)
            {
                throw new InvalidOperationException("Windows did not accept the requested CPU idle context-menu state.");
            }
        }

        private static string CreatePowerCommand(uint value)
        {
            string powerCommand =
                $"powercfg.exe /setacvalueindex scheme_current sub_processor {PROCESSOR_IDLE_DISABLE_GUID} {value}" +
                " && " +
                $"powercfg.exe /setdcvalueindex scheme_current sub_processor {PROCESSOR_IDLE_DISABLE_GUID} {value}" +
                " && powercfg.exe /setactive scheme_current";

            return "powershell.exe -NoLogo -NoProfile -NonInteractive -Command \"" +
                "$ErrorActionPreference='Stop';" +
                "try{" +
                "$p=Start-Process -FilePath $env:ComSpec -ArgumentList '/d','/c','" + powerCommand +
                "' -Verb RunAs -Wait -PassThru -ErrorAction Stop;" +
                "if($p.ExitCode -ne 0){throw ('The CPU idle update failed with exit code '+$p.ExitCode+'.')}" +
                "}catch{" +
                "Add-Type -AssemblyName PresentationFramework;" +
                "[System.Windows.MessageBox]::Show($_.Exception.Message,'SynToolkit CPU Idle','OK','Error')|Out-Null;" +
                "exit 1}\"";
        }
    }
}
