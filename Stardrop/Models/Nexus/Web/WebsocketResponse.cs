using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stardrop.Models.Nexus.Web
{
    internal class WebsocketResponse
    {
        public bool success { get; set; }
        public WebsocketResponseData? data { get; set; }


    }
}
