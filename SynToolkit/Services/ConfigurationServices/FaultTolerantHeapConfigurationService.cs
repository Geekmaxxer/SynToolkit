using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace SynToolkit.Services.ConfigurationServices
{
    public class FaultTolerantHeapConfigurationService : IConfigurationService
    {
        private const string SYNTOOLKIT_STORE_KEY_NAME = @"HKLM\SOFTWARE\SynToolkit\Services\FaultTolerantHeap";
        private const string STATE_VALUE_NAME = "state";

        private const string FTH_KEY_NAME = @"HKLM\SOFTWARE\Microsoft\FTH";


        private readonly ConfigurationStore _faultTolerantHeapConfigurationService;

        public FaultTolerantHeapConfigurationService(
            [FromKeyedServices("FaultTolerantHeap")] ConfigurationStore faultTolerantHeapConfigurationService)
        {
            _faultTolerantHeapConfigurationService = faultTolerantHeapConfigurationService;
        }

        public void Disable()
        {
            RegistryHelper.SetValue(FTH_KEY_NAME, "Enabled", 0, Microsoft.Win32.RegistryValueKind.DWord);
            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 0);

            _faultTolerantHeapConfigurationService.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            // Removing the override restores Windows' supported self-managed default.
            RegistryHelper.DeleteValue(FTH_KEY_NAME, "Enabled");
            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 1);

            _faultTolerantHeapConfigurationService.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            try
            {
                object value = RegistryHelper.GetValue(FTH_KEY_NAME, "Enabled");
                // Microsoft documents DWORD 0 as the system-wide FTH disable.
                // Not configured retains Windows' normal self-managed behavior.
                return value is null || value is int enabled && enabled != 0;
            }
            catch (Exception exception)
            {
                App.logger.Warn($"Unable to detect Fault Tolerant Heap state: {exception.Message}");
                return false;
            }
        }
    }
}
