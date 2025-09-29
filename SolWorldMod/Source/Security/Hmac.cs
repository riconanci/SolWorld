using System;
using System.Security.Cryptography;
using System.Text;
using Verse;
using SolWorldMod.Net; // ADDED: To access SimpleJson

namespace SolWorldMod.Security
{
    public class Hmac
    {
        public string Sign(object payload, string keyId)
        {
            try
            {
                // In development mode, just return a mock signature
                if (string.IsNullOrEmpty(keyId))
                {
                    Log.Message("SolWorld: Using mock HMAC signature for development");
                    return "dev_mode_signature_" + DateTime.Now.Ticks;
                }

                // For production, implement proper HMAC-SHA256
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
                    // FIXED: Convert to hex string (lowercase) to match backend
                    return BitConverter.ToString(hash).Replace("-", "").ToLower();
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
            try
            {
                // Use SimpleJson which ALREADY sorts properties alphabetically
                var jsonString = SimpleJson.Serialize(payload);
                
                // CRITICAL: Verify the JSON is actually sorted
                // Log first 500 chars to compare with backend
                var preview = jsonString.Length > 500 ? jsonString.Substring(0, 500) : jsonString;
                Log.Message($"SolWorld: Canonical JSON for signing:\n{preview}");
                
                return jsonString;
            }
            catch (Exception ex)
            {
                Log.Error($"SolWorld: Failed to create canonical string: {ex.Message}");
                throw;
            }
        }

        private string GetHmacKey(string keyId)
        {
            // CRITICAL: This MUST match your backend's HMAC_KEYS configuration exactly
            if (keyId == "default")
            {
                // This MUST match your .env file's HMAC_KEYS["default"] value exactly
                return "a7f8d9e2b4c6a1f3e8d7b9c2a5f6e3d8b1c4a7f9e2d6b8c3a5f7e4d9b2c6a8f1e3d7";
            }
            
            return null;
        }
    }
}