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
            if (arenaComp?.CurrentRoster == null || !arenaComp.IsActive)
                return;
            
            // CRITICAL: Handle preview countdown that works during pause
            HandlePreviewCountdown(arenaComp);
            
            var roster = arenaComp.CurrentRoster;
            
            // Main scoreboard window
            DrawMainScoreboard(arenaComp, roster);
            
            // Team bars (if space allows)
            if (UI.screenWidth > 1200)
            {
                DrawTeamBars(roster);
            }
        }
        
        // NEW: This runs during OnGUI and works even when game is paused
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
            
            // CRITICAL: When countdown reaches zero, trigger combat transition
            if (timeRemaining <= 0f && lastFrameWasPreview)
            {
                Log.Message("SolWorld: ===== UI TRIGGERED COMBAT TRANSITION =====");
                arenaComp.RequestCombatTransition();
                lastFrameWasPreview = false;
                return;
            }
            
            lastFrameWasPreview = true;
        }
        
        private static void DrawMainScoreboard(MapComponent_SolWorldArena arenaComp, RoundRoster roster)
        {
            // Position in top-right corner
            var rect = new Rect(UI.screenWidth - 320f, 10f, 300f, 240f);
            
            // Semi-transparent background
            var oldColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.DrawTexture(rect, BaseContent.WhiteTex);
            GUI.color = oldColor;
            
            var innerRect = rect.ContractedBy(10f);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            
            float y = innerRect.y;
            float lineHeight = 18f;
            
            // Header with enhanced styling
            GUI.color = Color.yellow;
            Text.Font = GameFont.Medium;
            var headerRect = new Rect(innerRect.x, y, innerRect.width, 24f);
            Widgets.Label(headerRect, "SolWorld Arena");
            y += 28f;
            
            // Phase with time - ENHANCED for preview
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            
            string phaseText = "Phase: " + arenaComp.CurrentState;
            var timeLeft = arenaComp.GetTimeLeftInCurrentPhase();
            
            if (arenaComp.CurrentState == ArenaState.Preview)
            {
                // SPECIAL HANDLING: Big countdown for preview
                GUI.color = timeLeft <= 5f ? Color.red : Color.cyan;
                Text.Font = GameFont.Medium;
                phaseText = $"PREVIEW: {timeLeft:F1}s";
                
                if (timeLeft <= 10f)
                {
                    // Flash in final 10 seconds
                    var flash = Mathf.Sin(Time.realtimeSinceStartup * 8f) > 0f;
                    GUI.color = flash ? Color.red : Color.yellow;
                }
            }
            else if (arenaComp.CurrentState != ArenaState.Idle)
            {
                phaseText += " (" + timeLeft.ToString("F0") + "s)";
                GUI.color = arenaComp.CurrentState == ArenaState.Combat ? Color.green : Color.white;
            }
            
            var phaseRect = new Rect(innerRect.x, y, innerRect.width, lineHeight + 4f);
            Widgets.Label(phaseRect, phaseText);
            y += lineHeight + 8f;
            
            // Match ID
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(innerRect.x, y, innerRect.width, 14f), "Match: " + roster.MatchId);
            y += 16f;
            
            // Pool info
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            var poolText = $"Pool: {roster.RoundRewardTotalSol:F2} SOL";
            Widgets.Label(new Rect(innerRect.x, y, innerRect.width, lineHeight), poolText);
            y += lineHeight;
            
            var payoutText = $"Per Winner: {roster.PerWinnerPayout:F3} SOL";
            GUI.color = Color.green;
            Widgets.Label(new Rect(innerRect.x, y, innerRect.width, lineHeight), payoutText);
            y += lineHeight + 5f;
            
            // Team scores with better formatting
            GUI.color = Color.red;
            var redText = $"Red: {roster.RedAlive}/10 alive, {roster.RedKills} kills";
            Widgets.Label(new Rect(innerRect.x, y, innerRect.width, lineHeight), redText);
            y += lineHeight;
            
            GUI.color = Color.blue;
            var blueText = $"Blue: {roster.BlueAlive}/10 alive, {roster.BlueKills} kills";
            Widgets.Label(new Rect(innerRect.x, y, innerRect.width, lineHeight), blueText);
            y += lineHeight + 5f;
            
            // Winner announcement
            if (roster.Winner.HasValue)
            {
                GUI.color = roster.Winner == TeamColor.Red ? Color.red : Color.blue;
                Text.Font = GameFont.Medium;
                var winnerText = roster.Winner + " TEAM WINS!";
                var winnerRect = new Rect(innerRect.x, y, innerRect.width, 24f);
                
                // Flash the winner text
                var flash = Mathf.Sin(Time.realtimeSinceStartup * 6f) > 0f;
                GUI.color = flash ? (roster.Winner == TeamColor.Red ? Color.red : Color.blue) : Color.white;
                
                Widgets.Label(winnerRect, winnerText);
                y += 28f;
            }
            
            // Manual controls for debugging (only during preview/combat)
            if (arenaComp.CurrentState == ArenaState.Preview || arenaComp.CurrentState == ArenaState.Combat)
            {
                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                
                if (arenaComp.CurrentState == ArenaState.Preview)
                {
                    Widgets.Label(new Rect(innerRect.x, y, innerRect.width, 12f), "Press SPACE if stuck");
                    y += 14f;
                }
                
                // Debug info
                var debugText = $"Paused: {Find.TickManager.Paused} | Speed: {Find.TickManager.CurTimeSpeed}";
                Widgets.Label(new Rect(innerRect.x, y, innerRect.width, 12f), debugText);
            }
            
            // Reset styling
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }
        
        private static void DrawTeamBars(RoundRoster roster)
        {
            const float pawnBoxSize = 32f;
            const float pawnBoxSpacing = 2f;
            const float topMargin = 60f;
            
            // Red team (left side)
            DrawTeamBar(roster.Red, TeamColor.Red, true, pawnBoxSize, pawnBoxSpacing, topMargin);
            
            // Blue team (right side)  
            DrawTeamBar(roster.Blue, TeamColor.Blue, false, pawnBoxSize, pawnBoxSpacing, topMargin);
        }
        
        private static void DrawTeamBar(System.Collections.Generic.List<Fighter> fighters, TeamColor team, bool isLeftSide, float boxSize, float spacing, float topMargin)
        {
            if (fighters == null || fighters.Count == 0) return;
            
            var teamColor = team == TeamColor.Red ? Color.red : Color.blue;
            var totalWidth = (boxSize + spacing) * fighters.Count - spacing;
            
            float startX = isLeftSide ? 20f : UI.screenWidth - totalWidth - 20f;
            var startY = topMargin;
            
            // Team header
            var oldColor = GUI.color;
            GUI.color = teamColor;
            
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            
            var headerRect = new Rect(startX, startY, totalWidth, 18f);
            var teamName = team.ToString().ToUpper() + " TEAM";
            Widgets.Label(headerRect, teamName);
            
            // Individual fighter boxes
            for (int i = 0; i < fighters.Count; i++)
            {
                var fighter = fighters[i];
                var boxRect = new Rect(
                    startX + i * (boxSize + spacing), 
                    startY + 22f, 
                    boxSize, 
                    boxSize
                );
                
                DrawFighterBox(boxRect, fighter, teamColor);
            }
            
            // Reset styling
            GUI.color = oldColor;
            Text.Anchor = TextAnchor.UpperLeft;
        }
        
        private static void DrawFighterBox(Rect rect, Fighter fighter, Color teamColor)
        {
            try
            {
                // Fighter status background
                var bgColor = fighter.Alive ? teamColor : Color.gray;
                bgColor.a = fighter.Alive ? 0.8f : 0.4f;
                
                var oldColor = GUI.color;
                GUI.color = bgColor;
                GUI.DrawTexture(rect, BaseContent.WhiteTex);
                
                // Border for alive fighters
                if (fighter.Alive)
                {
                    GUI.color = Color.white;
                    Widgets.DrawBox(rect, 1);
                }
                
                // Kill count badge
                if (fighter.Kills > 0)
                {
                    var killRect = new Rect(rect.xMax - 12f, rect.y, 12f, 12f);
                    GUI.color = Color.yellow;
                    GUI.DrawTexture(killRect, BaseContent.WhiteTex);
                    
                    GUI.color = Color.black;
                    Text.Font = GameFont.Tiny;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(killRect, fighter.Kills.ToString());
                }
                
                // Enhanced tooltip with more info
                if (Mouse.IsOver(rect))
                {
                    var tooltip = BuildFighterTooltip(fighter);
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
                    Log.Warning($"SolWorld: Fighter box draw error: {ex.Message}");
                }
            }
        }
        
        private static string BuildFighterTooltip(Fighter fighter)
        {
            var tooltip = $"Wallet: {fighter.WalletShort}\n";
            tooltip += $"Team: {fighter.Team}\n";
            tooltip += $"Status: {(fighter.Alive ? "ALIVE" : "DEAD")}\n";
            tooltip += $"Kills: {fighter.Kills}";
            
            if (fighter.PawnRef != null)
            {
                tooltip += $"\nPawn: {fighter.PawnRef.Name}";
                if (fighter.PawnRef.Spawned)
                {
                    tooltip += $"\nPosition: {fighter.PawnRef.Position}";
                    
                    if (fighter.PawnRef.CurJob != null)
                    {
                        tooltip += $"\nJob: {fighter.PawnRef.CurJob.def.defName}";
                    }
                    
                    if (fighter.PawnRef.health != null)
                    {
                        var healthPercent = (float)fighter.PawnRef.health.summaryHealth.SummaryHealthPercent;
                        tooltip += $"\nHealth: {healthPercent:P0}";
                    }
                }
                else
                {
                    tooltip += "\nPawn: Not spawned";
                }
            }
            else
            {
                tooltip += "\nPawn: Not assigned";
            }
            
            return tooltip;
        }
        
        // OPTIONAL: Large preview countdown overlay (for extra visibility)
        public static void DrawPreviewOverlay(MapComponent_SolWorldArena arenaComp)
        {
            if (!arenaComp.IsPreviewActive) return;
            
            var timeRemaining = arenaComp.PreviewTimeRemaining;
            
            // Center screen overlay
            var centerX = UI.screenWidth / 2f;
            var centerY = UI.screenHeight / 2f;
            
            // Large countdown text
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            
            var countdownText = $"PREVIEW: {timeRemaining:F0}";
            var textSize = Text.CalcSize(countdownText);
            var textRect = new Rect(centerX - textSize.x / 2f, centerY - 100f, textSize.x, textSize.y);
            
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
            var instructText = "Game paused for 30-second preview";
            var instructSize = Text.CalcSize(instructText);
            var instructRect = new Rect(centerX - instructSize.x / 2f, centerY - 60f, instructSize.x, instructSize.y);
            
            GUI.color = Color.white;
            Widgets.Label(instructRect, instructText);
            
            // Reset
            GUI.color = oldColor;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }
    }
}