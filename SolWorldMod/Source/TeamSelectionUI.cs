// solworld/SolWorldMod/Source/TeamSelectionUI.cs
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace SolWorldMod
{
    public static class TeamSelectionUI
    {
        // Simple UI class - NO Harmony patches to avoid startup errors
        public static void DrawTeamSelection()
        {
            var map = Find.CurrentMap;
            if (map == null) return;
            
            var arenaComp = map.GetComponent<MapComponent_SolWorldArena>();
            if (arenaComp?.CurrentRoster == null || !arenaComp.IsActive)
                return;
                
            var roster = arenaComp.CurrentRoster;
            
            // Only draw during OnGUI context to avoid UI errors
            if (Event.current?.type == EventType.Repaint)
            {
                DrawTeamBar(roster.Red, TeamColor.Red, true);
                DrawTeamBar(roster.Blue, TeamColor.Blue, false);
            }
        }
        
        private static void DrawTeamBar(System.Collections.Generic.List<Fighter> fighters, TeamColor team, bool isLeftSide)
        {
            if (fighters == null || fighters.Count == 0) return;
            
            const float pawnBoxSize = 48f;
            const float pawnBoxSpacing = 2f;
            const float topMargin = 10f;
            
            var teamColor = team == TeamColor.Red ? Color.red : Color.blue;
            
            var totalWidth = (pawnBoxSize + pawnBoxSpacing) * fighters.Count - pawnBoxSpacing;
            
            float startX = isLeftSide ? 20f : UI.screenWidth - totalWidth - 20f;
            var startY = topMargin;
            
            // Team header
            var headerRect = new Rect(startX, startY, totalWidth, 20f);
            
            try
            {
                var oldColor = GUI.color;
                GUI.color = teamColor;
                
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                
                var teamName = team.ToString().ToUpper() + " TEAM";
                Widgets.Label(headerRect, teamName);
                
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = oldColor;
                
                // Individual pawn boxes
                for (int i = 0; i < fighters.Count; i++)
                {
                    var fighter = fighters[i];
                    var pawnRect = new Rect(
                        startX + i * (pawnBoxSize + pawnBoxSpacing), 
                        startY + 25f, 
                        pawnBoxSize, 
                        pawnBoxSize
                    );
                    
                    DrawFighterBox(pawnRect, fighter, teamColor);
                }
            }
            catch
            {
                // Ignore UI drawing errors
            }
        }
        
        private static void DrawFighterBox(Rect rect, Fighter fighter, Color teamColor)
        {
            try
            {
                // Simple colored square
                var pawnColor = fighter.Alive ? teamColor : Color.gray;
                
                var oldColor = GUI.color;
                GUI.color = pawnColor;
                GUI.DrawTexture(rect, BaseContent.WhiteTex);
                GUI.color = oldColor;
                
                // Kill count if any
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
                
                // Simple tooltip
                if (Mouse.IsOver(rect))
                {
                    var tooltip = fighter.WalletShort + "\nTeam: " + fighter.Team + "\nKills: " + fighter.Kills + "\nAlive: " + fighter.Alive;
                    TooltipHandler.TipRegion(rect, tooltip);
                }
            }
            catch
            {
                // Ignore drawing errors
            }
        }
    }
}