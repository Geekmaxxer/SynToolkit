using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynToolkit.Services.ConfigurationServices
{
    public class RunWithPriorityConfigurationService : IConfigurationService
    {
        private const string SYNTOOLKIT_STORE_KEY_NAME = @"HKLM\SOFTWARE\SynToolkit\Services\RunWithPriority";
        private const string STATE_VALUE_NAME = "state";

        private const string PRIORITY_KEY_NAME = @"HKCR\exefile\shell\Priority";
        private const string MUI_VERB_VALUE_NAME = "MUIVerb";
        private const string SUB_COMMANDS_VALUE_NAME = "SubCommands";

        private const string ONE_FLYOUT_KEY_NAME = @"HKCR\exefile\Shell\Priority\shell\001flyout";
        private const string ONE_COMMAND_KEY_NAME = @"HKCR\exefile\Shell\Priority\shell\001flyout\command";
        private const string TWO_FLYOUT_KEY_NAME = @"HKCR\exefile\Shell\Priority\shell\002flyout";
        private const string TWO_COMMAND_KEY_NAME = @"HKCR\exefile\Shell\Priority\shell\002flyout\command";
        private const string THREE_FLYOUT_KEY_NAME = @"HKCR\exefile\Shell\Priority\shell\003flyout";
        private const string THREE_COMMAND_KEY_NAME = @"HKCR\exefile\Shell\Priority\shell\003flyout\command";
        private const string FOUR_FLYOUT_KEY_NAME = @"HKCR\exefile\Shell\Priority\shell\004flyout";
        private const string FOUR_COMMAND_KEY_NAME = @"HKCR\exefile\Shell\Priority\shell\004flyout\command";
        private const string FIVE_FLYOUT_KEY_NAME = @"HKCR\exefile\Shell\Priority\shell\005flyout";
        private const string FIVE_COMMAND_KEY_NAME = @"HKCR\exefile\Shell\Priority\shell\005flyout\command";
        private const string SIX_FLYOUT_KEY_NAME = @"HKCR\exefile\Shell\Priority\shell\006flyout";
        private const string SIX_COMMAND_KEY_NAME = @"HKCR\exefile\Shell\Priority\shell\006flyout\command";

        private const string REALTIME_COMMAND = "powershell start -file 'cmd' -args '/c start \"\"\"Realtime App\"\"\" /Realtime \"\"\"%1\"\"\"' -verb runas";
        private const string HIGH_COMMAND = "cmd /c start \"\" /High \"%1\"";
        private const string ABOVE_NORMAL_COMMAND = "cmd /c start \"\" /AboveNormal \"%1\"";
        private const string NORMAL_COMMAND = "cmd /c start \"\" /Normal \"%1\"";
        private const string BELOW_NORMAL_COMMAND = "cmd /c start \"\" /BelowNormal \"%1\"";
        private const string LOW_COMMAND = "cmd /c start \"\" /Low \"%1\"";


        private readonly ConfigurationStore _runWithPriorityConfigurationService;

        public RunWithPriorityConfigurationService(
            [FromKeyedServices("RunWithPriority")] ConfigurationStore runWithPriorityConfigurationService)
        {
            _runWithPriorityConfigurationService = runWithPriorityConfigurationService;
        }

        public void Disable()
        {
            RegistryHelper.DeleteKey(PRIORITY_KEY_NAME);
            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 0);

        }

        public void Enable()
        {
            RegistryHelper.SetValue(PRIORITY_KEY_NAME, MUI_VERB_VALUE_NAME, "Run with priority");
            RegistryHelper.SetValue(PRIORITY_KEY_NAME, SUB_COMMANDS_VALUE_NAME, "");
            RegistryHelper.SetValue(ONE_FLYOUT_KEY_NAME, "", "Realtime");
            RegistryHelper.SetValue(ONE_COMMAND_KEY_NAME, "", REALTIME_COMMAND);
            RegistryHelper.SetValue(TWO_FLYOUT_KEY_NAME, "", "High");
            RegistryHelper.SetValue(TWO_COMMAND_KEY_NAME, "", HIGH_COMMAND);
            RegistryHelper.SetValue(THREE_FLYOUT_KEY_NAME, "", "Above normal");
            RegistryHelper.SetValue(THREE_COMMAND_KEY_NAME, "", ABOVE_NORMAL_COMMAND);
            RegistryHelper.SetValue(FOUR_FLYOUT_KEY_NAME, "", "Normal");
            RegistryHelper.SetValue(FOUR_COMMAND_KEY_NAME, "", NORMAL_COMMAND);
            RegistryHelper.SetValue(FIVE_FLYOUT_KEY_NAME, "", "Below normal");
            RegistryHelper.SetValue(FIVE_COMMAND_KEY_NAME, "", BELOW_NORMAL_COMMAND);
            RegistryHelper.SetValue(SIX_FLYOUT_KEY_NAME, "", "Low");
            RegistryHelper.SetValue(SIX_COMMAND_KEY_NAME, "", LOW_COMMAND);

            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 1);
        }

        public bool IsEnabled()
        {
            return RegistryHelper.IsMatch(PRIORITY_KEY_NAME, MUI_VERB_VALUE_NAME, "Run with priority")
                && RegistryHelper.IsMatch(PRIORITY_KEY_NAME, SUB_COMMANDS_VALUE_NAME, string.Empty)
                && RegistryHelper.IsMatch(ONE_FLYOUT_KEY_NAME, string.Empty, "Realtime")
                && RegistryHelper.IsMatch(ONE_COMMAND_KEY_NAME, string.Empty, REALTIME_COMMAND)
                && RegistryHelper.IsMatch(TWO_FLYOUT_KEY_NAME, string.Empty, "High")
                && RegistryHelper.IsMatch(TWO_COMMAND_KEY_NAME, string.Empty, HIGH_COMMAND)
                && RegistryHelper.IsMatch(THREE_FLYOUT_KEY_NAME, string.Empty, "Above normal")
                && RegistryHelper.IsMatch(THREE_COMMAND_KEY_NAME, string.Empty, ABOVE_NORMAL_COMMAND)
                && RegistryHelper.IsMatch(FOUR_FLYOUT_KEY_NAME, string.Empty, "Normal")
                && RegistryHelper.IsMatch(FOUR_COMMAND_KEY_NAME, string.Empty, NORMAL_COMMAND)
                && RegistryHelper.IsMatch(FIVE_FLYOUT_KEY_NAME, string.Empty, "Below normal")
                && RegistryHelper.IsMatch(FIVE_COMMAND_KEY_NAME, string.Empty, BELOW_NORMAL_COMMAND)
                && RegistryHelper.IsMatch(SIX_FLYOUT_KEY_NAME, string.Empty, "Low")
                && RegistryHelper.IsMatch(SIX_COMMAND_KEY_NAME, string.Empty, LOW_COMMAND);
        }
    }
}
