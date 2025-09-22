// solworld/SolWorldMod/Source/SolWorldSettings.cs
using Verse;

namespace SolWorldMod
{
    public class SolWorldSettings : ModSettings
    {
        // Use fields instead of auto-properties to avoid ref/out issues
        private string apiBaseUrlField = "http://localhost:4000";
        private string hmacKeyIdField = "";
        private string tokenMintField = "";
        private float payoutPercentField = 0.20f;
        private float roundPoolSolField = 1.0f;
        private int selectedLoadoutPresetField = 0; // NEW: Loadout preset selection
        
        // Backend Configuration
        public string apiBaseUrl 
        { 
            get { return apiBaseUrlField; } 
            set { apiBaseUrlField = value; } 
        }
        
        public string hmacKeyId 
        { 
            get { return hmacKeyIdField; } 
            set { hmacKeyIdField = value; } 
        }
        
        public string tokenMint 
        { 
            get { return tokenMintField; } 
            set { tokenMintField = value; } 
        }
        
        // Payout Configuration
        public float payoutPercent 
        { 
            get { return payoutPercentField; } 
            set { payoutPercentField = value; } 
        }
        
        public float roundPoolSol 
        { 
            get { return roundPoolSolField; } 
            set { roundPoolSolField = value; } 
        }
        
        // NEW: Loadout Configuration
        public int selectedLoadoutPreset 
        { 
            get { return selectedLoadoutPresetField; } 
            set { selectedLoadoutPresetField = value; } 
        }
        
        // Fixed timing constants (not configurable via UI)
        public const int CADENCE_SECONDS = 300;     // 5 minutes
        public const int PREVIEW_SECONDS = 30;      // 30 seconds
        public const int COMBAT_SECONDS = 240;      // 4 minutes
        
        // Computed properties
        public bool IsDevMode 
        { 
            get { return string.IsNullOrEmpty(hmacKeyIdField); } 
        }
        
        public string HoldersEndpoint 
        { 
            get { return (apiBaseUrlField?.TrimEnd('/') ?? "") + "/api/arena/holders"; } 
        }
        
        public string ReportEndpoint 
        { 
            get { return (apiBaseUrlField?.TrimEnd('/') ?? "") + "/api/arena/report"; } 
        }
        
        public override void ExposeData()
        {
            Scribe_Values.Look(ref apiBaseUrlField, "apiBaseUrl", "http://localhost:4000");
            Scribe_Values.Look(ref hmacKeyIdField, "hmacKeyId", "");
            Scribe_Values.Look(ref tokenMintField, "tokenMint", "");
            Scribe_Values.Look(ref payoutPercentField, "payoutPercent", 0.20f);
            Scribe_Values.Look(ref roundPoolSolField, "roundPoolSol", 1.0f);
            Scribe_Values.Look(ref selectedLoadoutPresetField, "selectedLoadoutPreset", 0); // NEW: Save loadout setting
            base.ExposeData();
        }
    }
}