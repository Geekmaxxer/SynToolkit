using System;

namespace SynToolkit.Utils
{
    public static class ComputerStateHelper
    {
        public static void LogOffComputer()
        {
            CommandResult result = CommandPromptHelper.RunProcessResult(
                "logoff.exe",
                [],
                timeoutMilliseconds: 15_000);

            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"Windows could not sign out: {result.CombinedOutput}");
            }
        }

        public static void RestartComputer()
        {
            CommandResult result = CommandPromptHelper.RunProcessResult(
                "shutdown.exe",
                ["/r", "/t", "0"],
                timeoutMilliseconds: 15_000);

            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"Windows could not schedule a restart: {result.CombinedOutput}");
            }
        }

        public static void RestartApp()
        {
            App.RestartApp();
        }
    }
}
