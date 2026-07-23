using System.Diagnostics;
using System.Threading.Tasks;

namespace SynToolkit.Commands.ConfigurationButtonsCommand
{
    public class RestartToBiosCommand : AsyncCommandBase
    {
        protected override async Task ExecuteAsync(object parameter)
        {
            await Task.Run(() =>
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "shutdown.exe",
                    Arguments = "-r -fw -t 0",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(startInfo);
            });
        }
    }
}
