using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Stardrop.Utilities
{
    internal class Translation : INotifyPropertyChanged
    {
        public enum Language
        {
            English,
            Chinese,
            French,
            German,
            Hungarian,
            Italian,
            Japanese,
            Korean,
            Portuguese,
            Russian,
            Spanish,
            Thai,
            Turkish,
            Ukrainian
        }

        public enum LanguageAbbreviation
        {
            @default,
            zh,
            fr,
            de,
            hu,
            it,
            ja,
            ko,
            pt,
            ru,
            es,
            th,
            tr,
            uk
        }
        public Dictionary<Language, LanguageAbbreviation> LanguageNameToAbbreviations = new();
        public Dictionary<LanguageAbbreviation, Language> AbbreviationsToLanguageName = new();

        private Language _selectedLanguage = Language.English;
        private Dictionary<LanguageAbbreviation, Dictionary<string, string>> _languageTranslations = new();
        private const string IndexerName = "Item";
        private const string IndexerArrayName = "Item[]";


        public Translation()
        {
            int index = 0;
            foreach (Language language in Enum.GetValues(typeof(Language)))
            {
                LanguageNameToAbbreviations[language] = (LanguageAbbreviation)index;
                AbbreviationsToLanguageName[(LanguageAbbreviation)index] = language;

                index++;
            }
        }

        public string GetLanguageFromAbbreviation(string abbreviation)
        {
            if (Enum.TryParse(typeof(LanguageAbbreviation), abbreviation, out var languageAbbreviation))
            {
                if (AbbreviationsToLanguageName.ContainsKey((LanguageAbbreviation)languageAbbreviation))
                {
                    return AbbreviationsToLanguageName[(LanguageAbbreviation)languageAbbreviation].ToString();
                }
            }

            return Language.English.ToString();
        }

        public Language GetLanguage(string language)
        {
            if (Enum.TryParse(typeof(Language), language, out var parsedLanguage))
            {
                return (Language)parsedLanguage;
            }

            return Language.English;
        }

        public void SetLanguage(string language)
        {
            if (Enum.TryParse(typeof(Language), language, out var parsedLanguage))
            {
                SetLanguage((Language)parsedLanguage);
            }
        }

        public void SetLanguage(Language language)
        {
            _selectedLanguage = language;

            Invalidate();
        }

        public void LoadTranslations()
        {
            // Load the languages
            foreach (string fileFullName in Directory.EnumerateFiles(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "i18n"), "*.json"))
            {
                try
                {
                    var fileName = Path.GetFileNameWithoutExtension(fileFullName);
                    if (Enum.TryParse(typeof(LanguageAbbreviation), fileName, out var language))
                    {
                        _languageTranslations[(LanguageAbbreviation)language] = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(fileFullName), new JsonSerializerOptions { AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true });
                        Program.helper.Log($"Loaded language {Path.GetFileNameWithoutExtension(fileFullName)}", Helper.Status.Debug);
                    }
                }
                catch (Exception ex)
                {
                    Program.helper.Log($"Unable to load translation at {Path.GetFileNameWithoutExtension(fileFullName)}: {ex}", Helper.Status.Warning);
                }
            }
        }

        public void LoadTranslations(Language language)
        {
            // Set the language
            SetLanguage(language);

            LoadTranslations();
        }

        public List<Language> GetAvailableTranslations()
        {
            List<Language> availableLanguages = new();
            foreach (var abbreviation in _languageTranslations.Keys.Where(l => AbbreviationsToLanguageName.ContainsKey(l)))
            {
                availableLanguages.Add(AbbreviationsToLanguageName[abbreviation]);
            }

            return availableLanguages;
        }

        public string Get(string key)
        {
            var languageAbbreviation = LanguageNameToAbbreviations[_selectedLanguage];
            if (_languageTranslations.ContainsKey(languageAbbreviation) && _languageTranslations[languageAbbreviation].ContainsKey(key))
            {
                return _languageTranslations[languageAbbreviation][key];
            }
            else if (_languageTranslations.ContainsKey(LanguageAbbreviation.@default) && _languageTranslations[LanguageAbbreviation.@default].ContainsKey(key))
            {
                return _languageTranslations[LanguageAbbreviation.@default][key];
            }

            return $"(No translation provided for key {key})";
        }

        public string this[string key]
        {
            get
            {
                return Get(key);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public void Invalidate()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(IndexerName));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(IndexerArrayName));
        }
    }
}
