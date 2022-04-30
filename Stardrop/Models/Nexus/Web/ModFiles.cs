using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Stardrop.Models.Nexus.Web
{
    public class ModFiles
    {
        [JsonPropertyName("files")]
        public List<ModFile> Files { get; set; }
    }
}
