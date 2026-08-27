using System;
using System.Threading.Tasks;
using SynToolkit.Utils;

namespace SynToolkit.Commands.ConfigurationButtonsCommand;

internal sealed class RestartAudioServicesCommand : AsyncCommandBase
{
    private const string WindowsAudioServiceName = "Audiosrv";
    private const string AudioEndpointBuilderServiceName = "AudioEndpointBuilder";
    private static readonly TimeSpan ServiceTimeout = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(object parameter)
    {
        await Task.Run(RestartAudioServices);
        App.ReportConfigurationActionSuccess(App.GetValueFromItemList("RestartAudioServicesSuccess"));
    }

    private static void RestartAudioServices()
    {
        EnsureServiceExists(WindowsAudioServiceName);
        EnsureServiceExists(AudioEndpointBuilderServiceName);

        try
        {
            // Windows Audio depends on Audio Endpoint Builder, so stop and start
            // the services in dependency order.
            ServiceHelper.StopService(WindowsAudioServiceName, ServiceTimeout);
            ServiceHelper.StopService(AudioEndpointBuilderServiceName, ServiceTimeout);
            ServiceHelper.StartService(AudioEndpointBuilderServiceName, ServiceTimeout);
            ServiceHelper.StartService(WindowsAudioServiceName, ServiceTimeout);
        }
        catch
        {
            TryRestoreAudioServices();
            throw;
        }
    }

    private static void EnsureServiceExists(string serviceName)
    {
        if (!ServiceHelper.ServiceExists(serviceName))
        {
            throw new InvalidOperationException(
                $"The required audio service '{serviceName}' is not installed.");
        }
    }

    private static void TryRestoreAudioServices()
    {
        try
        {
            ServiceHelper.StartService(AudioEndpointBuilderServiceName, ServiceTimeout);
            ServiceHelper.StartService(WindowsAudioServiceName, ServiceTimeout);
        }
        catch (Exception exception)
        {
            App.logger.Error(exception, "Unable to recover the Windows audio services after a failed restart.");
        }
    }
}
