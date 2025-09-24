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
            Log.Message("SolWorld: Mod initialized with clean arena UI system");
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listingStandard = new Listing_Standard();
            listingStandard.Begin(inRect);

            // Header
            listingStandard.Label("SolWorld Arena - Configuration");
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

            // NEW: Combat Loadout Configuration
            listingStandard.Gap();
            listingStandard.Label("Combat Loadout Configuration:");
            listingStandard.Gap(4f);

            // Loadout preset selection with random option
            string currentLoadoutText;
            if (Settings.selectedLoadoutPreset == -1)
            {
                currentLoadoutText = "🎲 Random Each Round";
            }
            else
            {
                var currentPreset = LoadoutManager.GetPreset(Settings.selectedLoadoutPreset);
                currentLoadoutText = currentPreset.Name;
            }
            
            listingStandard.Label("Loadout Mode: " + currentLoadoutText);

            if (listingStandard.ButtonText("< " + currentLoadoutText + " >"))
            {
                // Cycle through presets: -1 (Random) -> 0 -> 1 -> 2 -> ... -> back to -1
                Settings.selectedLoadoutPreset++;
                if (Settings.selectedLoadoutPreset >= LoadoutManager.AVAILABLE_PRESETS.Length)
                {
                    Settings.selectedLoadoutPreset = -1; // Back to random
                }
            }

            // Show description of current loadout
            string loadoutDesc;
            if (Settings.selectedLoadoutPreset == -1)
            {
                loadoutDesc = "A different combat preset will be randomly selected each round for variety";
            }
            else
            {
                var preset = LoadoutManager.GetPreset(Settings.selectedLoadoutPreset);
                loadoutDesc = preset.Description;
            }
            
            listingStandard.Gap(4f);
            Text.Font = GameFont.Tiny;
            listingStandard.Label(loadoutDesc);
            Text.Font = GameFont.Small;
            listingStandard.Gap();

            // NEW: Clean Arena UI Section
            listingStandard.Gap();
            listingStandard.Label("Clean Arena UI Settings:");
            listingStandard.Gap(4f);
            
            GUI.color = Color.cyan;
            listingStandard.Label("🎬 Arena Combat Features:");
            GUI.color = Color.white;
            
            Text.Font = GameFont.Tiny;
            listingStandard.Label("✓ Hides inventory/resource display (top-left)");
            listingStandard.Label("✓ Removes popup alerts and notifications");
            listingStandard.Label("✓ Cleans up bottom-right play settings");
            listingStandard.Label("✓ Hides main tabs and build menus");
            listingStandard.Label("✓ Removes colonist bar for clean viewing");
            listingStandard.Label("✓ Shows only time controls + arena scoreboard");
            Text.Font = GameFont.Small;
            listingStandard.Gap(4f);
            
            GUI.color = Color.yellow;
            listingStandard.Label("Arena State Indicator:");
            GUI.color = Color.white;
            Text.Font = GameFont.Tiny;
            listingStandard.Label("• 🎬 PREVIEW - 30s paused preview phase");
            listingStandard.Label("• ⚔️ COMBAT - 90s active fighting");
            listingStandard.Label("• 🏆 ROUND END - Winner celebration");
            listingStandard.Label("• 🔄 RESETTING - Arena cleanup & restore");
            Text.Font = GameFont.Small;

            // Read-only timing information
            listingStandard.Gap();
            listingStandard.Label("Arena Timing Configuration (Fixed):");
            listingStandard.Gap(4f);

            Text.Font = GameFont.Tiny;
            listingStandard.Label("Preview Duration: 30 seconds (real-time, game paused)");
            listingStandard.Label("Combat Duration: 90 seconds (game time)");
            listingStandard.Label("Reset Duration: 3 seconds (show results)");
            listingStandard.Label("Round Cadence: 5 minutes total (3min break + 2min active)");
            Text.Font = GameFont.Small;
            
            // Backend Integration Status
            listingStandard.Gap();
            listingStandard.Label("Backend Integration:");
            listingStandard.Gap(4f);
            
            if (string.IsNullOrEmpty(Settings.apiBaseUrl))
            {
                GUI.color = Color.red;
                listingStandard.Label("⚠️ No backend URL configured - using mock data");
            }
            else
            {
                GUI.color = Color.green;
                listingStandard.Label("✓ Backend URL: " + Settings.apiBaseUrl);
            }
            GUI.color = Color.white;
            
            Text.Font = GameFont.Tiny;
            listingStandard.Label("Holders endpoint: " + Settings.HoldersEndpoint);
            listingStandard.Label("Report endpoint: " + Settings.ReportEndpoint);
            Text.Font = GameFont.Small;

            listingStandard.End();
        }

        public override string SettingsCategory()
        {
            return "SolWorld Arena";
        }
    }
}