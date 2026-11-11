using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Stardrop.Converters
{
    public class EnumConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is null)
            {
                return null;
            }
            return Enum.GetName(value.GetType(), value);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
