// solworld/SolWorldMod/Source/ScoreboardUI.cs
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace SolWorldMod
{
    public static class ScoreboardUI
    {
        private static bool lastFrameWasPreview = false;
        private static float lastPreviewTimeCheck = 0f;
        
        public static void DrawScoreboard()
        {
            var map = Find.CurrentMap;
            if (map == null) return;
            
            var arenaComp = map.GetComponent<MapComponent_SolWorldArena>();
            
            // FIXED: ALWAYS show scoreboard when arena is active OR has roster data
            if (arenaComp?.IsActive != true && arenaComp?.CurrentRoster == null)
                return;
            
            // Handle phase-specific countdown logic
            HandlePreviewCountdown(arenaComp);
            HandleCombatCountdown(arenaComp);
            HandleNextRoundCountdown(arenaComp);
            
            var roster = arenaComp.CurrentRoster;
            
            // MAIN DISPLAY: Choose display mode based on state and persistent winner data
            if (ShouldShowWinnerCelebration(arenaComp, roster))
            {
                DrawWinnerCelebration(arenaComp, roster);
            }
            else
            {
                DrawStandardLeaderboard(arenaComp, roster);
            }
        }
        
        // UPDATED: Use persistent winner storage instead of roster
        private static bool ShouldShowWinnerCelebration(MapComponent_SolWorldArena arenaComp, RoundRoster roster)
        {
            // Show winner celebration during the first 2 minutes of idle time after a round
            if (arenaComp.CurrentState == ArenaState.Idle && 
                arenaComp.LastRoundWinner.HasValue) // CHANGED: Use persistent storage
            {
                var timeUntilNext = arenaComp.GetTimeUntilNextRound();
                
                // Show winners for first 2 minutes (when timeUntilNext > 60)
                return timeUntilNext > 60;
            }
            
            return false;
        }
        
        // UPDATED: Use persistent winner storage instead of roster
        private static void DrawWinnerCelebration(MapComponent_SolWorldArena arenaComp, RoundRoster roster)
        {
            // CHANGED: Use persistent winner storage instead of roster
            if (!arenaComp.LastRoundWinner.HasValue) return;
            
            // Calculate dimensions for winner display
            var totalWidth = 800f; // Wider for winner celebration
            var totalHeight = 500f; // Taller for winner list
            
            var centerX = UI.screenWidth / 2f;
            var topY = 15f;
            
            var rect = new Rect(centerX - totalWidth / 2f, topY, totalWidth, totalHeight);
            
            // Enhanced celebration background
            var oldColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.95f); // More opaque for celebration
            GUI.DrawTexture(rect, BaseContent.WhiteTex);
            
            // Winner-themed border
            var winnerColor = arenaComp.LastRoundWinner == TeamColor.Red ? Color.red : Color.blue;
            GUI.color = winnerColor;
            Widgets.DrawBox(rect, 4); // Thicker border for celebration
            GUI.color = oldColor;
            
            var innerRect = rect.ContractedBy(25f);
            float y = innerRect.y;
            
            // CELEBRATION HEADER (using persistent storage)
            DrawWinnerHeaderPersistent(arenaComp, innerRect, ref y, winnerColor);
            
            // WINNER COUNTDOWN (always visible)
            DrawWinnerCountdown(arenaComp, innerRect, ref y);
            
            // WINNING WALLETS LIST (using persistent storage)
            DrawWinningWalletsPersistent(arenaComp, innerRect, y, winnerColor);
            
            // Reset styling
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }
        
        // NEW: Draw winner header using persistent storage
        private static void DrawWinnerHeaderPersistent(MapComponent_SolWorldArena arenaComp, Rect innerRect, ref float y, Color winnerColor)
        {
            // Main celebration title
            GUI.color = Color.yellow;
            Text.Font = GameFont.Medium; // FIXED: Use GameFont.Medium instead of Large
            Text.Anchor = TextAnchor.MiddleCenter;
            
            var titleText = $"🏆 {arenaComp.LastRoundWinner.ToString().ToUpper()} TEAM WINS! 🏆";
            var titleRect = new Rect(innerRect.x, y, innerRect.width, 40f);
            
            // Flashing effect for celebration
            var flash = Mathf.Sin(Time.realtimeSinceStartup * 6f) > 0f;
            GUI.color = flash ? Color.yellow : winnerColor;
            
            Widgets.Label(titleRect, titleText);
            y += 45f;
            
            // Prize information
            Text.Font = GameFont.Medium;
            GUI.color = Color.white;
            
            var prizeText = $"Each Winner Receives: {arenaComp.LastPerWinnerPayout:F3} SOL";
            var prizeRect = new Rect(innerRect.x, y, innerRect.width, 30f);
            Widgets.Label(prizeRect, prizeText);
            y += 35f;
            
            // Pool information (calculate from winner payout)
            Text.Font = GameFont.Small;
            GUI.color = Color.cyan;
            
            var totalPool = arenaComp.LastPerWinnerPayout * 10f / 0.20f; // Reverse calculate pool
            var poolText = $"Total Prize Pool: {totalPool:F2} SOL | Match: {arenaComp.LastMatchId}";
            var poolRect = new Rect(innerRect.x, y, innerRect.width, 25f);
            Widgets.Label(poolRect, poolText);
            y += 30f;
            
            Text.Anchor = TextAnchor.UpperLeft;
        }
        
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
        
        // NEW: Draw winning wallets using persistent storage
        private static void DrawWinningWalletsPersistent(MapComponent_SolWorldArena arenaComp, Rect innerRect, float startY, Color winnerColor)
        {
            var winningTeam = arenaComp.LastWinningTeam;
            if (winningTeam == null || winningTeam.Count == 0) return;
            
            // Section header
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = winnerColor;
            
            var headerText = $"WINNING WALLETS ({winningTeam.Count}):";
            var headerRect = new Rect(innerRect.x, startY, innerRect.width, 25f);
            Widgets.Label(headerRect, headerText);
            startY += 35f;
            
            // UPDATED: 2 columns layout
            const float walletBoxWidth = 250f; // Wider for 2 columns
            const float walletBoxHeight = 30f;
            const float walletSpacing = 10f;
            const int walletsPerRow = 2; // 2 columns of wallets
            
            var totalWalletWidth = (walletBoxWidth * walletsPerRow) + (walletSpacing * (walletsPerRow - 1));
            var walletStartX = innerRect.x + (innerRect.width - totalWalletWidth) / 2f;
            
            // Draw each winning wallet
            for (int i = 0; i < winningTeam.Count; i++)
            {
                var fighter = winningTeam[i];
                
                // Calculate position (2 columns, 5 rows)
                var row = i / walletsPerRow;
                var col = i % walletsPerRow;
                
                var walletX = walletStartX + col * (walletBoxWidth + walletSpacing);
                var walletY = startY + row * (walletBoxHeight + walletSpacing);
                
                var walletRect = new Rect(walletX, walletY, walletBoxWidth, walletBoxHeight);
                
                DrawWinnerWalletBox(walletRect, fighter, winnerColor);
            }
        }
        
        private static void DrawWinnerWalletBox(Rect rect, Fighter fighter, Color teamColor)
        {
            try
            {
                var oldColor = GUI.color;
                
                // Winner box background
                GUI.color = new Color(teamColor.r, teamColor.g, teamColor.b, 0.3f);
                GUI.DrawTexture(rect, BaseContent.WhiteTex);
                
                // Winner box border
                GUI.color = teamColor;
                Widgets.DrawBox(rect, 2);
                
                // Trophy icon area
                var trophyRect = new Rect(rect.x + 5f, rect.y + 5f, 20f, 20f);
                GUI.color = Color.yellow;
                GUI.DrawTexture(trophyRect, BaseContent.WhiteTex);
                
                // Draw trophy emoji text
                GUI.color = Color.black;
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(trophyRect, "🏆");
                
                // Wallet address (last 6 characters)
                var walletText = "..." + GetLast6Characters(fighter.WalletShort);
                var walletRect = new Rect(rect.x + 30f, rect.y + 6f, rect.width - 35f, rect.height - 12f); // Better vertical centering

                GUI.color = Color.white;
                Text.Font = GameFont.Tiny; // Change from GameFont.Small to GameFont.Tiny
                Text.Anchor = TextAnchor.MiddleCenter; // Change from MiddleLeft to MiddleCenter
                Widgets.Label(walletRect, walletText);
                
                // Enhanced tooltip for winners
                if (Mouse.IsOver(rect))
                {
                    var tooltip = BuildWinnerTooltip(fighter);
                    TooltipHandler.TipRegion(rect, tooltip);
                }
                
                GUI.color = oldColor;
                Text.Anchor = TextAnchor.UpperLeft;
            }
            catch (System.Exception ex)
            {
                if (Prefs.DevMode)
                {
                    Log.Warning($"SolWorld: Winner wallet box draw error: {ex.Message}");
                }
            }
        }
        
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
        
        // STANDARD LEADERBOARD (used for all non-winner phases)
        private static void DrawStandardLeaderboard(MapComponent_SolWorldArena arenaComp, RoundRoster roster)
        {
            // Calculate dimensions based on whether we have a roster
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
            
            // Calculate total dimensions
            var totalWidth = Mathf.Max(700f, pawnAreaWidth + 20f);
            var totalHeight = roster != null ? 220f : 180f; // REDUCED: From 280f to 240f
            
            var centerX = UI.screenWidth / 2f;
            var topY = 15f;
            
            var rect = new Rect(centerX - totalWidth / 2f, topY, totalWidth, totalHeight);
            
            // Standard background
            var oldColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.9f);
            GUI.DrawTexture(rect, BaseContent.WhiteTex);
            
            // Standard border
            GUI.color = Color.white;
            Widgets.DrawBox(rect, 3);
            GUI.color = oldColor;
            
            var innerRect = rect.ContractedBy(20f);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            
            float y = innerRect.y;
            float lineHeight = 24f;
            
            // Standard header
            GUI.color = Color.yellow;
            Text.Font = GameFont.Medium;
            var headerRect = new Rect(innerRect.x, y, innerRect.width, 32f);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(headerRect, "SolWorld Arena - Live Combat Dashboard");
            Text.Anchor = TextAnchor.UpperLeft;
            y += 36f;
            
            // MAIN TIMER DISPLAY
            DrawMainTimer(arenaComp, roster, innerRect, ref y);
            
            // Match info only if we have a roster
            if (roster != null)
            {
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
                var matchInfoRect = new Rect(innerRect.x, y, innerRect.width, lineHeight);
                Text.Anchor = TextAnchor.MiddleCenter;
                
                // Show loadout info if available
                var loadoutInfo = "";
                if (!string.IsNullOrEmpty(roster.LoadoutPresetName))
                {
                    loadoutInfo = $" | Loadout: {roster.LoadoutPresetName}";
                }
                
                var matchText = $"Match: {roster.MatchId} | Pool: {roster.RoundRewardTotalSol:F2} SOL | Per Winner: {roster.PerWinnerPayout:F3} SOL{loadoutInfo}";
                Widgets.Label(matchInfoRect, matchText);
                Text.Anchor = TextAnchor.UpperLeft;
                y += lineHeight + 10f;
                
                // Team displays
                DrawIntegratedTeamDisplays(roster, innerRect, y, pawnBoxSize, pawnBoxSpacing, teamSeparation);
            }
            else
            {
                // Show arena status when no active roster
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
            
            // Reset styling
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }
        
        // CRITICAL: Handle preview countdown that works during pause
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
            
            // CRITICAL: When countdown reaches zero, flag for Arena Core to handle
            if (timeRemaining <= 0f && lastFrameWasPreview)
            {
                Log.Message("SolWorld: ===== UI COUNTDOWN COMPLETE - FLAGGING FOR AUTO-UNPAUSE =====");
                arenaComp.RequestCombatTransition(); // This sets the flag for Arena Core to see
                lastFrameWasPreview = false;
                return;
            }
            
            lastFrameWasPreview = true;
        }
        
        // Handle combat countdown and auto-end
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
        
        // Handle next round countdown and auto-reset trigger
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
        
        // Main timer that changes based on current phase and handles null roster
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
                    // FIXED: Keep leaderboard visible and show next round countdown
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
        
        // Only draw team displays when roster exists (for standard leaderboard)
        private static void DrawIntegratedTeamDisplays(RoundRoster roster, Rect innerRect, float startY, float pawnBoxSize, float pawnBoxSpacing, float teamSeparation)
        {
            if (roster?.Red == null || roster?.Blue == null) return;
            
            var redTeamWidth = (pawnBoxSize + pawnBoxSpacing) * roster.Red.Count - pawnBoxSpacing;
            var blueTeamWidth = (pawnBoxSize + pawnBoxSpacing) * roster.Blue.Count - pawnBoxSpacing;
            var totalPawnWidth = redTeamWidth + teamSeparation + blueTeamWidth;
            
            var startX = innerRect.x + (innerRect.width - totalPawnWidth) / 2f;
            
            // Team headers with enhanced stats
            var headerY = startY;
            DrawTeamHeader(roster.Red, TeamColor.Red, startX, headerY, redTeamWidth);
            var blueStartX = startX + redTeamWidth + teamSeparation;
            DrawTeamHeader(roster.Blue, TeamColor.Blue, blueStartX, headerY, blueTeamWidth);
            
            // Pawn squares below headers
            var pawnY = startY + 25f;
            DrawTeamPawnSquares(roster.Red, TeamColor.Red, startX, pawnY, pawnBoxSize, pawnBoxSpacing);
            DrawTeamPawnSquares(roster.Blue, TeamColor.Blue, blueStartX, pawnY, pawnBoxSize, pawnBoxSpacing);
            
            // VS indicator in the middle
            var vsRect = new Rect(startX + redTeamWidth + 10f, pawnY + pawnBoxSize / 2f - 10f, teamSeparation - 20f, 20f);
            var oldColor = GUI.color;
            GUI.color = Color.yellow;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(vsRect, "VS");
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = oldColor;
        }
        
        private static void DrawTeamHeader(System.Collections.Generic.List<Fighter> fighters, TeamColor team, float startX, float y, float width)
        {
            if (fighters == null || fighters.Count == 0) return;
            
            var teamColor = team == TeamColor.Red ? Color.red : Color.blue;
            var oldColor = GUI.color;
            GUI.color = teamColor;
            
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            
            var headerRect = new Rect(startX, y, width, 22f);
            var aliveCount = fighters.Count(f => f.Alive);
            var teamHeader = $"{team.ToString().ToUpper()} TEAM ({aliveCount}/10)";
            Widgets.Label(headerRect, teamHeader);
            
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = oldColor;
        }
        
        private static void DrawTeamPawnSquares(System.Collections.Generic.List<Fighter> fighters, TeamColor team, float startX, float y, float boxSize, float spacing)
        {
            if (fighters == null || fighters.Count == 0) return;
            
            var teamColor = team == TeamColor.Red ? Color.red : Color.blue;
            
            // Individual fighter boxes (larger squares)
            for (int i = 0; i < fighters.Count; i++)
            {
                var fighter = fighters[i];
                var boxRect = new Rect(
                    startX + i * (boxSize + spacing), 
                    y, 
                    boxSize, 
                    boxSize
                );
                
                DrawEnhancedFighterBox(boxRect, fighter, teamColor);
            }
        }
        
        // Enhanced fighter boxes with larger size and better detail
        private static void DrawEnhancedFighterBox(Rect rect, Fighter fighter, Color teamColor)
        {
            try
            {
                // Fighter status background with better visual feedback
                var bgColor = fighter.Alive ? teamColor : Color.gray;
                bgColor.a = fighter.Alive ? 0.9f : 0.6f;
                
                var oldColor = GUI.color;
                GUI.color = bgColor;
                GUI.DrawTexture(rect, BaseContent.WhiteTex);
                
                // Enhanced border for alive fighters
                if (fighter.Alive)
                {
                    GUI.color = Color.white;
                    Widgets.DrawBox(rect, 2);
                }
                else
                {
                    // Enhanced death marker
                    GUI.color = Color.red;
                    Widgets.DrawBox(rect, 2);
                    
                    // Draw larger death X
                    var centerX = rect.center.x;
                    var centerY = rect.center.y;
                    var crossSize = rect.width * 0.4f; // Larger cross
                    
                    // Thicker cross lines for death indicator
                    GUI.DrawTexture(new Rect(centerX - crossSize/2, centerY - 2f, crossSize, 4f), BaseContent.WhiteTex);
                    GUI.DrawTexture(new Rect(centerX - 2f, centerY - crossSize/2, 4f, crossSize), BaseContent.WhiteTex);
                }
                
                // Enhanced kill count badge (larger and more visible)
                if (fighter.Kills > 0)
                {
                    var killRect = new Rect(rect.xMax - 22f, rect.y, 22f, 22f); // Larger badge
                    GUI.color = Color.yellow;
                    GUI.DrawTexture(killRect, BaseContent.WhiteTex);
                    
                    // Black border for kill badge
                    GUI.color = Color.black;
                    Widgets.DrawBox(killRect, 1);
                    
                    GUI.color = Color.black;
                    Text.Font = GameFont.Small; // Larger font
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(killRect, fighter.Kills.ToString());
                }
                
                // Fighter name on larger boxes (more visible)
                if (rect.width >= 50f)
                {
                    GUI.color = Color.white;
                    Text.Font = GameFont.Tiny;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    var nameRect = new Rect(rect.x, rect.yMax - 18f, rect.width, 16f); // Larger name area
                    var shortName = "..." + GetLast6Characters(fighter.WalletShort);
                    
                    // Background for name
                    GUI.color = new Color(0f, 0f, 0f, 0.8f);
                    GUI.DrawTexture(nameRect, BaseContent.WhiteTex);
                    GUI.color = Color.white;
                    
                    Widgets.Label(nameRect, shortName);
                }
                
                // Enhanced tooltip with more detailed info
                if (Mouse.IsOver(rect))
                {
                    var tooltip = BuildEnhancedFighterTooltip(fighter);
                    TooltipHandler.TipRegion(rect, tooltip);
                }
                
                GUI.color = oldColor;
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
            }
            catch (System.Exception ex)
            {
                // Ignore drawing errors but log for debugging
                if (Prefs.DevMode)
                {
                    Log.Warning($"SolWorld: Enhanced fighter box draw error: {ex.Message}");
                }
            }
        }
        
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
        
        // Keep the preview overlay for extra visibility during preview phase
        public static void DrawPreviewOverlay(MapComponent_SolWorldArena arenaComp)
        {
            if (!arenaComp.IsPreviewActive) return;
            
            var timeRemaining = arenaComp.PreviewTimeRemaining;
            
            // Center screen overlay (below the main leaderboard)
            var centerX = UI.screenWidth / 2f;
            var centerY = UI.screenHeight / 2f + 150f; // Offset down to avoid leaderboard
            
            // Large countdown text
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            
            var countdownText = $"PREVIEW: {timeRemaining:F0}";
            var textSize = Text.CalcSize(countdownText);
            var textRect = new Rect(centerX - textSize.x / 2f, centerY - 50f, textSize.x, textSize.y);
            
            // Flash effect in final seconds
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
            
            // Semi-transparent background
            var bgRect = textRect.ExpandedBy(20f);
            var bgColor = GUI.color;
            bgColor.a = 0.3f;
            var prevColor = GUI.color;
            GUI.color = bgColor;
            GUI.DrawTexture(bgRect, BaseContent.WhiteTex);
            GUI.color = prevColor;
            
            // Draw the text
            Widgets.Label(textRect, countdownText);
            
            // Instructions
            Text.Font = GameFont.Small;
            var instructText = "Game paused for 30-second preview - Combat starting soon!";
            var instructSize = Text.CalcSize(instructText);
            var instructRect = new Rect(centerX - instructSize.x / 2f, centerY, instructSize.x, instructSize.y);
            
            GUI.color = Color.white;
            Widgets.Label(instructRect, instructText);
            
            // Reset
            GUI.color = oldColor;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }
    }
}