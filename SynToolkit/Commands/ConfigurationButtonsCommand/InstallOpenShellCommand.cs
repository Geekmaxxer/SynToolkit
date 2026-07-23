using System;
using System.Threading.Tasks;
using SynToolkit.Utils;

namespace SynToolkit.Commands.ConfigurationButtonsCommand
{
    public class InstallOpenShellCommand : AsyncCommandBase
    {
        protected override async Task ExecuteAsync(object parameter)
        {
            CommandResult result = await Task.Run(() => CommandPromptHelper.RunProcessResult(
                "winget.exe",
                [
                    "install",
                    "--exact",
                    "--id",
                    "Open-Shell.Open-Shell-Menu",
                    "--silent",
                    "--accept-source-agreements",
                    "--accept-package-agreements",
                    "--disable-interactivity"
                ],
                timeoutMilliseconds: 180_000));

            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"Windows Package Manager could not install Open-Shell: {result.CombinedOutput}");
            }
        }
    }
}
