using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SynToolkit.Utils;

namespace SynToolkit.Commands.ConfigurationButtonsCommand
{
    public sealed class LaunchPortableToolCommand : AsyncCommandBase
    {
        private readonly string _fileName;
        private readonly IReadOnlyList<string> _arguments;
        private readonly string _successMessage;

        public LaunchPortableToolCommand(
            string fileName,
            IReadOnlyList<string> arguments = null,
            string successMessage = null)
        {
            _fileName = fileName;
            _arguments = arguments ?? Array.Empty<string>();
            _successMessage = successMessage;
        }

        protected override async Task ExecuteAsync(object parameter)
        {
            string toolPath = Path.Combine(
                AppContext.BaseDirectory,
                "assets",
                "Tools",
                _fileName);

            if (!File.Exists(toolPath))
            {
                App.logger.Warn($"Portable tool was not found: {toolPath}");
                return;
            }

            if (_arguments.Count == 0)
            {
                ProcessHelper.StartShellExecute(toolPath);
            }
            else
            {
                CommandResult result = await Task.Run(() =>
                    CommandPromptHelper.RunProcessResult(toolPath, _arguments));

                if (!result.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Portable tool '{_fileName}' failed: {result.CombinedOutput}");
                }
            }

            App.ReportConfigurationActionSuccess(_successMessage);
        }
    }
}
