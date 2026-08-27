using System;
using System.Threading.Tasks;
using SynToolkit.Utils;

namespace SynToolkit.Commands.ConfigurationButtonsCommand;

internal sealed class RegisterDnsCommand : AsyncCommandBase
{
    private const int TimeoutMilliseconds = 30_000;

    protected override async Task ExecuteAsync(object parameter)
    {
        CommandResult result = await Task.Run(() =>
            CommandPromptHelper.RunProcessResult("ipconfig.exe", ["/registerdns"], TimeoutMilliseconds));

        EnsureSucceeded(result, "DNS registration");
        App.ReportConfigurationActionSuccess(App.GetValueFromItemList("RegisterDnsSuccess"));
    }

    private static void EnsureSucceeded(CommandResult result, string operation)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"{operation} failed: {result.CombinedOutput}");
        }
    }
}
