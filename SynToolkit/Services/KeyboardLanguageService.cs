#nullable enable

using System;
using System.Text.RegularExpressions;
using SynToolkit.Utils;

namespace SynToolkit.Services
{
    /// <summary>
    /// Adds or removes a keyboard language via the WinUserLanguageList PowerShell cmdlets.
    /// These act on the caller's own per-user profile, so the command is run in the console
    /// user's session (InteractiveUserProcessHelper) rather than inline while SynToolkit is
    /// elevated, which would otherwise silently apply to the Administrator token's profile.
    /// </summary>
    public static class KeyboardLanguageService
    {
        private static readonly Regex InputTipPattern = new(@"^[A-Za-z0-9\-]+:[0-9A-Fa-f]{8}$", RegexOptions.Compiled);

        public static void AddKeyboardLanguage(string inputTip, bool setAsDefault)
        {
            ValidateInputTip(inputTip);
            string tip = EscapeForPowerShell(inputTip);

            string command = $"$List=Get-WinUserLanguageList;$List[0].InputMethodTips.Add('{tip}');Set-WinUserLanguageList $List -Force";
            if (setAsDefault)
            {
                command += $";Set-WinDefaultInputMethodOverride -InputTip '{tip}'";
            }

            RunPowerShell(command);
        }

        public static void RemoveKeyboardLanguage(string inputTip)
        {
            ValidateInputTip(inputTip);
            string tip = EscapeForPowerShell(inputTip);

            string command =
                $"$List=Get-WinUserLanguageList;" +
                $"$Match=$List | Where-Object {{$_.InputMethodTips -contains '{tip}'}};" +
                "if ($Match) { $List.Remove($Match) };" +
                "Set-WinUserLanguageList $List -Force";

            RunPowerShell(command);
        }

        private static void ValidateInputTip(string inputTip)
        {
            if (string.IsNullOrWhiteSpace(inputTip) || !InputTipPattern.IsMatch(inputTip))
            {
                throw new ArgumentException(
                    "Enter a language tag and keyboard identifier in the form \"en-US:00000409\".",
                    nameof(inputTip));
            }
        }

        private static string EscapeForPowerShell(string value) => value.Replace("'", "''");

        private static void RunPowerShell(string command)
        {
            int exitCode = InteractiveUserProcessHelper.RunAsInteractiveUser(
                Environment.ExpandEnvironmentVariables(@"%WINDIR%\System32\WindowsPowerShell\v1.0\powershell.exe"),
                $"-NoProfile -Command \"{command}\"",
                60_000);

            if (exitCode != 0)
            {
                throw new InvalidOperationException($"PowerShell exited with code {exitCode} while updating the keyboard language list.");
            }
        }
    }
}
