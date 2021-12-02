using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stardrop.Models
{
    public class Mod
    {
        public string UniqueId { get; set; }
        public string Name { get; set; }
        internal bool IsSelected { get; set; }

        public Mod(string uniqueId, string? name = null)
        {
            UniqueId = uniqueId;
            Name = String.IsNullOrEmpty(name) ? uniqueId : name;
        }
    }
}
