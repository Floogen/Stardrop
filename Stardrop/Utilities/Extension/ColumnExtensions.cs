using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using System;


namespace Stardrop.Utilities.Extension
{
    public class ColumnExtensions
    {
        public static readonly AttachedProperty<string?> KeyProperty = AvaloniaProperty.RegisterAttached<DataGridColumn, string?>("Key", typeof(ColumnExtensions));

        public static void SetKey(DataGridColumn column, string? value) => column.SetValue(KeyProperty, value);

        public static string? GetKey(DataGridColumn column) => column.GetValue(KeyProperty);
    }
}
