using System;
using System.Threading.Tasks;
using SynToolkit.Utils;

namespace SynToolkit.Commands.ConfigurationButtonsCommand;

internal sealed class RenewDhcpLeaseCommand : AsyncCommandBase
{
    private const int TimeoutMilliseconds = 60_000;

    protected override async Task ExecuteAsync(object parameter)
    {
        // ipconfig renews DHCP-enabled adapters only; statically configured adapters
        // are left unchanged by Windows.
        CommandResult result = await Task.Run(() =>
            CommandPromptHelper.RunProcessResult("ipconfig.exe", ["/renew"], TimeoutMilliseconds));

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"DHCP lease renewal failed: {result.CombinedOutput}");
        }

        App.ReportConfigurationActionSuccess(App.GetValueFromItemList("RenewDhcpLeaseSuccess"));
    }
}
