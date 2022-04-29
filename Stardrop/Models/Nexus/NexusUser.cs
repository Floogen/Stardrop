using Semver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stardrop.Models.Nexus
{
    public class NexusUser
    {
        public string Username { get; set; }

        public byte[] Key { get; set; }

        public NexusUser()
        {

        }

        public NexusUser(string username, byte[] key)
        {
            Username = username;
            Key = key;
        }
    }
}
