// solworld/SolWorldMod/Source/Thing_ArenaCore.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace SolWorldMod
{
    public class Thing_ArenaCore : Building
    {
        private CompPowerTrader powerComp;
        
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            powerComp = GetComp<CompPowerTrader>();
            
            // Register with map component when spawned
            var arenaComp = map.GetComponent<MapComponent_SolWorldArena>();
            if (arenaComp != null)
            {
                arenaComp.RegisterArenaCore(this);
            }
        }
        
        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            // Unregister from map component when destroyed
            var arenaComp = Map?.GetComponent<MapComponent_SolWorldArena>();
            if (arenaComp != null)
            {
                arenaComp.UnregisterArenaCore();
            }
            
            base.DeSpawn(mode);
        }
        
        public bool IsPowered => powerComp?.PowerOn ?? false;
        public bool IsOperational => IsPowered && !IsBrokenDown();
        
        private bool IsBrokenDown()
        {
            var breakdownComp = GetComp<CompBreakdownable>();
            return breakdownComp?.BrokenDown ?? false;
        }
        
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }
            
            if (Faction == Faction.OfPlayer)
            {
                var arenaComp = Map?.GetComponent<MapComponent_SolWorldArena>();
                if (arenaComp != null)
                {
                    // Manual start/stop gizmo
                    yield return new Command_Action
                    {
                        defaultLabel = arenaComp.IsActive ? "Stop Arena" : "Start Arena",
                        defaultDesc = arenaComp.IsActive ? "Stop automated arena rounds" : "Start automated arena rounds (requires power and team spawners)",
                        icon = ContentFinder<Texture2D>.Get("UI/Commands/Attack"),
                        action = () => {
                            if (arenaComp.IsActive)
                            {
                                arenaComp.StopArena();
                            }
                            else
                            {
                                arenaComp.StartArena();
                            }
                        }
                    };
                    
                    // Debug: Force next round
                    if (Prefs.DevMode)
                    {
                        yield return new Command_Action
                        {
                            defaultLabel = "DEV: Force Round",
                            defaultDesc = "Force start next round immediately",
                            icon = ContentFinder<Texture2D>.Get("UI/Commands/Draft"),
                            action = () => arenaComp.ForceNextRound()
                        };
                    }
                }
            }
        }
        
        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            
            // Draw arena bounds when selected
            var arenaComp = Map?.GetComponent<MapComponent_SolWorldArena>();
            if (arenaComp != null)
            {
                var bounds = arenaComp.GetArenaBounds();
                if (bounds.HasValue)
                {
                    // Can use LINQ ToList() in RimWorld 1.6
                    GenDraw.DrawFieldEdges(bounds.Value.Cells.ToList(), Color.yellow);
                }
            }
        }
        
        public override string GetInspectString()
        {
            var text = base.GetInspectString();
            
            var arenaComp = Map?.GetComponent<MapComponent_SolWorldArena>();
            if (arenaComp != null)
            {
                if (!string.IsNullOrEmpty(text))
                    text += "\n";
                    
                text += $"Arena Status: {arenaComp.CurrentState}";
                
                if (arenaComp.IsActive && arenaComp.CurrentRoster != null)
                {
                    var timeLeft = arenaComp.GetTimeLeftInCurrentPhase();
                    text += $"\nTime left: {timeLeft:F0}s";
                    text += $"\nMatch: {arenaComp.CurrentRoster.MatchId}";
                }
                
                if (!arenaComp.HasValidSetup)
                {
                    text += "\nMissing team spawners!";
                }
            }
            
            return text;
        }
    }
}