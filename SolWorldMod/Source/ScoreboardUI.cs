// solworld/SolWorldMod/Source/ScoreboardUI.cs
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace SolWorldMod
{
    public static class ScoreboardUI
    {
        public static void DrawScoreboard()
        {
            var map = Find.CurrentMap;
            if (map == null) return;
            
            var arenaComp = map.GetComponent<MapComponent_SolWorldArena>();
            if (arenaComp?.CurrentRoster == null || !arenaComp.IsActive)
                return;
                
            var roster = arenaComp.CurrentRoster;
            
            // Simple scoreboard in top-right
            var rect = new Rect(UI.screenWidth - 300f, 10f, 280f, 200f);
            
            Widgets.DrawWindowBackground(rect);
            
            var innerRect = rect.ContractedBy(10f);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            
            float y = innerRect.y;
            float lineHeight = 20f;
            
            // Title
            GUI.color = Color.yellow;
            Widgets.Label(new Rect(innerRect.x, y, innerRect.width, lineHeight), "SolWorld Arena");
            y += lineHeight + 5f;
            
            // Phase
            GUI.color = Color.white;
            var phaseText = "Phase: " + arenaComp.CurrentState;
            if (arenaComp.CurrentState != ArenaState.Idle)
            {
                var timeLeft = arenaComp.GetTimeLeftInCurrentPhase();
                phaseText += " (" + timeLeft.ToString("F0") + "s)";
            }
            Widgets.Label(new Rect(innerRect.x, y, innerRect.width, lineHeight), phaseText);
            y += lineHeight + 5f;
            
            // Teams
            GUI.color = Color.red;
            Widgets.Label(new Rect(innerRect.x, y, innerRect.width, lineHeight), "Red: " + roster.RedAlive + "/10 alive, " + roster.RedKills + " kills");
            y += lineHeight;
            
            GUI.color = Color.blue;
            Widgets.Label(new Rect(innerRect.x, y, innerRect.width, lineHeight), "Blue: " + roster.BlueAlive + "/10 alive, " + roster.BlueKills + " kills");
            y += lineHeight + 5f;
            
            // Winner
            if (roster.Winner.HasValue)
            {
                GUI.color = roster.Winner == TeamColor.Red ? Color.red : Color.blue;
                Widgets.Label(new Rect(innerRect.x, y, innerRect.width, lineHeight), roster.Winner + " TEAM WINS!");
            }
            
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }
    }
}