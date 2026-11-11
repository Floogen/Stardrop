using System;

namespace Stardrop.Models
{
    public class Config
    {
        public string UniqueId { get; set; } = null!;
        public string FilePath { get; set; } = null!;
        public DateTime LastWriteTimeUtc { get; set; }
        public string Data { get; set; } = null!;
    }
}
