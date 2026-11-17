namespace Stardrop.Models.Data
{
    public class PairedKeys
    {
        public byte[] Lock { get; set; } = null!;
        public byte[] Vector { get; set; } = null!;
    }
}