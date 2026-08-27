using System;
using System.Threading.Tasks;
using SynToolkit.Utils;

namespace SynToolkit.Commands.ConfigurationButtonsCommand;

internal sealed class SyncWindowsClockCommand : AsyncCommandBase
{
    private const int TimeoutMilliseconds = 30_000;
    private const string WindowsTimeServiceName = "W32Time";

    protected override async Task ExecuteAsync(object parameter)
    {
        CommandResult result = await Task.Run(() =>
        {
            ServiceHelper.StartService(WindowsTimeServiceName, TimeSpan.FromSeconds(15));
            return CommandPromptHelper.RunProcessResult("w32tm.exe", ["/resync"], TimeoutMilliseconds);
        });

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Windows clock synchronization failed: {result.CombinedOutput}");
        }

        App.ReportConfigurationActionSuccess(App.GetValueFromItemList("SyncWindowsClockSuccess"));
    }
}
