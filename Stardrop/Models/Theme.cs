using Avalonia.Styling;

namespace Stardrop.Models
{
    public class Theme
    {
        public required string Name { get; set; }
        public string? Author { get; set; }
        public bool IsEnabled { get; set; }
        public string FilePath { get; set; } = null!;
        public IStyle? Style { get; set; }
    }
}
