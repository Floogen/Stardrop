using Avalonia.Styling;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stardrop.Models
{
    public class Theme
    {
        public required string Name { get; set; }
        public string? Author { get; set; }
        public bool IsEnabled { get; set; }

        public IStyle? Style { get; set; }
    }
}
