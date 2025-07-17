using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stardrop.Utilities
{
    internal class NexusConnectionResult
    {
        public string? Error { get; set; }
        public string? Message { get; set; }

        public string? ApiKey { get; set; }
    }
}
