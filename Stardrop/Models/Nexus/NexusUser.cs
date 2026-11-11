namespace Stardrop.Models.Nexus
{
    public class NexusUser
    {
        public string Username { get; set; } = null!;
        public bool IsPremium { get; set; }

        public byte[] Key { get; set; } = null!;

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
