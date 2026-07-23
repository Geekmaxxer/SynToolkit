using SynToolkit.Services.ConfigurationServices;
using SynToolkit.Models;
using SynToolkit.Stores;
using System.Windows.Input;
using SynToolkit.Commands;
using SynToolkit.Enums;
using Windows.UI;
using System.Collections.Generic;
using Microsoft.UI.Xaml.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace SynToolkit.ViewModels
{
    public class MultiOptionConfigurationItemViewModel : ObservableObject, IConfigurationItem
    {
        private readonly MultiOptionConfigurationStore _configurationStore;
        private readonly IMultiOptionConfigurationServices _configurationService;

        public MultiOptionConfiguration Configuration { get; set; }
        public string Name => Configuration.Name;
        public ConfigurationType Type => Configuration.Type;
        public string Icon => Configuration.Icon;

        public List<string> Options => _configurationStore.Options; 
        public string Key => Configuration.Key;

        public Color Color { get; set; }


        private string _currentSetting;

        public string CurrentSetting
        {
            get => _currentSetting;
            set
            {
                if (!SetProperty(ref _currentSetting, value))
                {
                    return;
                }

                _configurationStore.CurrentSetting = CurrentSetting;
                this.MultiOptionSaveConfigurationCommand.Execute(this);
            }
        }

        private string _errorMessage = string.Empty;

        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        private bool _isBusy;

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    OnPropertyChanged(nameof(CanInteract));
                }
            }
        }

        private bool _isStateAvailable = true;

        public bool IsStateAvailable
        {
            get => _isStateAvailable;
            private set
            {
                if (SetProperty(ref _isStateAvailable, value))
                {
                    OnPropertyChanged(nameof(CanInteract));
                }
            }
        }

        public bool CanInteract => IsStateAvailable && !IsBusy;

        public ICommand MultiOptionSaveConfigurationCommand { get; }

        public MultiOptionConfigurationItemViewModel(
            MultiOptionConfiguration configuration,
            MultiOptionConfigurationStore configurationStore,
            IMultiOptionConfigurationServices configurationService)
        {
            Configuration = configuration;

            _configurationStore = configurationStore;
            _configurationService = configurationService;

            _currentSetting = FetchCurrentSetting();

            MultiOptionSaveConfigurationCommand = new MultiOptionSaveConfigurationCommand(this, configurationStore, configurationService);
        }

        public string FetchCurrentSetting()
        {
            IsBusy = true;

            try
            {
                string currentSetting = _configurationService.Status();
                _configurationStore.CurrentSetting = currentSetting;
                IsStateAvailable = true;
                return currentSetting;
            }
            catch (Exception exception)
            {
                const string unavailableState = "State unavailable";
                if (!_configurationStore.Options.Contains(unavailableState))
                {
                    _configurationStore.Options.Add(unavailableState);
                }

                IsStateAvailable = false;
                App.logger.Warn(exception, $"Unable to detect current state for {Key}.");
                return unavailableState;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void RefreshCurrentSetting()
        {
            string detectedSetting = FetchCurrentSetting();
            SetProperty(ref _currentSetting, detectedSetting, nameof(CurrentSetting));
            _configurationStore.CurrentSetting = detectedSetting;
        }
    }
}
