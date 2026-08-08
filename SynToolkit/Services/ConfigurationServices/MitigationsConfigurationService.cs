using System.Collections.Generic;
using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using System.Linq;

namespace SynToolkit.Services.ConfigurationServices
{
    public class MitigationsConfigurationService : IMultiOptionConfigurationServices
    {
        private readonly MultiOptionConfigurationStore _mitigationsConfigurationService;

        private static readonly string CONTEXT_MENU_REG_FILE_PATH = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Synergy", "ConfigurationServices", "Mitigations", "Mitigations_");

        private List<string> options = new List<string>()
        {
            "Disable mitigations",
            "Force-enable core mitigations",
            "Default Windows mitigations",
            "Custom mitigation state (detected)",
        };

        public MitigationsConfigurationService(
            [FromKeyedServices("Mitigations")] MultiOptionConfigurationStore mitigationsConfigurationService)
        {
            _mitigationsConfigurationService = mitigationsConfigurationService;
            _mitigationsConfigurationService.Options = options;
        }

        public void ChangeStatus(int status)
        {
            if (status < 0 || status > 2)
            {
                // The detected custom state is informational and must never
                // apply a different mitigation profile by itself.
                _mitigationsConfigurationService.CurrentSetting = Status();
                return;
            }

            string payloadPath = CONTEXT_MENU_REG_FILE_PATH + status.ToString() + ".cmd";
            if (!File.Exists(payloadPath))
            {
                throw new FileNotFoundException("The mitigation payload is missing.", payloadPath);
            }

            CommandResult result = CommandPromptHelper.RunBatchFileResult(
                payloadPath,
                ["/silent"],
                timeoutMilliseconds: 120_000);

            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"The mitigation payload failed: {result.CombinedOutput}");
            }

            string detectedStatus = Status();
            _mitigationsConfigurationService.CurrentSetting = detectedStatus;

            if (!string.Equals(detectedStatus, options[status], StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Windows did not report the requested mitigation profile.");
            }
        }

        public string Status()
        {
            IReadOnlyDictionary<string, string> processMitigations = GetCoreProcessMitigations();

            if (IsProcessMitigationState(processMitigations, "NOTSET"))
            {
                return options[2];
            }

            if (IsProcessMitigationState(processMitigations, "OFF"))
            {
                return options[0];
            }

            return IsProcessMitigationState(processMitigations, "ON") ? options[1] : options[3];
        }

        private static IReadOnlyDictionary<string, string> GetCoreProcessMitigations()
        {
            CommandResult result = CommandPromptHelper.RunProcessResult(
                "powershell.exe",
                [
                    "-NoLogo",
                    "-NoProfile",
                    "-NonInteractive",
                    "-Command",
                    "$m=Get-ProcessMitigation -System; " +
                    "Write-Output ('DEP=' + $m.Dep.Enable.ToString()); " +
                    "Write-Output ('CFG=' + $m.Cfg.Enable.ToString()); " +
                    "Write-Output ('SEHOP=' + $m.Sehop.Enable.ToString())"
                ],
                timeoutMilliseconds: 15_000);

            if (!result.Succeeded)
            {
                App.logger.Warn($"Unable to inspect Windows process mitigations: {result.CombinedOutput}");
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            Dictionary<string, string> states = new(StringComparer.OrdinalIgnoreCase);
            foreach (string line in result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
                if (parts.Length == 2)
                {
                    states[parts[0]] = parts[1];
                }
            }

            return states;
        }

        private static bool IsProcessMitigationState(
            IReadOnlyDictionary<string, string> states,
            string expectedState)
        {
            return states.Count == 3
                && states.Values.All(value => value.Equals(expectedState, StringComparison.OrdinalIgnoreCase));
        }
    }
}
