// solworld/SolWorldMod/Source/ScoreboardUI.cs
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;
using HarmonyLib;

namespace SolWorldMod
{
    [StaticConstructorOnStartup]
    public static class ScoreboardUI
    {
        private static readonly Texture2D BarFillTex = SolidColorMaterials.NewSolidColorTexture(Color.white);
        
        public static void DrawScoreboard()
        {
            // Only draw during active rounds
            var map = Find.CurrentMap;
            if (map == null) return;
            
            var arenaComp = map.GetComponent<MapComponent_SolWorldArena>();
            if (arenaComp?.CurrentRoster == null || !arenaComp.IsActive)
                return;
                
            var roster = arenaComp.CurrentRoster;
            
            // Position scoreboard in top-right corner
            var rect = new Rect(UI.screenWidth - 420f, 10f, 400f, 350f);
            
            // Background
            Widgets.DrawWindowBackground(rect);
            
            var innerRect = rect.ContractedBy(10f);
            Text.Font = GameFont.Small;
            
            float curY = innerRect.y;
            float lineHeight = 22f;
            
            // Header
            var headerRect = new Rect(innerRect.x, curY, innerRect.width, lineHeight);
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = Color.yellow;
            Widgets.Label(headerRect, $"SolWorld Arena");
            curY += lineHeight;
            
            var matchRect = new Rect(innerRect.x, curY, innerRect.width, lineHeight);
            GUI.color = Color.white;
            Widgets.Label(matchRect, $"Match: {roster.MatchId.Substring(roster.MatchId.Length - 4)}");
            curY += lineHeight + 4f;
            
            Text.Anchor = TextAnchor.MiddleLeft;
            
            // Round info
            var poolRect = new Rect(innerRect.x, curY, innerRect.width, lineHeight);
            Widgets.Label(poolRect, $"Pool: {roster.RoundRewardTotalSol:F2} SOL");
            curY += lineHeight;
            
            var payoutRect = new Rect(innerRect.x, curY, innerRect.width, lineHeight);
            Widgets.Label(payoutRect, $"Per Winner: {roster.PerWinnerPayout:F3} SOL");
            curY += lineHeight + 8f;
            
            // Phase info
            var phaseText = $"Phase: {arenaComp.CurrentState}";
            if (arenaComp.CurrentState != ArenaState.Idle)
            {
                var timeLeft = arenaComp.GetTimeLeftInCurrentPhase();
                phaseText += $" ({timeLeft:F0}s remaining)";
            }
            var phaseRect = new Rect(innerRect.x, curY, innerRect.width, lineHeight);
            Widgets.Label(phaseRect, phaseText);
            curY += lineHeight + 8f;
            
            // Team scores
            var redHeaderRect = new Rect(innerRect.x, curY, innerRect.width / 2f - 5f, lineHeight);
            var blueHeaderRect = new Rect(innerRect.x + innerRect.width / 2f + 5f, curY, innerRect.width / 2f - 5f, lineHeight);
            
            // Red team header
            GUI.color = new Color(1f, 0.3f, 0.3f);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(redHeaderRect, $"RED TEAM");
            
            // Blue team header
            GUI.color = new Color(0.3f, 0.3f, 1f);
            Widgets.Label(blueHeaderRect, $"BLUE TEAM");
            curY += lineHeight;
            
            // Team stats
            GUI.color = Color.red;
            var redStatsRect = new Rect(innerRect.x, curY, innerRect.width / 2f - 5f, lineHeight);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(redStatsRect, $"Alive: {roster.RedAlive}/10");
            
            GUI.color = Color.blue;
            var blueStatsRect = new Rect(innerRect.x + innerRect.width / 2f + 5f, curY, innerRect.width / 2f - 5f, lineHeight);
            Widgets.Label(blueStatsRect, $"Alive: {roster.BlueAlive}/10");
            curY += lineHeight;
            
            // Team kills
            GUI.color = Color.red;
            var redKillsRect = new Rect(innerRect.x, curY, innerRect.width / 2f - 5f, lineHeight);
            Widgets.Label(redKillsRect, $"Kills: {roster.RedKills}");
            
            GUI.color = Color.blue;
            var blueKillsRect = new Rect(innerRect.x + innerRect.width / 2f + 5f, curY, innerRect.width / 2f - 5f, lineHeight);
            Widgets.Label(blueKillsRect, $"Kills: {roster.BlueKills}");
            curY += lineHeight + 8f;
            
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.MiddleLeft;
            
            // Top fighters (if round is active)
            if (roster.IsLive && curY < innerRect.yMax - 50f)
            {
                var topFightersRect = new Rect(innerRect.x, curY, innerRect.width, lineHeight);
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = Color.yellow;
                Widgets.Label(topFightersRect, "Top Killers");
                curY += lineHeight + 4f;
                
                Text.Anchor = TextAnchor.MiddleLeft;
                
                // Show top 3 from each team
                var topRed = roster.Red.Where(f => f.Kills > 0).OrderByDescending(f => f.Kills).Take(3);
                var topBlue = roster.Blue.Where(f => f.Kills > 0).OrderByDescending(f => f.Kills).Take(3);
                
                foreach (var fighter in topRed.Take(2)) // Limit to 2 to save space
                {
                    if (curY >= innerRect.yMax - lineHeight) break;
                    
                    var fighterRect = new Rect(innerRect.x, curY, innerRect.width / 2f - 5f, lineHeight);
                    GUI.color = fighter.Alive ? Color.red : Color.gray;
                    var status = fighter.Alive ? "●" : "○";
                    Widgets.Label(fighterRect, $"{status} {fighter.WalletShort}: {fighter.Kills}");
                    curY += lineHeight;
                }
                
                curY -= lineHeight * 2; // Reset Y to draw blue alongside red
                
                foreach (var fighter in topBlue.Take(2)) // Limit to 2 to save space
                {
                    if (curY >= innerRect.yMax - lineHeight) break;
                    
                    var fighterRect = new Rect(innerRect.x + innerRect.width / 2f + 5f, curY, innerRect.width / 2f - 5f, lineHeight);
                    GUI.color = fighter.Alive ? Color.blue : Color.gray;
                    var status = fighter.Alive ? "●" : "○";
                    Widgets.Label(fighterRect, $"{status} {fighter.WalletShort}: {fighter.Kills}");
                    curY += lineHeight;
                }
                
                curY += lineHeight; // Add some spacing
            }
            
            // Winner display
            if (roster.Winner.HasValue)
            {
                var winnerRect = new Rect(innerRect.x, curY, innerRect.width, lineHeight * 2);
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Medium;
                GUI.color = roster.Winner == TeamColor.Red ? Color.red : Color.blue;
                Widgets.Label(winnerRect, $"{roster.Winner} TEAM WINS!");
                Text.Font = GameFont.Small;
            }
            
            // Reset UI state
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }
    }
    
    // Harmony patch to integrate with the game's UI system
    [HarmonyPatch]
    public static class ScoreboardUI_Patches
    {
        // Try to patch UIRoot's OnGUI method - this should work across RimWorld versions
        [HarmonyPatch(typeof(UIRoot), "UIRootOnGUI")]
        [HarmonyPostfix]
        public static void UIRoot_OnGUI_Postfix()
        {
            try
            {
                if (Find.CurrentMap != null && Event.current.type == EventType.Repaint)
                {
                    ScoreboardUI.DrawScoreboard();
                }
            }
            catch (System.Exception ex)
            {
                // Silently catch and log any UI errors to prevent game crashes
                Log.ErrorOnce($"SolWorld ScoreboardUI error: {ex.Message}", 12345);
            }
        }
        
        // Fallback patch for WindowStack if UIRoot doesn't work
        [HarmonyPatch(typeof(WindowStack), "WindowStackOnGUI")]
        [HarmonyPostfix]
        public static void WindowStack_OnGUI_Postfix()
        {
            try
            {
                if (Find.CurrentMap != null && Event.current.type == EventType.Repaint)
                {
                    ScoreboardUI.DrawScoreboard();
                }
            }
            catch (System.Exception ex)
            {
                Log.ErrorOnce($"SolWorld ScoreboardUI fallback error: {ex.Message}", 12346);
            }
        }
    }
}