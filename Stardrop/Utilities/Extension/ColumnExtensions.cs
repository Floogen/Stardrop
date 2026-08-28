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

        /// <summary>Marks a column as only being meaningful while Nexus Mods is connected, which gates its visibility</summary>
        public static readonly AttachedProperty<bool> RequiresNexusProperty = AvaloniaProperty.RegisterAttached<DataGridColumn, bool>("RequiresNexus", typeof(ColumnExtensions));

        public static void SetRequiresNexus(DataGridColumn column, bool value) => column.SetValue(RequiresNexusProperty, value);

        public static bool GetRequiresNexus(DataGridColumn column) => column.GetValue(RequiresNexusProperty);
    }
}
