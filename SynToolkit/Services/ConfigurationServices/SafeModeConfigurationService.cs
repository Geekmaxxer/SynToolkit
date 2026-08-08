using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using SynToolkit.Services.Bcd;
using SynToolkit.Services;

namespace SynToolkit.Services.ConfigurationServices
{
    internal class SafeModeConfigurationService : IMultiOptionConfigurationServices
    {
        private readonly MultiOptionConfigurationStore _safeModeConfigurationService;

        private static readonly string CONTEXT_MENU_REG_FILE_PATH = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Synergy", "ConfigurationServices", "SafeMode", "SafeMode_");

        private List<string> options = new List<string>()
        {
            "Exit Safe Mode",
            "Safe Mode with Command Prompt",
            "Safe Mode with Networking",
            "Safe Mode",
            "Custom Safe Mode state (detected)",
        };

        private readonly IBcdService _bcdService;
        private int? _lastAppliedStatus;

        public SafeModeConfigurationService(
            [FromKeyedServices("SafeMode")] MultiOptionConfigurationStore safeModeConfigurationService,
            IBcdService bcdService)
        {
            _safeModeConfigurationService = safeModeConfigurationService;
            _safeModeConfigurationService.Options = options;
            _bcdService = bcdService;
        }

        public void ChangeStatus(int status)
        {
            if (status < 0 || status > 3)
            {
                _safeModeConfigurationService.CurrentSetting = Status();
                return;
            }

            string payloadPath = CONTEXT_MENU_REG_FILE_PATH + status.ToString() + ".cmd";
            if (!File.Exists(payloadPath))
            {
                throw new FileNotFoundException("The Safe Mode payload is missing.", payloadPath);
            }

            CommandResult result = CommandPromptHelper.RunBatchFileResult(
                payloadPath,
                ["/silent"],
                timeoutMilliseconds: 30_000);

            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"The Safe Mode payload failed: {result.CombinedOutput}");
            }

            // The BCD WMI provider is commonly unavailable while Windows is already running
            // in Safe Mode. The payload checks every BCDEdit exit code, so remember a successful
            // request and use it as the UI state if the immediate WMI read cannot run.
            _lastAppliedStatus = status;
            string detectedStatus = Status();
            _safeModeConfigurationService.CurrentSetting = detectedStatus;

            if (!string.Equals(detectedStatus, options[status], StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Windows did not report the requested Safe Mode state.");
            }
        }

        public string Status()
        {
            try
            {
                object safeBootValue = _bcdService.GetElementValue(WellKnownObjectIdentifiers.Current, WellKnownElementTypes.SafeBoot);
                if (safeBootValue is null)
                {
                    return options[0];
                }

                ulong safeBootMode = Convert.ToUInt64(safeBootValue);
                bool alternateShell = _bcdService.GetElementValue(
                    WellKnownObjectIdentifiers.Current,
                    WellKnownElementTypes.SafeBootAlternateShell) is true;

                return safeBootMode switch
                {
                    0 when alternateShell => options[1],
                    1 when !alternateShell => options[2],
                    0 when !alternateShell => options[3],
                    _ => options[4]
                };
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "Unable to read the active Safe Mode BCD state.");
                return _lastAppliedStatus is int lastAppliedStatus
                    ? options[lastAppliedStatus]
                    : options[4];
            }
        }
    }
}
