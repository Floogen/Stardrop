using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Stardrop;

public partial class BaseWindow : Window
{
    public BaseWindow()
    {
        AvaloniaXamlLoader.Load(this);

#if DEBUG
        this.AttachDevTools();
#endif
    }
}