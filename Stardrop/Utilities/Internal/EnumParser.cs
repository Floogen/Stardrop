using System;
using System.ComponentModel;
using System.Reflection;

namespace Stardrop.Utilities.Internal
{
    internal static class EnumParser
    {
        // Shamelessly copied from: https://stackoverflow.com/questions/1415140/can-my-enums-have-friendly-names
        public static string? GetDescription(this Enum? value)
        {
            if (value is null)
            {
                return null;
            }

            Type type = value.GetType();
            string? name = Enum.GetName(type, value);
            if (name is not null)
            {
                FieldInfo? field = type.GetField(name);
                if (field is not null)
                {
                    DescriptionAttribute? attr = Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute)) as DescriptionAttribute;

                    if (attr is not null)
                    {
                        return attr.Description;
                    }
                }
            }

            return value.ToString();
        }
    }
}
