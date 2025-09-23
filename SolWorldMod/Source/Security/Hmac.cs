// SolWorldMod/Source/Security/Hmac.cs
using System;
using System.Security.Cryptography;
using System.Text;
using Verse;

namespace SolWorldMod.Security
{
    public class Hmac
    {
        public string Sign(object payload, string keyId)
        {
            try
            {
                // In development mode, just return a mock signature
                if (string.IsNullOrEmpty(keyId) || keyId == "default")
                {
                    Log.Message("SolWorld: Using mock HMAC signature for development");
                    return "dev_mode_signature_" + DateTime.Now.Ticks;
                }

                // For production, you'd implement proper HMAC-SHA256 here
                var canonical = CreateCanonicalString(payload);
                var key = GetHmacKey(keyId);
                
                if (string.IsNullOrEmpty(key))
                {
                    Log.Warning("SolWorld: HMAC key not found for keyId: " + keyId);
                    return "missing_key_signature";
                }

                using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key)))
                {
                    var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical));
                    return Convert.ToBase64String(hash);
                }
            }
            catch (Exception ex)
            {
                Log.Error("SolWorld: HMAC signing failed: " + ex.Message);
                return "error_signature";
            }
        }

        private string CreateCanonicalString(object payload)
        {
            // Create a canonical string representation for signing
            // This should match the backend's canonicalization
            return payload.ToString(); // Simplified - implement proper canonicalization
        }

        private string GetHmacKey(string keyId)
        {
            // In a real implementation, you'd securely store and retrieve HMAC keys
            // For development, use a hardcoded key that matches your backend
            if (keyId == "default")
            {
                return "supersecret"; // This should match your backend's HMAC_KEYS
            }
            
            return null;
        }
    }
}