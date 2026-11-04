using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Stardrop.Converters
{
    public class EnumEqualsConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value?.GetType()?.IsEnum is not true)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Value must be a non-null enum.");
            }
            if (parameter?.GetType()?.IsEnum is not true)
            {
                throw new ArgumentOutOfRangeException(nameof(parameter), "Parameter must be a non-null enum.");
            }

            return Equals(value, parameter);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
