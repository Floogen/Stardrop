using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Stardrop.Utilities
{
    internal class Translation : INotifyPropertyChanged
    {
        public sealed class LanguageOption
        {
            public string Code { get; }
            public string DisplayName { get; }

            public LanguageOption(string code)
            {
                Code = code;
                DisplayName = GetDisplayName(code);
            }

            public override string ToString()
            {
                return DisplayName;
            }

            private static string GetDisplayName(string code)
            {
                if (String.Equals(code, "default", StringComparison.OrdinalIgnoreCase))
                {
                    return "English";
                }

                try
                {
                    return CultureInfo.GetCultureInfo(code).EnglishName;
                }
                catch (CultureNotFoundException)
                {
                    return code;
                }
            }
        }

        private static readonly Dictionary<string, string> LegacyLanguageNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["English"] = "default",
            ["Chinese"] = "zh",
            ["French"] = "fr",
            ["German"] = "de",
            ["Hungarian"] = "hu",
            ["Italian"] = "it",
            ["Japanese"] = "ja",
            ["Korean"] = "ko",
            ["Portuguese"] = "pt",
            ["Russian"] = "ru",
            ["Spanish"] = "es",
            ["Thai"] = "th",
            ["Turkish"] = "tr",
            ["Ukrainian"] = "uk",
            ["Dutch"] = "nl"
        };

        private string _selectedLanguage = "default";
        private readonly Dictionary<string, Dictionary<string, string>> _languageTranslations = new(StringComparer.OrdinalIgnoreCase);
        private const string IndexerName = "Item";
        private const string IndexerArrayName = "Item[]";

        public string GetLanguageFromAbbreviation(string abbreviation)
        {
            if (_languageTranslations.ContainsKey(abbreviation))
            {
                return GetCanonicalLanguageCode(abbreviation);
            }

            return "default";
        }

        public string NormalizeLanguage(string language)
        {
            if (String.IsNullOrWhiteSpace(language))
            {
                return "default";
            }

            if (LegacyLanguageNames.TryGetValue(language, out var legacyCode))
            {
                language = legacyCode;
            }

            return _languageTranslations.ContainsKey(language)
                ? GetCanonicalLanguageCode(language)
                : "default";
        }

        public void SetLanguage(string language)
        {
            _selectedLanguage = NormalizeLanguage(language);

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
                    _languageTranslations[fileName] = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(fileFullName), new JsonSerializerOptions { AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true });
                    Program.helper.Log($"Loaded language {fileName}", Helper.Status.Debug);
                }
                catch (Exception ex)
                {
                    Program.helper.Log($"Unable to load translation at {Path.GetFileNameWithoutExtension(fileFullName)}: {ex}", Helper.Status.Warning);
                }
            }
        }

        public List<LanguageOption> GetAvailableTranslations()
        {
            return _languageTranslations.Keys
                .OrderBy(code => String.Equals(code, "default", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(code => new LanguageOption(code).DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .Select(code => new LanguageOption(code))
                .ToList();
        }

        public string Get(string key)
        {
            if (_languageTranslations.ContainsKey(_selectedLanguage) && _languageTranslations[_selectedLanguage].ContainsKey(key))
            {
                return _languageTranslations[_selectedLanguage][key];
            }
            else if (_languageTranslations.ContainsKey("default") && _languageTranslations["default"].ContainsKey(key))
            {
                return _languageTranslations["default"][key];
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

        private string GetCanonicalLanguageCode(string code)
        {
            return _languageTranslations.Keys.First(key => String.Equals(key, code, StringComparison.OrdinalIgnoreCase));
        }
    }
}
