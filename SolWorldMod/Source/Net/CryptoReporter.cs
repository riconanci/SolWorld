// SolWorldMod/Source/Net/CryptoReporter.cs
using System;
using System.Threading.Tasks;
using Verse;
using SolWorldMod.Security;

namespace SolWorldMod.Net
{
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
    }

    public class HoldersMeta
    {
        public string source;
        public HoldersStats stats;
        public string timestamp;
    }

    public class HoldersStats
    {
        public int totalAvailable;
        public int selected;
        public int mockUsed;
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

                // Simple JSON parsing (you might want to use a proper JSON library)
                var response = ParseHoldersResponse(jsonResponse);
                
                if (response?.success == true && response.data?.wallets?.Length == 20)
                {
                    Log.Message("SolWorld: Successfully fetched " + response.data.wallets.Length + " holders");
                    Log.Message("SolWorld: Pool: " + response.data.roundRewardTotalSol + " SOL, Payout: " + (response.data.payoutPercent * 100) + "%");
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

        private HoldersResponse ParseHoldersResponse(string json)
        {
            try
            {
                // Simple JSON parsing - you might want to use a proper JSON library
                return SimpleJson.Deserialize<HoldersResponse>(json);
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