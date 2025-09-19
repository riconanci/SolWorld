// solworld/SolWorldMod/Source/TeamSelectionUI.cs
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;
using HarmonyLib;

namespace SolWorldMod
{
    [StaticConstructorOnStartup]
    public static class TeamSelectionUI
    {
        private static readonly Texture2D RedTeamBG = SolidColorMaterials.NewSolidColorTexture(new Color(0.8f, 0.2f, 0.2f, 0.3f));
        private static readonly Texture2D BlueTeamBG = SolidColorMaterials.NewSolidColorTexture(new Color(0.2f, 0.2f, 0.8f, 0.3f));
        private static readonly Texture2D RedTeamBorder = SolidColorMaterials.NewSolidColorTexture(new Color(1f, 0.3f, 0.3f, 0.8f));
        private static readonly Texture2D BlueTeamBorder = SolidColorMaterials.NewSolidColorTexture(new Color(0.3f, 0.3f, 1f, 0.8f));
        
        public static void DrawTeamSelection()
        {
            var map = Find.CurrentMap;
            if (map == null) return;
            
            var arenaComp = map.GetComponent<MapComponent_SolWorldArena>();
            if (arenaComp?.CurrentRoster == null || !arenaComp.IsActive)
                return;
                
            var roster = arenaComp.CurrentRoster;
            
            // Draw team selection bars at the top of the screen like colonist bar
            DrawTeamBar(roster.Red, TeamColor.Red, true);  // Left side
            DrawTeamBar(roster.Blue, TeamColor.Blue, false); // Right side
        }
        
        private static void DrawTeamBar(System.Collections.Generic.List<Fighter> fighters, TeamColor team, bool isLeftSide)
        {
            if (fighters == null || fighters.Count == 0) return;
            
            const float pawnBoxSize = 48f;
            const float pawnBoxSpacing = 2f;
            const float topMargin = 10f;
            
            var teamColor = team == TeamColor.Red ? Color.red : Color.blue;
            var teamBG = team == TeamColor.Red ? RedTeamBG : BlueTeamBG;
            var teamBorder = team == TeamColor.Red ? RedTeamBorder : BlueTeamBorder;
            
            // Calculate total width needed for team
            var totalWidth = (pawnBoxSize + pawnBoxSpacing) * fighters.Count - pawnBoxSpacing;
            
            // Position on screen
            float startX;
            if (isLeftSide)
            {
                startX = 20f; // Left side of screen
            }
            else
            {
                startX = UI.screenWidth - totalWidth - 20f; // Right side of screen
            }
            
            var startY = topMargin;
            
            // Draw team header
            var headerRect = new Rect(startX, startY, totalWidth, 20f);
            GUI.DrawTexture(headerRect, teamBG);
            Widgets.DrawBox(headerRect, 1);
            
            var oldColor = GUI.color;
            GUI.color = teamColor;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            
            var teamName = team.ToString().ToUpper() + " TEAM";
            Widgets.Label(headerRect, teamName);
            
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = oldColor;
            
            // Draw individual pawn boxes
            for (int i = 0; i < fighters.Count; i++)
            {
                var fighter = fighters[i];
                var pawnRect = new Rect(
                    startX + i * (pawnBoxSize + pawnBoxSpacing), 
                    startY + 25f, 
                    pawnBoxSize, 
                    pawnBoxSize
                );
                
                DrawFighterBox(pawnRect, fighter, teamBG, teamBorder, teamColor);
            }
        }
        
        private static void DrawFighterBox(Rect rect, Fighter fighter, Texture2D bgTexture, Texture2D borderTexture, Color teamColor)
        {
            // Background
            GUI.DrawTexture(rect, bgTexture);
            
            // Border - thicker for alive, thin for dead
            var borderWidth = fighter.Alive ? 2 : 1;
            Widgets.DrawBox(rect, borderWidth);
            
            // Pawn portrait or placeholder
            if (fighter.PawnRef?.Spawned == true)
            {
                // Try to draw pawn portrait - simplified version
                var pawn = fighter.PawnRef;
                
                // Draw simple colored square representing pawn
                var innerRect = rect.ContractedBy(4f);
                var pawnColor = fighter.Alive ? teamColor : Color.gray;
                
                var oldColor = GUI.color;
                GUI.color = pawnColor;
                GUI.DrawTexture(innerRect, BaseContent.WhiteTex);
                GUI.color = oldColor;
                
                // Health bar
                if (fighter.Alive && pawn.health != null)
                {
                    var healthRect = new Rect(rect.x + 2f, rect.yMax - 8f, rect.width - 4f, 4f);
                    var healthPct = pawn.health.summaryHealth.SummaryHealthPercent;
                    
                    // Background
                    GUI.color = Color.black;
                    GUI.DrawTexture(healthRect, BaseContent.WhiteTex);
                    
                    // Health bar
                    var healthBarRect = new Rect(healthRect.x, healthRect.y, healthRect.width * healthPct, healthRect.height);
                    GUI.color = Color.Lerp(Color.red, Color.green, healthPct);
                    GUI.DrawTexture(healthBarRect, BaseContent.WhiteTex);
                    
                    GUI.color = Color.white;
                }
                
                // Kill count badge
                if (fighter.Kills > 0)
                {
                    var killRect = new Rect(rect.xMax - 16f, rect.y, 16f, 16f);
                    GUI.color = Color.yellow;
                    GUI.DrawTexture(killRect, BaseContent.WhiteTex);
                    
                    GUI.color = Color.black;
                    Text.Font = GameFont.Tiny;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(killRect, fighter.Kills.ToString());
                    Text.Anchor = TextAnchor.UpperLeft;
                    Text.Font = GameFont.Small;
                    GUI.color = Color.white;
                }
            }
            else
            {
                // Dead or not spawned - draw X
                GUI.color = Color.red;
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(rect, "X");
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }
            
            // Tooltip with fighter info
            if (Mouse.IsOver(rect))
            {
                var tooltip = fighter.WalletShort + "\n" +
                             "Team: " + fighter.Team.ToString() + "\n" +
                             "Kills: " + fighter.Kills.ToString() + "\n" +
                             "Status: " + (fighter.Alive ? "Alive" : "Dead");
                             
                if (fighter.PawnRef?.Spawned == true)
                {
                    var pawn = fighter.PawnRef;
                    var healthPercent = (pawn.health.summaryHealth.SummaryHealthPercent * 100f).ToString("F0");
                    tooltip += "\nHealth: " + healthPercent + "%";
                    
                    if (pawn.CurJob != null)
                    {
                        tooltip += "\nJob: " + pawn.CurJob.def.label;
                    }
                }
                
                TooltipHandler.TipRegion(rect, tooltip);
            }
            
            // Click handling - select pawn if alive
            if (Widgets.ButtonInvisible(rect) && fighter.PawnRef?.Spawned == true && fighter.Alive)
            {
                // Select this pawn
                Find.Selector.ClearSelection();
                Find.Selector.Select(fighter.PawnRef);
                
                // Jump to pawn
                Find.CameraDriver.JumpToCurrentMapLoc(fighter.PawnRef.Position);
            }
        }
    }
    
    // Harmony patch to integrate with the game's UI system
    [HarmonyPatch]
    public static class TeamSelectionUI_Patches
    {
        // Patch MainTabsRoot to draw our team selection
        [HarmonyPatch(typeof(MainTabsRoot), "MainTabsRootOnGUI")]
        [HarmonyPostfix]
        public static void MainTabsRoot_OnGUI_Postfix()
        {
            try
            {
                if (Find.CurrentMap != null && Event.current.type == EventType.Repaint)
                {
                    TeamSelectionUI.DrawTeamSelection();
                }
            }
            catch (System.Exception ex)
            {
                Log.ErrorOnce("SolWorld TeamSelectionUI error: " + ex.Message, 12347);
            }
        }
        
        // Also patch ColonistBar to hide it during arena fights to avoid confusion
        [HarmonyPatch(typeof(ColonistBar), "ColonistBarOnGUI")]
        [HarmonyPrefix]
        public static bool ColonistBar_OnGUI_Prefix()
        {
            try
            {
                var map = Find.CurrentMap;
                if (map != null)
                {
                    var arenaComp = map.GetComponent<MapComponent_SolWorldArena>();
                    if (arenaComp?.CurrentRoster != null && arenaComp.IsActive && 
                        (arenaComp.CurrentState == ArenaState.Preview || arenaComp.CurrentState == ArenaState.Combat))
                    {
                        // Hide colonist bar during arena fights
                        return false;
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.ErrorOnce("SolWorld ColonistBar patch error: " + ex.Message, 12348);
            }
            
            return true; // Allow normal colonist bar
        }
    }
}