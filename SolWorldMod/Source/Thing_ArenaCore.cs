// solworld/SolWorldMod/Source/Thing_ArenaCore.cs
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace SolWorldMod
{
    public class Thing_ArenaCore : Building
    {
        // FIXED: Track arena component reference to prevent button disappearing
        private MapComponent_SolWorldArena cachedArenaComp;
        
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            Log.Message("SolWorld: Arena Core spawned");
            
            // Register with the arena component and cache reference
            RefreshArenaComponent();
            if (cachedArenaComp != null)
            {
                cachedArenaComp.RegisterArenaCore(this);
            }
        }
        
        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            Log.Message("SolWorld: Arena Core despawned");
            
            // Unregister from the arena component
            if (cachedArenaComp != null)
            {
                cachedArenaComp.UnregisterArenaCore();
            }
            
            base.DeSpawn(mode);
        }
        
        public override void TickRare()
        {
            base.TickRare();
            
            // Refresh arena component reference every rare tick (250 ticks = ~4 seconds) to prevent issues
            RefreshArenaComponent();
        }
        
        private void RefreshArenaComponent()
        {
            if (Map != null)
            {
                var arenaComp = Map.GetComponent<MapComponent_SolWorldArena>();
                if (arenaComp != cachedArenaComp)
                {
                    cachedArenaComp = arenaComp;
                    if (cachedArenaComp != null)
                    {
                        Log.Message("SolWorld: Arena component reference refreshed");
                    }
                }
            }
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
            // ALWAYS show base gizmos first
            foreach (var gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }
            
            // CRITICAL: Always check for valid arena component to prevent button disappearing
            if (Faction == Faction.OfPlayer && IsOperational)
            {
                // Ensure we have a valid arena component reference
                RefreshArenaComponent();
                
                if (cachedArenaComp != null)
                {
                    // AUTO-UNPAUSE: Check if UI wants us to automatically trigger the unpause
                    if (cachedArenaComp.ShouldUITriggerUnpause)
                    {
                        Log.Message("SolWorld: UI flagged for auto-unpause - triggering from Gizmo context!");
                        cachedArenaComp.ForceUnpause();
                    }
                    
                    // AUTO-RESET: Check if UI wants us to automatically trigger the reset
                    if (cachedArenaComp.ShouldUITriggerReset)
                    {
                        Log.Message("SolWorld: UI flagged for auto-reset - triggering from Gizmo context!");
                        cachedArenaComp.ForceReset();
                    }
                    
                    // MAIN ARENA CONTROLS - ALWAYS visible regardless of state
                    if (!cachedArenaComp.IsActive)
                    {
                        // Start Arena button
                        var startButton = new Command_Action
                        {
                            defaultLabel = "🚀 Start Arena",
                            defaultDesc = "Begin automated 10v10 arena rounds with 3-minute cadence\n\n" +
                                         "Requires: Arena Core + Red Spawner + Blue Spawner\n\n" +
                                         "Features:\n• 30s preview (paused)\n• 90s combat\n• Auto-reset between rounds",
                            icon = ContentFinder<UnityEngine.Texture2D>.Get("UI/Commands/Attack", false) ?? BaseContent.BadTex,
                            action = () => {
                                Log.Message("SolWorld: Start Arena button clicked");
                                cachedArenaComp.StartArena();
                            }
                        };
                        
                        if (!cachedArenaComp.HasValidSetup)
                        {
                            startButton.Disable(GetSetupErrorMessage());
                        }
                        
                        yield return startButton;
                        
                        // Show setup status button
                        yield return new Command_Action
                        {
                            defaultLabel = cachedArenaComp.HasValidSetup ? "✅ Check Setup" : "⚠️ Check Setup",
                            defaultDesc = GetDetailedSetupStatus(),
                            icon = ContentFinder<UnityEngine.Texture2D>.Get("UI/Commands/Info", false) ?? BaseContent.BadTex,
                            action = () => {
                                Messages.Message(GetDetailedSetupStatus(), 
                                    cachedArenaComp.HasValidSetup ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.CautionInput);
                            }
                        };
                    }
                    else
                    {
                        // ACTIVE ARENA CONTROLS - Always show these when arena is active
                        
                        // Stop Arena button
                        yield return new Command_Action
                        {
                            defaultLabel = "🛑 Stop Arena",
                            defaultDesc = "Stop automated arena rounds and cleanup all fighters\n\n" +
                                         "This will:\n• End current round immediately\n• Despawn all fighters\n• Reset arena to inactive state",
                            icon = ContentFinder<UnityEngine.Texture2D>.Get("UI/Commands/Halt", false) ?? BaseContent.BadTex,
                            action = () => {
                                Log.Message("SolWorld: Stop Arena button clicked");
                                cachedArenaComp.StopArena();
                            }
                        };
                        
                        // Force Next Round button
                        yield return new Command_Action
                        {
                            defaultLabel = "⏭️ Force Next Round",
                            defaultDesc = "Immediately start the next arena round\n\n" +
                                         "This will:\n• Skip current phase\n• Start new round in 1 second\n• Useful for testing or manual control",
                            icon = ContentFinder<UnityEngine.Texture2D>.Get("UI/Commands/Skip", false) ?? BaseContent.BadTex,
                            action = () => {
                                Log.Message("SolWorld: Force Next Round button clicked");
                                cachedArenaComp.ForceNextRound();
                            }
                        };
                        
                        // PHASE-SPECIFIC EMERGENCY CONTROLS
                        
                        // Manual unpause for emergency situations only
                        if (cachedArenaComp.CurrentState == ArenaState.Preview)
                        {
                            yield return new Command_Action
                            {
                                defaultLabel = "⚡ Manual Unpause",
                                defaultDesc = "EMERGENCY: Manual unpause if automatic transition fails\n\n" +
                                             "This should normally happen automatically after 30 seconds.\n" +
                                             "Only use if the countdown reaches 0 but game stays paused.",
                                icon = ContentFinder<UnityEngine.Texture2D>.Get("UI/Commands/Play", false) ?? BaseContent.BadTex,
                                action = () => {
                                    Log.Message("SolWorld: Manual Unpause button clicked");
                                    cachedArenaComp.ForceUnpause();
                                }
                            };
                        }
                        
                        // Manual reset for emergency situations
                        if (cachedArenaComp.CurrentState == ArenaState.Ended || cachedArenaComp.CurrentState == ArenaState.Resetting)
                        {
                            yield return new Command_Action
                            {
                                defaultLabel = "⚡ Manual Reset",
                                defaultDesc = "EMERGENCY: Manual reset if automatic transition fails\n\n" +
                                             "This should normally happen automatically after 3 seconds.\n" +
                                             "Only use if arena gets stuck in ended/resetting state.",
                                icon = ContentFinder<UnityEngine.Texture2D>.Get("UI/Commands/Refresh", false) ?? BaseContent.BadTex,
                                action = () => {
                                    Log.Message("SolWorld: Manual Reset button clicked");
                                    cachedArenaComp.ForceReset();
                                }
                            };
                        }
                        
                        // DEVELOPMENT/DEBUG CONTROLS (always available for testing)
                        
                        // Debug: Test movement
                        if (cachedArenaComp.CurrentRoster != null)
                        {
                            yield return new Command_Action
                            {
                                defaultLabel = "🔧 Test Movement",
                                defaultDesc = "DEV: Test pawn movement and AI\n\n" +
                                             "Forces all pawns to move toward enemy spawn points.\n" +
                                             "Useful for testing combat mechanics.",
                                icon = BaseContent.BadTex,
                                action = () => {
                                    Log.Message("SolWorld: Test Movement button clicked");
                                    cachedArenaComp.TestCombatMovement();
                                }
                            };
                        }
                        
                        // Debug: List pawns
                        yield return new Command_Action
                        {
                            defaultLabel = "📋 List Pawns",
                            defaultDesc = "DEV: Log all active arena pawns to console\n\n" +
                                         "Shows current jobs and states of all fighters.\n" +
                                         "Check the debug log for detailed pawn information.",
                            icon = BaseContent.BadTex,
                            action = () => {
                                Log.Message("SolWorld: List Pawns button clicked");
                                cachedArenaComp.DebugListActivePawns();
                            }
                        };
                    }
                    
                    // Test spawn fighters button for development (always available)
                    yield return new Command_Action
                    {
                        defaultLabel = "🧪 Test Spawn",
                        defaultDesc = "DEV: Test spawn fighters without backend\n\n" +
                                     "Spawns mock fighters for testing combat mechanics.\n" +
                                     "Works regardless of setup status.",
                        icon = BaseContent.BadTex,
                        action = () => {
                            Log.Message("SolWorld: Test Spawn button clicked");
                            var testRoster = CreateTestRoster();
                            cachedArenaComp.TestSpawnFighters(testRoster);
                        }
                    };
                }
                else
                {
                    // FALLBACK: Show error state if no arena component
                    var errorButton = new Command_Action
                    {
                        defaultLabel = "⚠️ Arena Error",
                        defaultDesc = "Arena component not found!\n\n" +
                                     "This is a serious error. Try:\n" +
                                     "• Saving and reloading the game\n" +
                                     "• Restarting RimWorld\n" +
                                     "• Reporting this bug to mod author",
                        icon = BaseContent.BadTex,
                        action = () => {
                            Messages.Message("Arena component error - try reloading save", MessageTypeDefOf.RejectInput);
                            Log.Error("SolWorld: Arena component is null in GetGizmos - this should not happen!");
                        }
                    };
                    errorButton.Disable("Arena component missing");
                    yield return errorButton;
                }
            }
        }
        
        private string GetSetupErrorMessage()
        {
            if (cachedArenaComp == null)
                return "Arena component missing";
                
            if (!IsOperational)
                return "Arena Core not operational";
                
            if (!cachedArenaComp.HasValidSetup)
            {
                return "Missing spawners - need Red Team Spawner and Blue Team Spawner";
            }
            
            return "Unknown setup error";
        }
        
        private string GetDetailedSetupStatus()
        {
            if (cachedArenaComp == null)
                return "Arena component is missing from this map.";
                
            var status = "Arena Setup Status:\n\n";
            
            // Arena Core status
            status += "✓ Arena Core: Operational\n";
            
            // Check for spawners
            if (Map != null)
            {
                var redSpawner = Map.listerBuildings.allBuildingsColonist
                    .FirstOrDefault(b => b.def?.defName == "SolWorld_RedSpawn");
                var blueSpawner = Map.listerBuildings.allBuildingsColonist
                    .FirstOrDefault(b => b.def?.defName == "SolWorld_BlueSpawn");
                
                if (redSpawner != null)
                    status += "✓ Red Team Spawner: Found\n";
                else
                    status += "✗ Red Team Spawner: MISSING\n";
                    
                if (blueSpawner != null)
                    status += "✓ Blue Team Spawner: Found\n";
                else
                    status += "✗ Blue Team Spawner: MISSING\n";
                    
                if (redSpawner != null && blueSpawner != null)
                {
                    var bounds = cachedArenaComp.GetArenaBounds();
                    if (bounds.HasValue)
                    {
                        status += $"\n✓ Arena Bounds: {bounds.Value.Width}x{bounds.Value.Height} cells";
                        status += $"\n✓ Arena Area: {bounds.Value.Width * bounds.Value.Height} cells";
                    }
                    else
                    {
                        status += "\n✗ Arena Bounds: Could not calculate";
                    }
                }
                else
                {
                    status += "\nPlace both team spawners to calculate arena bounds.";
                }
            }
            
            if (cachedArenaComp.HasValidSetup)
            {
                status += "\n\n✓ Setup Complete! Ready to start arena.";
            }
            else
            {
                status += "\n\n✗ Setup Incomplete. Place missing spawners to continue.";
            }
            
            return status;
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
                roster.Red.Add(new Fighter($"RedTest{i:D2}MockWallet{i:D2}", TeamColor.Red));
                roster.Blue.Add(new Fighter($"BlueTest{i:D2}MockWallet{i:D2}", TeamColor.Blue));
            }
            
            return roster;
        }
        
        public override string GetInspectString()
        {
            var text = base.GetInspectString();
            if (!string.IsNullOrEmpty(text))
                text += "\n";
                
            text += "Status: " + (IsOperational ? "Operational" : "Not Operational");
            
            // Ensure we have current arena component data
            RefreshArenaComponent();
            
            if (cachedArenaComp != null)
            {
                text += "\nArena: " + (cachedArenaComp.IsActive ? "Active" : "Inactive");
                
                if (cachedArenaComp.IsActive)
                {
                    text += "\nPhase: " + cachedArenaComp.GetPhaseDisplayText();
                    
                    if (cachedArenaComp.CurrentState == ArenaState.Idle)
                    {
                        var nextRoundTime = cachedArenaComp.GetTimeUntilNextRound();
                        if (nextRoundTime > 0)
                        {
                            var minutes = nextRoundTime / 60;
                            var seconds = nextRoundTime % 60;
                            text += $"\nNext round: {minutes:F0}:{seconds:D2}";
                        }
                        else
                        {
                            text += "\nReady to start next round";
                        }
                    }
                    
                    if (cachedArenaComp.CurrentRoster != null)
                    {
                        text += "\nMatch: " + cachedArenaComp.CurrentRoster.MatchId;
                        text += $"\nRed team: {cachedArenaComp.CurrentRoster.RedAlive}/10 alive, {cachedArenaComp.CurrentRoster.RedKills} kills";
                        text += $"\nBlue team: {cachedArenaComp.CurrentRoster.BlueAlive}/10 alive, {cachedArenaComp.CurrentRoster.BlueKills} kills";
                        
                        if (cachedArenaComp.CurrentRoster.Winner.HasValue)
                        {
                            text += "\nWinner: " + cachedArenaComp.CurrentRoster.Winner + " Team";
                        }
                        
                        text += $"\nPool: {cachedArenaComp.CurrentRoster.RoundRewardTotalSol:F2} SOL";
                        text += $"\nPer winner: {cachedArenaComp.CurrentRoster.PerWinnerPayout:F3} SOL";
                    }
                }
                
                // Show setup status
                if (!cachedArenaComp.HasValidSetup)
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
                    var bounds = cachedArenaComp.GetArenaBounds();
                    if (bounds.HasValue)
                    {
                        text += $"\nArena: {bounds.Value.Width}x{bounds.Value.Height} ({bounds.Value.Width * bounds.Value.Height} cells)";
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
                        var shortMint = settings.tokenMint.Length > 16 ? 
                            settings.tokenMint.Substring(0, 8) + "..." + settings.tokenMint.Substring(settings.tokenMint.Length - 8) : 
                            settings.tokenMint;
                        text += "\nToken: " + shortMint;
                    }
                }
                
                // Auto-trigger status for debugging (only show in dev mode)
                if (Prefs.DevMode)
                {
                    if (cachedArenaComp.ShouldUITriggerUnpause)
                    {
                        text += "\n[DEV] AUTO-UNPAUSE: Ready to trigger!";
                    }
                    
                    if (cachedArenaComp.ShouldUITriggerReset)
                    {
                        text += "\n[DEV] AUTO-RESET: Ready to trigger!";
                    }
                }
            }
            else
            {
                text += "\nArena: ERROR - Component not found";
            }
            
            return text;
        }
    }
}