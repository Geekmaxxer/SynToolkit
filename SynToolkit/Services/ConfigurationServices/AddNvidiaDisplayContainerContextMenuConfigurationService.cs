using System;
using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace SynToolkit.Services.ConfigurationServices
{
    public class AddNvidiaDisplayContainerContextMenuConfigurationService : IConfigurationService
    {

        private const string SYNTOOLKIT_STORE_KEY_NAME = @"HKLM\SOFTWARE\SynToolkit\Services\NVidiaDisplayContainerContextMenu";
        private const string STATE_VALUE_NAME = "state";

        // Use the explicit machine Classes store. HKCR is a merged view and can
        // otherwise redirect writes to an existing per-user key.
        private const string NVIDIA_CONTAINER_KEY_NAME = @"HKLM\SOFTWARE\Classes\DesktopBackground\Shell\NVIDIAContainer";
        private const string NVIDIA_CONTAINER_001_KEY_NAME = NVIDIA_CONTAINER_KEY_NAME + @"\shell\NVIDIAContainer001";
        private const string NVIDIA_CONTAINER_001_COMMAND_KEY_NAME = NVIDIA_CONTAINER_001_KEY_NAME + @"\command";
        private const string NVIDIA_CONTAINER_002_KEY_NAME = NVIDIA_CONTAINER_KEY_NAME + @"\shell\NVIDIAContainer002";
        private const string NVIDIA_CONTAINER_002_COMMAND_KEY_NAME = NVIDIA_CONTAINER_002_KEY_NAME + @"\command";
        private const string OWNER_VALUE_NAME = "SynToolkitOwner";
        private const string OWNER_VALUE = "SynToolkit";

        private readonly ConfigurationStore _addNvidiaDisplayContainerContextMenuConfigurationService;

        public AddNvidiaDisplayContainerContextMenuConfigurationService(
            [FromKeyedServices("AddNvidiaDisplayContainerContextMenu")]  ConfigurationStore addNvidiaDisplayContainerContextMenuConfigurationService)
        {
            _addNvidiaDisplayContainerContextMenuConfigurationService = addNvidiaDisplayContainerContextMenuConfigurationService;
        }
        public void Disable()
        {
            if (RegistryHelper.KeyExists(NVIDIA_CONTAINER_KEY_NAME))
            {
                GetExpectedValues(
                    out string enableCommand,
                    out string disableCommand,
                    out string iconLocation,
                    out _,
                    out _,
                    out _);

                bool ownedBySynToolkit = RegistryHelper.IsMatch(
                    NVIDIA_CONTAINER_KEY_NAME,
                    OWNER_VALUE_NAME,
                    OWNER_VALUE);
                if (!ownedBySynToolkit && !HasExpectedRegistryLayout(enableCommand, disableCommand, iconLocation))
                {
                    throw new InvalidOperationException(
                        "The NVIDIA Display Container context-menu key already exists but is not owned by SynToolkit. It was left unchanged.");
                }

                RegistryHelper.DeleteKey(NVIDIA_CONTAINER_KEY_NAME);
            }

            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 0);

            _addNvidiaDisplayContainerContextMenuConfigurationService.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            GetExpectedValues(
                out string enableCommand,
                out string disableCommand,
                out string iconLocation,
                out string enableScriptPath,
                out string disableScriptPath,
                out string iconPath);
            string stateScriptPath = GetStateScriptPath();

            if (!System.IO.File.Exists(enableScriptPath)
                || !System.IO.File.Exists(disableScriptPath)
                || !System.IO.File.Exists(stateScriptPath)
                || !System.IO.File.Exists(iconPath))
            {
                throw new System.IO.FileNotFoundException(
                    "The SynToolkit NVIDIA support files are missing from ProgramData.");
            }

            if (RegistryHelper.KeyExists(NVIDIA_CONTAINER_KEY_NAME)
                && !RegistryHelper.IsMatch(NVIDIA_CONTAINER_KEY_NAME, OWNER_VALUE_NAME, OWNER_VALUE)
                && !HasExpectedRegistryLayout(enableCommand, disableCommand, iconLocation))
            {
                throw new InvalidOperationException(
                    "The NVIDIA Display Container context-menu key is already used by another application. SynToolkit left it unchanged.");
            }

            // Write ownership first. If a later write is interrupted, SynToolkit can
            // still identify and safely clean up only the partial tree it created.
            RegistryHelper.SetValue(NVIDIA_CONTAINER_KEY_NAME, OWNER_VALUE_NAME, OWNER_VALUE, Microsoft.Win32.RegistryValueKind.String);
            RegistryHelper.SetValue(NVIDIA_CONTAINER_KEY_NAME, "Icon", iconLocation, Microsoft.Win32.RegistryValueKind.String);
            RegistryHelper.SetValue(NVIDIA_CONTAINER_KEY_NAME, "MUIVerb", "SynToolkit NVIDIA Container", Microsoft.Win32.RegistryValueKind.String);
            RegistryHelper.SetValue(NVIDIA_CONTAINER_KEY_NAME, "Position", "Bottom", Microsoft.Win32.RegistryValueKind.String);
            RegistryHelper.SetValue(NVIDIA_CONTAINER_KEY_NAME, "SubCommands", "", Microsoft.Win32.RegistryValueKind.String);
            RegistryHelper.SetValue(NVIDIA_CONTAINER_001_KEY_NAME, "HasLUAShield", "", Microsoft.Win32.RegistryValueKind.String);
            RegistryHelper.SetValue(NVIDIA_CONTAINER_001_KEY_NAME, "MUIVerb", "Enable NVIDIA Display Container LS", Microsoft.Win32.RegistryValueKind.String);
            RegistryHelper.SetValue(NVIDIA_CONTAINER_001_COMMAND_KEY_NAME, "", enableCommand, Microsoft.Win32.RegistryValueKind.String);
            RegistryHelper.SetValue(NVIDIA_CONTAINER_002_KEY_NAME, "HasLUAShield", "", Microsoft.Win32.RegistryValueKind.String);
            RegistryHelper.SetValue(NVIDIA_CONTAINER_002_KEY_NAME, "MUIVerb", "Disable NVIDIA Display Container LS", Microsoft.Win32.RegistryValueKind.String);
            RegistryHelper.SetValue(NVIDIA_CONTAINER_002_COMMAND_KEY_NAME, "", disableCommand, Microsoft.Win32.RegistryValueKind.String);

            CommandPromptHelper.RestartExplorer();
            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 1);

            _addNvidiaDisplayContainerContextMenuConfigurationService.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            if (!GpuDetectionService.HasNvidiaGpu())
            {
                throw new InvalidOperationException("No NVIDIA GPU was detected on this system.");
            }

            try
            {
                GetExpectedValues(
                    out string enableCommand,
                    out string disableCommand,
                    out string iconLocation,
                    out string enableScriptPath,
                    out string disableScriptPath,
                    out string iconPath);
                string stateScriptPath = GetStateScriptPath();

                bool keyExists = RegistryHelper.KeyExists(NVIDIA_CONTAINER_KEY_NAME);
                if (!keyExists)
                {
                    return false;
                }

                bool hasExpectedLayout = HasExpectedRegistryLayout(enableCommand, disableCommand, iconLocation);
                bool ownedBySynToolkit = RegistryHelper.IsMatch(
                    NVIDIA_CONTAINER_KEY_NAME,
                    OWNER_VALUE_NAME,
                    OWNER_VALUE);
                if (!ownedBySynToolkit && !hasExpectedLayout)
                {
                    throw new InvalidOperationException(
                        "The NVIDIA Display Container context-menu key is owned by another application.");
                }

                return hasExpectedLayout
                    && System.IO.File.Exists(enableScriptPath)
                    && System.IO.File.Exists(disableScriptPath)
                    && System.IO.File.Exists(stateScriptPath)
                    && System.IO.File.Exists(iconPath);
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "Unable to detect NVIDIA context-menu state.");
                throw;
            }
        }

        private static void GetExpectedValues(
            out string enableCommand,
            out string disableCommand,
            out string iconLocation,
            out string enableScriptPath,
            out string disableScriptPath,
            out string iconPath)
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            enableScriptPath = $@"{programData}\Synergy\Scripts\NVidia\EnableNVIDIADisplayContainerLS.cmd";
            disableScriptPath = $@"{programData}\Synergy\Scripts\NVidia\DisableNVIDIADisplayContainerLS.cmd";
            iconPath = $@"{programData}\Synergy\Assets\SynToolkit.ico";
            enableCommand = BuildElevatedScriptCommand(enableScriptPath);
            disableCommand = BuildElevatedScriptCommand(disableScriptPath);
            iconLocation = $"\"{iconPath}\",0";
        }

        private static string GetStateScriptPath()
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            return $@"{programData}\Synergy\Scripts\NVidia\NVIDIADisplayContainerState.ps1";
        }

        private static bool HasExpectedRegistryLayout(
            string enableCommand,
            string disableCommand,
            string iconLocation)
        {
            return RegistryHelper.IsMatch(NVIDIA_CONTAINER_KEY_NAME, "MUIVerb", "SynToolkit NVIDIA Container")
                && RegistryHelper.IsMatch(NVIDIA_CONTAINER_KEY_NAME, "Icon", iconLocation)
                && RegistryHelper.IsMatch(NVIDIA_CONTAINER_KEY_NAME, "Position", "Bottom")
                && RegistryHelper.IsMatch(NVIDIA_CONTAINER_KEY_NAME, "SubCommands", string.Empty)
                && RegistryHelper.IsMatch(NVIDIA_CONTAINER_001_KEY_NAME, "HasLUAShield", string.Empty)
                && RegistryHelper.IsMatch(NVIDIA_CONTAINER_001_KEY_NAME, "MUIVerb", "Enable NVIDIA Display Container LS")
                && RegistryHelper.IsMatch(NVIDIA_CONTAINER_001_COMMAND_KEY_NAME, string.Empty, enableCommand)
                && RegistryHelper.IsMatch(NVIDIA_CONTAINER_002_KEY_NAME, "HasLUAShield", string.Empty)
                && RegistryHelper.IsMatch(NVIDIA_CONTAINER_002_KEY_NAME, "MUIVerb", "Disable NVIDIA Display Container LS")
                && RegistryHelper.IsMatch(NVIDIA_CONTAINER_002_COMMAND_KEY_NAME, string.Empty, disableCommand);
        }

        private static string BuildElevatedScriptCommand(string scriptPath)
        {
            string escapedScriptPath = scriptPath.Replace("'", "''");
            return $"powershell.exe -NoProfile -NonInteractive -WindowStyle Hidden -Command \"Start-Process -FilePath '{escapedScriptPath}' -Verb RunAs\"";
        }
    }
}
