using Semver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
