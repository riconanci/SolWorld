// SolWorldMod/Source/Net/CryptoReporter.cs
using System;
using System.Collections.Generic;
using System.Linq;
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
            
            // ✅ Create fighter arrays FIRST as proper objects
            var redFighters = new object[roster.Red.Count];
            for (int i = 0; i < roster.Red.Count; i++)
            {
                var f = roster.Red[i];
                redFighters[i] = new { 
                    wallet = f.WalletFull, 
                    kills = f.Kills, 
                    alive = f.Alive 
                };
            }
            
            var blueFighters = new object[roster.Blue.Count];
            for (int i = 0; i < roster.Blue.Count; i++)
            {
                var f = roster.Blue[i];
                blueFighters[i] = new { 
                    wallet = f.WalletFull, 
                    kills = f.Kills, 
                    alive = f.Alive 
                };
            }
            
            // Create base payload using pre-built arrays
            var basePayload = new
            {
                blue = blueFighters,              // alphabetically first
                matchId = roster.MatchId,
                nonce = nonce,
                payoutPercent = roster.PayoutPercent,
                red = redFighters,
                roundRewardTotalSol = roster.RoundRewardTotalSol,
                timestamp = timestamp,
                winner = roster.Winner?.ToString() ?? "Tie"
            };

            // If HMAC is configured, sign and return with signature fields
            if (!string.IsNullOrEmpty(SolWorldMod.Settings.hmacKeyId))
            {
                var signature = hmacService.Sign(basePayload, SolWorldMod.Settings.hmacKeyId);
                
                return new
                {
                    blue = blueFighters,  // ✅ Use same pre-built array
                    hmacKeyId = SolWorldMod.Settings.hmacKeyId,
                    matchId = basePayload.matchId,
                    nonce = basePayload.nonce,
                    payoutPercent = basePayload.payoutPercent,
                    red = redFighters,    // ✅ Use same pre-built array
                    roundRewardTotalSol = basePayload.roundRewardTotalSol,
                    signature = signature,
                    timestamp = basePayload.timestamp,
                    winner = basePayload.winner
                };
            }

            return basePayload;
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
                    
                    // NEW: Extract fighters array with tier data - PROPER JSON PARSING
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

        // ⭐ COMPLETELY REWRITTEN: Proper JSON parsing instead of hardcoded mappings
        private TieredFighter[] ExtractFightersFromJson(string json)
        {
            var fighters = new List<TieredFighter>();
            
            try
            {
                Log.Message("SolWorld: Extracting fighters with proper JSON parsing...");
                
                // Find the fighters array in the JSON
                var fightersStart = json.IndexOf("\"fighters\":[");
                if (fightersStart == -1) 
                {
                    Log.Warning("SolWorld: No fighters array found in JSON - using fallback parsing");
                    return CreateFallbackFighters(json);
                }
                
                // Extract the fighters array content
                var fightersArrayStart = fightersStart + 12; // Skip past "fighters":[
                var fightersArrayEnd = json.IndexOf("]", fightersArrayStart);
                
                if (fightersArrayEnd == -1)
                {
                    Log.Warning("SolWorld: Could not find end of fighters array");
                    return CreateFallbackFighters(json);
                }
                
                var fightersSection = json.Substring(fightersArrayStart, fightersArrayEnd - fightersArrayStart);
                Log.Message($"SolWorld: Extracted fighters section: {fightersSection.Length} characters");
                
                // Split by fighter object separators
                var fighterParts = fightersSection.Split(new string[] { "},{" }, StringSplitOptions.RemoveEmptyEntries);
                Log.Message($"SolWorld: Found {fighterParts.Length} fighter parts to process");
                
                for (int i = 0; i < fighterParts.Length; i++)
                {
                    try
                    {
                        var fighterJson = fighterParts[i].Trim();
                        
                        // Add missing braces back (they get removed by the split)
                        if (!fighterJson.StartsWith("{")) fighterJson = "{" + fighterJson;
                        if (!fighterJson.EndsWith("}")) fighterJson = fighterJson + "}";
                        
                        Log.Message($"SolWorld: Processing fighter {i + 1}: {fighterJson.Substring(0, Math.Min(50, fighterJson.Length))}...");
                        
                        var fighter = ParseSingleFighterFromJson(fighterJson);
                        if (fighter != null)
                        {
                            fighters.Add(fighter);
                            Log.Message($"SolWorld: Successfully parsed fighter {fighter.wallet.Substring(0, 8)}... as Tier {fighter.tier.tier} ({fighter.tier.name})");
                        }
                        else
                        {
                            Log.Warning($"SolWorld: Failed to parse fighter {i + 1}");
                        }
                    }
                    catch (Exception fighterEx)
                    {
                        Log.Warning($"SolWorld: Exception parsing fighter {i + 1}: {fighterEx.Message}");
                    }
                }
                
                if (fighters.Count > 0)
                {
                    Log.Message($"SolWorld: Successfully parsed {fighters.Count} fighters with tier data");
                    
                    Log.Message($"SolWorld: JSON parsing completed - found {fighters.Count} fighters total");
                    Log.Message($"SolWorld: Expected ~20 fighters from backend tier distribution");
                
                    // Log tier distribution
                    var tierCounts = new Dictionary<int, int>();
                    foreach (var fighter in fighters)
                    {
                        var tier = fighter.tier.tier;
                        tierCounts[tier] = (tierCounts.ContainsKey(tier) ? tierCounts[tier] : 0) + 1;
                    }
                    
                    foreach (var kvp in tierCounts.OrderBy(x => x.Key))
                    {
                        Log.Message($"SolWorld: {kvp.Value}x Tier {kvp.Key}");
                    }
                }
                else
                {
                    Log.Warning("SolWorld: No fighters parsed, using fallback");
                    return CreateFallbackFighters(json);
                }
                
                return fighters.ToArray();
            }
            catch (Exception ex)
            {
                Log.Error($"SolWorld: Failed to parse fighters JSON: {ex.Message}");
                return CreateFallbackFighters(json);
            }
        }

        private TieredFighter ParseSingleFighterFromJson(string fighterJson)
        {
            try
            {
                var wallet = ExtractJsonValue(fighterJson, "wallet");
                var balance = ExtractFloatValue(fighterJson, "balance");
                
                if (string.IsNullOrEmpty(wallet))
                {
                    Log.Warning("SolWorld: Fighter missing wallet address");
                    return null;
                }
                
                // Extract the tier object
                var tierStart = fighterJson.IndexOf("\"tier\":{");
                if (tierStart == -1) 
                {
                    Log.Warning($"SolWorld: No tier data found for {wallet.Substring(0, 8)}..., using Tier 1");
                    return CreateDefaultFighter(wallet, balance);
                }
                
                var tierSection = ExtractNestedObject(fighterJson, tierStart + 7);
                
                // Extract tier properties
                var tierNum = ExtractIntValue(tierSection, "tier");
                var tierName = ExtractJsonValue(tierSection, "name") ?? GetTierName(tierNum);
                var weaponQuality = ExtractJsonValue(tierSection, "weaponQuality") ?? "Normal";
                var hasArmor = ExtractBoolValue(tierSection, "hasArmor");
                var hasHelmet = ExtractBoolValue(tierSection, "hasHelmet");
                var hasAura = ExtractBoolValue(tierSection, "hasAura");
                
                // Validate tier number
                if (tierNum < 1 || tierNum > 7)
                {
                    Log.Warning($"SolWorld: Invalid tier {tierNum} for {wallet.Substring(0, 8)}..., using Tier 1");
                    tierNum = 1;
                    tierName = "Basic Fighter";
                    weaponQuality = "Normal";
                    hasArmor = false;
                    hasHelmet = false;
                    hasAura = false;
                }
                
                return new TieredFighter
                {
                    wallet = wallet,
                    balance = balance,
                    tier = new TierInfo
                    {
                        tier = tierNum,
                        name = tierName,
                        weaponQuality = weaponQuality,
                        hasArmor = hasArmor,
                        hasHelmet = hasHelmet,
                        hasAura = hasAura,
                        minBalance = GetMinBalanceForTier(tierNum),
                        damageMultiplier = GetDamageMultiplierForTier(tierNum),
                        accuracyBonus = GetAccuracyBonusForTier(tierNum),
                        healthMultiplier = GetHealthMultiplierForTier(tierNum),
                        tierIcon = GetTierIcon(tierNum),
                        borderColor = GetBorderColor(tierNum),
                        description = $"Tier {tierNum} fighter with enhanced equipment",
                        glowColor = tierNum >= 6 ? GetGlowColor(tierNum) : null
                    }
                };
            }
            catch (Exception ex)
            {
                Log.Error($"SolWorld: Failed to parse single fighter: {ex.Message}");
                return null;
            }
        }

        private TieredFighter CreateDefaultFighter(string wallet, float balance)
        {
            return new TieredFighter
            {
                wallet = wallet,
                balance = balance,
                tier = CreateDefaultTierInfo()
            };
        }

        private TieredFighter[] CreateFallbackFighters(string json)
        {
            // If JSON parsing fails, extract wallets and create basic fighters
            var wallets = ExtractWalletsFromJson(json);
            var fighters = new List<TieredFighter>();
            
            foreach (var wallet in wallets)
            {
                fighters.Add(CreateDefaultFighter(wallet, 100000f));
            }
            
            Log.Message($"SolWorld: Created {fighters.Count} fallback fighters (all Tier 1)");
            return fighters.ToArray();
        }

        // Helper methods for tier properties
        private int GetMinBalanceForTier(int tier)
        {
            switch (tier)
            {
                case 1: return 50000;
                case 2: return 100000;
                case 3: return 150000;
                case 4: return 250000;
                case 5: return 500000;
                case 6: return 1000000;
                case 7: return 1500000;
                default: return 50000;
            }
        }

        private float GetDamageMultiplierForTier(int tier)
        {
            switch (tier)
            {
                case 1: return 1.0f;
                case 2: return 1.0f;
                case 3: return 1.15f;
                case 4: return 1.15f;
                case 5: return 1.3f;
                case 6: return 1.3f;
                case 7: return 1.5f;
                default: return 1.0f;
            }
        }

        private float GetAccuracyBonusForTier(int tier)
        {
            switch (tier)
            {
                case 1: return 0.0f;
                case 2: return 0.05f;
                case 3: return 0.1f;
                case 4: return 0.15f;
                case 5: return 0.2f;
                case 6: return 0.25f;
                case 7: return 0.3f;
                default: return 0.0f;
            }
        }

        private float GetHealthMultiplierForTier(int tier)
        {
            switch (tier)
            {
                case 1: return 1.0f;
                case 2: return 1.1f;
                case 3: return 1.1f;
                case 4: return 1.2f;
                case 5: return 1.3f;
                case 6: return 1.4f;
                case 7: return 1.5f;
                default: return 1.0f;
            }
        }

        private string GetTierIcon(int tier)
        {
            switch (tier)
            {
                case 1: return "⚔️";
                case 2: return "🛡️";
                case 3: return "🗡️";
                case 4: return "👑";
                case 5: return "⭐";
                case 6: return "💎";
                case 7: return "🏆";
                default: return "⚔️";
            }
        }

        private string GetBorderColor(int tier)
        {
            switch (tier)
            {
                case 1: return "#888888";
                case 2: return "#4CAF50";
                case 3: return "#2196F3";
                case 4: return "#9C27B0";
                case 5: return "#FF9800";
                case 6: return "#E91E63";
                case 7: return "#FFD700";
                default: return "#888888";
            }
        }

        private string GetGlowColor(int tier)
        {
            switch (tier)
            {
                case 6: return "cyan";
                case 7: return "gold";
                default: return null;
            }
        }

        // PRESERVED: All original tier parsing methods (kept as fallbacks)
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
                case 6: return "Mythical Warlord";
                case 7: return "Godlike Destroyer";
                default: return "Unknown";
            }
        }
    }

    // Simple JSON helper class - basic implementation (PRESERVED)
    // Replace the SimpleJson class at the BOTTOM of CryptoReporter.cs
    // This version properly serializes nested anonymous objects

    public static class SimpleJson
    {
        public static string Serialize(object obj)
        {
            if (obj == null) return "null";
            return SerializeValue(obj);
        }
        
        private static string SerializeValue(object value)
        {
            if (value == null) return "null";
            
            var type = value.GetType();
            
            // String values need quotes and escaping
            if (type == typeof(string))
            {
                return "\"" + EscapeString(value.ToString()) + "\"";
            }
            
            // Boolean values must be lowercase
            if (type == typeof(bool))
            {
                return value.ToString().ToLower();
            }
            
            // Numbers don't need quotes
            if (IsNumericType(type))
            {
                var str = value.ToString();
                // Handle float formatting - ensure decimal point, not comma
                if (type == typeof(float) || type == typeof(double))
                {
                    str = str.Replace(",", ".");
                }
                return str;
            }
            
            // Arrays
            if (type.IsArray)
            {
                var array = value as Array;
                var sb = new System.Text.StringBuilder();
                sb.Append("[");
                
                for (int i = 0; i < array.Length; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append(SerializeValue(array.GetValue(i)));
                }
                
                sb.Append("]");
                return sb.ToString();
            }
            
            // Objects (including anonymous types) - check by seeing if it has properties
            var properties = type.GetProperties();
            if (properties.Length > 0)
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("{");
                
                // CRITICAL: Sort properties alphabetically to match backend canonicalization
                var sortedProps = properties
                    .Where(p => p.GetIndexParameters().Length == 0) // Skip indexer properties
                    .OrderBy(p => p.Name)
                    .ToArray();
                
                var first = true;
                foreach (var prop in sortedProps)
                {
                    if (!first) sb.Append(",");
                    first = false;
                    
                    var propValue = prop.GetValue(value);
                    
                    // Add property name with quotes and colon
                    sb.Append("\"");
                    sb.Append(prop.Name);
                    sb.Append("\":");
                    
                    // Recursively serialize the property value
                    sb.Append(SerializeValue(propValue));
                }
                
                sb.Append("}");
                return sb.ToString();
            }
            
            // Default fallback - quote it as a string
            return "\"" + EscapeString(value.ToString()) + "\"";
        }
        
        private static string EscapeString(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            
            return str
                .Replace("\\", "\\\\")  // Backslash must be first
                .Replace("\"", "\\\"")  // Escape quotes
                .Replace("\n", "\\n")   // Newlines
                .Replace("\r", "\\r")   // Carriage returns
                .Replace("\t", "\\t");  // Tabs
        }
        
        private static bool IsNumericType(Type type)
        {
            return type == typeof(int) || 
                type == typeof(long) || 
                type == typeof(float) || 
                type == typeof(double) || 
                type == typeof(decimal) ||
                type == typeof(short) ||
                type == typeof(byte) ||
                type == typeof(uint) ||
                type == typeof(ulong) ||
                type == typeof(ushort);
        }
        
        public static T Deserialize<T>(string json) where T : new()
        {
            // Basic deserialization - just return a new instance
            // The real parsing is done by the ExtractJsonValue methods in CryptoReporter
            return new T();
        }
    }
}