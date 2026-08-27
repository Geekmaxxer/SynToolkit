#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using SynToolkit.Models;
using SynToolkit.Utils;
using SynToolkit.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;

namespace SynToolkit.ViewModels
{
    public partial class SettingsPageViewModel : INotifyPropertyChanged
    {
        private Language? _currentLanguage;
        private bool _isInitializing = true;

        public Language? CurrentLanguage 
        {
            get => _currentLanguage;
            set
            {
                // WinUI can briefly push a null SelectedItem while a ComboBox is
                // being realized. Treat that as binding setup, not as a language
                // change. Comparing keys also ignores a re-created Language model
                // that represents the same selection.
                if (value == null)
                {
                    return;
                }

                if (string.Equals(
                    _currentLanguage?.Key,
                    value.Key,
                    StringComparison.OrdinalIgnoreCase))
                {
                    _currentLanguage = value;
                    return;
                }

                _currentLanguage = value;
                OnPropertyChanged();
                
                if (!_isInitializing)
                {
                    if (SaveLanguageSelection(value.Key))
                    {
                        App.ContentDialogCaller("restartApp");
                    }
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool SaveLanguageSelection(string langKey)
        {
            try
            {
                RegistryHelper.SetValue(
                    @"HKLM\SOFTWARE\SynToolkit",
                    "lang",
                    langKey.Trim().ToLowerInvariant());

                // Keep the current visual tree and its view models on one
                // language. App.LoadLangString runs during the requested restart;
                // swapping the shared dictionary here could leave Home partially
                // translated if the restart dialog is dismissed.
                return true;
            }
            catch (Exception ex)
            {
                App.logger.Error($"Failed to save language selection: {ex.Message}");
                return false;
            }
        }

        public ObservableCollection<Language> Languages { get; set; }

        public SettingsPageViewModel()
        {
            Languages = new ObservableCollection<Language>();
            
            try
            {
                string langFilePath = Path.Combine(AppContext.BaseDirectory, "lang", "index.json");
                if (File.Exists(langFilePath))
                {
                    string jsonContent = File.ReadAllText(langFilePath);
                    Dictionary<string, string>? langs = JsonConvert.DeserializeObject<Dictionary<string, string>>(jsonContent);
                    
                    if (langs != null)
                    {
                        foreach (KeyValuePair<string, string> language in langs)
                        {
                            Languages.Add(new Language(language.Value, language.Key));
                        }
                    }
                }
                else
                {
                    App.logger.Warn($"Language index file not found at: {langFilePath}");
                }
            }
            catch (Exception ex)
            {
                App.logger.Error($"Failed to load languages: {ex.Message}");
            }

            try
            {
                object? langValue = RegistryHelper.GetValue(@"HKLM\SOFTWARE\SynToolkit", "lang");
                string? lang = (langValue as string)?.Trim();
                
                if (!string.IsNullOrEmpty(lang))
                {
                    _currentLanguage = Languages.FirstOrDefault(
                        item => string.Equals(item.Key, lang, StringComparison.OrdinalIgnoreCase));
                }
                
                if (_currentLanguage == null && Languages.Count > 0)
                {
                    _currentLanguage = Languages.FirstOrDefault(
                        item => string.Equals(item.Key, App.DefaultLanguageKey, StringComparison.OrdinalIgnoreCase))
                        ?? Languages.First();
                }
            }
            catch (Exception ex)
            {
                App.logger.Error($"Failed to load current language from registry: {ex.Message}");
                if (Languages.Count > 0)
                {
                    _currentLanguage = Languages.FirstOrDefault(
                        item => string.Equals(item.Key, App.DefaultLanguageKey, StringComparison.OrdinalIgnoreCase))
                        ?? Languages.First();
                }
            }

            _isInitializing = false;
            
            // Notify UI of initial language selection
            OnPropertyChanged(nameof(CurrentLanguage));
        }

    }
}
