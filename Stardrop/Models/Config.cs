using System;

namespace Stardrop.Models
{
    public class Config
    {
        public string UniqueId { get; set; }
        public string FilePath { get; set; }
        public DateTime LastWriteTimeUtc { get; set; }
        public string Data { get; set; }
    }
}
