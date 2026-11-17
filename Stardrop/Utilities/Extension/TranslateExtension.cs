using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using System;


namespace Stardrop.Utilities.Extension
{
    public class TranslateExtension(string key) : MarkupExtension
    {

        public string Key { get; set; } = key;

        public string? Context { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            var keyToUse = Key;
            if (!string.IsNullOrWhiteSpace(Context))
            {
                keyToUse = $"{Context}/{Key}";
            }

            var binding = new ReflectionBindingExtension($"[{keyToUse}]")
            {
                Mode = BindingMode.OneWay,
                Source = Program.translation,
            };

            return binding.ProvideValue(serviceProvider);
        }
    }
}
