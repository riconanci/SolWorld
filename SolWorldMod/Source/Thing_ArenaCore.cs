// solworld/SolWorldMod/Source/Thing_ArenaCore.cs
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace SolWorldMod
{
    public class Thing_ArenaCore : Building
    {
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            Log.Message("SolWorld: Arena Core spawned");
            
            // Register with the arena component
            var arenaComp = map.GetComponent<MapComponent_SolWorldArena>();
            if (arenaComp != null)
            {
                arenaComp.RegisterArenaCore(this);
            }
        }
        
        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            Log.Message("SolWorld: Arena Core despawned");
            
            // Unregister from the arena component
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
                return Spawned && !Destroyed;
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
                    if (!arenaComp.IsActive)
                    {
                        // Start Arena button
                        yield return new Command_Action
                        {
                            defaultLabel = "Start Arena",
                            defaultDesc = "Begin automated 10v10 arena rounds with 5-minute cadence",
                            icon = BaseContent.BadTex,
                            action = () => {
                                arenaComp.StartArena();
                            }
                        };
                    }
                    else
                    {
                        // Stop Arena button
                        yield return new Command_Action
                        {
                            defaultLabel = "Stop Arena",
                            defaultDesc = "Stop automated arena rounds",
                            icon = BaseContent.BadTex,
                            action = () => {
                                arenaComp.StopArena();
                            }
                        };
                        
                        // Force Next Round button
                        yield return new Command_Action
                        {
                            defaultLabel = "Force Next Round",
                            defaultDesc = "Immediately start the next arena round",
                            icon = BaseContent.BadTex,
                            action = () => {
                                arenaComp.ForceNextRound();
                            }
                        };
                        
                        // CRITICAL: Manual Unpause button for debugging the stuck pause issue
                        if (arenaComp.CurrentState == ArenaState.Preview)
                        {
                            yield return new Command_Action
                            {
                                defaultLabel = "MANUAL UNPAUSE",
                                defaultDesc = "Force unpause the game (debug button for stuck preview)",
                                icon = BaseContent.BadTex,
                                action = () => {
                                    arenaComp.ForceUnpause();
                                }
                            };
                        }
                        
                        // Debug: Test movement
                        if (arenaComp.CurrentRoster != null)
                        {
                            yield return new Command_Action
                            {
                                defaultLabel = "DEV: Test Movement",
                                defaultDesc = "Test pawn movement without combat (dev mode only)",
                                icon = BaseContent.BadTex,
                                action = () => {
                                    arenaComp.TestCombatMovement();
                                }
                            };
                        }
                        
                        // Debug: List pawns
                        yield return new Command_Action
                        {
                            defaultLabel = "DEV: List Pawns",
                            defaultDesc = "Log all active arena pawns (dev mode only)",
                            icon = BaseContent.BadTex,
                            action = () => {
                                arenaComp.DebugListActivePawns();
                            }
                        };
                        
                        // Test spawn fighters button for development
                        if (!arenaComp.HasValidSetup)
                        {
                            yield return new Command_Action
                            {
                                defaultLabel = "DEV: Test Spawn",
                                defaultDesc = "Test spawn fighters without backend (dev mode only)",
                                icon = BaseContent.BadTex,
                                action = () => {
                                    var testRoster = CreateTestRoster();
                                    arenaComp.TestSpawnFighters(testRoster);
                                }
                            };
                        }
                    }
                }
            }
        }
        
        private RoundRoster CreateTestRoster()
        {
            var roster = new RoundRoster
            {
                RoundRewardTotalSol = 1.0f,
                PayoutPercent = 0.20f
            };
            
            // Create 10 red and 10 blue fighters with mock wallet addresses
            for (int i = 0; i < 10; i++)
            {
                roster.Red.Add(new Fighter($"RedWallet{i:D3}MockAddress{i:D3}", TeamColor.Red));
                roster.Blue.Add(new Fighter($"BlueWallet{i:D3}MockAddress{i:D3}", TeamColor.Blue));
            }
            
            return roster;
        }
        
        public override string GetInspectString()
        {
            var text = base.GetInspectString();
            if (!string.IsNullOrEmpty(text))
                text += "\n";
                
            text += "Status: " + (IsOperational ? "Operational" : "Not Operational");
            
            var arenaComp = Map?.GetComponent<MapComponent_SolWorldArena>();
            if (arenaComp != null)
            {
                text += "\nArena: " + (arenaComp.IsActive ? "Active" : "Inactive");
                
                if (arenaComp.IsActive)
                {
                    text += "\nPhase: " + arenaComp.GetPhaseDisplayText();
                    
                    if (arenaComp.CurrentState == ArenaState.Idle)
                    {
                        var nextRoundTime = arenaComp.GetTimeUntilNextRound();
                        if (nextRoundTime > 0)
                        {
                            text += "\nNext round in: " + nextRoundTime + "s";
                        }
                        else
                        {
                            text += "\nReady to start next round";
                        }
                    }
                    
                    if (arenaComp.CurrentRoster != null)
                    {
                        text += "\nMatch: " + arenaComp.CurrentRoster.MatchId;
                        text += "\nRed alive: " + arenaComp.CurrentRoster.RedAlive + "/10";
                        text += "\nBlue alive: " + arenaComp.CurrentRoster.BlueAlive + "/10";
                        text += "\nRed kills: " + arenaComp.CurrentRoster.RedKills;
                        text += "\nBlue kills: " + arenaComp.CurrentRoster.BlueKills;
                        
                        if (arenaComp.CurrentRoster.Winner.HasValue)
                        {
                            text += "\nWinner: " + arenaComp.CurrentRoster.Winner + " Team";
                        }
                        
                        text += "\nPool: " + arenaComp.CurrentRoster.RoundRewardTotalSol.ToString("F2") + " SOL";
                        text += "\nPer winner: " + arenaComp.CurrentRoster.PerWinnerPayout.ToString("F3") + " SOL";
                    }
                }
                
                // Show setup status
                if (!arenaComp.HasValidSetup)
                {
                    text += "\nSetup: INCOMPLETE";
                    if (Map != null)
                    {
                        var redSpawner = Map.listerBuildings.allBuildingsColonist
                            .FirstOrDefault(b => b.def?.defName == "SolWorld_RedSpawn");
                        var blueSpawner = Map.listerBuildings.allBuildingsColonist
                            .FirstOrDefault(b => b.def?.defName == "SolWorld_BlueSpawn");
                            
                        if (redSpawner == null)
                            text += "\nMissing: Red Team Spawner";
                        if (blueSpawner == null)
                            text += "\nMissing: Blue Team Spawner";
                    }
                }
                else
                {
                    text += "\nSetup: COMPLETE";
                    var bounds = arenaComp.GetArenaBounds();
                    if (bounds.HasValue)
                    {
                        text += "\nArena size: " + bounds.Value.Width + "x" + bounds.Value.Height + " cells";
                        text += "\nArena area: " + (bounds.Value.Width * bounds.Value.Height) + " cells";
                    }
                }
                
                // Backend integration status
                var settings = SolWorldMod.Settings;
                if (settings != null)
                {
                    text += "\nBackend: " + (string.IsNullOrEmpty(settings.apiBaseUrl) ? "Not configured" : 
                        settings.IsDevMode ? "Dev mode" : "Production");
                    
                    if (!string.IsNullOrEmpty(settings.tokenMint))
                    {
                        text += "\nToken: " + settings.tokenMint.Substring(0, 8) + "...";
                    }
                }
            }
            
            return text;
        }
    }
}