using System;
using System.IO;
using System.Threading.Tasks;
using SynToolkit.Utils;

namespace SynToolkit.Commands.ConfigurationButtonsCommand
{
    public sealed class LaunchPortableToolCommand : AsyncCommandBase
    {
        private readonly string _fileName;

        public LaunchPortableToolCommand(string fileName)
        {
            _fileName = fileName;
        }

        protected override Task ExecuteAsync(object parameter)
        {
            string toolPath = Path.Combine(
                AppContext.BaseDirectory,
                "assets",
                "Tools",
                _fileName);

            if (!File.Exists(toolPath))
            {
                App.logger.Warn($"Portable tool was not found: {toolPath}");
                return Task.CompletedTask;
            }

            ProcessHelper.StartShellExecute(toolPath);
            return Task.CompletedTask;
        }
    }
}
