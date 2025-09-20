// solworld/SolWorldMod/Source/ModEntry.cs
using UnityEngine;
using Verse;

namespace SolWorldMod
{
    public class SolWorldMod : Mod
    {
        public static SolWorldSettings Settings;

        public SolWorldMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<SolWorldSettings>();
            
            // Initialize UI drawer for scoreboard
            SimpleUIDrawer.Initialize();
            Log.Message("SolWorld: Mod initialized with UI drawer");
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listingStandard = new Listing_Standard();
            listingStandard.Begin(inRect);

            // Header
            listingStandard.Label("SolWorld Arena - Backend Configuration");
            listingStandard.Gap();

            // API Base URL
            listingStandard.Label("Backend API Base URL:");
            Settings.apiBaseUrl = listingStandard.TextEntry(Settings.apiBaseUrl);
            listingStandard.Gap(4f);

            // HMAC Key ID
            listingStandard.Label("HMAC Key ID (leave empty for dev mode):");
            Settings.hmacKeyId = listingStandard.TextEntry(Settings.hmacKeyId);
            listingStandard.Gap(4f);

            // Token Mint Address
            listingStandard.Label("Native Token Mint Address:");
            Settings.tokenMint = listingStandard.TextEntry(Settings.tokenMint);
            listingStandard.Gap(4f);

            // Payout Percent
            listingStandard.Label("Payout Percent: " + Settings.payoutPercent.ToString("P1"));
            Settings.payoutPercent = listingStandard.Slider(Settings.payoutPercent, 0.05f, 0.50f);
            listingStandard.Gap(4f);

            // Round Pool Amount
            listingStandard.Label("Round Pool Amount (SOL): " + Settings.roundPoolSol.ToString("F2"));
            Settings.roundPoolSol = listingStandard.Slider(Settings.roundPoolSol, 0.1f, 10.0f);
            listingStandard.Gap();

            // Read-only timing display
            listingStandard.Label("Fixed Timing Configuration:");
            listingStandard.Label("• Round Cadence: 5 minutes (300 seconds)");
            listingStandard.Label("• Preview Phase: 30 seconds (paused)");
            listingStandard.Label("• Combat Phase: 4 minutes (240 seconds)");
            listingStandard.Gap();

            // Connection status
            if (!string.IsNullOrEmpty(Settings.apiBaseUrl))
            {
                listingStandard.Label("Backend Status: " + (Settings.IsDevMode ? "Dev Mode" : "Production Mode"));
            }
            else
            {
                listingStandard.Label("Backend Status: Not configured");
            }

            listingStandard.End();
            base.DoSettingsWindowContents(inRect);
        }

        public override string SettingsCategory()
        {
            return "SolWorld Arena";
        }
    }
}