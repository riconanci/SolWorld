// SolWorldMod/Source/Util/AddressUtil.cs
namespace SolWorldMod.Util
{
    public static class AddressUtil
    {
        public static string ShortenWalletAddress(string fullAddress)
        {
            if (string.IsNullOrEmpty(fullAddress) || fullAddress.Length < 10)
                return fullAddress;
                
            // Format: first 5 chars + "...." + last 5 chars
            // Example: "bVRCZ...yBAu"
            return fullAddress.Substring(0, 5) + "...." + fullAddress.Substring(fullAddress.Length - 5);
        }
    }
}