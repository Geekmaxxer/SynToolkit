using Microsoft.Extensions.DependencyInjection;
using SynToolkit.Stores;
using SynToolkit.Utils;
using System;

namespace SynToolkit.Services.ConfigurationServices
{
    internal class CpuIdleConfigurationService : IConfigurationService
    {
        private static readonly Guid ProcessorSubgroup = new("54533251-82be-4824-96c1-47b60b740d00");
        private static readonly Guid ProcessorIdleDisable = new("5d76a2ca-e8c0-402f-a133-2158492d58ad");

        private readonly ConfigurationStore _cpuIdleConfigurationStore;

        public CpuIdleConfigurationService(
            [FromKeyedServices("CpuIdle")] ConfigurationStore cpuIdleConfigurationStore)
        {
            _cpuIdleConfigurationStore = cpuIdleConfigurationStore;
        }

        public void Disable()
        {
            PowerSettingsHelper.WriteCurrentValues(ProcessorSubgroup, ProcessorIdleDisable, 1, 1);
            UpdateDetectedState(expectedState: false);
        }

        public void Enable()
        {
            PowerSettingsHelper.WriteCurrentValues(ProcessorSubgroup, ProcessorIdleDisable, 0, 0);
            UpdateDetectedState(expectedState: true);
        }

        public bool IsEnabled()
        {
            (uint acValue, uint dcValue) = PowerSettingsHelper.ReadCurrentValues(
                ProcessorSubgroup,
                ProcessorIdleDisable);

            return acValue == 0 && dcValue == 0;
        }

        private void UpdateDetectedState(bool expectedState)
        {
            bool detectedState = IsEnabled();
            _cpuIdleConfigurationStore.CurrentSetting = detectedState;

            if (detectedState != expectedState)
            {
                throw new InvalidOperationException("Windows did not accept the requested CPU idle state.");
            }
        }
    }
}
