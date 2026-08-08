#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace SynToolkit.Utils
{
    public sealed record CommandResult(
        int ExitCode,
        string StandardOutput,
        string StandardError,
        bool TimedOut)
    {
        public bool Succeeded => !TimedOut && ExitCode == 0;

        public string CombinedOutput => StandardOutput +
            (string.IsNullOrWhiteSpace(StandardError) ? string.Empty : $"\n[Error]\n{StandardError}");
    }

    public class CommandPromptHelper
    {
        private const int DEFAULT_TIMEOUT_MILLISECONDS = 30_000;

        /// <summary>
        /// Runs a command through cmd.exe. Prefer RunProcess for commands whose
        /// executable and arguments are known separately.
        /// </summary>
        public static string RunCommand(string command, bool noWindow = true, bool waitForExit = true)
        {
            if (!waitForExit)
            {
                ProcessStartInfo detachedStartInfo = CreateStartInfo("cmd.exe", noWindow);
                detachedStartInfo.ArgumentList.Add("/d");
                detachedStartInfo.ArgumentList.Add("/s");
                detachedStartInfo.ArgumentList.Add("/c");
                detachedStartInfo.ArgumentList.Add(command);

                using Process detachedProcess = Process.Start(detachedStartInfo)
                    ?? throw new InvalidOperationException("Unable to start cmd.exe.");
                return string.Empty;
            }

            CommandResult result = RunCommandResult(command, DEFAULT_TIMEOUT_MILLISECONDS, noWindow);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Command failed with exit code {result.ExitCode} (timed out: {result.TimedOut}): {result.CombinedOutput}");
            }

            return result.CombinedOutput;
        }

        public static CommandResult RunCommandResult(
            string command,
            int timeoutMilliseconds = DEFAULT_TIMEOUT_MILLISECONDS,
            bool noWindow = true)
        {
            return RunProcessResult(
                "cmd.exe",
                ["/d", "/s", "/c", command],
                timeoutMilliseconds,
                noWindow);
        }

        /// <summary>
        /// Runs a trusted batch file without embedding a quoted path in the command string.
        /// ProcessStartInfo.ArgumentList escapes embedded quotes for normal executables, but
        /// cmd.exe interprets that escaping literally when the /c command itself starts with a
        /// quoted path. Passing CALL, the path, and each argument separately avoids that mismatch.
        /// </summary>
        public static CommandResult RunBatchFileResult(
            string batchFilePath,
            IEnumerable<string> arguments,
            int timeoutMilliseconds = DEFAULT_TIMEOUT_MILLISECONDS,
            bool noWindow = true)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(batchFilePath);
            ArgumentNullException.ThrowIfNull(arguments);

            List<string> commandArguments = ["/d", "/s", "/c", "call", batchFilePath];
            commandArguments.AddRange(arguments);
            return RunProcessResult("cmd.exe", commandArguments, timeoutMilliseconds, noWindow);
        }

        public static CommandResult RunProcessResult(
            string fileName,
            IEnumerable<string> arguments,
            int timeoutMilliseconds = DEFAULT_TIMEOUT_MILLISECONDS,
            bool noWindow = true)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

            if (timeoutMilliseconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
            }

            ProcessStartInfo startInfo = CreateStartInfo(fileName, noWindow);
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;

            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            StringBuilder standardOutput = new();
            StringBuilder standardError = new();

            using Process process = new() { StartInfo = startInfo };
            process.OutputDataReceived += (_, eventArgs) => AppendLine(standardOutput, eventArgs.Data);
            process.ErrorDataReceived += (_, eventArgs) => AppendLine(standardError, eventArgs.Data);

            if (!process.Start())
            {
                throw new InvalidOperationException($"Unable to start {fileName}.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            bool exited = process.WaitForExit(timeoutMilliseconds);
            if (!exited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5_000);
                }
                catch (Exception exception)
                {
                    App.logger.Warn(exception, $"Unable to terminate timed-out process {fileName}.");
                }
            }
            else
            {
                // Flush asynchronous output events after the process handle is signaled.
                process.WaitForExit();
            }

            CommandResult result = new(
                exited ? process.ExitCode : -1,
                standardOutput.ToString().TrimEnd(),
                standardError.ToString().TrimEnd(),
                !exited);

            App.logger.Info(
                $"[PROCESS] {fileName}: exit={result.ExitCode}, timeout={result.TimedOut}\n\t{result.CombinedOutput}");

            return result;
        }

        public static void RunCommandToUpdate(string command, bool exitEnv = true)
        {
            ProcessStartInfo startInfo = CreateStartInfo("cmd.exe", noWindow: true);
            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/s");
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(command);

            using (Process.Start(startInfo))
            { }

            if (exitEnv)
            {
                Environment.Exit(0);
            }
        }

        public static void RestartExplorer()
        {
            CommandResult stopResult = RunProcessResult(
                "taskkill.exe",
                ["/f", "/im", "explorer.exe"],
                15_000);

            if (!stopResult.Succeeded && stopResult.ExitCode != 128)
            {
                throw new InvalidOperationException($"Unable to stop Explorer: {stopResult.CombinedOutput}");
            }

            ProcessStartInfo startInfo = CreateStartInfo("explorer.exe", noWindow: true);
            using Process explorer = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Unable to restart Explorer.");
        }

        public static string ReturnRunCommand(string command)
        {
            CommandResult result = RunCommandResult(command);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Command failed with exit code {result.ExitCode} (timed out: {result.TimedOut}): {result.CombinedOutput}");
            }

            return result.StandardOutput;
        }

        private static ProcessStartInfo CreateStartInfo(string fileName, bool noWindow)
        {
            return new ProcessStartInfo
            {
                FileName = fileName,
                CreateNoWindow = noWindow,
                UseShellExecute = false,
                WindowStyle = noWindow ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal
            };
        }

        private static void AppendLine(StringBuilder builder, string? value)
        {
            if (value is null)
            {
                return;
            }

            lock (builder)
            {
                builder.AppendLine(value);
            }
        }
    }
}
