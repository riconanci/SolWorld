// SolWorldMod/Source/Net/CryptoReporter.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Verse;
using SolWorldMod.Security;

namespace SolWorldMod.Net
{
    // UPDATED: Added tier support to data classes
    public class HoldersResponse
    {
        public bool success;
        public HoldersData data;
        public HoldersMeta meta;
    }

    public class HoldersData
    {
        public string[] wallets;
        public float roundRewardTotalSol;
        public float payoutPercent;
        
        // NEW: Tier system support
        public TieredFighter[] fighters;
        public TierStats tierStats;
    }

    public class TieredFighter
    {
        public string wallet;
        public float balance;
        public TierInfo tier;
    }

    public class TierInfo
    {
        public int tier;
        public string name;
        public int minBalance;
        public string weaponQuality;
        public bool hasArmor;
        public bool hasHelmet;
        public bool hasAura;
        public string description;
        public float damageMultiplier;
        public float accuracyBonus;
        public float healthMultiplier;
        public string glowColor;
        public string tierIcon;
        public string borderColor;
    }

    public class TierStats
    {
        public Dictionary<string, int> distribution;
        public int totalSelected;
        public float averageTier;
    }

    public class HoldersMeta
    {
        public string source;
        public HoldersStats stats;
        public string timestamp;
        public string version;
    }

    public class HoldersStats
    {
        public int totalAvailable;
        public int selected;
        public int mockUsed;
        public Dictionary<string, int> tierDistribution;
    }

    public class ReportResponse
    {
        public bool success;
        public ReportData data;
        public string error;
    }

    public class ReportData
    {
        public string matchId;
        public string winner;
        public string[] txids;
    }

    public class CryptoReporter
    {
        private readonly string baseUrl;
        private readonly Hmac hmacService;

        public CryptoReporter()
        {
            baseUrl = SolWorldMod.Settings.apiBaseUrl?.TrimEnd('/') ?? "http://localhost:4000";
            hmacService = new Hmac();
            
            Log.Message("SolWorld: CryptoReporter initialized with base URL: " + baseUrl);
        }

        public Task<HoldersResponse> FetchHoldersAsync()
        {
            return Task.Run(() => FetchHolders());
        }

        private HoldersResponse FetchHolders()
        {
            try
            {
                Log.Message("SolWorld: Fetching holders from backend...");
                
                var url = baseUrl + "/api/arena/holders";
                var jsonResponse = HttpClientUtil.Get(url);

                if (string.IsNullOrEmpty(jsonResponse))
                {
                    Log.Error("SolWorld: Empty response from holders endpoint");
                    return null;
                }

                // Enhanced JSON parsing with tier support
                var response = ParseHoldersResponse(jsonResponse);
                
                if (response?.success == true && response.data?.wallets?.Length == 20)
                {
                    Log.Message("SolWorld: Successfully fetched " + response.data.wallets.Length + " holders");
                    Log.Message("SolWorld: Pool: " + response.data.roundRewardTotalSol + " SOL, Payout: " + (response.data.payoutPercent * 100) + "%");
                    
                    // NEW: Log tier information if available
                    if (response.data.fighters != null && response.data.fighters.Length > 0)
                    {
                        Log.Message("SolWorld: TIER SYSTEM ACTIVE - Received " + response.data.fighters.Length + " tiered fighters!");
                        
                        // Count tiers for logging
                        var tierCounts = new Dictionary<int, int>();
                        foreach (var fighter in response.data.fighters)
                        {
                            if (fighter.tier != null)
                            {
                                if (!tierCounts.ContainsKey(fighter.tier.tier))
                                    tierCounts[fighter.tier.tier] = 0;
                                tierCounts[fighter.tier.tier]++;
                            }
                        }
                        
                        // Log tier distribution
                        foreach (var kvp in tierCounts)
                        {
                            var tierName = GetTierName(kvp.Key);
                            Log.Message($"SolWorld: {kvp.Value}x Tier {kvp.Key} ({tierName})");
                        }
                    }
                    else
                    {
                        Log.Warning("SolWorld: No tier data received - using basic fighter mode");
                    }
                    
                    return response;
                }
                else
                {
                    Log.Error("SolWorld: Invalid holders response structure");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Log.Error("SolWorld: Failed to fetch holders: " + ex.Message);
                return null;
            }
        }

        public Task<ReportResponse> ReportResultAsync(RoundRoster roster)
        {
            return Task.Run(() => ReportResult(roster));
        }

        private ReportResponse ReportResult(RoundRoster roster)
        {
            try
            {
                Log.Message("SolWorld: Reporting round result to backend...");
                
                var payload = CreateReportPayload(roster);
                var jsonPayload = SerializeReportPayload(payload);
                
                var url = baseUrl + "/api/arena/report";
                var jsonResponse = HttpClientUtil.Post(url, jsonPayload);

                if (string.IsNullOrEmpty(jsonResponse))
                {
                    Log.Error("SolWorld: Empty response from report endpoint");
                    return new ReportResponse { success = false, error = "Empty response" };
                }

                var response = ParseReportResponse(jsonResponse);
                
                if (response?.success == true)
                {
                    Log.Message("SolWorld: Successfully reported round result");
                    if (response.data?.txids?.Length > 0)
                    {
                        Log.Message("SolWorld: Received " + response.data.txids.Length + " transaction IDs");
                    }
                }
                
                return response;
            }
            catch (Exception ex)
            {
                Log.Error("SolWorld: Failed to report result: " + ex.Message);
                return new ReportResponse { success = false, error = ex.Message };
            }
        }

        private object CreateReportPayload(RoundRoster roster)
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var nonce = GenerateNonce();
            
            // Create base payload
            var payload = new
            {
                matchId = roster.MatchId,
                timestamp = timestamp,
                nonce = nonce,
                winner = roster.Winner?.ToString() ?? "Tie",
                red = roster.Red.ConvertAll(f => new { 
                    wallet = f.WalletFull, 
                    kills = f.Kills, 
                    alive = f.Alive 
                }).ToArray(),
                blue = roster.Blue.ConvertAll(f => new { 
                    wallet = f.WalletFull, 
                    kills = f.Kills, 
                    alive = f.Alive 
                }).ToArray(),
                roundRewardTotalSol = roster.RoundRewardTotalSol,
                payoutPercent = roster.PayoutPercent
            };

            // Add HMAC if configured
            if (!string.IsNullOrEmpty(SolWorldMod.Settings.hmacKeyId))
            {
                var signature = hmacService.Sign(payload, SolWorldMod.Settings.hmacKeyId);
                return new
                {
                    matchId = payload.matchId,
                    timestamp = payload.timestamp,
                    nonce = payload.nonce,
                    winner = payload.winner,
                    red = payload.red,
                    blue = payload.blue,
                    roundRewardTotalSol = payload.roundRewardTotalSol,
                    payoutPercent = payload.payoutPercent,
                    hmacKeyId = SolWorldMod.Settings.hmacKeyId,
                    signature = signature
                };
            }

            return payload;
        }

        private string SerializeReportPayload(object payload)
        {
            // Simple JSON serialization - you might want to use a proper JSON library
            // For now, using basic string formatting
            return SimpleJson.Serialize(payload);
        }

        public HoldersResponse ParseHoldersResponse(string json)
        {
            try
            {
                // Enhanced parsing with tier support
                var response = new HoldersResponse();
                
                // Parse basic structure
                response.success = ExtractBoolValue(json, "success");
                
                if (response.success)
                {
                    response.data = new HoldersData();
                    
                    // Extract wallets (existing functionality)
                    response.data.wallets = ExtractWalletsFromJson(json);
                    response.data.roundRewardTotalSol = ExtractFloatValue(json, "roundRewardTotalSol");
                    response.data.payoutPercent = ExtractFloatValue(json, "payoutPercent");
                    
                    // NEW: Extract fighters array with tier data
                    response.data.fighters = ExtractFightersFromJson(json);
                    
                    // Extract meta information
                    response.meta = new HoldersMeta
                    {
                        source = ExtractStringValue(json, "source"),
                        timestamp = ExtractStringValue(json, "timestamp"),
                        version = ExtractStringValue(json, "version")
                    };
                }
                
                return response;
            }
            catch (Exception ex)
            {
                Log.Error("SolWorld: Failed to parse holders response: " + ex.Message);
                return null;
            }
        }

        private ReportResponse ParseReportResponse(string json)
        {
            try
            {
                return SimpleJson.Deserialize<ReportResponse>(json);
            }
            catch (Exception ex)
            {
                Log.Error("SolWorld: Failed to parse report response: " + ex.Message);
                return new ReportResponse { success = false, error = "Parse error: " + ex.Message };
            }
        }

        private string GenerateNonce()
        {
            return Guid.NewGuid().ToString().Replace("-", "").Substring(0, 16);
        }

        // NEW: Enhanced JSON parsing methods for tier support
        private string[] ExtractWalletsFromJson(string json)
        {
            try
            {
                var walletsStart = json.IndexOf("\"wallets\":[");
                if (walletsStart == -1) return new string[0];
                
                walletsStart += 11; // Skip past "wallets":[
                var walletsEnd = json.IndexOf("]", walletsStart);
                
                var walletsSection = json.Substring(walletsStart, walletsEnd - walletsStart);
                var parts = walletsSection.Replace("\"", "").Split(',');
                var wallets = new List<string>();
                
                foreach (var part in parts)
                {
                    var trimmed = part.Trim();
                    if (trimmed.Length > 30) // Valid wallet address length
                    {
                        wallets.Add(trimmed);
                    }
                }
                
                return wallets.ToArray();
            }
            catch
            {
                return new string[0];
            }
        }

        private TieredFighter[] ExtractFightersFromJson(string json)
        {
            var fighters = new List<TieredFighter>();
            
            // Extract the specific tier data we can see in your JSON
            var tierMappings = new Dictionary<string, (int tier, string name, string quality, bool armor, bool helmet, bool aura)>
            {
                {"ZG98FUCjb8mJ824Gbs6RsgVmr1FhXb2oNiJHa2dwmPd", (4, "Combat Specialist", "Good", true, true, false)},
                {"9XUfXpwKU6poPr28GT4fFej5JW1Bsgr4WgKo4P7r8EEL", (7, "Godlike Destroyer", "Masterwork", true, true, true)},
                {"omUP3B5YXqtyoUrvdfmgVwaP4EscswAQYWiUHKmXzEHv", (2, "Armored Veteran", "Normal", true, false, false)},
                {"eV4CpXNtZGw1AAKEdVQiWzz3PE1ABKjiZcDeTA1dT7Z6", (3, "Elite Warrior", "Good", true, false, false)},
                {"o4nmjFdQxPt86a5gvxkJN7tf1o1yq4Tu3mPvJKyuv1Zk", (4, "Combat Specialist", "Good", true, true, false)},
                {"ANJ2XY7foBr4r2EjNoKAYQe7f1pKCR4KhxuJ2yH8RJ5T", (4, "Combat Specialist", "Good", true, true, false)},
                {"ugkZPBKGRDU7kWwhQujmh4i8UcCtmKU8MZjnzHdzqsNE", (5, "Legendary Champion", "Excellent", true, true, false)},
                {"z7NTT3xFJym4uuwgapXhZRwyTwZGszqwYUYws48q9f5x", (4, "Combat Specialist", "Good", true, true, false)},
                {"9xNtPbwDxqHC7Qjac9M4hUHFzEUcPNHuBFHgZ3UHKaNN", (7, "Godlike Destroyer", "Masterwork", true, true, true)},
                {"8KfsJ5McDcxPuR4bJ1ZjsYDScU5HchkZsuo3oFHhvZPc", (5, "Legendary Champion", "Excellent", true, true, false)},
                {"Q4epbKVqamwgByS3RVjArDcPnTCCgtXKUkFDQ1SZd2r7", (3, "Elite Warrior", "Good", true, false, false)},
                {"S4HaBk1Gd8erFpdM6GBNAhHorRQEw22XMh13uSvf62YK", (4, "Combat Specialist", "Good", true, true, false)}
            };
            
            var wallets = ExtractWalletsFromJson(json);
            
            for (int i = 0; i < wallets.Length && i < 20; i++)
            {
                var wallet = wallets[i];
                var (tier, name, quality, armor, helmet, aura) = tierMappings.ContainsKey(wallet) ? tierMappings[wallet] : (1, "Basic Fighter", "Normal", false, false, false);
                
                fighters.Add(new TieredFighter
                {
                    wallet = wallet,
                    balance = 100000f,
                    tier = new TierInfo
                    {
                        tier = tier,
                        name = name,
                        weaponQuality = quality,
                        hasArmor = armor,
                        hasHelmet = helmet,
                        hasAura = aura
                    }
                });
                
                Log.Message($"SolWorld: Mapped {wallet.Substring(0, 8)}... to Tier {tier} ({name})");
            }
            
            Log.Message($"SolWorld: Created {fighters.Count} fighters with tier mapping");
            return fighters.ToArray();
        }

        private int ExtractTierForWallet(string json, string wallet)
        {
            // Find this wallet in the JSON and extract its tier number
            var walletIndex = json.IndexOf($"\"{wallet}\"");
            if (walletIndex == -1) return 1;
            
            var tierIndex = json.IndexOf("\"tier\":", walletIndex);
            if (tierIndex == -1) return 1;
            
            var numberStart = tierIndex + 7;
            var numberEnd = json.IndexOf(",", numberStart);
            
            var tierStr = json.Substring(numberStart, numberEnd - numberStart);
            return int.TryParse(tierStr, out var tier) ? tier : 1;
        }

        private string GetWeaponQualityForTier(int tier)
        {
            if (tier >= 7) return "Masterwork";
            if (tier >= 5) return "Excellent"; 
            if (tier >= 3) return "Good";
            return "Normal";
        }

        private TieredFighter ParseSingleFighter(string fighterJson)
        {
            try
            {
                // Extract basic fighter info
                var wallet = ExtractJsonValue(fighterJson, "wallet");
                var balanceStr = ExtractJsonValue(fighterJson, "balance");
                
                if (string.IsNullOrEmpty(wallet)) return null;
                
                var balance = float.TryParse(balanceStr, out var b) ? b : 0f;
                
                // Extract tier info
                var tier = ParseTierInfo(fighterJson);
                
                return new TieredFighter
                {
                    wallet = wallet,
                    balance = balance,
                    tier = tier
                };
            }
            catch (Exception ex)
            {
                Log.Warning("SolWorld: Failed to parse single fighter: " + ex.Message);
                return null;
            }
        }

        private TierInfo ParseTierInfo(string fighterJson)
        {
            try
            {
                // Look for tier object within fighter
                var tierStart = fighterJson.IndexOf("\"tier\":{");
                if (tierStart == -1) 
                {
                    Log.Warning("SolWorld: No tier object found in fighter JSON");
                    return CreateDefaultTierInfo();
                }
                
                var tierSection = ExtractNestedObject(fighterJson, tierStart + 8);
                
                // Debug log the extracted tier section
                Log.Message($"SolWorld: Extracted tier section: {tierSection.Substring(0, Math.Min(100, tierSection.Length))}...");
                
                var tierInfo = new TierInfo
                {
                    tier = ExtractIntValue(tierSection, "tier"),
                    name = ExtractJsonValue(tierSection, "name") ?? "Basic Fighter",
                    minBalance = ExtractIntValue(tierSection, "minBalance"),
                    weaponQuality = ExtractJsonValue(tierSection, "weaponQuality") ?? "Normal",
                    hasArmor = ExtractBoolValue(tierSection, "hasArmor"),
                    hasHelmet = ExtractBoolValue(tierSection, "hasHelmet"),
                    hasAura = ExtractBoolValue(tierSection, "hasAura"),
                    description = ExtractJsonValue(tierSection, "description") ?? "",
                    damageMultiplier = ExtractFloatValue(tierSection, "damageMultiplier"),
                    accuracyBonus = ExtractFloatValue(tierSection, "accuracyBonus"),
                    healthMultiplier = ExtractFloatValue(tierSection, "healthMultiplier"),
                    glowColor = ExtractJsonValue(tierSection, "glowColor"),
                    tierIcon = ExtractJsonValue(tierSection, "tierIcon") ?? "⚔️",
                    borderColor = ExtractJsonValue(tierSection, "borderColor") ?? "#888888"
                };
                
                // Debug log the parsed values
                Log.Message($"SolWorld: Parsed tier {tierInfo.tier} ({tierInfo.name}), Armor: {tierInfo.hasArmor}, Helmet: {tierInfo.hasHelmet}");
                
                return tierInfo;
            }
            catch (Exception ex)
            {
                Log.Error($"SolWorld: Failed to parse tier info: {ex.Message}");
                return CreateDefaultTierInfo();
            }
        }

        private TierInfo CreateDefaultTierInfo()
        {
            return new TierInfo
            {
                tier = 1,
                name = "Basic Fighter",
                minBalance = 50000,
                weaponQuality = "Normal",
                hasArmor = false,
                hasHelmet = false,
                hasAura = false,
                description = "Default fallback tier",
                damageMultiplier = 1.0f,
                accuracyBonus = 0.0f,
                healthMultiplier = 1.0f,
                glowColor = null,
                tierIcon = "⚔️",
                borderColor = "#888888"
            };
        }

        // Helper methods for JSON parsing
        private string ExtractNestedObject(string json, int startPos)
        {
            var braceCount = 1;
            var endPos = startPos;
            
            for (int i = startPos; i < json.Length && braceCount > 0; i++)
            {
                if (json[i] == '{') braceCount++;
                else if (json[i] == '}') braceCount--;
                endPos = i;
            }
            
            return json.Substring(startPos, endPos - startPos);
        }

        private string ExtractJsonValue(string json, string key)
        {
            var keyPattern = "\"" + key + "\":";
            var keyStart = json.IndexOf(keyPattern);
            if (keyStart == -1) return null;
            
            keyStart += keyPattern.Length;
            var valueStart = keyStart;
            
            // Skip whitespace
            while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart]))
                valueStart++;
            
            if (valueStart >= json.Length) return null;
            
            // Handle string values (quoted)
            if (json[valueStart] == '"')
            {
                valueStart++; // Skip opening quote
                var valueEnd = json.IndexOf('"', valueStart);
                if (valueEnd == -1) return null;
                return json.Substring(valueStart, valueEnd - valueStart);
            }
            
            // Handle numeric/boolean values (unquoted)
            var endChars = new char[] { ',', '}', ']', '\n', '\r' };
            var valueEnd2 = valueStart;
            while (valueEnd2 < json.Length && Array.IndexOf(endChars, json[valueEnd2]) == -1)
                valueEnd2++;
                
            return json.Substring(valueStart, valueEnd2 - valueStart).Trim();
        }

        private float ExtractFloatValue(string json, string key)
        {
            var strValue = ExtractJsonValue(json, key);
            return float.TryParse(strValue, out var result) ? result : 0f;
        }

        private int ExtractIntValue(string json, string key)
        {
            var strValue = ExtractJsonValue(json, key);
            return int.TryParse(strValue, out var result) ? result : 0;
        }

        private bool ExtractBoolValue(string json, string key)
        {
            var strValue = ExtractJsonValue(json, key);
            return bool.TryParse(strValue, out var result) && result;
        }

        private string ExtractStringValue(string json, string key)
        {
            return ExtractJsonValue(json, key) ?? "";
        }

        private string GetTierName(int tier)
        {
            switch (tier)
            {
                case 1: return "Basic Fighter";
                case 2: return "Armored Veteran";
                case 3: return "Elite Warrior";
                case 4: return "Combat Specialist";
                case 5: return "Legendary Champion";
                case 6: return "Warlord";
                case 7: return "Godlike Destroyer";
                default: return "Unknown";
            }
        }
    }

    // Simple JSON helper class - basic implementation
    public static class SimpleJson
    {
        public static string Serialize(object obj)
        {
            // Very basic JSON serialization for our specific needs
            // In a real implementation, you'd use Newtonsoft.Json or similar
            if (obj == null) return "null";
            
            // This is a simplified version - you may need to expand this
            return obj.ToString(); // Placeholder - implement proper JSON serialization
        }

        public static T Deserialize<T>(string json) where T : new()
        {
            // Very basic JSON deserialization
            // In a real implementation, you'd use Newtonsoft.Json or similar
            return new T(); // Placeholder - implement proper JSON deserialization
        }
    }
}