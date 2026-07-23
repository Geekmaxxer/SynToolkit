using System.Collections.Generic;
using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;

namespace SynToolkit.Services.ConfigurationServices
{
    public class ContextMenuTerminalsConfigurationService : IMultiOptionConfigurationServices
    {

        private const string TERMINALS_MENU_KEY_NAME =
            @"HKLM\SOFTWARE\Classes\Directory\shell\SynToolkitTerminals";
        private const string WINDOWS_TERMINAL_ITEM_KEY_NAME =
            @"HKLM\SOFTWARE\Classes\Directory\shell\SynToolkitTerminals\shell\WindowsTerminal";

        private readonly MultiOptionConfigurationStore _contextMenuTerminalsConfigurationService;

        private static readonly string CONTEXT_MENU_REG_FILE_PATH = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Synergy", "ConfigurationServices", "ContextMenuTerminals", "ContextMenuTerminals_");

        private List<string> options = new List<string>()
        {
            "Add terminals",
            "Add terminals (no Windows Terminal)",
            "Remove terminals from the context menu",
        };

        public ContextMenuTerminalsConfigurationService(
            [FromKeyedServices("ContextMenuTerminals")] MultiOptionConfigurationStore contextMenuTerminalsConfigurationService)
        {
            _contextMenuTerminalsConfigurationService = contextMenuTerminalsConfigurationService;
            _contextMenuTerminalsConfigurationService.Options = options;
        }

        public void ChangeStatus(int status)
        {
            if (status < 0 || status >= options.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(status));
            }

            // The payload files use 0=remove, 1=add with Windows Terminal,
            // and 2=add without Windows Terminal. The UI is ordered by the
            // user-facing choices instead.
            int[] payloadByOption = { 1, 2, 0 };
            string payloadPath = CONTEXT_MENU_REG_FILE_PATH + payloadByOption[status].ToString() + ".reg";
            if (!File.Exists(payloadPath))
            {
                throw new FileNotFoundException("The terminal context-menu payload is missing.", payloadPath);
            }

            RegistryHelper.MergeRegFile(payloadPath);

            string detectedStatus = Status();
            _contextMenuTerminalsConfigurationService.CurrentSetting = detectedStatus;

            if (!string.Equals(detectedStatus, options[status], StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Windows did not report the requested terminal context-menu state.");
            }
        }

        public string Status()
        {
            if (!RegistryHelper.IsMatch(TERMINALS_MENU_KEY_NAME, "MUIVerb", "Terminals"))
            {
                return options[2];
            }

            return RegistryHelper.KeyExists(WINDOWS_TERMINAL_ITEM_KEY_NAME)
                ? options[0]
                : options[1];
        }
    }
}
