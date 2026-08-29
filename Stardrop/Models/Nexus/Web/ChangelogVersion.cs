using System.Collections.Generic;

namespace Stardrop.Models.Nexus.Web
{
    public record ChangelogVersion(string Version, List<string> Changes);
}
