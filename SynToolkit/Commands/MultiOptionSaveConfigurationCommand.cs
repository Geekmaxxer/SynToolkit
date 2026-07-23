using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SynToolkit.Services.ConfigurationServices;
using SynToolkit.Stores;
using SynToolkit.Utils;
using SynToolkit.ViewModels;

namespace SynToolkit.Commands
{
    public class MultiOptionSaveConfigurationCommand : AsyncCommandBase
    {
        private readonly MultiOptionConfigurationItemViewModel _configurationItemViewModel;
        private readonly MultiOptionConfigurationStore _configurationStore;
        private readonly IMultiOptionConfigurationServices _configurationService;

        public MultiOptionSaveConfigurationCommand(
            MultiOptionConfigurationItemViewModel configurationItemViewModel,
            MultiOptionConfigurationStore configurationStore,
            IMultiOptionConfigurationServices configurationService)
        {
            _configurationItemViewModel = configurationItemViewModel;
            _configurationStore = configurationStore;
            _configurationService = configurationService;
        }
        /// <summary>
        /// Saves the state of a MultiOptionConfigurationService
        /// </summary>
        /// <param name="parameter"></param>
        /// <returns></returns>
        protected override async Task ExecuteAsync(object parameter)
        {
            int currentSetting = _configurationItemViewModel.Options.IndexOf(_configurationStore.CurrentSetting);

            App.logger.Info($"Changed {_configurationItemViewModel.Key} to option index {currentSetting}");
            _configurationItemViewModel.IsBusy = true;

            try
            {
                await Task.Run(() => _configurationService.ChangeStatus(currentSetting));

                _configurationItemViewModel.ErrorMessage = string.Empty;
                _configurationItemViewModel.RefreshCurrentSetting();
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, $"Unable to apply {_configurationItemViewModel.Key} option {currentSetting}.");
                _configurationItemViewModel.ErrorMessage = exception.Message;
                _configurationItemViewModel.RefreshCurrentSetting();
            }
            finally
            {
                _configurationItemViewModel.IsBusy = false;
            }
        }
    }
}
