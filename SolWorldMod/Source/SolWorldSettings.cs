// solworld/SolWorldMod/Source/SolWorldSettings.cs
using Verse;

namespace SolWorldMod
{
    public class SolWorldSettings : ModSettings
    {
        // Backend Configuration
        public string apiBaseUrl = "http://localhost:4000";
        public string hmacKeyId = "";
        public string tokenMint = "";
        
        // Payout Configuration
        public float payoutPercent = 0.20f; // 20%
        public float roundPoolSol = 1.0f;    // 1 SOL per round
        
        // Fixed timing constants (not configurable via UI)
        public const int CADENCE_SECONDS = 300;     // 5 minutes
        public const int PREVIEW_SECONDS = 30;      // 30 seconds
        public const int COMBAT_SECONDS = 240;      // 4 minutes
        
        // Computed properties
        public bool IsDevMode => string.IsNullOrEmpty(hmacKeyId);
        public string HoldersEndpoint => (apiBaseUrl?.TrimEnd('/') ?? "") + "/api/arena/holders";
        public string ReportEndpoint => (apiBaseUrl?.TrimEnd('/') ?? "") + "/api/arena/report";
        
        public override void ExposeData()
        {
            Scribe_Values.Look(ref apiBaseUrl, "apiBaseUrl", "http://localhost:4000");
            Scribe_Values.Look(ref hmacKeyId, "hmacKeyId", "");
            Scribe_Values.Look(ref tokenMint, "tokenMint", "");
            Scribe_Values.Look(ref payoutPercent, "payoutPercent", 0.20f);
            Scribe_Values.Look(ref roundPoolSol, "roundPoolSol", 1.0f);
            base.ExposeData();
        }
    }
}