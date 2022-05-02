using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stardrop.Models.Data.Enums
{
    public enum EndorsementResponse
    {
        Unknown,
        IsOwnMod,
        TooSoonAfterDownload,
        NotDownloadedMod,
        Abstained,
        Endorsed
    }
}
