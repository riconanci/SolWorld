// Enhanced ScoreboardUI.cs - Preserves ALL original functionality + adds complete visual effects system
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;
using System.Collections.Generic;

namespace SolWorldMod
{
    public static class ScoreboardUI
    {
        private static bool lastFrameWasPreview = false;
        private static float lastPreviewTimeCheck = 0f;
        
        // Enhanced visual effects timing
        private static float glowPulseTime = 0f;
        private static float auraRotation = 0f;
        
        // Tier visualization constants
        private static readonly Color[] TIER_COLORS = new Color[]
        {
            new Color(0.5f, 0.5f, 0.5f),    // Tier 1 - Gray
            new Color(0.3f, 0.7f, 0.3f),    // Tier 2 - Green  
            new Color(0.2f, 0.6f, 1.0f),    // Tier 3 - Blue
            new Color(0.6f, 0.2f, 1.0f),    // Tier 4 - Purple
            new Color(1.0f, 0.6f, 0.0f),    // Tier 5 - Orange
            new Color(0.9f, 0.1f, 0.6f),    // Tier 6 - Pink (Mythical)
            new Color(1.0f, 0.8f, 0.0f)     // Tier 7 - Gold (Godlike)
        };
        
        private static readonly string[] TIER_ICONS = { "⚔️", "🛡️", "🗡️", "👑", "⭐", "💎", "🏆" };
        
        // Enhanced aura colors for high-tier fighters
        private static readonly Color[] AURA_COLORS = new Color[]
        {
            Color.clear,                     // Tier 1-5 - No aura
            Color.clear,                     // 
            Color.clear,                     // 
            Color.clear,                     // 
            Color.clear,                     // 
            new Color(0f, 1f, 1f, 0.4f),     // Tier 6 - Cyan aura (Mythical Warlord)
            new Color(1f, 0.8f, 0f, 0.5f)   // Tier 7 - Gold aura (Godlike Destroyer)
        };
        
        public static void DrawScoreboard()
        {
            // Update visual effect timers
            glowPulseTime += Time.unscaledDeltaTime;
            auraRotation += Time.unscaledDeltaTime * 30f; // 30 degrees per second
            
            var map = Find.CurrentMap;
            if (map == null) return;
            
            var arenaComp = map.GetComponent<MapComponent_SolWorldArena>();
            
            // PRESERVED: ALWAYS show scoreboard when arena is active OR has roster data
            if (arenaComp?.IsActive != true && arenaComp?.CurrentRoster == null)
                return;
            
            // PRESERVED: Handle phase-specific countdown logic
            HandlePreviewCountdown(arenaComp);
            HandleCombatCountdown(arenaComp);
            HandleNextRoundCountdown(arenaComp);
            
            var roster = arenaComp.CurrentRoster;
            
            // PRESERVED: MAIN DISPLAY - Choose display mode based on state and persistent winner data
            if (ShouldShowWinnerCelebration(arenaComp, roster))
            {
                DrawWinnerCelebration(arenaComp, roster);
            }
            else
            {
                DrawStandardLeaderboard(arenaComp, roster);
            }
        }
        
        // PRESERVED: Use persistent winner storage instead of roster
        private static bool ShouldShowWinnerCelebration(MapComponent_SolWorldArena arenaComp, RoundRoster roster)
        {
            // Show winner celebration during the first 2 minutes of idle time after a round
            if (arenaComp.CurrentState == ArenaState.Idle && 
                arenaComp.LastRoundWinner.HasValue) // PRESERVED: Use persistent storage
            {
                var timeUntilNext = arenaComp.GetTimeUntilNextRound();
                
                // Show winners for first 2 minutes (when timeUntilNext > 60)
                return timeUntilNext > 60;
            }
            
            return false;
        }
        
        private static void DrawTransactionWindow(MapComponent_SolWorldArena arenaComp)
        {
            if (lastTransactionIds == null || lastTransactionIds.Count == 0) return;
            
            // Position in bottom right corner
            var txWindowWidth = 600f;
            var txWindowHeight = 300f;
            
            // Bottom right positioning
            var txWindowX = UI.screenWidth - txWindowWidth - 20f; // 20px from right edge
            var txWindowY = UI.screenHeight - txWindowHeight - 23f; // 20px from bottom edge (orignally)
            var txRect = new Rect(txWindowX, txWindowY, txWindowWidth, txWindowHeight);
            
            // Background
            var oldColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.95f);
            GUI.DrawTexture(txRect, BaseContent.WhiteTex);
            
            // Border
            GUI.color = Color.yellow;
            Widgets.DrawBox(txRect, 3);
            GUI.color = oldColor;
            
            var innerRect = txRect.ContractedBy(15f);
            float y = innerRect.y;
            
            // Header - simplified
            GUI.color = Color.yellow;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            
            var headerText = $"💰 TRANSACTIONS ({lastTransactionIds.Count})";
            var headerRect = new Rect(innerRect.x, y, innerRect.width, 30f);
            Widgets.Label(headerRect, headerText);
            y += 40f;
            
            // Subtitle
            Text.Font = GameFont.Small;
            GUI.color = Color.cyan;
            var subtitleText = "Complete Transaction Signatures";
            var subtitleRect = new Rect(innerRect.x, y, innerRect.width, 20f);
            Widgets.Label(subtitleRect, subtitleText);
            y += 30f;
            
            // Transaction list with better labels
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            
            for (int i = 0; i < lastTransactionIds.Count; i++)
            {
                var txid = lastTransactionIds[i];
                
                // Use smart labeling based on position and count
                string label;
                if (i == 0 && lastTransactionIds.Count > 1)
                {
                    label = "Claim Fees:";
                }
                else if (i > 0)
                {
                    label = $"Payout Batch {i}:";
                }
                else
                {
                    label = "Payment:"; // Single transaction case
                }
                
                // Transaction label
                var txLabelRect = new Rect(innerRect.x, y, 100f, 20f);
                GUI.color = Color.yellow;
                Widgets.Label(txLabelRect, label);
                
                // Full transaction address (copyable)
                var txAddressRect = new Rect(innerRect.x + 105f, y, innerRect.width - 105f, 20f);
                
                // Alternate background for readability
                if (i % 2 == 0)
                {
                    GUI.color = new Color(0.1f, 0.1f, 0.1f, 0.5f);
                    GUI.DrawTexture(txAddressRect.ExpandedBy(2f), BaseContent.WhiteTex);
                }
                
                GUI.color = Color.white;
                Widgets.Label(txAddressRect, txid);
                
                y += 25f;
                
                // Add some spacing between transactions
                if (i < lastTransactionIds.Count - 1)
                {
                    y += 5f;
                }
            }
            
            // Instructions at bottom
            y += 15f;
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Text.Anchor = TextAnchor.MiddleCenter;
            
            var instructionText = "Copy these signatures to verify payments on Solscan.io";
            var instructionRect = new Rect(innerRect.x, y, innerRect.width, 18f);
            Widgets.Label(instructionRect, instructionText);
            
            y += 20f;
            var urlText = "https://solscan.io";
            var urlRect = new Rect(innerRect.x, y, innerRect.width, 18f);
            GUI.color = Color.cyan;
            Widgets.Label(urlRect, urlText);
            
            // Reset text settings
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        // ENHANCED: Winner celebration with tier analysis
        private static void DrawWinnerCelebration(MapComponent_SolWorldArena arenaComp, RoundRoster roster)
        {
            // PRESERVED: Use persistent winner storage instead of roster
            if (!arenaComp.LastRoundWinner.HasValue) return;
            
            // Draw main winner window (make it slightly narrower)
            DrawMainWinnerWindow(arenaComp);
            
            // Draw transaction window to the right
            DrawTransactionWindow(arenaComp);
        }

        private static void DrawMainWinnerWindow(MapComponent_SolWorldArena arenaComp)
        {
            // PRESERVED: Calculate dimensions for winner display - back to original center position
            var totalWidth = 800f; // Back to original width
            var totalHeight = 500f;
            
            var centerX = UI.screenWidth / 2f;
            var topY = 15f;
            
            // Centered positioning (original location)
            var rect = new Rect(centerX - totalWidth / 2f, topY, totalWidth, totalHeight);
            
            // PRESERVED: Enhanced celebration background
            var oldColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.95f);
            GUI.DrawTexture(rect, BaseContent.WhiteTex);
            
            // PRESERVED: Winner-themed border
            var winnerColor = arenaComp.LastRoundWinner == TeamColor.Red ? Color.red : Color.blue;
            GUI.color = winnerColor;
            Widgets.DrawBox(rect, 4);
            GUI.color = oldColor;
            
            var innerRect = rect.ContractedBy(25f);
            float y = innerRect.y;
            
            // Clean celebration display
            DrawWinnerHeaderClean(arenaComp, innerRect, ref y, winnerColor);
            DrawWinnerCountdown(arenaComp, innerRect, ref y);
            DrawWinningWalletsWithTiers(arenaComp, innerRect, y, winnerColor);
            
            // Reset styling
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }
        
        private static List<string> lastTransactionIds = new List<string>();

        // Add this method to update the transaction IDs when a round ends
        public static void SetLastTransactionIds(List<string> txids)

        {
            lastTransactionIds.Clear();
            if (txids != null)
            {
                lastTransactionIds.AddRange(txids);
            }
        }

        private static void DrawWinnerHeaderClean(MapComponent_SolWorldArena arenaComp, Rect innerRect, ref float y, Color winnerColor)
        {
            // PRESERVED: Main celebration title
            GUI.color = Color.yellow;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            
            var titleText = $"🏆 {arenaComp.LastRoundWinner.ToString().ToUpper()} TEAM WINS! 🏆";
            var titleRect = new Rect(innerRect.x, y, innerRect.width, 40f);
            
            // PRESERVED: Flashing effect for celebration
            var flash = Mathf.Sin(Time.realtimeSinceStartup * 6f) > 0f;
            GUI.color = flash ? Color.yellow : winnerColor;
            
            Widgets.Label(titleRect, titleText);
            y += 45f;
            
            // PRESERVED: Prize information
            Text.Font = GameFont.Medium;
            GUI.color = Color.white;
            
            var prizeText = $"Each Winner Receives: {arenaComp.LastPerWinnerPayout:F3} SOL";
            var prizeRect = new Rect(innerRect.x, y, innerRect.width, 30f);
            Widgets.Label(prizeRect, prizeText);
            y += 35f;
            
            // PRESERVED: Pool information (calculate from winner payout)
            Text.Font = GameFont.Small;
            GUI.color = Color.cyan;
            
            var totalPool = arenaComp.LastPerWinnerPayout * 10f / 0.20f; // Reverse calculate pool
            var poolText = $"Total Prize Pool: {totalPool:F2} SOL | Match: {arenaComp.LastMatchId}";
            var poolRect = new Rect(innerRect.x, y, innerRect.width, 25f);
            Widgets.Label(poolRect, poolText);
            y += 30f;
            
            Text.Anchor = TextAnchor.UpperLeft;
        }

        // Helper method - add this if it doesn't exist
        private static string ShortenTransactionId(string txid)
        {
            if (string.IsNullOrEmpty(txid) || txid.Length < 16)
                return txid;
            
            // Show first 8 and last 8 characters
            return $"{txid.Substring(0, 8)}...{txid.Substring(txid.Length - 8)}";
        }
    
        // PRESERVED: Winner countdown
        private static void DrawWinnerCountdown(MapComponent_SolWorldArena arenaComp, Rect innerRect, ref float y)
        {
            var timeUntilNext = arenaComp.GetTimeUntilNextRound();
            var minutes = timeUntilNext / 60;
            var seconds = timeUntilNext % 60;
            
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = Color.green;
            
            var countdownText = $"⏰ Next Round: {minutes:F0}:{seconds:D2}";
            var countdownRect = new Rect(innerRect.x, y, innerRect.width, 30f);
            Widgets.Label(countdownRect, countdownText);
            y += 40f;
            
            Text.Anchor = TextAnchor.UpperLeft;
        }
        
        // NEW: Enhanced winning wallets display with tier information
        private static void DrawWinningWalletsWithTiers(MapComponent_SolWorldArena arenaComp, Rect innerRect, float startY, Color winnerColor)
        {
            var winningTeam = arenaComp.LastWinningTeam;
            if (winningTeam == null || winningTeam.Count == 0) return;
            
            // PRESERVED: Section header
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = winnerColor;
            
            var headerText = $"WINNING WALLETS ({winningTeam.Count}):";
            var headerRect = new Rect(innerRect.x, startY, innerRect.width, 25f);
            Widgets.Label(headerRect, headerText);
            startY += 35f;
            
            // NEW: Show tier distribution first
            var tierStats = AnalyzeWinningTeamTiers(winningTeam, arenaComp.currentRoundTierData);
            if (tierStats.Count > 0)
            {
                Text.Font = GameFont.Small;
                GUI.color = Color.cyan;
                
                var tierText = "Tier Distribution: " + string.Join(", ", tierStats.OrderByDescending(kvp => kvp.Key)
                    .Select(kvp => $"T{kvp.Key}({kvp.Value})"));
                var tierRect = new Rect(innerRect.x, startY, innerRect.width, 20f);
                Widgets.Label(tierRect, tierText);
                startY += 25f;
            }
            
            // PRESERVED: 2 columns layout
            const float walletBoxWidth = 250f; // Wider for 2 columns
            const float walletBoxHeight = 35f; // Slightly taller for tier info
            const float walletSpacing = 10f;
            const int walletsPerRow = 2; // 2 columns of wallets
            
            var totalWalletWidth = (walletBoxWidth * walletsPerRow) + (walletSpacing * (walletsPerRow - 1));
            var walletStartX = innerRect.x + (innerRect.width - totalWalletWidth) / 2f;
            
            // ENHANCED: Draw each winning wallet with tier info
            for (int i = 0; i < winningTeam.Count; i++)
            {
                var fighter = winningTeam[i];
                
                // PRESERVED: Calculate position (2 columns, 5 rows)
                var row = i / walletsPerRow;
                var col = i % walletsPerRow;
                
                var walletX = walletStartX + col * (walletBoxWidth + walletSpacing);
                var walletY = startY + row * (walletBoxHeight + walletSpacing);
                
                var walletRect = new Rect(walletX, walletY, walletBoxWidth, walletBoxHeight);
                
                DrawWinnerWalletBoxWithTier(walletRect, fighter, winnerColor, arenaComp.currentRoundTierData);
            }
        }
        
        // ENHANCED: Winner wallet box with tier information and visual effects
        private static void DrawWinnerWalletBoxWithTier(Rect rect, Fighter fighter, Color teamColor, System.Collections.Generic.Dictionary<string, MapComponent_SolWorldArena.TieredFighter> tierData)
        {
            try
            {
                var oldColor = GUI.color;
                
                // Get tier info if available
                var tierInfo = tierData?.ContainsKey(fighter.WalletFull) == true ? tierData[fighter.WalletFull] : null;
                var tierLevel = tierInfo?.Tier ?? 1;
                
                // Bright solid tier colors (matching leaderboard)
                Color solidTierColor;
                switch (tierLevel)
                {
                    case 1: solidTierColor = new Color(0.6f, 0.6f, 0.6f, 1.0f); break; // Gray
                    case 2: solidTierColor = new Color(0.2f, 0.8f, 0.2f, 1.0f); break; // Green
                    case 3: solidTierColor = new Color(0.2f, 0.5f, 1.0f, 1.0f); break; // Blue
                    case 4: solidTierColor = new Color(0.7f, 0.2f, 1.0f, 1.0f); break; // Purple
                    case 5: solidTierColor = new Color(1.0f, 0.6f, 0.1f, 1.0f); break; // Orange
                    case 6: solidTierColor = new Color(1.0f, 0.2f, 0.6f, 1.0f); break; // Pink
                    case 7: solidTierColor = new Color(1.0f, 0.9f, 0.1f, 1.0f); break; // Gold
                    default: solidTierColor = new Color(0.6f, 0.6f, 0.6f, 1.0f); break;
                }
                
                // SUBTLE glow only for T7 winners
                if (tierLevel >= 7)
                {
                    var subtleGlow = 0.3f + 0.1f * Mathf.Sin(glowPulseTime * 1f);
                    GUI.color = new Color(solidTierColor.r, solidTierColor.g, solidTierColor.b, subtleGlow);
                    GUI.DrawTexture(rect.ExpandedBy(2f), BaseContent.WhiteTex);
                }
                
                // Winner box background
                GUI.color = new Color(teamColor.r, teamColor.g, teamColor.b, 0.3f);
                GUI.DrawTexture(rect, BaseContent.WhiteTex);
                
                // Winner box border
                GUI.color = teamColor;
                Widgets.DrawBox(rect, 2);
                
                // Tier square with solid color and number (consistent with leaderboard)
                var iconRect = new Rect(rect.x + 5f, rect.y + 5f, 24f, 24f);
                
                // Solid tier color fill
                GUI.color = solidTierColor;
                GUI.DrawTexture(iconRect, BaseContent.WhiteTex);
                
                // White border
                GUI.color = Color.white;
                Widgets.DrawBox(iconRect, 1);
                
                // White tier number
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(iconRect, tierLevel.ToString());
                
                // Wallet address (bigger since we removed tier text)
                var walletText = "..." + GetLast6Characters(fighter.WalletShort);
                var walletRect = new Rect(rect.x + 35f, rect.y + 8f, rect.width - 40f, 24f); // Centered vertically
                
                GUI.color = Color.white;
                Text.Font = GameFont.Small; // Bigger font
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(walletRect, walletText);
                
                // REMOVED: Redundant "Tier X" text since we have the tier square with number
                
                // Tooltip
                if (Mouse.IsOver(rect))
                {
                    var tooltip = BuildWinnerTooltipWithTier(fighter, tierInfo);
                    TooltipHandler.TipRegion(rect, tooltip);
                }
                
                GUI.color = oldColor;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
            }
            catch (System.Exception ex)
            {
                if (Prefs.DevMode)
                {
                    Log.Warning($"SolWorld: Winner wallet box draw error: {ex.Message}");
                }
            }
        }
        
        // PRESERVED: All standard leaderboard functionality
        private static void DrawStandardLeaderboard(MapComponent_SolWorldArena arenaComp, RoundRoster roster)
        {
            // PRESERVED: Calculate dimensions based on whether we have a roster
            const float pawnBoxSize = 56f;
            const float pawnBoxSpacing = 6f;
            const float teamSeparation = 80f;
            
            float pawnAreaWidth = 600f; // Default width when no roster
            if (roster != null)
            {
                var redTeamWidth = (pawnBoxSize + pawnBoxSpacing) * roster.Red.Count - pawnBoxSpacing;
                var blueTeamWidth = (pawnBoxSize + pawnBoxSpacing) * roster.Blue.Count - pawnBoxSpacing;
                pawnAreaWidth = redTeamWidth + teamSeparation + blueTeamWidth;
            }
            
            // PRESERVED: Calculate total dimensions
            var totalWidth = Mathf.Max(700f, pawnAreaWidth + 20f);
            var totalHeight = roster != null ? 220f : 180f; // PRESERVED: From 280f to 240f
            
            var centerX = UI.screenWidth / 2f;
            var topY = 15f;
            
            var rect = new Rect(centerX - totalWidth / 2f, topY, totalWidth, totalHeight);
            
            // PRESERVED: Standard background
            var oldColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.9f);
            GUI.DrawTexture(rect, BaseContent.WhiteTex);
            
            // PRESERVED: Standard border
            GUI.color = Color.white;
            Widgets.DrawBox(rect, 3);
            GUI.color = oldColor;
            
            var innerRect = rect.ContractedBy(20f);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            
            float y = innerRect.y;
            float lineHeight = 24f;
            
            // PRESERVED: Standard header
            GUI.color = Color.yellow;
            Text.Font = GameFont.Medium;
            var headerRect = new Rect(innerRect.x, y, innerRect.width, 32f);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(headerRect, "SolWorld Arena - Live Combat Dashboard");
            Text.Anchor = TextAnchor.UpperLeft;
            y += 36f;
            
            // PRESERVED: MAIN TIMER DISPLAY
            DrawMainTimer(arenaComp, roster, innerRect, ref y);
            
            // PRESERVED: Match info only if we have a roster
            if (roster != null)
            {
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
                var matchInfoRect = new Rect(innerRect.x, y, innerRect.width, lineHeight);
                Text.Anchor = TextAnchor.MiddleCenter;
                
                // PRESERVED: Show loadout info if available
                var loadoutInfo = "";
                if (!string.IsNullOrEmpty(roster.LoadoutPresetName))
                {
                    loadoutInfo = $" | Loadout: {roster.LoadoutPresetName}";
                }
                
                var matchText = $"Match: {roster.MatchId} | Pool: {roster.RoundRewardTotalSol:F2} SOL | Per Winner: {roster.PerWinnerPayout:F3} SOL{loadoutInfo}";
                Widgets.Label(matchInfoRect, matchText);
                Text.Anchor = TextAnchor.UpperLeft;
                y += lineHeight + 10f;
                
                // ENHANCED: Team displays with tier information
                DrawIntegratedTeamDisplaysWithTiers(roster, arenaComp, innerRect, y, pawnBoxSize, pawnBoxSpacing, teamSeparation);
            }
            else
            {
                // PRESERVED: Show arena status when no active roster
                Text.Font = GameFont.Small;
                GUI.color = Color.cyan;
                var statusRect = new Rect(innerRect.x, y, innerRect.width, lineHeight);
                Text.Anchor = TextAnchor.MiddleCenter;
                
                string statusText = "Arena Active - Waiting for fighters...";
                switch (arenaComp.CurrentState)
                {
                    case ArenaState.Idle:
                        var nextRoundTime = arenaComp.GetTimeUntilNextRound();
                        if (nextRoundTime > 0)
                        {
                            var minutes = nextRoundTime / 60;
                            var seconds = nextRoundTime % 60;
                            statusText = $"⏰ NEXT ROUND: {minutes:F0}:{seconds:D2}";
                        }
                        else
                        {
                            statusText = "✅ STARTING NEW ROUND...";
                        }
                        break;
                    case ArenaState.Resetting:
                        statusText = "⚙️ RESETTING ARENA...";
                        break;
                    case ArenaState.Ended:
                        statusText = "🏆 ROUND COMPLETE - PREPARING RESET...";
                        break;
                }
                
                Widgets.Label(statusRect, statusText);
                Text.Anchor = TextAnchor.UpperLeft;
            }
            
            // PRESERVED: Reset styling
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }
        
        // ALL PRESERVED METHODS BELOW - maintaining exact original functionality
        
        // PRESERVED: Handle preview countdown that works during pause
        private static void HandlePreviewCountdown(MapComponent_SolWorldArena arenaComp)
        {
            if (!arenaComp.IsPreviewActive)
            {
                lastFrameWasPreview = false;
                return;
            }
            
            // We're in preview mode - check the countdown
            var timeRemaining = arenaComp.PreviewTimeRemaining;
            
            // Log every second for debugging
            if (Mathf.Floor(timeRemaining) != Mathf.Floor(lastPreviewTimeCheck))
            {
                Log.Message($"SolWorld: Preview countdown: {timeRemaining:F1} seconds remaining");
            }
            lastPreviewTimeCheck = timeRemaining;
            
            // PRESERVED: When countdown reaches zero, flag for Arena Core to handle
            if (timeRemaining <= 0f && lastFrameWasPreview)
            {
                Log.Message("SolWorld: ===== UI COUNTDOWN COMPLETE - FLAGGING FOR AUTO-UNPAUSE =====");
                arenaComp.RequestCombatTransition(); // This sets the flag for Arena Core to see
                lastFrameWasPreview = false;
                return;
            }
            
            lastFrameWasPreview = true;
        }
        
        // PRESERVED: Handle combat countdown and auto-end
        private static void HandleCombatCountdown(MapComponent_SolWorldArena arenaComp)
        {
            if (arenaComp.CurrentState != ArenaState.Combat)
            {
                return;
            }
            
            var timeRemaining = arenaComp.GetTimeLeftInCurrentPhase();
            
            // Log combat time every 10 seconds for performance
            if (Mathf.Floor(timeRemaining / 10f) != Mathf.Floor(lastPreviewTimeCheck / 10f))
            {
                Log.Message($"SolWorld: Combat time remaining: {timeRemaining:F0} seconds");
                lastPreviewTimeCheck = timeRemaining; // Reuse existing field
            }
        }
        
        // PRESERVED: Handle next round countdown and auto-reset trigger
        private static void HandleNextRoundCountdown(MapComponent_SolWorldArena arenaComp)
        {
            if (arenaComp.CurrentState != ArenaState.Idle)
            {
                return;
            }
            
            var timeUntilNext = arenaComp.GetTimeUntilNextRound();
            
            // Log countdown every 30 seconds for performance
            if (Mathf.Floor(timeUntilNext / 30f) != Mathf.Floor(lastPreviewTimeCheck / 30f))
            {
                Log.Message($"SolWorld: Next round in: {timeUntilNext} seconds");
                lastPreviewTimeCheck = timeUntilNext; // Reuse existing field
            }
        }
        
        // PRESERVED: Main timer that changes based on current phase and handles null roster
        private static void DrawMainTimer(MapComponent_SolWorldArena arenaComp, RoundRoster roster, Rect innerRect, ref float y)
        {
            string timerText = "";
            Color timerColor = Color.white;
            bool shouldFlash = false;
            
            switch (arenaComp.CurrentState)
            {
                case ArenaState.Preview:
                    var previewTime = arenaComp.PreviewTimeRemaining;
                    timerText = $"PREVIEW: {previewTime:F0}s (PAUSED)";
                    timerColor = previewTime <= 5f ? Color.red : Color.cyan;
                    shouldFlash = previewTime <= 10f;
                    break;
                    
                case ArenaState.Combat:
                    var combatTime = arenaComp.GetTimeLeftInCurrentPhase();
                    timerText = $"COMBAT: {combatTime:F0}s REMAINING";
                    timerColor = combatTime <= 10f ? Color.red : Color.yellow;
                    shouldFlash = combatTime <= 5f;
                    break;
                    
                case ArenaState.Ended:
                    // PRESERVED: Keep leaderboard visible and show next round countdown
                    var nextRoundTime = arenaComp.GetTimeUntilNextRound();
                    if (roster?.Winner.HasValue == true)
                    {
                        if (nextRoundTime > 0)
                        {
                            var minutes = nextRoundTime / 60;
                            var seconds = nextRoundTime % 60;
                            timerText = $"🏆 {roster.Winner} TEAM WINS! 🏆 | NEXT ROUND: {minutes:F0}:{seconds:D2}";
                            timerColor = roster.Winner == TeamColor.Red ? Color.red : Color.blue;
                        }
                        else
                        {
                            timerText = $"🏆 {roster.Winner} TEAM WINS! 🏆 | RESETTING...";
                            timerColor = roster.Winner == TeamColor.Red ? Color.red : Color.blue;
                        }
                        shouldFlash = true;
                    }
                    else
                    {
                        timerText = nextRoundTime > 0 ? $"ROUND COMPLETE | NEXT ROUND: {nextRoundTime / 60:F0}:{nextRoundTime % 60:D2}" : "ROUND COMPLETE | RESETTING...";
                        timerColor = Color.green;
                    }
                    break;
                    
                case ArenaState.Resetting:
                    var resetTime = arenaComp.GetTimeUntilNextRound();
                    if (resetTime > 0)
                    {
                        var minutes = resetTime / 60;
                        var seconds = resetTime % 60;
                        timerText = $"⚙️ RESETTING ARENA... | NEXT ROUND: {minutes:F0}:{seconds:D2}";
                    }
                    else
                    {
                        timerText = "⚙️ RESETTING ARENA...";
                    }
                    timerColor = Color.cyan;
                    break;
                    
                case ArenaState.Idle:
                    var idleTime = arenaComp.GetTimeUntilNextRound();
                    if (idleTime > 0)
                    {
                        var minutes = idleTime / 60;
                        var seconds = idleTime % 60;
                        timerText = $"⏰ NEXT ROUND: {minutes:F0}:{seconds:D2}";
                        timerColor = idleTime <= 30 ? Color.yellow : Color.white;
                        shouldFlash = idleTime <= 10;
                    }
                    else
                    {
                        timerText = "✅ ARENA READY - STARTING SOON";
                        timerColor = Color.green;
                        shouldFlash = true;
                    }
                    break;
            }
            
            if (!string.IsNullOrEmpty(timerText))
            {
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleCenter;
                
                var timerRect = new Rect(innerRect.x, y, innerRect.width, 36f);
                
                // Optional flashing effect
                if (shouldFlash)
                {
                    var flash = Mathf.Sin(Time.realtimeSinceStartup * 8f) > 0f;
                    GUI.color = flash ? timerColor : Color.white;
                }
                else
                {
                    GUI.color = timerColor;
                }
                
                Widgets.Label(timerRect, timerText);
                
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                y += 40f;
            }
        }
        
        

        // ENHANCED: Team displays with tier information
        private static void DrawIntegratedTeamDisplaysWithTiers(RoundRoster roster, MapComponent_SolWorldArena arenaComp, Rect innerRect, float startY, float pawnBoxSize, float pawnBoxSpacing, float teamSeparation)
        {
            if (roster?.Red == null || roster?.Blue == null) return;
            
            var redTeamWidth = (pawnBoxSize + pawnBoxSpacing) * roster.Red.Count - pawnBoxSpacing;
            var blueTeamWidth = (pawnBoxSize + pawnBoxSpacing) * roster.Blue.Count - pawnBoxSpacing;
            var totalPawnWidth = redTeamWidth + teamSeparation + blueTeamWidth;
            
            var startX = innerRect.x + (innerRect.width - totalPawnWidth) / 2f;
            
            // PRESERVED: Team headers with enhanced stats
            var headerY = startY;
            DrawTeamHeader(roster.Red, TeamColor.Red, startX, headerY, redTeamWidth);
            var blueStartX = startX + redTeamWidth + teamSeparation;
            DrawTeamHeader(roster.Blue, TeamColor.Blue, blueStartX, headerY, blueTeamWidth);
            
            // ENHANCED: Pawn squares below headers with tier visualization
            var pawnY = startY + 25f;
            DrawTeamPawnSquaresWithTiers(roster.Red, TeamColor.Red, startX, pawnY, pawnBoxSize, pawnBoxSpacing, arenaComp.currentRoundTierData);
            DrawTeamPawnSquaresWithTiers(roster.Blue, TeamColor.Blue, blueStartX, pawnY, pawnBoxSize, pawnBoxSpacing, arenaComp.currentRoundTierData);
            
            // ENHANCED: VS indicator in the middle with bold styling and better positioning
            var vsRect = new Rect(startX + redTeamWidth + 10f, pawnY + pawnBoxSize / 2f - 15f, teamSeparation - 20f, 30f); // Increased height and moved up
            var oldColor = GUI.color;
            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;
            
            // Create bold effect for VS text
            GUI.color = Color.yellow;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            
            // Draw bold outline (multiple passes for bold effect)
            var boldVsRect1 = new Rect(vsRect.x - 1f, vsRect.y, vsRect.width, vsRect.height);
            var boldVsRect2 = new Rect(vsRect.x + 1f, vsRect.y, vsRect.width, vsRect.height);
            var boldVsRect3 = new Rect(vsRect.x, vsRect.y - 1f, vsRect.width, vsRect.height);
            var boldVsRect4 = new Rect(vsRect.x, vsRect.y + 1f, vsRect.width, vsRect.height);
            
            // Slightly darker color for outline
            GUI.color = Color.yellow * 0.8f;
            Widgets.Label(boldVsRect1, "VS");
            Widgets.Label(boldVsRect2, "VS");
            Widgets.Label(boldVsRect3, "VS");
            Widgets.Label(boldVsRect4, "VS");
            
            // Draw main VS text on top
            GUI.color = Color.yellow;
            Widgets.Label(vsRect, "VS");
            
            // Restore original styling
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
            GUI.color = oldColor;
        }
        
        // PRESERVED: Team header functionality
        private static void DrawTeamHeader(System.Collections.Generic.List<Fighter> fighters, TeamColor team, float startX, float y, float width)
        {
            if (fighters == null || fighters.Count == 0) return;
            
            var teamColor = team == TeamColor.Red ? Color.red : Color.blue;
            var oldColor = GUI.color;
            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;
            
            // ENHANCED: Bold and larger font for team headers
            GUI.color = teamColor;
            Text.Font = GameFont.Medium; // Larger font (was GameFont.Small)
            Text.Anchor = TextAnchor.MiddleCenter;
            
            // RAISED: Move the header up by reducing the Y position
            var headerRect = new Rect(startX, y - 10f, width, 30f); // Moved up by 10 pixels
            var aliveCount = fighters.Count(f => f.Alive);
            var teamHeader = $"{team.ToString().ToUpper()} TEAM ({aliveCount}/10)";
            
            // Create bold effect by drawing text multiple times with slight offsets
            var originalColor = GUI.color;
            
            // Draw bold outline (multiple passes for bold effect)
            GUI.color = teamColor * 0.8f; // Slightly darker for outline
            var boldRect1 = new Rect(headerRect.x - 1f, headerRect.y, headerRect.width, headerRect.height);
            var boldRect2 = new Rect(headerRect.x + 1f, headerRect.y, headerRect.width, headerRect.height);
            var boldRect3 = new Rect(headerRect.x, headerRect.y - 1f, headerRect.width, headerRect.height);
            var boldRect4 = new Rect(headerRect.x, headerRect.y + 1f, headerRect.width, headerRect.height);
            
            Widgets.Label(boldRect1, teamHeader);
            Widgets.Label(boldRect2, teamHeader);
            Widgets.Label(boldRect3, teamHeader);
            Widgets.Label(boldRect4, teamHeader);
            
            // Draw main text on top
            GUI.color = originalColor;
            Widgets.Label(headerRect, teamHeader);
            
            // Restore original styling
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
            GUI.color = oldColor;
        }
        
        // ENHANCED: Team pawn squares with tier visualization
        private static void DrawTeamPawnSquaresWithTiers(System.Collections.Generic.List<Fighter> fighters, TeamColor team, float startX, float y, float boxSize, float spacing, System.Collections.Generic.Dictionary<string, MapComponent_SolWorldArena.TieredFighter> tierData)
        {
            if (fighters == null || fighters.Count == 0) return;
            
            var teamColor = team == TeamColor.Red ? Color.red : Color.blue;
            
            // ENHANCED: Individual fighter boxes with tier information
            for (int i = 0; i < fighters.Count; i++)
            {
                var fighter = fighters[i];
                var boxRect = new Rect(
                    startX + i * (boxSize + spacing), 
                    y, 
                    boxSize, 
                    boxSize
                );
                
                DrawEnhancedFighterBoxWithTier(boxRect, fighter, teamColor, tierData);
            }
        }
        
        // ENHANCED: Fighter boxes with full tier visualization including auras
        private static void DrawEnhancedFighterBoxWithTier(Rect rect, Fighter fighter, Color teamColor, System.Collections.Generic.Dictionary<string, MapComponent_SolWorldArena.TieredFighter> tierData)
        {
            try
            {
                // Get tier information
                var tierInfo = tierData?.ContainsKey(fighter.WalletFull) == true ? tierData[fighter.WalletFull] : null;
                var tierLevel = tierInfo?.Tier ?? 1;
                
                var oldColor = GUI.color;
                
                // 1. MAIN FIGHTER BOX
                var bgColor = fighter.Alive ? teamColor : Color.gray;
                bgColor.a = fighter.Alive ? 0.9f : 0.6f;
                
                GUI.color = bgColor;
                GUI.DrawTexture(rect, BaseContent.WhiteTex);
                
                // Box border (red for dead, white for alive)
                GUI.color = fighter.Alive ? Color.white : Color.red;
                Widgets.DrawBox(rect, 2);
                
                // 2. SUBTLE AURAS FOR HIGH TIERS (T6-T7) - ONLY FOR ALIVE FIGHTERS
                if (fighter.Alive && tierLevel >= 6)
                {
                    var auraColor = tierLevel == 7 ? new Color(1f, 0.8f, 0f, 0.3f) : new Color(0f, 1f, 1f, 0.2f); // Gold or Cyan
                    var pulseIntensity = 0.3f + 0.1f * Mathf.Sin(glowPulseTime * 2f);
                    auraColor.a *= pulseIntensity;
                    
                    // Simple 2-layer aura effect
                    for (int i = 0; i < 2; i++)
                    {
                        var expansion = 3f + (i * 2f);
                        var auraRect = rect.ExpandedBy(expansion);
                        var layerAlpha = auraColor.a * (0.8f - i * 0.3f);
                        
                        GUI.color = new Color(auraColor.r, auraColor.g, auraColor.b, layerAlpha);
                        GUI.DrawTexture(auraRect, BaseContent.WhiteTex);
                    }
                }
                
                // 3. TIER SQUARE - ENHANCED DIMMING FOR DEAD FIGHTERS
                var tierSquareSize = 28f;
                var tierRect = new Rect(rect.x + 2f, rect.y + 2f, tierSquareSize, tierSquareSize);
                
                // Bright solid tier colors
                Color solidTierColor;
                switch (tierLevel)
                {
                    case 1: solidTierColor = new Color(0.6f, 0.6f, 0.6f, 1.0f); break; // Gray
                    case 2: solidTierColor = new Color(0.2f, 0.8f, 0.2f, 1.0f); break; // Green
                    case 3: solidTierColor = new Color(0.2f, 0.5f, 1.0f, 1.0f); break; // Blue
                    case 4: solidTierColor = new Color(0.7f, 0.2f, 1.0f, 1.0f); break; // Purple
                    case 5: solidTierColor = new Color(1.0f, 0.6f, 0.1f, 1.0f); break; // Orange
                    case 6: solidTierColor = new Color(1.0f, 0.2f, 0.6f, 1.0f); break; // Pink
                    case 7: solidTierColor = new Color(1.0f, 0.9f, 0.1f, 1.0f); break; // Gold
                    default: solidTierColor = new Color(0.6f, 0.6f, 0.6f, 1.0f); break;
                }
                
                // ENHANCED: Dim tier colors for dead fighters
                if (!fighter.Alive)
                {
                    solidTierColor = Color.Lerp(solidTierColor, Color.gray, 0.7f);
                    solidTierColor.a = 0.5f;
                }
                
                // Fill with solid color (dimmed if dead)
                GUI.color = solidTierColor;
                GUI.DrawTexture(tierRect, BaseContent.WhiteTex);
                
                // Border (red for dead, white for alive)
                GUI.color = fighter.Alive ? Color.white : Color.red;
                Widgets.DrawBox(tierRect, 1);
                
                // Tier number (dimmed for dead fighters)
                GUI.color = fighter.Alive ? Color.white : new Color(0.7f, 0.7f, 0.7f, 0.8f);
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(tierRect, tierLevel.ToString());
                
                // 4. KILL COUNT BADGE (ENHANCED DIMMING FOR DEAD FIGHTERS)
                if (fighter.Kills > 0)
                {
                    var killRect = new Rect(rect.xMax - 22f, rect.y, 22f, 22f);
                    
                    // Dim kill badge for dead fighters
                    if (fighter.Alive)
                    {
                        GUI.color = Color.yellow;
                    }
                    else
                    {
                        GUI.color = new Color(0.6f, 0.6f, 0.2f, 0.7f); // Dimmed yellow
                    }
                    GUI.DrawTexture(killRect, BaseContent.WhiteTex);
                    
                    GUI.color = fighter.Alive ? Color.black : new Color(0.3f, 0.3f, 0.3f, 0.8f);
                    Widgets.DrawBox(killRect, 1);
                    
                    GUI.color = fighter.Alive ? Color.black : new Color(0.5f, 0.5f, 0.5f, 0.9f);
                    Text.Font = GameFont.Small;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(killRect, fighter.Kills.ToString());
                }
                
                // 5. FIGHTER NAME (ENHANCED DIMMING FOR DEAD FIGHTERS)
                if (rect.width >= 50f)
                {
                    var nameRect = new Rect(rect.x, rect.yMax - 18f, rect.width, 16f);
                    var shortName = "..." + GetLast6Characters(fighter.WalletShort);
                    
                    // Semi-transparent background (more transparent for dead)
                    GUI.color = fighter.Alive ? new Color(0f, 0f, 0f, 0.8f) : new Color(0f, 0f, 0f, 0.5f);
                    GUI.DrawTexture(nameRect, BaseContent.WhiteTex);
                    
                    // Dimmed text for dead fighters
                    GUI.color = fighter.Alive ? Color.white : new Color(0.6f, 0.6f, 0.6f, 0.8f);
                    Text.Font = GameFont.Tiny;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(nameRect, shortName);
                }
                
                // 6. TOOLTIP
                if (Mouse.IsOver(rect))
                {
                    var tooltip = BuildEnhancedFighterTooltipWithTier(fighter, tierInfo);
                    TooltipHandler.TipRegion(rect, tooltip);
                }
                
                // Reset GUI state
                GUI.color = oldColor;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
            }
            catch (System.Exception ex)
            {
                if (Prefs.DevMode)
                {
                    Log.Warning($"SolWorld: Fighter box draw error: {ex.Message}");
                }
            }
        }
        
        // PRESERVED: Keep the preview overlay for extra visibility during preview phase
        public static void DrawPreviewOverlay(MapComponent_SolWorldArena arenaComp)
        {
            if (!arenaComp.IsPreviewActive) return;
            
            var timeRemaining = arenaComp.PreviewTimeRemaining;
            
            // PRESERVED: Center screen overlay (below the main leaderboard)
            var centerX = UI.screenWidth / 2f;
            var centerY = UI.screenHeight / 2f + 150f;
            
            // PRESERVED: Large countdown text
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            
            var countdownText = $"PREVIEW: {timeRemaining:F0}";
            var textSize = Text.CalcSize(countdownText);
            var textRect = new Rect(centerX - textSize.x / 2f, centerY - 50f, textSize.x, textSize.y);
            
            // PRESERVED: Flash effect in final seconds
            var oldColor = GUI.color;
            if (timeRemaining <= 5f)
            {
                var flash = Mathf.Sin(Time.realtimeSinceStartup * 10f) > 0f;
                GUI.color = flash ? Color.red : Color.yellow;
            }
            else
            {
                GUI.color = Color.cyan;
            }
            
            // PRESERVED: Semi-transparent background
            var bgRect = textRect.ExpandedBy(20f);
            var bgColor = GUI.color;
            bgColor.a = 0.3f;
            var prevColor = GUI.color;
            GUI.color = bgColor;
            GUI.DrawTexture(bgRect, BaseContent.WhiteTex);
            GUI.color = prevColor;
            
            // PRESERVED: Draw the text
            Widgets.Label(textRect, countdownText);
            
            // PRESERVED: Instructions
            Text.Font = GameFont.Small;
            var instructText = "Game paused for 30-second preview - Combat starting soon!";
            var instructSize = Text.CalcSize(instructText);
            var instructRect = new Rect(centerX - instructSize.x / 2f, centerY, instructSize.x, instructSize.y);
            
            GUI.color = Color.white;
            Widgets.Label(instructRect, instructText);
            
            // PRESERVED: Reset
            GUI.color = oldColor;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }
        
        // HELPER METHODS - All preserved and some enhanced
        
        private static string GetLast6Characters(string wallet)
        {
            if (string.IsNullOrEmpty(wallet) || wallet.Length <= 6)
                return wallet;
            
            return wallet.Substring(wallet.Length - 6);
        }
        
        private static string BuildWinnerTooltip(Fighter fighter)
        {
            var tooltip = $"🏆 WINNER! 🏆\n";
            tooltip += $"Wallet: {fighter.WalletFull}\n";
            tooltip += $"Team: {fighter.Team}\n";
            tooltip += $"Final Kills: {fighter.Kills}\n";
            tooltip += $"Status: VICTORIOUS";
            
            return tooltip;
        }
        
        // NEW: Enhanced winner tooltip with tier info
        private static string BuildWinnerTooltipWithTier(Fighter fighter, MapComponent_SolWorldArena.TieredFighter tierInfo)
        {
            var tooltip = BuildWinnerTooltip(fighter);
            
            if (tierInfo != null)
            {
                tooltip += $"\n\n=== TIER INFORMATION ===";
                tooltip += $"\nTier: {tierInfo.Tier} ({tierInfo.TierName})";
                tooltip += $"\nToken Balance: {tierInfo.Balance:N0}";
                tooltip += $"\nWeapon Quality: {tierInfo.WeaponQuality}";
                tooltip += $"\nArmor: {(tierInfo.HasArmor ? "Yes" : "No")}";
                tooltip += $"\nHelmet: {(tierInfo.HasHelmet ? "Yes" : "No")}";
                tooltip += $"\nAura: {(tierInfo.HasAura ? "Yes" : "No")}";
            }
            
            return tooltip;
        }
        
        // PRESERVED: Enhanced fighter tooltip
        private static string BuildEnhancedFighterTooltip(Fighter fighter)
        {
            var tooltip = $"Fighter: {fighter.WalletFull}\n";
            tooltip += $"Short: {fighter.WalletShort}\n";
            tooltip += $"Team: {fighter.Team}\n";
            tooltip += $"Status: {(fighter.Alive ? "ALIVE & FIGHTING" : "ELIMINATED")}\n";
            tooltip += $"Kills: {fighter.Kills}";
            
            if (fighter.PawnRef != null)
            {
                tooltip += $"\n\nPawn Details:\n";
                tooltip += $"Name: {fighter.PawnRef.Name}";
                
                if (fighter.PawnRef.Spawned && fighter.Alive)
                {
                    tooltip += $"\nPosition: {fighter.PawnRef.Position}";
                    tooltip += $"\nFaction: {fighter.PawnRef.Faction?.Name ?? "None"}";
                    
                    if (fighter.PawnRef.CurJob != null)
                    {
                        tooltip += $"\nCurrent Job: {fighter.PawnRef.CurJob.def.defName}";
                        
                        if (fighter.PawnRef.CurJob.targetA.IsValid && fighter.PawnRef.CurJob.targetA.Thing != null)
                        {
                            tooltip += $"\nTarget: {fighter.PawnRef.CurJob.targetA.Thing.LabelShort}";
                        }
                    }
                    
                    if (fighter.PawnRef.health != null)
                    {
                        var healthPercent = fighter.PawnRef.health.summaryHealth.SummaryHealthPercent;
                        tooltip += $"\nHealth: {healthPercent:P0}";
                    }
                    
                    if (fighter.PawnRef.equipment?.Primary != null)
                    {
                        tooltip += $"\nWeapon: {fighter.PawnRef.equipment.Primary.LabelShort}";
                    }
                }
                else if (!fighter.Alive)
                {
                    tooltip += $"\nStatus: DEAD";
                }
                else
                {
                    tooltip += $"\nStatus: Not spawned";
                }
            }
            else
            {
                tooltip += $"\n\nPawn: Not assigned";
            }
            
            return tooltip;
        }
        
        // NEW: Enhanced fighter tooltip with tier information
        private static string BuildEnhancedFighterTooltipWithTier(Fighter fighter, MapComponent_SolWorldArena.TieredFighter tierInfo)
        {
            var tooltip = BuildEnhancedFighterTooltip(fighter);
            
            if (tierInfo != null)
            {
                tooltip += $"\n\n=== TIER INFORMATION ===";
                tooltip += $"\nTier: {tierInfo.Tier} ({tierInfo.TierName})";
                tooltip += $"\nToken Balance: {tierInfo.Balance:N0}";
                tooltip += $"\nWeapon Quality: {tierInfo.WeaponQuality}";
                tooltip += $"\nEquipment: ";
                var equipment = new System.Collections.Generic.List<string>();
                if (tierInfo.HasArmor) equipment.Add("Armor");
                if (tierInfo.HasHelmet) equipment.Add("Helmet");
                if (tierInfo.HasAura) equipment.Add("Aura");
                tooltip += equipment.Count > 0 ? string.Join(", ", equipment) : "Standard";
            }
            
            return tooltip;
        }
        
        // NEW: Analyze winning team tier distribution
        private static System.Collections.Generic.Dictionary<int, int> AnalyzeWinningTeamTiers(System.Collections.Generic.List<Fighter> winners, System.Collections.Generic.Dictionary<string, MapComponent_SolWorldArena.TieredFighter> tierData)
        {
            var tierCounts = new System.Collections.Generic.Dictionary<int, int>();
            
            if (tierData == null) return tierCounts;
            
            foreach (var winner in winners)
            {
                var tier = tierData.ContainsKey(winner.WalletFull) ? tierData[winner.WalletFull].Tier : 1;
                tierCounts[tier] = tierCounts.ContainsKey(tier) ? tierCounts[tier] + 1 : 1;
            }
            
            return tierCounts;
        }
        
        // NEW: Draw mystical aura effects for T6-T7 fighters
        private static void DrawMysticalAura(Rect rect, int tierLevel, Color tierColor)
        {
            if (tierLevel < 6) return;
            
            var auraColor = tierLevel <= AURA_COLORS.Length ? AURA_COLORS[tierLevel - 1] : Color.clear;
            if (auraColor == Color.clear) return;
            
            // Pulsing aura effect
            var pulseIntensity = 0.5f + 0.3f * Mathf.Sin(glowPulseTime * 3f);
            auraColor.a *= pulseIntensity;
            
            // Multiple aura layers for depth
            for (int i = 0; i < 3; i++)
            {
                var expansion = 3f + (i * 2f);
                var auraRect = rect.ExpandedBy(expansion);
                var layerAlpha = auraColor.a * (0.8f - i * 0.2f);
                
                GUI.color = new Color(auraColor.r, auraColor.g, auraColor.b, layerAlpha);
                GUI.DrawTexture(auraRect, BaseContent.WhiteTex);
            }
            
            // Tier 7 gets additional rotating sparkle effect
            if (tierLevel == 7)
            {
                DrawGodlikeSparkles(rect);
            }
        }
        
        // NEW: Enhanced glow system for T5+ fighters  
        private static void DrawEnhancedGlow(Rect rect, int tierLevel, Color tierColor)
        {
            if (tierLevel < 5) return;
            
            var pulseIntensity = 0.4f + 0.2f * Mathf.Sin(glowPulseTime * 4f);
            var glowAlpha = pulseIntensity * (tierLevel == 7 ? 0.6f : tierLevel == 6 ? 0.5f : 0.3f);
            
            // Multi-layer glow effect
            for (int i = 0; i < 2; i++)
            {
                var expansion = (i == 0) ? 2f : 4f;
                var layerAlpha = glowAlpha * (i == 0 ? 1f : 0.6f);
                var glowRect = rect.ExpandedBy(expansion);
                
                GUI.color = new Color(tierColor.r, tierColor.g, tierColor.b, layerAlpha);
                GUI.DrawTexture(glowRect, BaseContent.WhiteTex);
            }
        }
        
        // NEW: Special sparkle effects for Tier 7 Godlike Destroyers
        private static void DrawGodlikeSparkles(Rect rect)
        {
            var sparkleCount = 4;
            var radius = rect.width * 0.6f;
            var centerX = rect.center.x;
            var centerY = rect.center.y;
            
            for (int i = 0; i < sparkleCount; i++)
            {
                var angle = (auraRotation + i * 90f) * Mathf.Deg2Rad;
                var sparkleX = centerX + Mathf.Cos(angle) * radius;
                var sparkleY = centerY + Mathf.Sin(angle) * radius;
                
                var sparkleRect = new Rect(sparkleX - 3f, sparkleY - 3f, 6f, 6f);
                var sparkleAlpha = 0.7f + 0.3f * Mathf.Sin(glowPulseTime * 5f + i);
                
                GUI.color = new Color(1f, 1f, 0f, sparkleAlpha); // Golden sparkles
                GUI.DrawTexture(sparkleRect, BaseContent.WhiteTex);
                
                // Draw sparkle symbol
                GUI.color = Color.white;
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(sparkleRect, "✦");
            }
        }
        
        // NEW: Winner celebration aura for high-tier winners
        private static void DrawWinnerCelebrationAura(Rect rect, int tierLevel, Color tierColor)
        {
            if (tierLevel < 6) return;
            
            // Winner celebration uses more intense effects
            var celebrationPulse = 0.6f + 0.4f * Mathf.Sin(glowPulseTime * 2f);
            var auraColor = tierLevel == 7 ? 
                new Color(1f, 0.8f, 0f, celebrationPulse) : // Gold for T7
                new Color(0f, 1f, 1f, celebrationPulse);     // Cyan for T6
            
            // Multiple celebration layers
            for (int i = 0; i < 4; i++)
            {
                var expansion = 2f + (i * 1.5f);
                var auraRect = rect.ExpandedBy(expansion);
                var layerAlpha = auraColor.a * (1f - i * 0.2f);
                
                GUI.color = new Color(auraColor.r, auraColor.g, auraColor.b, layerAlpha);
                GUI.DrawTexture(auraRect, BaseContent.WhiteTex);
            }
            
            // Winner sparkle burst for T7
            if (tierLevel == 7)
            {
                var burstCount = 6;
                var burstRadius = rect.width * 0.8f;
                var centerX = rect.center.x;
                var centerY = rect.center.y;
                
                for (int i = 0; i < burstCount; i++)
                {
                    var angle = (glowPulseTime * 50f + i * 60f) * Mathf.Deg2Rad;
                    var burstX = centerX + Mathf.Cos(angle) * burstRadius;
                    var burstY = centerY + Mathf.Sin(angle) * burstRadius;
                    
                    var burstRect = new Rect(burstX - 2f, burstY - 2f, 4f, 4f);
                    var burstAlpha = 0.8f + 0.2f * Mathf.Sin(glowPulseTime * 7f + i);
                    
                    GUI.color = new Color(1f, 1f, 1f, burstAlpha);
                    GUI.DrawTexture(burstRect, BaseContent.WhiteTex);
                }
            }
        }
    }
}