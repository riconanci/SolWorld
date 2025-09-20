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
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            
            Log.Message("SolWorld: Arena Core spawned, registering with map component...");
            
            // Register with map component when spawned
            var arenaComp = map.GetComponent<MapComponent_SolWorldArena>();
            if (arenaComp != null)
            {
                arenaComp.RegisterArenaCore(this);
                Log.Message("SolWorld: Arena Core successfully registered");
            }
            else
            {
                Log.Error("SolWorld: Could not find MapComponent_SolWorldArena!");
            }
        }
        
        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            Log.Message("SolWorld: Arena Core being despawned, unregistering...");
            
            // Unregister from map component when destroyed
            var arenaComp = Map?.GetComponent<MapComponent_SolWorldArena>();
            if (arenaComp != null)
            {
                arenaComp.UnregisterArenaCore();
            }
            
            base.DeSpawn(mode);
        }
        
        public bool IsOperational 
        { 
            get 
            {
                // Check if spawned and not destroyed
                if (!Spawned || Destroyed)
                {
                    return false;
                }
                
                // Check if broken down
                var breakdownComp = GetComp<CompBreakdownable>();
                if (breakdownComp?.BrokenDown == true)
                {
                    return false;
                }
                
                // Check if flickable and switched off
                var flickComp = GetComp<CompFlickable>();
                if (flickComp != null && !flickComp.SwitchIsOn)
                {
                    return false;
                }
                
                // No power requirements - always operational if not broken/switched off
                return true;
            }
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
                        defaultDesc = arenaComp.IsActive 
                            ? "Stop automated arena rounds" 
                            : "Start automated arena rounds (requires both Red and Blue team spawners)",
                        icon = BaseContent.BadTex, // FIXED: Use reliable texture
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
                            defaultDesc = "Force start next round immediately (dev mode only)",
                            icon = BaseContent.BadTex, // FIXED: Use reliable texture
                            action = () => arenaComp.ForceNextRound()
                        };
                        
                        // Debug: Refresh spawners
                        yield return new Command_Action
                        {
                            defaultLabel = "DEV: Refresh Setup",
                            defaultDesc = "Force refresh spawner detection (dev mode only)",
                            icon = BaseContent.BadTex, // FIXED: Use reliable texture
                            action = () => {
                                arenaComp.RegisterArenaCore(this); // This calls RefreshSpawners
                                Messages.Message("Spawner detection refreshed - check log for results", MessageTypeDefOf.NeutralEvent);
                            }
                        };
                        
                        // Debug: Test spawn fighters
                        yield return new Command_Action
                        {
                            defaultLabel = "DEV: Test Spawn",
                            defaultDesc = "Test fighter spawning without starting a round (dev mode only)",
                            icon = BaseContent.BadTex, // FIXED: Use reliable texture
                            action = () => {
                                if (arenaComp.HasValidSetup)
                                {
                                    // Create a test roster and spawn some fighters
                                    var testRoster = new RoundRoster
                                    {
                                        RoundRewardTotalSol = 1.0f,
                                        PayoutPercent = 0.2f
                                    };
                                    
                                    // Add a few test fighters
                                    for (int i = 0; i < 3; i++)
                                    {
                                        testRoster.Red.Add(new Fighter($"TestRed{i}Address", TeamColor.Red));
                                        testRoster.Blue.Add(new Fighter($"TestBlue{i}Address", TeamColor.Blue));
                                    }
                                    
                                    arenaComp.TestSpawnFighters(testRoster);
                                    Messages.Message("Test fighters spawned", MessageTypeDefOf.PositiveEvent);
                                }
                                else
                                {
                                    Messages.Message("Cannot test spawn - invalid arena setup", MessageTypeDefOf.RejectInput);
                                }
                            }
                        };
                    }
                }
            }
        }
        
        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            
            // Always draw arena bounds when selected
            DrawArenaBounds();
        }
        
        public void DrawArenaBounds()
        {
            var arenaComp = Map?.GetComponent<MapComponent_SolWorldArena>();
            if (arenaComp == null) return;
            
            var bounds = arenaComp.GetArenaBounds();
            if (bounds.HasValue)
            {
                // Draw the arena perimeter in bright yellow
                GenDraw.DrawFieldEdges(bounds.Value.Cells.ToList(), Color.yellow);
                
                // Draw corner markers for better visibility
                var corners = new IntVec3[]
                {
                    new IntVec3(bounds.Value.minX, 0, bounds.Value.minZ),
                    new IntVec3(bounds.Value.maxX, 0, bounds.Value.minZ),
                    new IntVec3(bounds.Value.maxX, 0, bounds.Value.maxZ),
                    new IntVec3(bounds.Value.minX, 0, bounds.Value.maxZ)
                };
                
                foreach (var corner in corners)
                {
                    GenDraw.DrawRadiusRing(corner, 2f, Color.yellow);
                }
                
                // Draw center point in green
                var center = new IntVec3(
                    bounds.Value.minX + bounds.Value.Width / 2,
                    0,
                    bounds.Value.minZ + bounds.Value.Height / 2
                );
                GenDraw.DrawRadiusRing(center, 3f, Color.green);
                
                // Draw spawner locations if they exist
                Building redSpawner = null;
                Building blueSpawner = null;
                
                // Find spawners
                var allBuildings = Map.listerBuildings.allBuildingsColonist;
                foreach (var building in allBuildings)
                {
                    if (building.def?.defName == "SolWorld_RedSpawn")
                        redSpawner = building;
                    else if (building.def?.defName == "SolWorld_BlueSpawn")
                        blueSpawner = building;
                }
                
                // Highlight spawners
                if (redSpawner != null)
                {
                    GenDraw.DrawRadiusRing(redSpawner.Position, 4f, Color.red);
                }
                
                if (blueSpawner != null)
                {
                    GenDraw.DrawRadiusRing(blueSpawner.Position, 4f, Color.blue);
                }
                
                // Draw connection lines from core to spawners
                var coreCenter = Position.ToVector3Shifted();
                
                if (redSpawner != null)
                {
                    var redCenter = redSpawner.Position.ToVector3Shifted();
                    GenDraw.DrawLineBetween(coreCenter, redCenter, SimpleColor.Red);
                }
                
                if (blueSpawner != null)
                {
                    var blueCenter = blueSpawner.Position.ToVector3Shifted();
                    GenDraw.DrawLineBetween(coreCenter, blueCenter, SimpleColor.Blue);
                }
            }
            else
            {
                // Show a warning radius if no valid bounds can be calculated
                GenDraw.DrawRadiusRing(Position, 10f, Color.red);
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
                    
                // Core status
                text += $"Status: {(IsOperational ? "Operational" : "Not Operational")}";
                
                if (!IsOperational)
                {
                    var breakdownComp = GetComp<CompBreakdownable>();
                    if (breakdownComp?.BrokenDown == true)
                        text += " (Broken Down)";
                    var flickComp = GetComp<CompFlickable>();
                    if (flickComp != null && !flickComp.SwitchIsOn)
                        text += " (Switched Off)";
                }
                
                // Arena state
                text += $"\nArena: {arenaComp.CurrentState}";
                
                if (arenaComp.IsActive)
                {
                    if (arenaComp.CurrentRoster != null)
                    {
                        var timeLeft = arenaComp.GetTimeLeftInCurrentPhase();
                        text += $"\nTime left: {timeLeft:F0}s";
                        text += $"\nMatch: {arenaComp.CurrentRoster.MatchId}";
                        text += $"\nRed: {arenaComp.CurrentRoster.RedAlive}/10, Blue: {arenaComp.CurrentRoster.BlueAlive}/10";
                        text += $"\nKills - Red: {arenaComp.CurrentRoster.RedKills}, Blue: {arenaComp.CurrentRoster.BlueKills}";
                    }
                    else
                    {
                        // Show time until next round
                        text += " (Waiting for next round)";
                    }
                }
                else
                {
                    text += " (Inactive)";
                }
                
                // Setup validation
                if (!arenaComp.HasValidSetup)
                {
                    text += "\n<color=red>Setup incomplete!</color>";
                    var bounds = arenaComp.GetArenaBounds();
                    if (bounds == null)
                        text += "\n• Missing team spawners";
                    else
                        text += "\n• Unknown setup issue";
                }
                else
                {
                    var bounds = arenaComp.GetArenaBounds();
                    if (bounds.HasValue)
                        text += $"\nArena size: {bounds.Value.Width}×{bounds.Value.Height} cells";
                }
                
                // Power status (now irrelevant but show for clarity)
                text += "\nPower: Not required";
            }
            
            return text;
        }
        
        // Override Tick to handle periodic spawner refresh
        protected override void Tick()
        {
            base.Tick();
            
            // Periodically refresh spawner detection (every 5 seconds when operational)
            if (IsOperational && Find.TickManager.TicksGame % 300 == 0)
            {
                var arenaComp = Map?.GetComponent<MapComponent_SolWorldArena>();
                if (arenaComp != null && !arenaComp.HasValidSetup)
                {
                    // Only refresh if we don't have valid setup - this catches newly built spawners
                    arenaComp.RegisterArenaCore(this);
                }
            }
        }
    }
}