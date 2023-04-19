namespace Stardrop.Models.Nexus
{
    public class NexusUser
    {
        public string Username { get; set; }
        public bool IsPremium { get; set; }

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
