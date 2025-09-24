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
        
        // INVINCIBILITY IMPLEMENTATION - Using PostApplyDamage to nullify damage after it's applied
        public override void PostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            // Always show impact effects when hit
            if (dinfo.Amount > 0)
            {
                // Create visual/audio feedback
                if (Map != null)
                {
                    // Create sparks effect
                    FleckMaker.ThrowMicroSparks(DrawPos, Map);
                    
                    // Optional: Show "INVULNERABLE" text
                    if (Prefs.DevMode)
                    {
                        MoteMaker.ThrowText(DrawPos, Map, "INVULNERABLE", Color.yellow, 2f);
                    }
                }
            }
            
            // Restore full health after any damage
            if (HitPoints < MaxHitPoints)
            {
                HitPoints = MaxHitPoints;
            }
            
            base.PostApplyDamage(dinfo, 0f); // Pass 0 damage to base
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
                        cachedArenaComp.OnUITriggeredUnpause();
                    }
                    
                    // AUTO-RESET: Check if UI wants us to automatically trigger the reset
                    if (cachedArenaComp.ShouldUITriggerReset)
                    {
                        Log.Message("SolWorld: UI flagged for auto-reset - triggering from Gizmo context!");
                        cachedArenaComp.OnUITriggeredReset();
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
                            icon = ContentFinder<Texture2D>.Get("UI/Commands/Attack", false) ??
                                   ContentFinder<Texture2D>.Get("UI/Commands/AttackMelee", true) ??
                                   BaseContent.BadTex,
                            action = () =>
                            {
                                try
                                {
                                    cachedArenaComp.StartArena();
                                }
                                catch (System.Exception ex)
                                {
                                    Log.Error("SolWorld: Error starting arena: " + ex.Message);
                                    Messages.Message("Failed to start arena: " + ex.Message, MessageTypeDefOf.RejectInput);
                                }
                            }
                        };
                        
                        // Check setup and disable if invalid
                        if (!cachedArenaComp.HasValidSetup)
                        {
                            startButton.Disable(GetSetupErrorMessage());
                        }
                        
                        yield return startButton;
                    }
                    else
                    {
                        // Stop Arena button
                        yield return new Command_Action
                        {
                            defaultLabel = "⏹️ Stop Arena",
                            defaultDesc = "Stop the arena and end current round\n\n" +
                                         "This will:\n• End the current round immediately\n• Despawn all fighters\n• Reset the arena\n• Return to inactive state",
                            icon = ContentFinder<Texture2D>.Get("UI/Commands/Halt", true) ?? BaseContent.BadTex,
                            action = () =>
                            {
                                try
                                {
                                    cachedArenaComp.StopArena();
                                    Messages.Message("Arena stopped", MessageTypeDefOf.NeutralEvent);
                                }
                                catch (System.Exception ex)
                                {
                                    Log.Error("SolWorld: Error stopping arena: " + ex.Message);
                                    Messages.Message("Failed to stop arena: " + ex.Message, MessageTypeDefOf.RejectInput);
                                }
                            }
                        };
                    }
                    
                    // DEVELOPMENT CONTROLS (only visible in dev mode)
                    if (Prefs.DevMode)
                    {
                        // Force Next Round (when arena is idle)
                        if (cachedArenaComp.IsActive && cachedArenaComp.CurrentState == ArenaState.Idle)
                        {
                            yield return new Command_Action
                            {
                                defaultLabel = "[DEV] Next Round Now",
                                defaultDesc = "Skip the wait time and start the next round immediately",
                                icon = ContentFinder<Texture2D>.Get("UI/Commands/AttackMelee", true) ?? BaseContent.BadTex,
                                action = () =>
                                {
                                    try
                                    {
                                        cachedArenaComp.ForceNextRound();
                                        Messages.Message("Next round triggered", MessageTypeDefOf.NeutralEvent);
                                    }
                                    catch (System.Exception ex)
                                    {
                                        Log.Error("SolWorld: Error triggering next round: " + ex.Message);
                                    }
                                }
                            };
                        }
                        
                        // Force Unpause (when in preview)
                        if (cachedArenaComp.IsActive && cachedArenaComp.IsPreviewActive)
                        {
                            yield return new Command_Action
                            {
                                defaultLabel = "[DEV] Force Unpause",
                                defaultDesc = "Skip the preview phase and start combat immediately",
                                icon = ContentFinder<Texture2D>.Get("UI/Commands/AttackMelee", true) ?? BaseContent.BadTex,
                                action = () =>
                                {
                                    try
                                    {
                                        cachedArenaComp.ForceUnpause();
                                        Messages.Message("Combat started", MessageTypeDefOf.NeutralEvent);
                                    }
                                    catch (System.Exception ex)
                                    {
                                        Log.Error("SolWorld: Error force unpausing: " + ex.Message);
                                    }
                                }
                            };
                        }
                        
                        // Force Reset (when in combat)
                        if (cachedArenaComp.IsActive && cachedArenaComp.CurrentState == ArenaState.Combat)
                        {
                            yield return new Command_Action
                            {
                                defaultLabel = "[DEV] Force Reset",
                                defaultDesc = "End combat immediately and reset the arena",
                                icon = ContentFinder<Texture2D>.Get("UI/Commands/Halt", true) ?? BaseContent.BadTex,
                                action = () =>
                                {
                                    try
                                    {
                                        cachedArenaComp.ForceReset();
                                        Messages.Message("Arena reset", MessageTypeDefOf.NeutralEvent);
                                    }
                                    catch (System.Exception ex)
                                    {
                                        Log.Error("SolWorld: Error force resetting: " + ex.Message);
                                    }
                                }
                            };
                        }
                        
                        // Force Refresh Setup
                        yield return new Command_Action
                        {
                            defaultLabel = "[DEV] Refresh Setup",
                            defaultDesc = "Force refresh the arena setup and spawner detection",
                            icon = BaseContent.BadTex,
                            action = () =>
                            {
                                try
                                {
                                    cachedArenaComp.ForceRefreshSpawners();
                                    Messages.Message("Setup refreshed - check inspect text", MessageTypeDefOf.NeutralEvent);
                                }
                                catch (System.Exception ex)
                                {
                                    Log.Error("SolWorld: Error refreshing setup: " + ex.Message);
                                }
                            }
                        };
                        
                        // Test Spawn Fighters
                        yield return new Command_Action
                        {
                            defaultLabel = "[DEV] Test Spawn",
                            defaultDesc = "Spawn test fighters for debugging arena layout",
                            icon = BaseContent.BadTex,
                            action = () => {
                                Log.Message("SolWorld: Test Spawn button clicked");
                                var testRoster = CreateTestRoster();
                                cachedArenaComp.TestSpawnFighters(testRoster);
                            }
                        };
                    }
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
            
            // Check arena core
            status += "✓ Arena Core: Present\n";
            
            // Check for spawners
            if (Map != null)
            {
                var redSpawner = Map.listerBuildings.allBuildingsColonist
                    .FirstOrDefault(b => b.def?.defName == "SolWorld_RedSpawn");
                var blueSpawner = Map.listerBuildings.allBuildingsColonist
                    .FirstOrDefault(b => b.def?.defName == "SolWorld_BlueSpawn");
                    
                if (redSpawner != null)
                {
                    status += "✓ Red Team Spawner: Present\n";
                }
                else
                {
                    status += "✗ Red Team Spawner: MISSING\n";
                }
                
                if (blueSpawner != null)
                {
                    status += "✓ Blue Team Spawner: Present\n";
                }
                else
                {
                    status += "✗ Blue Team Spawner: MISSING\n";
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
                
                // FIXED: Always force refresh spawners before checking setup status
                cachedArenaComp.ForceRefreshSpawners();
                
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
                    text += "\nBackend: " + (string.IsNullOrEmpty(settings.apiBaseUrl) ? 
                        "Not configured" : 
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
            
            // Add invincibility status
            text += "\nStatus: INVULNERABLE";
            
            return text;
        }
    }
}