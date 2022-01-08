using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Stardrop.Utilities
{
    internal class Translation
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
            Turkish
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
            tr
        }
        public Dictionary<Language, LanguageAbbreviation> LanguageNameToAbbreviations = new();

        private Language _selectedLanguage = Language.English;
        private Dictionary<LanguageAbbreviation, Dictionary<string, string>> _languageTranslations = new();


        public Translation()
        {
            int index = 0;
            foreach (Language language in Enum.GetValues(typeof(Language)))
            {
                LanguageNameToAbbreviations[language] = (LanguageAbbreviation)index;
            }
        }

        public void SetLanguage(Language language)
        {
            _selectedLanguage = language;
        }

        public void LoadTranslations()
        {
            // Load the languages
            foreach (string fileFullName in Directory.EnumerateFiles("i18n", "*.json"))
            {
                try
                {
                    var fileName = Path.GetFileNameWithoutExtension(fileFullName);
                    if (Enum.TryParse(typeof(LanguageAbbreviation), fileName, out var language))
                    {
                        _languageTranslations[(LanguageAbbreviation)language] = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(fileFullName));
                        Program.helper.Log($"Loaded language {Path.GetFileNameWithoutExtension(fileFullName)}", Helper.Status.Debug);
                    }
                }
                catch (Exception ex)
                {
                    Program.helper.Log($"Unable to load theme on {Path.GetFileNameWithoutExtension(fileFullName)}: {ex}", Helper.Status.Warning);
                }
            }
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
    }
}
