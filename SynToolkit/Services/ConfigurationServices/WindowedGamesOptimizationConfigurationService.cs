using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// Enables optimizations for windowed games using flip presentation model.
    /// Reduces latency and enables advanced features in compatible games.
    /// </summary>
    public class WindowedGamesOptimizationConfigurationService : IConfigurationService
    {
        private const string GAME_CONFIG_STORE_KEY = @"HKCU\System\GameConfigStore";
        private const string FSE_BEHAVIOR_VALUE = "GameDVR_FSEBehavior";
        private const string DXGI_HONOR_VALUE = "GameDVR_DXGIHonorFSEWindowsCompatible";
        private const string HONOR_USER_MODE_VALUE = "GameDVR_HonorUserFSEBehaviorMode";

        private readonly ConfigurationStore _windowedGamesOptimizationStore;

        public WindowedGamesOptimizationConfigurationService(
            [FromKeyedServices("WindowedGamesOptimization")] ConfigurationStore windowedGamesOptimizationStore)
        {
            _windowedGamesOptimizationStore = windowedGamesOptimizationStore;
        }

        public void Disable()
        {
            RegistryHelper.SetValue(GAME_CONFIG_STORE_KEY, FSE_BEHAVIOR_VALUE, 0);
            RegistryHelper.SetValue(GAME_CONFIG_STORE_KEY, DXGI_HONOR_VALUE, 0);
            RegistryHelper.SetValue(GAME_CONFIG_STORE_KEY, HONOR_USER_MODE_VALUE, 0);

            _windowedGamesOptimizationStore.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            RegistryHelper.SetValue(GAME_CONFIG_STORE_KEY, FSE_BEHAVIOR_VALUE, 2);
            RegistryHelper.SetValue(GAME_CONFIG_STORE_KEY, DXGI_HONOR_VALUE, 1);
            RegistryHelper.SetValue(GAME_CONFIG_STORE_KEY, HONOR_USER_MODE_VALUE, 1);

            _windowedGamesOptimizationStore.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            return RegistryHelper.IsMatch(GAME_CONFIG_STORE_KEY, DXGI_HONOR_VALUE, 1) ||
                   RegistryHelper.IsMatch(GAME_CONFIG_STORE_KEY, FSE_BEHAVIOR_VALUE, 2);
        }
    }
}
