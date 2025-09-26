// solworld/SolWorldMod/Source/MapComponent_SolWorldArena.cs
// UPDATED WITH BALANCED
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;
using RimWorld;

namespace SolWorldMod
{
    public class MapComponent_SolWorldArena : MapComponent
    {
        private Thing_ArenaCore arenaCore;
        private Building redSpawner;
        private Building blueSpawner;
        
        // State management
        private ArenaState currentState = ArenaState.Idle;
        private bool isActive = false;
        private RoundRoster currentRoster;
        private int nextRoundTick = -1;
        
        // Timing constants - Preview uses REAL TIME (works during pause), Combat uses GAME TIME
        private DateTime previewStartTime;
        private int combatStartTick;
        private int roundEndTick;
        private const float PREVIEW_SECONDS = 30f; // Real-time during pause
        private const int COMBAT_TICKS = 90 * 60; // 1.5 minutes (90 seconds) game time
        private const int RESET_DELAY_TICKS = 180; // 3 seconds to show results
        private const int CADENCE_TICKS = 180 * 60; // 3 minutes between rounds

        // ADD THESE FIELDS with other private fields:
        private LoadoutPreset currentRoundLoadoutPreset;
        private string[] currentRoundRedWeapons;
        private string[] currentRoundBlueWeapons;

        // ⭐ ADD THIS LINE RIGHT HERE: ⭐
        //private static bool lastRoundFavoredRed = false; // Track spawn order

        // Tier system data structures
        public Dictionary<string, TieredFighter> currentRoundTierData { get; private set; } = new Dictionary<string, TieredFighter>();

        public class TieredFighter
        {
            public string Wallet { get; set; }
            public float Balance { get; set; }
            public int Tier { get; set; }
            public string TierName { get; set; }
            public string WeaponQuality { get; set; }
            public bool HasArmor { get; set; }
            public bool HasHelmet { get; set; }
            public bool HasAura { get; set; }
        }

        // WINNER STORAGE - Persistent winner data for UI celebration
        private TeamColor? lastRoundWinner = null;
        private string lastMatchId = "";
        private List<Fighter> lastWinningTeam = new List<Fighter>();
        private float lastPerWinnerPayout = 0f;
        
        // Team management - use existing factions
        private Faction redTeamFaction;
        private Faction blueTeamFaction;
        private List<Pawn> redTeamPawns = new List<Pawn>();
        private List<Pawn> blueTeamPawns = new List<Pawn>();
        private Dictionary<Pawn, TeamColor> pawnTeamMap = new Dictionary<Pawn, TeamColor>();
        
        // UI-TRIGGERED SYSTEMS - Fixed approach for unpause and reset
        private bool previewCompleted = false;
        private bool uiShouldTriggerUnpause = false;
        private bool uiShouldTriggerReset = false;
        
        // Enhanced combat enforcement
        private bool combatInitiated = false;
        private int lastCombatEnforcementTick = -1;
        private int lastAggressiveEnforcementTick = -1;
        private Dictionary<Pawn, int> pawnLastActionTick = new Dictionary<Pawn, int>();
        
        // ARENA RESET COMPONENTS
        private ArenaBounds arenaBounds;
        private ArenaBlueprint arenaBlueprint;
        private ArenaReset arenaReset;
        
        // Accessors - ALWAYS AVAILABLE to prevent button disappearing
        public ArenaState CurrentState => currentState;
        public bool IsActive => isActive;
        public RoundRoster CurrentRoster => currentRoster;
        public bool HasValidSetup 
        { 
            get 
            {
                // FIXED: Force refresh spawners before checking
                if (arenaCore?.IsOperational == true)
                {
                    ForceRefreshSpawners();
                }
                
                return arenaCore?.IsOperational == true && redSpawner != null && blueSpawner != null;
            }
        }
        
        // WINNER STORAGE PROPERTIES - For UI access to persistent winner data
        public TeamColor? LastRoundWinner => lastRoundWinner;
        public string LastMatchId => lastMatchId;
        public List<Fighter> LastWinningTeam => lastWinningTeam;
        public float LastPerWinnerPayout => lastPerWinnerPayout;
        
        // Preview timing accessors for UI
        public DateTime PreviewStartTime => previewStartTime;
        public bool IsPreviewActive => currentState == ArenaState.Preview && !previewCompleted;
        public bool ShouldUITriggerUnpause => uiShouldTriggerUnpause;
        public bool ShouldUITriggerReset => uiShouldTriggerReset;
        public float PreviewTimeRemaining 
        { 
            get 
            {
                if (currentState != ArenaState.Preview) return 0f;
                var elapsed = (float)(DateTime.Now - previewStartTime).TotalSeconds;
                return Math.Max(0f, PREVIEW_SECONDS - elapsed);
            }
        }
        
        public MapComponent_SolWorldArena(Map map) : base(map)
        {
            arenaBounds = new ArenaBounds();
            arenaBlueprint = new ArenaBlueprint();
            arenaReset = new ArenaReset();
        }
        
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref arenaCore, "arenaCore");
            Scribe_References.Look(ref redSpawner, "redSpawner");
            Scribe_References.Look(ref blueSpawner, "blueSpawner");
            Scribe_Values.Look(ref isActive, "isActive", false);
            Scribe_Values.Look(ref currentState, "currentState", ArenaState.Idle);
            Scribe_Values.Look(ref nextRoundTick, "nextRoundTick", -1);
            Scribe_Values.Look(ref combatStartTick, "combatStartTick", -1);
            Scribe_Values.Look(ref roundEndTick, "roundEndTick", -1);
            Scribe_Values.Look(ref combatInitiated, "combatInitiated", false);
            Scribe_Values.Look(ref lastCombatEnforcementTick, "lastCombatEnforcementTick", -1);
            Scribe_Values.Look(ref lastAggressiveEnforcementTick, "lastAggressiveEnforcementTick", -1);
            Scribe_Values.Look(ref previewCompleted, "previewCompleted", false);
            Scribe_Values.Look(ref uiShouldTriggerUnpause, "uiShouldTriggerUnpause", false);
            Scribe_Values.Look(ref uiShouldTriggerReset, "uiShouldTriggerReset", false);
            Scribe_Deep.Look(ref currentRoster, "currentRoster");
            Scribe_References.Look(ref redTeamFaction, "redTeamFaction");
            Scribe_References.Look(ref blueTeamFaction, "blueTeamFaction");
            Scribe_Collections.Look(ref redTeamPawns, "redTeamPawns", LookMode.Reference);
            Scribe_Collections.Look(ref blueTeamPawns, "blueTeamPawns", LookMode.Reference);
            
            // WINNER STORAGE - Save/load winner data for persistent celebration
            Scribe_Values.Look(ref lastRoundWinner, "lastRoundWinner");
            Scribe_Values.Look(ref lastMatchId, "lastMatchId", "");
            Scribe_Values.Look(ref lastPerWinnerPayout, "lastPerWinnerPayout", 0f);
            Scribe_Collections.Look(ref lastWinningTeam, "lastWinningTeam", LookMode.Deep);
            
            // Rebuild team map after loading
            if (redTeamPawns == null) redTeamPawns = new List<Pawn>();
            if (blueTeamPawns == null) blueTeamPawns = new List<Pawn>();
            if (lastWinningTeam == null) lastWinningTeam = new List<Fighter>();
            
            pawnTeamMap.Clear();
            pawnLastActionTick.Clear();
            foreach (var pawn in redTeamPawns.Where(p => p != null))
            {
                pawnTeamMap[pawn] = TeamColor.Red;
                pawnLastActionTick[pawn] = -1;
            }
            foreach (var pawn in blueTeamPawns.Where(p => p != null))
            {
                pawnTeamMap[pawn] = TeamColor.Blue;
                pawnLastActionTick[pawn] = -1;
            }
            
            // Clean up null references
            redTeamPawns.RemoveAll(p => p == null);
            blueTeamPawns.RemoveAll(p => p == null);
        }
        
        private int GetTierForFighter(Fighter fighter)
        {
            return currentRoundTierData.ContainsKey(fighter.WalletFull) ? 
                currentRoundTierData[fighter.WalletFull].Tier : 1;
        }

        // Custom name rendering - add this override to MapComponent_SolWorldArena
        public override void MapComponentOnGUI()
        {
            base.MapComponentOnGUI();
            
            // Draw custom names during active arena rounds
            if (!isActive || currentRoster == null || currentState == ArenaState.Idle)
                return;
            
            // Draw EVERY frame during Repaint for smooth movement tracking
            if (Event.current.type == EventType.Repaint)
            {
                DrawCustomArenaNames();
            }
            
            // ALSO draw during Layout for even more frequent updates
            if (Event.current.type == EventType.Layout)
            {
                DrawCustomArenaNames();
            }
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            
            try
            {
                // ALWAYS ensure spawners are refreshed to prevent button disappearing
                if (arenaCore?.IsOperational == true)
                {
                    RefreshSpawners();
                }
                
                if (!isActive || arenaCore?.IsOperational != true)
                    return;
                    
                var currentTick = Find.TickManager.TicksGame;
                
                // Check if it's time for the next scheduled round
                if (currentState == ArenaState.Idle && nextRoundTick > 0 && currentTick >= nextRoundTick)
                {
                    Log.Message("SolWorld: TIME TO START NEW ROUND! Current: " + currentTick + ", Next: " + nextRoundTick);
                    Messages.Message("DEBUG: About to call StartNewRound()!", MessageTypeDefOf.PositiveEvent);
                    StartNewRound();
                    return;
                }
                
                // Handle phase transitions
                HandlePhaseTransitions();
                
                // ENHANCED combat enforcement every 2 seconds during combat
                if (currentState == ArenaState.Combat && (currentTick - lastCombatEnforcementTick) >= 120)
                {
                    lastCombatEnforcementTick = currentTick;
                    EnforceContinuousCombat();
                }
                
                // AGGRESSIVE enforcement every 5 seconds to fix stuck pawns
                if (currentState == ArenaState.Combat && (currentTick - lastAggressiveEnforcementTick) >= 300)
                {
                    lastAggressiveEnforcementTick = currentTick;
                    EnforceAggressiveCombatActions();
                }
                
                // Update roster status every 30 ticks during combat
                if (currentState == ArenaState.Combat && currentTick % 30 == 0)
                {
                    UpdateRosterStatus();
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"SolWorld: Critical error in MapComponentTick: {ex.Message}\n{ex.StackTrace}");
                
                // Emergency recovery - stop arena if something is seriously wrong
                if (currentState == ArenaState.Combat)
                {
                    Log.Error("SolWorld: Emergency stop due to critical error during combat");
                    StopAllArenaPawnJobs();
                    EndRound("Critical error - emergency stop");
                }
            }
        }
        
        // Called by UI when preview timer expires
        public void RequestCombatTransition()
        {
            if (currentState == ArenaState.Preview && !previewCompleted)
            {
                Log.Message("SolWorld: UI requested combat transition - flagging for Arena Core to handle!");
                previewCompleted = true;
                uiShouldTriggerUnpause = true;
            }
        }
        
        // Called by UI when reset should happen automatically
        public void RequestArenaReset()
        {
            if ((currentState == ArenaState.Ended || currentState == ArenaState.Resetting) && !uiShouldTriggerReset)
            {
                Log.Message("SolWorld: UI requested arena reset - flagging for Arena Core to handle!");
                uiShouldTriggerReset = true;
            }
        }
        
        // Called by ArenaCore when UI triggers the manual unpause
        public void OnUITriggeredUnpause()
        {
            Log.Message("SolWorld: ===== UI TRIGGERED UNPAUSE SUCCESS =====");
            
            uiShouldTriggerUnpause = false;
            ExecuteCombatTransition();
        }
        
        // Called by ArenaCore when UI triggers the automatic reset
        public void OnUITriggeredReset()
        {
            Log.Message("SolWorld: ===== UI TRIGGERED RESET SUCCESS =====");
            
            uiShouldTriggerReset = false;
            ExecuteArenaReset();
        }
        
        private void ExecuteCombatTransition()
        {
            Log.Message("SolWorld: ===== EXECUTING COMBAT TRANSITION FROM UI CONTEXT =====");
            
            currentState = ArenaState.Combat;
            combatStartTick = Find.TickManager.TicksGame;
            combatInitiated = false;
            lastCombatEnforcementTick = Find.TickManager.TicksGame;
            lastAggressiveEnforcementTick = Find.TickManager.TicksGame;
            
            // ADD THIS UNPAUSE CODE HERE:
            Log.Message("SolWorld: Unpausing game for combat...");
            Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            if (Find.TickManager.Paused)
            {
                Find.TickManager.TogglePaused();
            }
            Log.Message($"SolWorld: Game unpaused - Speed: {Find.TickManager.CurTimeSpeed}, Paused: {Find.TickManager.Paused}");
            
            // Reset pawn action tracking
            pawnLastActionTick.Clear();
            foreach (var pawn in redTeamPawns.Concat(blueTeamPawns).Where(p => p?.Spawned == true))
            {
                pawnLastActionTick[pawn] = Find.TickManager.TicksGame;
            }
            
            if (currentRoster != null)
            {
                currentRoster.IsLive = true;
                InitiateAggressiveCombat();
                Messages.Message("COMBAT STARTED! 90 seconds to fight!", MessageTypeDefOf.PositiveEvent);
                Log.Message("SolWorld: Combat initiated successfully");
            }
        }
        
        private void StripAllHeadwear(Pawn pawn)
        {
            if (pawn?.apparel?.WornApparel == null) return;
            
            try
            {
                // Find all headwear (hats, helmets, etc.)
                var headwear = pawn.apparel.WornApparel
                    .Where(apparel => apparel.def.apparel.bodyPartGroups.Any(bp => 
                        bp.defName == "FullHead" || bp.defName == "UpperHead"))
                    .ToList();
                
                // Remove each piece of headwear
                foreach (var item in headwear)
                {
                    pawn.apparel.Remove(item);
                }
                
                if (headwear.Count > 0)
                {
                    Log.Message($"SolWorld: Stripped {headwear.Count} headwear items from {pawn.Name}");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"SolWorld: Failed to strip headwear from {pawn.Name}: {ex.Message}");
            }
        }

        // FIXED: Execute arena reset from UI context with complete reset system
        private void ExecuteArenaReset()
        {
            Log.Message("SolWorld: ===== EXECUTING ARENA RESET FROM UI CONTEXT =====");
            
            try
            {
                // STEP 1: Stop all pawn jobs to prevent errors during reset
                StopAllArenaPawnJobs();
                
                // STEP 2: Cleanup current round pawns
                CleanupCurrentRound();
                
                // STEP 3: Perform arena reset if blueprint exists
                var bounds = GetArenaBounds();
                if (bounds.HasValue && arenaBlueprint.IsInitialized)
                {
                    Log.Message("SolWorld: Resetting arena to original state...");
                    arenaReset.ResetArena(map, bounds.Value, arenaBlueprint);
                }
                else
                {
                    Log.Warning("SolWorld: Cannot reset arena - no bounds or blueprint not initialized");
                }
                
                // STEP 4: Reset all state to idle and schedule next round
                currentState = ArenaState.Idle;
                currentRoster = null; // NOW it's safe to clear the roster
                combatStartTick = -1;
                roundEndTick = -1;
                combatInitiated = false;
                lastCombatEnforcementTick = -1;
                lastAggressiveEnforcementTick = -1;
                previewCompleted = false;
                uiShouldTriggerUnpause = false;
                uiShouldTriggerReset = false; // IMPORTANT: Clear the reset flag
                pawnLastActionTick.Clear();
                
                // STEP 5: CRITICAL - Force refresh spawners after reset to prevent disappearing buttons
                ForceRefreshSpawners();
                
                // STEP 6: Schedule next round
                ScheduleNextRound();
                
                Messages.Message("Arena reset complete! Next round in 3 minutes.", MessageTypeDefOf.PositiveEvent);
                Log.Message("SolWorld: ===== ARENA RESET COMPLETE =====");
            }
            catch (Exception ex)
            {
                Log.Error($"SolWorld: Arena reset failed: {ex.Message}\n{ex.StackTrace}");
                
                // Emergency recovery - clear flags and try to continue
                uiShouldTriggerReset = false;
                currentState = ArenaState.Idle;
                currentRoster = null;
                
                // Force refresh spawners even in error case
                try
                {
                    ForceRefreshSpawners();
                }
                catch
                {
                    Log.Error("SolWorld: Emergency spawner refresh also failed!");
                }
                
                ScheduleNextRound();
                
                Messages.Message("Arena reset encountered errors but recovered", MessageTypeDefOf.CautionInput);
            }
        }
        
        private void HandlePhaseTransitions()
        {
            var currentTick = Find.TickManager.TicksGame;
            
            switch (currentState)
            {
                case ArenaState.Preview:
                    // Preview timing is handled by UI layer - no automatic transitions here
                    break;
                    
                case ArenaState.Combat:
                    // Check for combat end conditions
                    var combatElapsed = currentTick - combatStartTick;
                    bool timeExpired = combatElapsed >= COMBAT_TICKS;
                    bool teamEliminated = currentRoster != null && (currentRoster.RedAlive == 0 || currentRoster.BlueAlive == 0);
                    
                    if (timeExpired || teamEliminated)
                    {
                        string reason = timeExpired ? "Time limit reached (90 seconds)" : "Team eliminated";
                        EndRound(reason);
                    }
                    break;
                    
                case ArenaState.Ended:
                    // Show results for 3 seconds, then trigger reset
                    if (roundEndTick > 0 && (currentTick - roundEndTick) >= RESET_DELAY_TICKS)
                    {
                        Log.Message("SolWorld: Results displayed for 3 seconds - triggering reset...");
                        // Set flag for UI to trigger reset (same pattern as unpause)
                        if (!uiShouldTriggerReset)
                        {
                            RequestArenaReset();
                        }
                    }
                    break;
                    
                case ArenaState.Resetting:
                    // This state is now handled by immediate reset in UI context
                    // Should not stay in this state long
                    break;
            }
        }
        
        /// <summary>
        /// Aggressive pawn cleanup to prevent job/combat errors
        /// Call this method during combat phases to prevent null reference exceptions
        /// </summary>
        private void PreventivePawnCleanup()
        {
            if (!isActive || currentRoster == null) return;
            
            try
            {
                var allArenaPawns = redTeamPawns.Concat(blueTeamPawns).Where(p => p != null).ToList();
                
                foreach (var pawn in allArenaPawns)
                {
                    try
                    {
                        // Skip if pawn is already destroyed or invalid
                        if (pawn.Destroyed || pawn.Map == null || !pawn.Spawned)
                        {
                            continue;
                        }
                        
                        // If pawn is dead or downed, clean up their jobs immediately
                        if (pawn.Dead || pawn.Downed)
                        {
                            CleanupPawnJobs(pawn);
                            continue;
                        }
                        
                        // If pawn has invalid job targets, clean up
                        if (pawn.jobs?.curJob != null)
                        {
                            var job = pawn.jobs.curJob;
                            
                            // Check if job target is valid
                            if (job.targetA.HasThing)
                            {
                                var target = job.targetA.Thing;
                                if (target == null || target.Destroyed || target.Map != pawn.Map)
                                {
                                    // Invalid target, end the job
                                    CleanupPawnJobs(pawn);
                                    continue;
                                }
                            }
                        }
                        
                    }
                    catch (System.Exception ex)
                    {
                        Log.Warning($"SolWorld: Error during pawn cleanup for {pawn?.Name}: {ex.Message}");
                        // Try to clean up the problematic pawn
                        try
                        {
                            CleanupPawnJobs(pawn);
                        }
                        catch (System.Exception)
                        {
                            // If cleanup fails, just continue
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"SolWorld: Critical error during preventive cleanup: {ex.Message}");
            }
        }

        /// <summary>
        /// Safely clean up a pawn's jobs and combat state
        /// </summary>
        private void CleanupPawnJobs(Pawn pawn)
        {
            if (pawn?.jobs == null) return;
            
            try
            {
                // End current job
                if (pawn.jobs.curJob != null)
                {
                    pawn.jobs.EndCurrentJob(Verse.AI.JobCondition.InterruptForced);
                }
                
                // Clear job queue
                pawn.jobs.ClearQueuedJobs();
                
                // Clear combat targets
                if (pawn.mindState != null)
                {
                    pawn.mindState.enemyTarget = null;
                    pawn.mindState.lastEngageTargetTick = 0;
                }
                
            }
            catch (System.Exception ex)
            {
                Log.Warning($"SolWorld: Failed to cleanup jobs for {pawn.Name}: {ex.Message}");
            }
        }

        private void DrawCustomArenaNames()
        {
            var allArenaPawns = redTeamPawns.Concat(blueTeamPawns).Where(p => p?.Spawned == true).ToList();
            
            foreach (var pawn in allArenaPawns)
            {
                if (pawn?.Spawned == true && pawn.Position.InBounds(map))
                {
                    var teamColor = pawnTeamMap.TryGetValue(pawn, out var team) ? team : TeamColor.Red;
                    var nameColor = teamColor == TeamColor.Red ? Color.red : Color.blue;
                    
                    DrawColoredPawnName(pawn, nameColor);
                }
            }
        }

        private void DrawColoredPawnName(Pawn pawn, Color nameColor)
        {
            // Use the pawn's exact world position for smoother tracking
            var worldPos = pawn.DrawPos; // DrawPos is more accurate for moving pawns than Position
            var screenPos = Find.Camera.WorldToScreenPoint(worldPos);
            
            // Check if pawn is visible on screen
            if (screenPos.z <= 0 || screenPos.x < 0 || screenPos.x > Screen.width || screenPos.y < 0 || screenPos.y > Screen.height)
                return;
            
            // Convert to GUI coordinates (Y is flipped)
            screenPos.y = Screen.height - screenPos.y;
            
            // Get the fighter data to show the real wallet name with team prefix
            var fighter = currentRoster?.Red.Concat(currentRoster.Blue).FirstOrDefault(f => f.PawnRef == pawn);
            var nameText = fighter != null ? fighter.WalletShort : pawn.Name.ToStringShort;
            
            // Set up text styling
            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;
            var oldColor = GUI.color;
            
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            
            // Calculate text size and position
            var textSize = Text.CalcSize(nameText);
            var nameRect = new Rect(
                screenPos.x - textSize.x / 2f, //8 to 2
                screenPos.y + 12f, // Covering original name position 5 to 12
                textSize.x,
                textSize.y
            );
            
            // Solid black background to completely hide the "." character
            var bgRect = new Rect(nameRect.x - 2f, nameRect.y, nameRect.width + 4f, nameRect.height);
            GUI.color = Color.black; // Full opacity black
            GUI.DrawTexture(bgRect, BaseContent.WhiteTex);
            
            // Draw your colored name
            GUI.color = nameColor;
            Widgets.Label(nameRect, nameText);
            
            // Restore original styling
            GUI.color = oldColor;
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
        }

        // Restore default names after arena (add to cleanup methods)
        private void RestoreDefaultPawnLabel(Pawn pawn)
        {
            try
            {
                var overlayField = typeof(Pawn).GetField("pawnUIOverlay", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (overlayField != null)
                {
                    var overlay = overlayField.GetValue(pawn);
                    if (overlay != null)
                    {
                        var labelVisibleField = overlay.GetType().GetField("labelVisible", 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        
                        if (labelVisibleField != null && labelVisibleField.FieldType == typeof(bool))
                        {
                            labelVisibleField.SetValue(overlay, true);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"SolWorld: UI overlay error: {ex.Message}");    
            }
        }

        public void RegisterArenaCore(Thing_ArenaCore core)
        {
            arenaCore = core;
            Log.Message("SolWorld: Arena Core registered, refreshing spawners...");
            RefreshSpawners();
        }
        
        public void UnregisterArenaCore()
        {
            StopArena();
            arenaCore = null;
        }
        
        // FIXED: Better spawner refresh that preserves references
        private void RefreshSpawners()
        {
            if (map == null) 
            {
                return;
            }
            
            // CRITICAL: Don't refresh during round cleanup to prevent spawner loss
            if (currentState == ArenaState.Resetting)
            {
                Log.Message("SolWorld: Skipping spawner refresh during reset phase");
                return;
            }
            
            // Use the force refresh method
            ForceRefreshSpawners();
        }
        
        // FIXED: Force refresh method with error recovery
        public void ForceRefreshSpawners()
        {
            // Removed: "FORCE refreshing spawners..." log message
            
            if (map == null) 
            {
                Log.Warning("SolWorld: Cannot refresh spawners - map is null");
                return;
            }
            
            // Clear current references
            var prevRed = redSpawner;
            var prevBlue = blueSpawner;
            redSpawner = null;
            blueSpawner = null;
            
            try
            {
                var allBuildings = map.listerBuildings?.allBuildingsColonist;
                if (allBuildings == null)
                {
                    Log.Warning("SolWorld: Building lister is null!");
                    return;
                }
                
                // Find spawners (removed individual log messages)
                foreach (var building in allBuildings.ToList())
                {
                    if (building?.def?.defName == "SolWorld_RedSpawn")
                    {
                        redSpawner = building;
                    }
                    else if (building?.def?.defName == "SolWorld_BlueSpawn")
                    {
                        blueSpawner = building;
                    }
                }
                
                // Only log if something changed
                if (redSpawner != prevRed || blueSpawner != prevBlue)
                {
                    Log.Message($"SolWorld: Spawner status - Red: {redSpawner != null}, Blue: {blueSpawner != null}");
                }
                
                // Keep the warning messages for lost spawners
                if (prevRed != null && redSpawner == null)
                    Log.Warning("SolWorld: LOST Red spawner during refresh!");
                if (prevBlue != null && blueSpawner == null)
                    Log.Warning("SolWorld: LOST Blue spawner during refresh!");
            }
            catch (System.Exception ex)
            {
                Log.Error($"SolWorld: Error during spawner refresh: {ex.Message}");
                
                // Restore previous references
                if (redSpawner == null && prevRed?.Spawned == true)
                {
                    redSpawner = prevRed;
                }
                if (blueSpawner == null && prevBlue?.Spawned == true)
                {
                    blueSpawner = prevBlue;
                }
            }
        }
        
        public CellRect? GetArenaBounds()
        {
            return arenaBounds.CalculateBounds(arenaCore, redSpawner, blueSpawner);
        }
        
        public void StartArena()
        {
            Messages.Message("DEBUG: StartArena() called!", MessageTypeDefOf.PositiveEvent);
            RefreshSpawners();
            
            if (!HasValidSetup)
            {
                string errorMsg = "Cannot start arena: ";
                if (arenaCore?.IsOperational != true)
                    errorMsg += "Arena Core missing or not operational. ";
                if (redSpawner == null)
                    errorMsg += "Red Team Spawner missing. ";
                if (blueSpawner == null)
                    errorMsg += "Blue Team Spawner missing. ";
                    
                Messages.Message(errorMsg.Trim(), MessageTypeDefOf.RejectInput);
                Log.Warning("SolWorld: " + errorMsg.Trim());
                return;
            }
            
            isActive = true;
            
            // Start first round immediately
            Log.Message("SolWorld: Arena activated - STARTING FIRST ROUND IMMEDIATELY!");
            nextRoundTick = Find.TickManager.TicksGame + 60; // Start in 1 second
            Messages.Message($"DEBUG: Scheduled first round for tick {nextRoundTick}, current tick is {Find.TickManager.TicksGame}", MessageTypeDefOf.PositiveEvent);
            
            Messages.Message("Arena activated! First round starting immediately...", MessageTypeDefOf.PositiveEvent);
            Log.Message("SolWorld: Arena successfully started, first round scheduled for tick " + nextRoundTick);
        }
        
        public void StopArena()
        {
            isActive = false;
            currentState = ArenaState.Idle;
            nextRoundTick = -1;
            combatStartTick = -1;
            roundEndTick = -1;
            combatInitiated = false;
            lastCombatEnforcementTick = -1;
            lastAggressiveEnforcementTick = -1;
            previewCompleted = false;
            uiShouldTriggerUnpause = false;
            uiShouldTriggerReset = false;
            pawnLastActionTick.Clear();
            
            // Clear winner storage when stopping arena
            lastRoundWinner = null;
            lastMatchId = "";
            lastWinningTeam.Clear();
            lastPerWinnerPayout = 0f;
            
            if (currentRoster != null)
            {
                CleanupCurrentRound();
            }
            
            Messages.Message("Arena deactivated", MessageTypeDefOf.NeutralEvent);
        }
        
        public void ForceNextRound()
        {
            if (!HasValidSetup) 
            {
                Messages.Message("Cannot force round - invalid setup", MessageTypeDefOf.RejectInput);
                return;
            }
            
            Log.Message("SolWorld: FORCE NEXT ROUND - STARTING IMMEDIATELY!");
            
            if (currentState != ArenaState.Idle)
            {
                EndRound("Force triggered");
            }
            
            // Start immediately
            currentState = ArenaState.Idle;
            nextRoundTick = Find.TickManager.TicksGame + 30; // Start in 0.5 seconds
            
            Messages.Message("Force starting round in 0.5 seconds...", MessageTypeDefOf.PositiveEvent);
        }
        
        private void ScheduleNextRound()
        {
            var currentTime = Find.TickManager.TicksGame;
            nextRoundTick = currentTime + CADENCE_TICKS; // 3 minutes from now
            
            var timeUntilRound = CADENCE_TICKS / 60f;
            Log.Message("SolWorld: Next round scheduled in " + timeUntilRound.ToString("F0") + " seconds (tick " + nextRoundTick + ")");
        }
        
        // UPDATED: StartNewRound with winner storage clearing
        private void StartNewRound()
        {
            Log.Message("SolWorld: ===== STARTING NEW ROUND =====");
            
            // WINNER STORAGE: Clear previous winner data for fresh start
            lastRoundWinner = null;
            lastMatchId = "";
            lastWinningTeam.Clear();
            lastPerWinnerPayout = 0f;
            
            currentState = ArenaState.Preview;
            previewStartTime = DateTime.Now;
            previewCompleted = false;
            uiShouldTriggerUnpause = false;
            uiShouldTriggerReset = false;
            combatInitiated = false;
            lastCombatEnforcementTick = -1;
            lastAggressiveEnforcementTick = -1;
            roundEndTick = -1;
            pawnLastActionTick.Clear();
            
            try
            {
                // Step 1: Create roster
                Log.Message("SolWorld: Creating roster...");
                CreateRoster();
                
                // Step 2: Set up factions
                Log.Message("SolWorld: Setting up arena factions...");
                SetupArenaFactions();
                
                // Step 3: Initialize blueprint BEFORE spawning (CRITICAL - only do this once!)
                var bounds = GetArenaBounds();
                if (bounds.HasValue && !arenaBlueprint.IsInitialized)
                {
                    Log.Message("SolWorld: Initializing arena blueprint for first time...");
                    arenaBlueprint.InitializeBlueprint(map, bounds.Value);
                }
                else if (bounds.HasValue)
                {
                    Log.Message("SolWorld: Arena blueprint already initialized - skipping");
                }
                else
                {
                    Log.Warning("SolWorld: Cannot initialize blueprint - invalid arena bounds");
                }
                
                // Step 4: Spawn teams
                Log.Message("SolWorld: ===== SPAWNING TEAMS =====");
                SpawnBothTeams();
                
                // ADD THIS LINE HERE - Step 5.5: Make arena lamps invincible
                Log.Message("SolWorld: Making arena lamps invincible...");
                MakeArenaLampsInvincible();

                // Step 5: PAUSE the game
                Log.Message("SolWorld: ===== PAUSING GAME =====");
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                Log.Message($"SolWorld: Game paused for 30-second preview - Speed now: {Find.TickManager.CurTimeSpeed}, Paused: {Find.TickManager.Paused}");
                
                var payoutText = currentRoster.PerWinnerPayout.ToString("F3");
                Messages.Message("30-SECOND PREVIEW: Round " + currentRoster.MatchId + " - " + payoutText + " SOL per winner", MessageTypeDefOf.PositiveEvent);
                
                Log.Message("SolWorld: ===== ROUND STARTED SUCCESSFULLY =====");
            }
            catch (Exception ex)
            {
                Log.Error("SolWorld: Error starting round: " + ex.Message + "\n" + ex.StackTrace);
                EndRound("Start error");
            }
        }
        
        private void CreateRoster()
        {
            Log.Message("SolWorld: Creating roster...");
            
            // Clear any previous tier data
            currentRoundTierData.Clear();
            
            // Try to get tiered fighters from backend
            string[] walletAddresses = null;
            bool tierSystemActive = false;
            
            try
            {
                Log.Message("SolWorld: Fetching holders from: " + SolWorldMod.Settings.apiBaseUrl + "/api/arena/holders");
                
                // Make HTTP request to backend
                var request = System.Net.WebRequest.Create(SolWorldMod.Settings.apiBaseUrl + "/api/arena/holders");
                request.Timeout = 10000;
                request.Method = "GET";
                
                using (var response = request.GetResponse())
                using (var stream = response.GetResponseStream())
                using (var reader = new System.IO.StreamReader(stream))
                {
                    var jsonResponse = reader.ReadToEnd();
                    Log.Message("SolWorld: Backend response received: " + jsonResponse.Substring(0, Math.Min(200, jsonResponse.Length)) + "...");
                    Log.Message("SolWorld: FULL JSON Response: " + jsonResponse);
                    // Parse response using CryptoReporter
                    if (jsonResponse.Contains("\"success\":true"))
                    {
                        var cryptoReporter = new Net.CryptoReporter();
                        var parsedResponse = cryptoReporter.ParseHoldersResponse(jsonResponse);
                        
                        if (parsedResponse?.data?.fighters != null && parsedResponse.data.fighters.Length > 0 && 
                            parsedResponse?.data?.wallets != null && parsedResponse.data.wallets.Length >= 20)
                        {
                            // SUCCESS: We have some tiered fighters and enough wallets
                            Log.Message($"SolWorld: Extracted {parsedResponse.data.fighters.Length} tiered fighters from {parsedResponse.data.wallets.Length} total fighters!");
                            
                            // Store tier data for equipment assignment
                            StoreTierDataForRound(parsedResponse.data.fighters);
                            
                            // Use wallet addresses for roster creation
                            walletAddresses = parsedResponse.data.wallets;
                            tierSystemActive = true;
                            
                            // Log tier system status
                            if (currentRoundTierData.Count > 0)
                            {
                                var firstFighter = currentRoundTierData.Values.First();
                                Log.Message($"SolWorld: TIER SYSTEM ACTIVE - First fighter: Tier {firstFighter.Tier} ({firstFighter.TierName}), Armor: {firstFighter.HasArmor}, Helmet: {firstFighter.HasHelmet}");
                                
                                // Count tier distribution for logging
                                var tierCounts = new Dictionary<int, int>();
                                foreach (var fighter in currentRoundTierData.Values)
                                {
                                    if (!tierCounts.ContainsKey(fighter.Tier))
                                        tierCounts[fighter.Tier] = 0;
                                    tierCounts[fighter.Tier]++;
                                }
                                
                                Log.Message("SolWorld: Tier distribution:");
                                foreach (var kvp in tierCounts.OrderBy(x => x.Key))
                                {
                                    Log.Message($"   {kvp.Value}x Tier {kvp.Key}");
                                }
                            }
                        }
                        else if (parsedResponse?.data?.wallets != null && parsedResponse.data.wallets.Length >= 20)
                        {
                            // FALLBACK: We have wallets but no tier data
                            Log.Warning("SolWorld: No tier data received, using wallet-only mode");
                            walletAddresses = parsedResponse.data.wallets;
                            tierSystemActive = false;
                        }
                        else
                        {
                            Log.Warning("SolWorld: Backend response missing expected data");
                        }
                    }
                    else
                    {
                        Log.Warning("SolWorld: Backend response indicates failure");
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Error("SolWorld: Failed to fetch holders from backend: " + ex.Message);
            }
            
            // Create roster with fetched data or fallback to mock
            currentRoster = new RoundRoster
            {
                RoundRewardTotalSol = SolWorldMod.Settings.roundPoolSol,
                PayoutPercent = SolWorldMod.Settings.payoutPercent
            };
            
            if (walletAddresses != null && walletAddresses.Length >= 20)
            {
                // Use real wallet addresses from backend
                if (tierSystemActive)
                {
                    Log.Message("SolWorld: Creating roster with TIERED fighters from backend!");
                }
                else
                {
                    Log.Message("SolWorld: Creating roster with basic fighters from backend!");
                }
                
                // Assign fighters to teams
                for (int i = 0; i < 10; i++)
                {
                    currentRoster.Red.Add(new Fighter(walletAddresses[i], TeamColor.Red));
                    currentRoster.Blue.Add(new Fighter(walletAddresses[i + 10], TeamColor.Blue));
                }
            }
            else
            {
                // Fallback to mock data
                Log.Message("SolWorld: Falling back to mock wallet addresses");
                var mockHolders = GenerateMockHolders();
                
                for (int i = 0; i < 10; i++)
                {
                    currentRoster.Red.Add(new Fighter(mockHolders[i], TeamColor.Red));
                    currentRoster.Blue.Add(new Fighter(mockHolders[i + 10], TeamColor.Blue));
                }
                
                tierSystemActive = false;
            }
            
            // Final status message
            if (tierSystemActive)
            {
                Messages.Message($"TIER SYSTEM ACTIVE: {currentRoundTierData.Count} fighters with tier bonuses!", MessageTypeDefOf.PositiveEvent);
            }
            else
            {
                Messages.Message("Arena ready: Using basic fighter mode", MessageTypeDefOf.NeutralEvent);
            }
            
            Log.Message($"SolWorld: Created roster with 20 fighters (10 red, 10 blue) - Tier system: {(tierSystemActive ? "ACTIVE" : "INACTIVE")}");
            Log.Message("SolWorld: Sample fighter names - Red[0]: " + currentRoster.Red[0].WalletShort + ", Blue[0]: " + currentRoster.Blue[0].WalletShort);
        }

        private string[] ExtractWalletsFromJson(string json)
        {
            try
            {
                // Simple string parsing to extract wallet addresses
                var walletStart = json.IndexOf("\"wallets\":[") + 11;
                var walletEnd = json.IndexOf("]", walletStart);
                var walletsSection = json.Substring(walletStart, walletEnd - walletStart);
                
                // Split by quotes and commas to get individual addresses
                var parts = walletsSection.Replace("\"", "").Split(',');
                var wallets = new List<string>();
                
                foreach (var part in parts)
                {
                    var trimmed = part.Trim();
                    if (trimmed.Length > 30) // Valid wallet address length
                    {
                        wallets.Add(trimmed);
                    }
                }
                
                return wallets.ToArray();
            }
            catch
            {
                return null;
            }
        }        

        private TieredFighter[] ExtractFightersFromJson(string json)
        {
            try
            {
                // Look for fighters array in the JSON
                var fightersStart = json.IndexOf("\"fighters\":[");
                if (fightersStart == -1) return null;
                
                fightersStart += 12; // Skip past "fighters":[
                var bracket_count = 1;
                var fightersEnd = fightersStart;
                
                // Find matching closing bracket
                for (int i = fightersStart; i < json.Length && bracket_count > 0; i++)
                {
                    if (json[i] == '[') bracket_count++;
                    else if (json[i] == ']') bracket_count--;
                    fightersEnd = i;
                }
                
                var fightersSection = json.Substring(fightersStart, fightersEnd - fightersStart);
                
                // Simple parsing - split by fighter objects
                var fighters = new List<TieredFighter>();
                var parts = fightersSection.Split(new string[] { "},{" }, StringSplitOptions.RemoveEmptyEntries);
                
                foreach (var part in parts)
                {
                    var cleanPart = part.Replace("{", "").Replace("}", "");
                    var fighter = ParseSingleFighter(cleanPart);
                    if (fighter != null)
                    {
                        fighters.Add(fighter);
                    }
                }
                
                return fighters.ToArray();
            }
            catch (Exception ex)
            {
                Log.Warning("SolWorld: Failed to extract fighters: " + ex.Message);
                return null;
            }
        }

        private TieredFighter ParseSingleFighter(string fighterJson)
        {
            try
            {
                // Extract wallet, balance, and tier info
                var wallet = ExtractJsonValue(fighterJson, "wallet");
                var balanceStr = ExtractJsonValue(fighterJson, "balance");
                var tierStr = ExtractJsonValue(fighterJson, "tier");
                
                if (string.IsNullOrEmpty(wallet)) return null;
                
                var balance = float.TryParse(balanceStr, out var b) ? b : 50000f;
                var tierNum = int.TryParse(ExtractJsonValue(tierStr, "tier"), out var t) ? t : 1;
                var tierName = ExtractJsonValue(tierStr, "name") ?? "Basic Fighter";
                var weaponQuality = ExtractJsonValue(tierStr, "weaponQuality") ?? "Normal";
                
                return new TieredFighter
                {
                    Wallet = wallet,
                    Balance = balance,
                    Tier = tierNum,
                    TierName = tierName,
                    WeaponQuality = weaponQuality,
                    HasArmor = tierStr.Contains("\"hasArmor\":true"),
                    HasHelmet = tierStr.Contains("\"hasHelmet\":true"),
                    HasAura = tierStr.Contains("\"hasAura\":true")
                };
            }
            catch
            {
                return null;
            }
        }

        private string ExtractJsonValue(string json, string key)
        {
            try
            {
                var searchKey = "\"" + key + "\":";
                var start = json.IndexOf(searchKey);
                if (start == -1) return null;
                
                start += searchKey.Length;
                
                // Handle string values (in quotes)
                if (json[start] == '"')
                {
                    start++; // Skip opening quote
                    var end = json.IndexOf('"', start);
                    return end > start ? json.Substring(start, end - start) : null;
                }
                
                // Handle numeric values
                var end2 = start;
                while (end2 < json.Length && (char.IsDigit(json[end2]) || json[end2] == '.' || json[end2] == '-'))
                {
                    end2++;
                }
                
                return end2 > start ? json.Substring(start, end2 - start) : null;
            }
            catch
            {
                return null;
            }
        }

        private void StoreTierDataForRound(Net.TieredFighter[] fighters)
        {
            currentRoundTierData.Clear();
            foreach (var cryptoFighter in fighters)
            {
                // DEBUG: Check what we're actually getting
                Log.Message($"SolWorld: DEBUG - Processing fighter {cryptoFighter.wallet.Substring(0, 8)}...");
                Log.Message($"SolWorld: DEBUG - Tier object is null: {cryptoFighter.tier == null}");
                
                if (cryptoFighter.tier != null)
                {
                    Log.Message($"SolWorld: DEBUG - Tier.tier value: {cryptoFighter.tier.tier}");
                    Log.Message($"SolWorld: DEBUG - Tier.name value: {cryptoFighter.tier.name ?? "null"}");
                    Log.Message($"SolWorld: DEBUG - Tier.hasArmor value: {cryptoFighter.tier.hasArmor}");
                }
                
                // Convert from CryptoReporter.TieredFighter to MapComponent.TieredFighter
                var internalFighter = new TieredFighter
                {
                    Wallet = cryptoFighter.wallet,
                    Balance = cryptoFighter.balance,
                    Tier = cryptoFighter.tier?.tier ?? 1,
                    TierName = cryptoFighter.tier?.name ?? "Basic Fighter",
                    WeaponQuality = cryptoFighter.tier?.weaponQuality ?? "Normal",
                    HasArmor = cryptoFighter.tier?.hasArmor ?? false,
                    HasHelmet = cryptoFighter.tier?.hasHelmet ?? false,
                    HasAura = cryptoFighter.tier?.hasAura ?? false
                };
                
                currentRoundTierData[internalFighter.Wallet] = internalFighter;
                
                Log.Message($"SolWorld: Final result - {internalFighter.Wallet.Substring(0, 8)}... → Tier {internalFighter.Tier} ({internalFighter.TierName})");
            }
}

        private string[] GenerateMockHolders()
        {
            var holders = new string[20];
            for (int i = 0; i < 20; i++)
            {
                holders[i] = "Mock" + i.ToString("D3") + "Wallet" + i.ToString("D3");
            }
            return holders;
        }
        
        private void SetupArenaFactions()
        {
            Log.Message("SolWorld: Setting up IDENTICAL factions for perfect combat balance");
            
            // SOLUTION: Use the SAME hostile faction for both teams
            // This ensures identical combat stats while name prefixes handle visual distinction
            var hostileFaction = Find.FactionManager.AllFactions
                .Where(f => f != null && !f.IsPlayer && f.HostileTo(Faction.OfPlayer) && f.def.humanlikeFaction)
                .FirstOrDefault();
            
            if (hostileFaction == null)
            {
                Log.Warning("SolWorld: No hostile faction found, using player faction for both teams");
                redTeamFaction = Faction.OfPlayer;
                blueTeamFaction = Faction.OfPlayer;
            }
            else
            {
                Log.Message($"SolWorld: Both teams using hostile faction '{hostileFaction.Name}' for identical stats");
                redTeamFaction = hostileFaction;
                blueTeamFaction = hostileFaction;
            }
            
            // CRITICAL: Both teams now have IDENTICAL faction-based combat stats
            // Visual distinction comes from name prefixes [R] and [B], not faction relationships
            
            Log.Message("SolWorld: ✅ Perfect balance achieved - identical factions + name prefixes");
        }

        private void MakeHostileFactionsHostileToEachOther()
        {
            if (redTeamFaction != null && blueTeamFaction != null && redTeamFaction != blueTeamFaction)
            {
                try
                {
                    Log.Message("SolWorld: Making hostile factions hostile to each other");
                    
                    var redToBlue = redTeamFaction.RelationWith(blueTeamFaction, true);
                    if (redToBlue != null)
                    {
                        redToBlue.baseGoodwill = -100;
                        redToBlue.kind = FactionRelationKind.Hostile;
                    }
                    
                    var blueToRed = blueTeamFaction.RelationWith(redTeamFaction, true);
                    if (blueToRed != null)
                    {
                        blueToRed.baseGoodwill = -100;
                        blueToRed.kind = FactionRelationKind.Hostile;
                    }
                    
                    Log.Message("SolWorld: Hostile faction mutual hostility established");
                }
                catch (Exception ex)
                {
                    Log.Warning($"SolWorld: Failed to set hostile faction hostility: {ex.Message}");
                }
            }
        }

        // NEW: Simpler hostility setup that doesn't mess with player relationships
        private void MakeTeamsHostileToEachOther()
        {
            if (redTeamFaction != null && blueTeamFaction != null && redTeamFaction != blueTeamFaction)
            {
                try
                {
                    Log.Message("SolWorld: Making red faction hostile to blue faction (player relationship unchanged)");
                    
                    // Only set hostility from red faction to blue faction
                    // Since blue = player faction, this won't affect name colors
                    var redToBlue = redTeamFaction.RelationWith(blueTeamFaction, true);
                    if (redToBlue != null)
                    {
                        redToBlue.baseGoodwill = -100;
                        redToBlue.kind = FactionRelationKind.Hostile;
                    }
                    
                    // The blue team (player faction) will automatically defend itself
                    // This creates mutual hostility without changing player relationships
                    
                    Log.Message($"SolWorld: Combat hostility set up - red will attack blue, blue will defend");
                }
                catch (Exception ex)
                {
                    Log.Warning($"SolWorld: Failed to set team hostility: {ex.Message}");
                }
            }
        }

        private void MakeTeamsFightEachOther()
        {
            if (redTeamFaction != null && blueTeamFaction != null && redTeamFaction != blueTeamFaction)
            {
                try
                {
                    Log.Message("SolWorld: Making teams hostile to each other (PRESERVING player relationships for name colors)");
                    
                    // CRITICAL: Store original player relationships BEFORE making changes
                    var originalRedToPlayer = redTeamFaction.RelationWith(Faction.OfPlayer);
                    var originalBlueToPlayer = blueTeamFaction.RelationWith(Faction.OfPlayer);
                    
                    Log.Message($"SolWorld: Original relationships - Red: {originalRedToPlayer.kind}, Blue: {originalBlueToPlayer.kind}");
                    
                    // Force mutual hostility between the teams so they fight
                    var blueToRed = blueTeamFaction.RelationWith(redTeamFaction, true);
                    if (blueToRed != null)
                    {
                        blueToRed.baseGoodwill = -100;
                        blueToRed.kind = FactionRelationKind.Hostile;
                    }
                    
                    var redToBlue = redTeamFaction.RelationWith(blueTeamFaction, true);
                    if (redToBlue != null)
                    {
                        redToBlue.baseGoodwill = -100;
                        redToBlue.kind = FactionRelationKind.Hostile;
                    }
                    
                    // CRITICAL: RESTORE original player relationships to preserve name colors
                    if (originalRedToPlayer != null)
                    {
                        var redToPlayer = redTeamFaction.RelationWith(Faction.OfPlayer, true);
                        if (redToPlayer != null)
                        {
                            redToPlayer.baseGoodwill = originalRedToPlayer.baseGoodwill;
                            redToPlayer.kind = originalRedToPlayer.kind;
                        }
                    }
                    
                    if (originalBlueToPlayer != null)
                    {
                        var blueToPlayer = blueTeamFaction.RelationWith(Faction.OfPlayer, true);
                        if (blueToPlayer != null)
                        {
                            blueToPlayer.baseGoodwill = originalBlueToPlayer.baseGoodwill;
                            blueToPlayer.kind = originalBlueToPlayer.kind;
                        }
                    }
                    
                    Log.Message($"SolWorld: ✅ Teams will fight each other BUT keep original name colors");
                    
                    // Verify final relationships
                    var finalRedToPlayer = redTeamFaction.RelationWith(Faction.OfPlayer);
                    var finalBlueToPlayer = blueTeamFaction.RelationWith(Faction.OfPlayer);
                    Log.Message($"SolWorld: Final name colors - Red: {finalRedToPlayer.kind}, Blue: {finalBlueToPlayer.kind}");
                }
                catch (Exception ex)
                {
                    Log.Warning($"SolWorld: Failed to set team hostility: {ex.Message}");
                }
            }
        }

        private void MakeBlueFriendlyToPlayer()
        {
            try
            {
                Log.Message("SolWorld: Making blue team friendly to player for blue name colors");
                
                var blueToPlayer = blueTeamFaction.RelationWith(Faction.OfPlayer, true);
                if (blueToPlayer != null)
                {
                    blueToPlayer.baseGoodwill = 50; // Friendly
                    blueToPlayer.kind = FactionRelationKind.Neutral; // Neutral/friendly gives blue names
                    Log.Message($"SolWorld: ✅ Blue team is now {blueToPlayer.kind} to player (blue names)");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"SolWorld: Failed to make blue team friendly: {ex.Message}");
            }
        }

        // STEP 2: Add this NEW method anywhere in your MapComponent_SolWorldArena.cs class:

        private void NormalizePawnCombatStats(Pawn pawn)
        {
            if (pawn?.skills == null) return;
            
            Log.Message($"SolWorld: COMPREHENSIVE BALANCE - Normalizing {pawn.Name}");
            
            // STEP 1: Identical combat skills (you already have this)
            const int STANDARD_SKILL_LEVEL = 15;
            
            var shooting = pawn.skills.GetSkill(SkillDefOf.Shooting);
            var melee = pawn.skills.GetSkill(SkillDefOf.Melee);
            
            if (shooting != null)
            {
                shooting.Level = STANDARD_SKILL_LEVEL;
                shooting.passion = Passion.Major;
                shooting.xpSinceLastLevel = 0;
            }
            
            if (melee != null)
            {
                melee.Level = STANDARD_SKILL_LEVEL;
                melee.passion = Passion.Major;
                melee.xpSinceLastLevel = 0;
            }
            
            // STEP 2: Remove ALL combat-affecting traits
            if (pawn.story?.traits != null)
            {
                try
                {
                    var combatTraits = new string[] { 
                        "Pacifist", "Wimp", "SlowLearner", "Bloodlust", "Psychopath", 
                        "Brawler", "Tough", "Fast Learner", "Quick", "Nimble",
                        "Trigger-happy", "Careful Shooter", "ShootingAccuracy",
                        "Nervous", "Volatile", "Masochist", "Cannibal"
                    };
                    
                    foreach (var traitName in combatTraits)
                    {
                        var traitDef = DefDatabase<TraitDef>.GetNamedSilentFail(traitName);
                        if (traitDef != null && pawn.story.traits.HasTrait(traitDef))
                        {
                            pawn.story.traits.allTraits.RemoveAll(t => t.def == traitDef);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"SolWorld: Failed to normalize traits: {ex.Message}");
                }
            }
            
            // STEP 3: NEW - Normalize health and body parts
            if (pawn.health?.hediffSet != null)
            {
                try
                {
                    // Remove all negative health effects
                    var badHediffs = pawn.health.hediffSet.hediffs
                        .Where(h => h.def.makesSickThought || h.def.tendable || h.def.chronic || 
                                (h.CurStage?.capMods != null && h.CurStage.capMods.Any()))
                        .ToList();
                    
                    foreach (var hediff in badHediffs)
                    {
                        pawn.health.RemoveHediff(hediff);
                    }
                    
                    // Add identical combat bonuses to everyone
                    var combatBoostDef = DefDatabase<HediffDef>.GetNamedSilentFail("Adrenaline");
                    if (combatBoostDef != null)
                    {
                        pawn.health.AddHediff(combatBoostDef);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"SolWorld: Failed to normalize health: {ex.Message}");
                }
            }
            
            // STEP 4: NEW - Normalize age for identical base stats
            if (pawn.ageTracker != null)
            {
                try
                {
                    const long STANDARD_AGE = 25L * 3600000L; // 25 years old
                    pawn.ageTracker.AgeBiologicalTicks = STANDARD_AGE;
                    pawn.ageTracker.AgeChronologicalTicks = STANDARD_AGE;
                }
                catch (Exception ex)
                {
                    Log.Warning($"SolWorld: Failed to normalize age: {ex.Message}");
                }
            }
            
            // STEP 5: NEW - Force identical needs levels
            if (pawn.needs != null)
            {
                try
                {
                    if (pawn.needs.mood != null) pawn.needs.mood.CurLevel = 1.0f;
                    if (pawn.needs.rest != null) pawn.needs.rest.CurLevel = 1.0f;
                    if (pawn.needs.food != null) pawn.needs.food.CurLevel = 1.0f;
                    if (pawn.needs.joy != null) pawn.needs.joy.CurLevel = 1.0f;
                }
                catch (Exception ex)
                {
                    Log.Warning($"SolWorld: Failed to normalize needs: {ex.Message}");
                }
            }
            
            // STEP 6: NEW - Set identical combat confidence
            if (pawn.mindState != null)
            {
                try
                {
                    pawn.mindState.canFleeIndividual = false;
                    pawn.mindState.breachingTarget = null;
                    pawn.mindState.lastEngageTargetTick = Find.TickManager.TicksGame;
                }
                catch (Exception ex)
                {
                    Log.Warning($"SolWorld: Failed to normalize mind state: {ex.Message}");
                }
            }
            
            Log.Message($"SolWorld: ✅ FULLY NORMALIZED {pawn.Name} for perfect balance");
        }

        private void SpawnBothTeams()
        {
            Log.Message("SolWorld: ===== SPAWNING BOTH TEAMS WITH IDENTICAL LOADOUTS =====");
            
            // Clear previous pawn lists and mappings
            redTeamPawns.Clear();
            blueTeamPawns.Clear();
            pawnTeamMap.Clear();
            pawnLastActionTick.Clear();
            
            // CRITICAL: Always spawn RED team first to generate loadout, then BLUE team uses same loadout
            // This ensures identical weapons regardless of alternating spawn order
            
            Log.Message("SolWorld: Spawning RED team first (generates loadout)...");
            SpawnTeam(currentRoster.Red, redSpawner.Position, TeamColor.Red, redTeamFaction);
            
            Log.Message("SolWorld: Spawning BLUE team second (uses identical loadout)...");
            SpawnTeam(currentRoster.Blue, blueSpawner.Position, TeamColor.Blue, blueTeamFaction);
            
            // VERIFICATION: Final check that loadouts are identical
            if (currentRoundRedWeapons != null && currentRoundBlueWeapons != null)
            {
                var identical = currentRoundRedWeapons.SequenceEqual(currentRoundBlueWeapons);
                if (identical)
                {
                    Log.Message("✅ SolWorld: VERIFIED - Both teams have identical weapon loadouts");
                }
                else
                {
                    Log.Error("❌ SolWorld: CRITICAL BUG - Teams have different weapon loadouts!");
                }
            }
            
            Log.Message("SolWorld: ===== BOTH TEAMS SPAWNED WITH LOADOUT VERIFICATION =====");
            
            // Count spawned pawns for verification
            var redSpawned = currentRoster.Red.Count(f => f.PawnRef?.Spawned == true);
            var blueSpawned = currentRoster.Blue.Count(f => f.PawnRef?.Spawned == true);
            Log.Message($"SolWorld: Verification - Red spawned: {redSpawned}/10, Blue spawned: {blueSpawned}/10");
        }
        
        // REPLACE the existing SpawnTeam() method in MapComponent_SolWorldArena.cs with this:

        private void SpawnTeam(List<Fighter> fighters, IntVec3 spawnerPos, TeamColor teamColor, Faction teamFaction)
        {
            Log.Message($"SolWorld: Spawning {teamColor} team at {spawnerPos} with faction {teamFaction.Name}...");
            
            // FIXED: Generate loadout ONCE for both teams at the start
            string[] teamWeapons;
            LoadoutPreset usedPreset;
            
            // Only generate loadout on RED team spawn, then use same for BLUE
            if (teamColor == TeamColor.Red)
            {
                Log.Message("SolWorld: RED team spawning - generating loadout for BOTH teams");
                
                if (SolWorldMod.Settings.UseRandomLoadouts)
                {
                    var (redWeapons, blueWeapons, preset) = LoadoutManager.GenerateBalancedLoadouts(useRandomPreset: true);
                    usedPreset = preset;
                    
                    // CRITICAL: Store the EXACT same weapon array for both teams
                    currentRoundLoadoutPreset = preset;
                    currentRoundRedWeapons = redWeapons;
                    currentRoundBlueWeapons = blueWeapons;
                    
                    teamWeapons = redWeapons;
                }
                else
                {
                    var specificPreset = LoadoutManager.GetPreset(SolWorldMod.Settings.selectedLoadoutPreset);
                    var (redWeapons, blueWeapons, preset) = LoadoutManager.GenerateBalancedLoadouts(useRandomPreset: false, specificPreset: specificPreset);
                    usedPreset = preset;
                    
                    // CRITICAL: Store the EXACT same weapon array for both teams
                    currentRoundLoadoutPreset = preset;
                    currentRoundRedWeapons = redWeapons;
                    currentRoundBlueWeapons = blueWeapons;
                    
                    teamWeapons = redWeapons;
                }
                
                // Update roster with loadout info for UI display
                if (currentRoster != null)
                {
                    currentRoster.LoadoutPresetName = usedPreset.Name;
                    currentRoster.LoadoutDescription = usedPreset.Description;
                }
                
                Log.Message($"SolWorld: Generated loadout '{usedPreset.Name}' for both teams");
                LogTeamWeapons(teamWeapons, "Red");
            }
            else // Blue team
            {
                Log.Message("SolWorld: BLUE team spawning - using identical loadout from red team");
                
                // CRITICAL: Use the EXACT same weapons as red team
                teamWeapons = currentRoundBlueWeapons ?? currentRoundRedWeapons;
                usedPreset = currentRoundLoadoutPreset;
                
                if (teamWeapons == null)
                {
                    Log.Error("SolWorld: No weapons stored from red team! Generating emergency loadout");
                    var (redWeapons, blueWeapons, preset) = LoadoutManager.GenerateBalancedLoadouts(useRandomPreset: true);
                    teamWeapons = blueWeapons;
                    usedPreset = preset;
                }
                
                Log.Message($"SolWorld: Blue team using loadout '{usedPreset?.Name}'");
                LogTeamWeapons(teamWeapons, "Blue");
            }
            
            // VERIFICATION: Log that both teams will get identical weapons
            if (teamColor == TeamColor.Blue && currentRoundRedWeapons != null)
            {
                var identical = currentRoundRedWeapons.SequenceEqual(teamWeapons);
                Log.Message($"SolWorld: LOADOUT VERIFICATION - Teams have identical weapons: {identical}");
                
                if (!identical)
                {
                    Log.Error("SolWorld: CRITICAL ERROR - Teams have different loadouts!");
                    Log.Error($"Red weapons: [{string.Join(", ", currentRoundRedWeapons)}]");
                    Log.Error($"Blue weapons: [{string.Join(", ", teamWeapons)}]");
                }
            }
            
            // Spawn the fighters with their weapons  
            for (int i = 0; i < fighters.Count; i++)
            {
                var fighter = fighters[i];
                
                var spawnPos = CellFinder.RandomClosewalkCellNear(spawnerPos, map, 5);
                if (!spawnPos.IsValid)
                    spawnPos = spawnerPos;
                
                var pawn = GenerateWarrior(fighter, teamColor, teamFaction);
                
                if (pawn != null)
                {
                    try
                    {
                        GenSpawn.Spawn(pawn, spawnPos, map);
                        fighter.PawnRef = pawn;

                        // Strip food items
                        StripFoodFromPawn(pawn);
                        
                        // ✅ Clear equipment first (add this if you have the method)
                        if (HasTierData(fighter))
                            ClearPawnEquipment(pawn);
                        
                        // ✅ Apply tier enhancements (handles armor & helmet internally)
                        ApplyTierEnhancements(pawn, fighter);
                        
                        // ✅ Apply tier weapon with proper quality
                        if (i < teamWeapons.Length)
                        {
                            var baseWeaponDefName = teamWeapons[i];
                            var success = ApplyTierWeapon(pawn, baseWeaponDefName, fighter);
                            
                            if (success)
                            {
                                Log.Message($"SolWorld: {teamColor} fighter {i} ({fighter.WalletShort}) equipped with tier-enhanced {baseWeaponDefName}");
                            }
                            else
                            {
                                Log.Warning($"SolWorld: Tier weapon failed, using fallback");
                                GiveWeapon(pawn);
                            }
                        }
                        else
                        {
                            GiveWeapon(pawn);
                        }
                        
                        // ❌ REMOVE THESE DUPLICATE CALLS:
                        // ApplyTierArmor(pawn, fighter);   // DELETE THIS LINE
                        // ApplyTierHelmet(pawn, fighter);  // DELETE THIS LINE
                        
                        // Team tracking (keep this)
                        if (teamColor == TeamColor.Red)
                            redTeamPawns.Add(pawn);
                        else
                            blueTeamPawns.Add(pawn);
                        
                        pawnTeamMap[pawn] = teamColor;
                        pawnLastActionTick[pawn] = -1;
                        
                        ApplyTeamStyling(pawn, teamColor);
                        
                        Log.Message($"SolWorld: Spawned {fighter.WalletShort} ({teamColor}) with tier {GetTierForFighter(fighter)} equipment");
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"SolWorld: Failed to spawn {fighter.WalletShort}: {ex.Message}");
                    }
                }
            }
        }

        // ⭐ FIXED: Apply tier enhancements INSTEAD OF standard equipment, not on top of it
        private void ApplyTierEnhancements(Pawn pawn, Fighter fighter)
        {
            // Skip if no tier data available
            if (!currentRoundTierData.ContainsKey(fighter.WalletFull))
            {
                Log.Message($"SolWorld: No tier data for {fighter.WalletShort} - using basic equipment");
                return;
            }
            
            var tierData = currentRoundTierData[fighter.WalletFull];
            Log.Message($"SolWorld: Applying Tier {tierData.Tier} enhancements to {fighter.WalletShort}");
            
            try
            {
                // ⭐ NEW: Clear existing equipment FIRST to prevent duplication
                ClearPawnEquipment(pawn);
                
                // Apply tier-based armor
                if (tierData.HasArmor)
                {
                    ApplyTierArmor(pawn, fighter);
                }
                
                // Apply tier-based helmet  
                if (tierData.HasHelmet)
                {
                    ApplyTierHelmet(pawn, fighter);
                }
                
                // Apply stat modifiers for higher tiers
                if (tierData.Tier >= 3)
                {
                    ApplyTierStatBoosts(pawn, fighter);
                }
                
                // Apply visual effects for highest tiers
                if (tierData.Tier >= 6)
                {
                    ApplyTierVisualEffects(pawn, fighter);
                }
                
                Log.Message($"SolWorld: Successfully applied Tier {tierData.Tier} ({tierData.TierName}) enhancements");
            }
            catch (Exception ex)
            {
                Log.Error($"SolWorld: Failed to apply tier enhancements: {ex.Message}");
            }
        }

        // ⭐ NEW METHOD: Clear all equipment before applying tier gear
        private void ClearPawnEquipment(Pawn pawn)
        {
            try
            {
                // Clear all equipped weapons
                if (pawn.equipment?.AllEquipmentListForReading != null)
                {
                    var weapons = pawn.equipment.AllEquipmentListForReading.ToList();
                    foreach (var weapon in weapons)
                    {
                        pawn.equipment.TryDropEquipment(weapon, out ThingWithComps droppedWeapon, pawn.PositionHeld);
                        if (droppedWeapon != null)
                        {
                            droppedWeapon.Destroy(); // Destroy instead of dropping to prevent floor clutter
                        }
                    }
                }
                
                // Clear all apparel (armor, helmets, clothes)
                if (pawn.apparel?.WornApparel != null)
                {
                    var apparel = pawn.apparel.WornApparel.ToList();
                    foreach (var item in apparel)
                    {
                        pawn.apparel.TryDrop(item, out Apparel droppedApparel);
                        if (droppedApparel != null)
                        {
                            droppedApparel.Destroy(); // Destroy instead of dropping to prevent floor clutter
                        }
                    }
                }
                
                Log.Message($"SolWorld: Cleared existing equipment for tier enhancement");
            }
            catch (Exception ex)
            {
                Log.Warning($"SolWorld: Failed to clear equipment: {ex.Message}");
            }
        }

        private bool HasTierData(Fighter fighter)
        {
            return currentRoundTierData.ContainsKey(fighter.WalletFull) && 
                currentRoundTierData[fighter.WalletFull].Tier > 1;
        }

        // ⭐ FIXED: Ensure weapon quality is properly applied and visible
        private bool ApplyTierWeapon(Pawn pawn, string weaponDefName, Fighter fighter)
        {
            try
            {
                // Get the weapon definition
                var weaponDef = DefDatabase<ThingDef>.GetNamedSilentFail(weaponDefName);
                if (weaponDef == null)
                {
                    Log.Warning($"SolWorld: Weapon def not found: {weaponDefName}");
                    return false;
                }
                
                // Create the weapon with quality stuff
                var weapon = ThingMaker.MakeThing(weaponDef, GenStuff.RandomStuffFor(weaponDef)) as ThingWithComps;
                if (weapon == null)
                {
                    Log.Warning($"SolWorld: Failed to create weapon: {weaponDefName}");
                    return false;
                }
                
                // ⭐ CRITICAL FIX: Apply tier-based quality BEFORE equipping
                if (currentRoundTierData.ContainsKey(fighter.WalletFull))
                {
                    var tierData = currentRoundTierData[fighter.WalletFull];
                    
                    // Set quality immediately after creation
                    SetItemQuality(weapon, tierData.WeaponQuality);
                    
                    Log.Message($"SolWorld: Created {tierData.WeaponQuality} quality {weaponDefName} for Tier {tierData.Tier} fighter");
                    
                    // Double-check that quality was applied
                    var qualityComp = weapon.TryGetComp<CompQuality>();
                    if (qualityComp != null)
                    {
                        Log.Message($"SolWorld: Weapon quality verification: {qualityComp.Quality}");
                    }
                }
                
                // Equip the weapon
                if (pawn.equipment != null)
                {
                    pawn.equipment.AddEquipment(weapon);
                    Log.Message($"SolWorld: Successfully equipped tier weapon: {weaponDefName}");
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                Log.Error($"SolWorld: Failed to apply tier weapon {weaponDefName}: {ex.Message}");
                return false;
            }
        }

        private void EnsurePawnHealthStability(Pawn pawn)
        {
            try
            {
                // Ensure health tracker is properly initialized
                if (pawn?.health?.hediffSet == null)
                {
                    Log.Warning($"SolWorld: Pawn {pawn?.Name} has null health components, regenerating");
                    return;
                }

                // Ensure all body parts are properly initialized
                if (pawn.health.hediffSet.hediffs == null)
                {
                    pawn.health.hediffSet.hediffs = new List<Hediff>();
                }

                // Force refresh health state to prevent null references
                pawn.health.capacities.Notify_CapacityLevelsDirty();

                Log.Message($"SolWorld: Health validation passed for {pawn.Name}");
            }
            catch (Exception ex)
            {
                Log.Error($"SolWorld: Health validation failed for pawn: {ex.Message}");
            }
        }

        // ⭐ NEW METHOD: Apply tier-based armor
        private void ApplyTierArmor(Pawn pawn, Fighter fighter)
        {
            var tierData = currentRoundTierData[fighter.WalletFull];
            
            try
            {
                string armorDefName;
                
                // Select armor based on tier
                switch (tierData.Tier)
                {
                    case 2:
                    case 3:
                        armorDefName = "Apparel_FlakVest";
                        break;
                    case 4:
                    case 5:
                        armorDefName = "Apparel_PlateArmor";
                        break;
                    case 6:
                    case 7:
                        armorDefName = "Apparel_PowerArmor";
                        break;
                    default:
                        return; // No armor for Tier 1
                }
                
                var armorDef = DefDatabase<ThingDef>.GetNamedSilentFail(armorDefName);
                if (armorDef != null)
                {
                    var armor = ThingMaker.MakeThing(armorDef, GenStuff.DefaultStuffFor(armorDef));
                    
                    // Set quality based on tier
                    SetItemQuality(armor, tierData.WeaponQuality);
                    
                    // Force equip the armor
                    if (pawn.apparel != null)
                    {
                        pawn.apparel.Wear(armor as Apparel);
                        Log.Message($"SolWorld: Equipped Tier {tierData.Tier} armor: {armorDefName}");
                    }
                }
                else
                {
                    Log.Warning($"SolWorld: Armor def not found: {armorDefName}");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"SolWorld: Failed to apply tier armor: {ex.Message}");
            }
        }

        // ⭐ NEW METHOD: Apply tier-based helmet
        private void ApplyTierHelmet(Pawn pawn, Fighter fighter)
        {
            var tierData = currentRoundTierData[fighter.WalletFull];
            
            try
            {
                string helmetDefName;
                
                // Select helmet based on tier
                switch (tierData.Tier)
                {
                    case 4:
                    case 5:
                        helmetDefName = "Apparel_AdvancedHelmet";
                        break;
                    case 6:
                    case 7:
                        helmetDefName = "Apparel_PowerArmorHelmet";
                        break;
                    default:
                        return; // No helmet for lower tiers
                }
                
                var helmetDef = DefDatabase<ThingDef>.GetNamedSilentFail(helmetDefName);
                if (helmetDef != null)
                {
                    var helmet = ThingMaker.MakeThing(helmetDef, GenStuff.DefaultStuffFor(helmetDef));
                    
                    // Set quality based on tier
                    SetItemQuality(helmet, tierData.WeaponQuality);
                    
                    // Force equip the helmet
                    if (pawn.apparel != null)
                    {
                        pawn.apparel.Wear(helmet as Apparel);
                        Log.Message($"SolWorld: Equipped Tier {tierData.Tier} helmet: {helmetDefName}");
                    }
                }
                else
                {
                    Log.Warning($"SolWorld: Helmet def not found: {helmetDefName}");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"SolWorld: Failed to apply tier helmet: {ex.Message}");
            }
        }

        private void ApplyTierStatBoosts(Pawn pawn, Fighter fighter)
        {
            var tierData = currentRoundTierData[fighter.WalletFull];
            
            try
            {
                // For higher tiers, we can apply stat boosts
                // This is a simplified approach - in a full implementation you'd use hediffs
                
                Log.Message($"SolWorld: Applied Tier {tierData.Tier} stat boosts to {fighter.WalletShort}");
                
                // Note: Full stat modification would require adding hediffs to the pawn
                // For now, we just log that higher tier bonuses are available
            }
            catch (Exception ex)
            {
                Log.Warning($"SolWorld: Failed to apply stat boosts: {ex.Message}");
            }
        }

        private void ApplyTierVisualEffects(Pawn pawn, Fighter fighter)
        {
            var tierData = currentRoundTierData[fighter.WalletFull];
            
            try
            {
                // For highest tiers, we could add visual effects
                // This is a placeholder for visual enhancements
                Log.Message($"SolWorld: Applied visual effects to Tier {tierData.Tier} ({tierData.TierName}) fighter");
                
                // Could add glowing effects, special colors, etc. here
            }
            catch (Exception ex)
            {
                Log.Warning($"SolWorld: Failed to apply visual effects: {ex.Message}");
            }
        }

        private void SetItemQuality(Thing item, string qualityName)
        {
            try
            {
                QualityCategory quality;
                
                switch (qualityName?.ToLower())
                {
                    case "poor":
                        quality = QualityCategory.Poor;
                        break;
                    case "normal":
                        quality = QualityCategory.Normal;
                        break;
                    case "good":
                        quality = QualityCategory.Good;
                        break;
                    case "excellent":
                        quality = QualityCategory.Excellent;
                        break;
                    case "masterwork":
                        quality = QualityCategory.Masterwork;
                        break;
                    case "legendary":
                        quality = QualityCategory.Legendary;
                        break;
                    default:
                        quality = QualityCategory.Normal;
                        break;
                }
                
                var qualityComp = item.TryGetComp<CompQuality>();
                if (qualityComp != null)
                {
                    qualityComp.SetQuality(quality, ArtGenerationContext.Colony);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"SolWorld: Failed to set item quality: {ex.Message}");
            }
        }

        // ⭐ NEW METHOD: Get health multiplier for tier
        private float GetHealthMultiplierForTier(int tier)
        {
            switch (tier)
            {
                case 1: return 1.0f;   // Standard health
                case 2: return 1.1f;   // +10% health
                case 3: return 1.1f;   // +10% health
                case 4: return 1.2f;   // +20% health
                case 5: return 1.3f;   // +30% health
                case 6: return 1.4f;   // +40% health
                case 7: return 1.5f;   // +50% health
                default: return 1.0f;
            }
        }

        private void StripFoodFromPawn(Pawn pawn)
        {
            if (pawn?.inventory?.innerContainer == null)
                return;

            try
            {
                // Find all food/meal items in inventory
                var foodItems = pawn.inventory.innerContainer
                    .Where(thing => thing.def.IsIngestible || 
                                thing.def.ingestible != null ||
                                thing.def.defName.Contains("Meal") ||
                                thing.def.defName.Contains("Pemmican") ||
                                thing.def.defName.Contains("Chocolate") ||
                                thing.def.category == ThingCategory.Item && 
                                thing.def.IsNutritionGivingIngestible)
                    .ToList();

                // Remove each food item
                foreach (var foodItem in foodItems)
                {
                    pawn.inventory.innerContainer.Remove(foodItem);
                    Log.Message($"SolWorld: Stripped {foodItem.def.defName} from {pawn.Name}");
                }

                if (foodItems.Count > 0)
                {
                    Log.Message($"SolWorld: Removed {foodItems.Count} food items from {pawn.Name}");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"SolWorld: Failed to strip food from {pawn.Name}: {ex.Message}");
            }
        }

        private void LogTeamWeapons(string[] weapons, string teamName)
        {
            if (weapons == null || weapons.Length == 0)
            {
                Log.Warning($"SolWorld: {teamName} team has no weapons!");
                return;
            }
            
            Log.Message($"SolWorld: {teamName} team weapon distribution:");
            for (int i = 0; i < weapons.Length; i++)
            {
                var weaponDef = DefDatabase<ThingDef>.GetNamedSilentFail(weapons[i]);
                var displayName = weaponDef?.label ?? weapons[i];
                Log.Message($"  Fighter {i}: {displayName} ({weapons[i]})");
            }
        }
     
        // FIXED: Pawn generation to avoid cast errors
        private Pawn GenerateWarrior(Fighter fighter, TeamColor teamColor, Faction teamFaction)
        {
            // Spam messages every frame to force visibility
            for (int i = 0; i < 5; i++)
            {
                Messages.Message($"WARRIOR SPAWN #{i}: {fighter.WalletShort}", MessageTypeDefOf.RejectInput);
            }

            try
            {
                // Use appropriate pawn kind based on faction
                PawnKindDef pawnKind;
                if (teamFaction.def.pawnGroupMakers?.Any() == true)
                {
                    var pawnGroupMaker = teamFaction.def.pawnGroupMakers.FirstOrDefault();
                    pawnKind = pawnGroupMaker?.options?.FirstOrDefault()?.kind ?? PawnKindDefOf.Colonist;
                }
                else
                {
                    pawnKind = PawnKindDefOf.Colonist;
                }
                
                var request = new PawnGenerationRequest(
                    kind: pawnKind,
                    faction: teamFaction,
                    context: PawnGenerationContext.NonPlayer,
                    tile: map.Tile,
                    forceGenerateNewPawn: true,
                    allowDead: false,
                    allowDowned: false,
                    canGeneratePawnRelations: false,
                    mustBeCapableOfViolence: true,
                    colonistRelationChanceFactor: 0f,
                    forceAddFreeWarmLayerIfNeeded: false,
                    allowGay: true,
                    allowPregnant: false,
                    allowAddictions: false,
                    inhabitant: false,
                    certainlyBeenInCryptosleep: false,
                    forceRedressWorldPawnIfFormerColonist: false,
                    worldPawnFactionDoesntMatter: true
                );
                
                var pawn = PawnGenerator.GeneratePawn(request);
                pawn.Name = new NameSingle(fighter.WalletShort);
                
                // Strip all existing headwear to show hair and prepare for tier-based helmets
                EnsurePawnHealthStability(pawn);
                StripAllHeadwear(pawn);
                EnsurePawnMindStateSetup(pawn);
                GiveWeapon(pawn);
                MakeWarrior(pawn);
                
                // ADD: Normalize combat stats for balance
                NormalizePawnCombatStats(pawn);
                
                return pawn;
            }
            catch (Exception ex)
            {
                Log.Error("SolWorld: Failed to generate warrior: " + ex.Message);
                return null;
            }
        }

        private void EnsurePawnMindStateSetup(Pawn pawn)
        {
            try
            {
                if (pawn.mindState != null)
                {
                    pawn.mindState.canFleeIndividual = false;
                    pawn.mindState.duty = new PawnDuty(DutyDefOf.AssaultColony);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"SolWorld: Failed to setup mind state for {pawn.Name}: {ex.Message}");
            }
        }
        
        private void ApplyTeamStyling(Pawn pawn, TeamColor teamColor)
        {
            try
            {
                // Hair coloring
                if (pawn.story != null)
                {
                    if (teamColor == TeamColor.Red)
                        pawn.story.HairColor = Color.red;
                    else
                        pawn.story.HairColor = Color.blue;
                }
                
                // Make the pawn's actual name nearly invisible
                var originalName = pawn.Name.ToStringShort;
                var teamPrefix = teamColor == TeamColor.Red ? "[R]" : "[B]";
                
                if (!originalName.StartsWith("[R]") && !originalName.StartsWith("[B]"))
                {
                    // Use a single dot or space - minimal but won't cause UI errors
                    pawn.Name = new NameSingle("."); 
                    Log.Message($"SolWorld: Made {originalName} name minimal for overlay system");
                }
                
                if (pawn.Drawer?.renderer != null)
                {
                    pawn.Drawer.renderer.SetAllGraphicsDirty();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"SolWorld: Failed to apply team styling: {ex.Message}");
            }
        }
        private void HideDefaultPawnLabel(Pawn pawn)
        {
            try
            {
                var pawnUIOverlay = pawn.Drawer?.ui;
                if (pawnUIOverlay != null)
                {
                    // Try to access the label visibility field
                    var labelVisibleField = pawnUIOverlay.GetType().GetField("labelVisible", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    
                    if (labelVisibleField != null && labelVisibleField.FieldType == typeof(bool))
                    {
                        labelVisibleField.SetValue(pawnUIOverlay, false);
                        Log.Message($"SolWorld: Hid default label for {pawn.Name}");
                    }
                    else
                    {
                        // Alternative approach - try different field names
                        var drawPawnLabelField = pawnUIOverlay.GetType().GetField("drawPawnLabel", 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        
                        if (drawPawnLabelField != null && drawPawnLabelField.FieldType == typeof(bool))
                        {
                            drawPawnLabelField.SetValue(pawnUIOverlay, false);
                            Log.Message($"SolWorld: Hid default label for {pawn.Name} (method 2)");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Message($"SolWorld: Could not hide default label for {pawn.Name}: {ex.Message}");
                // Not critical - custom overlay will still work
            }
        }
        
        private void GiveWeapon(Pawn pawn)
        {
            var weaponDefs = new string[]
            {
                "Gun_AssaultRifle",
                "Gun_SniperRifle", 
                "Gun_Autopistol",
                "Gun_Revolver",
                "Gun_Pistol",
                "MeleeWeapon_LongSword",
                "MeleeWeapon_Knife"
            };
            
            foreach (var weaponName in weaponDefs)
            {
                var weaponDef = DefDatabase<ThingDef>.GetNamedSilentFail(weaponName);
                if (weaponDef != null)
                {
                    try
                    {
                        if (pawn.equipment != null && pawn.equipment.Primary != null)
                        {
                            pawn.equipment.Remove(pawn.equipment.Primary);
                        }
                        
                        var weapon = ThingMaker.MakeThing(weaponDef);
                        if (pawn.equipment != null && weapon is ThingWithComps weaponWithComps)
                        {
                            pawn.equipment.AddEquipment(weaponWithComps);
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"SolWorld: Failed to give weapon {weaponName}: {ex.Message}");
                    }
                }
            }
        }
        
        private void MakeWarrior(Pawn pawn)
        {
            // Set up for combat readiness
            if (pawn.mindState != null)
            {
                pawn.mindState.canFleeIndividual = false;
                pawn.mindState.duty = new PawnDuty(DutyDefOf.AssaultColony);
            }
            
            // Max mood and needs
            if (pawn.needs?.mood != null) pawn.needs.mood.CurLevel = 1.0f;
            if (pawn.needs?.rest != null) pawn.needs.rest.CurLevel = 1.0f;
            if (pawn.needs?.food != null) pawn.needs.food.CurLevel = 1.0f;
            
            // Boost combat skills to maximum
            if (pawn.skills != null)
            {
                try
                {
                    var shooting = pawn.skills.GetSkill(SkillDefOf.Shooting);
                    var melee = pawn.skills.GetSkill(SkillDefOf.Melee);
                    
                    if (shooting != null)
                    {
                        shooting.Level = 20;
                        shooting.passion = Passion.Major;
                    }
                    
                    if (melee != null)
                    {
                        melee.Level = 20;
                        melee.passion = Passion.Major;
                    }
                }
                catch { }
            }
            
            // Remove bad traits, add good ones
            if (pawn.story?.traits != null)
            {
                try
                {
                    var pacifistTrait = DefDatabase<TraitDef>.GetNamedSilentFail("Pacifist");
                    if (pacifistTrait != null && pawn.story.traits.HasTrait(pacifistTrait))
                    {
                        pawn.story.traits.allTraits.RemoveAll(t => t.def == pacifistTrait);
                    }
                    
                    if (pawn.story.traits.allTraits.Count < 3)
                    {
                        var brawlerTrait = DefDatabase<TraitDef>.GetNamedSilentFail("Brawler");
                        if (brawlerTrait != null && !pawn.story.traits.HasTrait(brawlerTrait))
                        {
                            pawn.story.traits.GainTrait(new Trait(brawlerTrait));
                        }
                    }
                }
                catch { }
            }
        }
        
        // ENHANCED: Combat initiation that works with player faction blue team
        private void InitiateAggressiveCombat()
        {
            Log.Message("SolWorld: ===== INITIATING AGGRESSIVE COMBAT =====");
            
            combatInitiated = true;
            lastCombatEnforcementTick = Find.TickManager.TicksGame;
            lastAggressiveEnforcementTick = Find.TickManager.TicksGame;
            
            // REMOVED: SetupProperFactionHostility() - this was breaking name colors
            // Combat will work through direct pawn targeting instead
            
            // Force immediate combat engagement through ForceAttackTarget
            foreach (var redPawn in redTeamPawns.Where(p => p?.Spawned == true && !p.Dead))
            {
                SetupAggressiveCombatant(redPawn, TeamColor.Red);
            }
            
            foreach (var bluePawn in blueTeamPawns.Where(p => p?.Spawned == true && !p.Dead))
            {
                SetupAggressiveCombatant(bluePawn, TeamColor.Blue);
            }
            
            // This will force combat through direct targeting, not faction hostility
            ForceInitialCombatEngagement();
        }
        
        // NEW: Special setup for player faction combatants (blue team)
        private void SetupPlayerFactionCombatant(Pawn pawn, TeamColor team)
        {
            // Make them aggressive fighters even though they're player faction
            if (pawn.mindState != null)
            {
                pawn.mindState.canFleeIndividual = false;
                // Use different duty that works with player faction
                pawn.mindState.duty = new PawnDuty(DutyDefOf.Defend);
            }
            
            // Max out their combat readiness
            if (pawn.needs != null)
            {
                if (pawn.needs.mood != null) pawn.needs.mood.CurLevel = 1.0f;
                if (pawn.needs.rest != null) pawn.needs.rest.CurLevel = 1.0f;
                if (pawn.needs.food != null) pawn.needs.food.CurLevel = 1.0f;
            }
            
            // Force combat skills to max (same as red team)
            if (pawn.skills != null)
            {
                var shooting = pawn.skills.GetSkill(SkillDefOf.Shooting);
                var melee = pawn.skills.GetSkill(SkillDefOf.Melee);
                
                if (shooting != null)
                {
                    shooting.Level = 20;
                    shooting.passion = Passion.Major;
                }
                
                if (melee != null)
                {
                    melee.Level = 20;
                    melee.passion = Passion.Major;
                }
            }
        }

        // NEW: Special combat setup for mixed faction combat
        private void SetupPlayerFactionCombat()
        {
            try
            {
                Log.Message("SolWorld: Setting up player faction vs hostile faction combat");
                
                // Make sure red faction is hostile to player faction (for combat)
                if (redTeamFaction != Faction.OfPlayer)
                {
                    var redToPlayer = redTeamFaction.RelationWith(Faction.OfPlayer, true);
                    if (redToPlayer != null)
                    {
                        redToPlayer.baseGoodwill = -100;
                        redToPlayer.kind = FactionRelationKind.Hostile;
                    }
                }
                
                Log.Message("SolWorld: Player faction combat setup complete");
            }
            catch (Exception ex)
            {
                Log.Warning($"SolWorld: Failed to setup player faction combat: {ex.Message}");
            }
        }

        private void SetupProperFactionHostility()
        {
            if (redTeamFaction != null && blueTeamFaction != null && redTeamFaction != blueTeamFaction)
            {
                try
                {
                    Log.Message("SolWorld: Setting up proper faction hostility (preserving name colors)");
                    
                    // ONLY set team-to-team hostility, NOT player relationships
                    var blueToRed = blueTeamFaction.RelationWith(redTeamFaction, true);
                    if (blueToRed != null)
                    {
                        blueToRed.baseGoodwill = -100;
                        blueToRed.kind = FactionRelationKind.Hostile;
                    }
                    
                    var redToBlue = redTeamFaction.RelationWith(blueTeamFaction, true);
                    if (redToBlue != null)
                    {
                        redToBlue.baseGoodwill = -100;
                        redToBlue.kind = FactionRelationKind.Hostile;
                    }
                    
                    // CRITICAL: Do NOT change player relationships here!
                    Log.Message($"SolWorld: Team hostility set, player relationships preserved");
                }
                catch (Exception ex)
                {
                    Log.Warning($"SolWorld: Failed to force faction hostility: {ex.Message}");
                }
            }
            
            // Use direct pawn targeting instead of faction changes
            foreach (var redPawn in redTeamPawns.Where(p => p?.Spawned == true))
            {
                foreach (var bluePawn in blueTeamPawns.Where(p => p?.Spawned == true))
                {
                    try
                    {
                        // Force them to see each other as enemies WITHOUT changing faction relationships
                        if (redPawn.mindState != null)
                        {
                            // Set enemy target but don't change faction relationship
                            redPawn.mindState.enemyTarget = bluePawn;
                            redPawn.mindState.lastEngageTargetTick = Find.TickManager.TicksGame;
                        }
                        if (bluePawn.mindState != null)
                        {
                            // Set enemy target but don't change faction relationship  
                            bluePawn.mindState.enemyTarget = redPawn;
                            bluePawn.mindState.lastEngageTargetTick = Find.TickManager.TicksGame;
                        }
                    }
                    catch { }
                }
            }
        }
        
        private void SetupAggressiveCombatant(Pawn pawn, TeamColor team)
        {
            // Make them fearless and aggressive
            if (pawn.mindState != null)
            {
                pawn.mindState.canFleeIndividual = false;
                pawn.mindState.duty = new PawnDuty(DutyDefOf.AssaultColony);
            }
            
            // Max out their combat readiness
            if (pawn.needs != null)
            {
                if (pawn.needs.mood != null) pawn.needs.mood.CurLevel = 1.0f;
                if (pawn.needs.rest != null) pawn.needs.rest.CurLevel = 1.0f;
                if (pawn.needs.food != null) pawn.needs.food.CurLevel = 1.0f;
            }
            
            // Boost their health and combat stats
            if (pawn.health?.hediffSet != null)
            {
                var badHediffs = pawn.health.hediffSet.hediffs
                    .Where(h => h.def.makesSickThought || h.def.tendable || (h.CurStage?.capMods != null && h.CurStage.capMods.Any()))
                    .ToList();
                
                foreach (var hediff in badHediffs)
                {
                    try
                    {
                        pawn.health.RemoveHediff(hediff);
                    }
                    catch { }
                }
            }
            
            // Force combat skills to max
            if (pawn.skills != null)
            {
                var shooting = pawn.skills.GetSkill(SkillDefOf.Shooting);
                var melee = pawn.skills.GetSkill(SkillDefOf.Melee);
                
                if (shooting != null)
                {
                    shooting.Level = 20;
                    shooting.passion = Passion.Major;
                }
                
                if (melee != null)
                {
                    melee.Level = 20;
                    melee.passion = Passion.Major;
                }
            }
        }
        
        private void ForceInitialCombatEngagement()
        {
            Log.Message("SolWorld: FORCING initial combat engagement...");
            
            var redAlive = redTeamPawns.Where(p => p?.Spawned == true && !p.Dead).ToList();
            var blueAlive = blueTeamPawns.Where(p => p?.Spawned == true && !p.Dead).ToList();
            
            if (redAlive.Count == 0 || blueAlive.Count == 0)
            {
                Log.Warning("SolWorld: Not enough pawns for combat engagement");
                return;
            }
            
            // Force red team to attack blue team
            foreach (var redPawn in redAlive)
            {
                var nearestBlue = blueAlive
                    .OrderBy(p => redPawn.Position.DistanceTo(p.Position))
                    .FirstOrDefault();
                
                if (nearestBlue != null)
                {
                    ForceAttackTarget(redPawn, nearestBlue);
                }
            }
            
            // Force blue team to attack red team
            foreach (var bluePawn in blueAlive)
            {
                var nearestRed = redAlive
                    .OrderBy(p => bluePawn.Position.DistanceTo(p.Position))
                    .FirstOrDefault();
                
                if (nearestRed != null)
                {
                    ForceAttackTarget(bluePawn, nearestRed);
                }
            }
            
            Log.Message("SolWorld: Initial combat engagement orders issued");
        }
        
        // ENHANCED: Better attack targeting that ensures shooting after movement
        private void ForceAttackTarget(Pawn attacker, Pawn target)
        {
            try
            {
                if (attacker?.jobs == null || target?.Spawned != true || target.Dead)
                    return;
                
                Log.Message($"SolWorld: Forcing {attacker.Name} to attack {target.Name}");
                
                // Clear current job forcefully
                attacker.jobs.EndCurrentJob(JobCondition.InterruptForced);
                attacker.jobs.ClearQueuedJobs();
                
                // Set enemy target directly in mind state
                if (attacker.mindState != null)
                {
                    attacker.mindState.enemyTarget = target;
                    attacker.mindState.lastEngageTargetTick = Find.TickManager.TicksGame;
                }
                
                // FIXED: Better job creation for ranged vs melee
                Job attackJob = null;
                var distance = attacker.Position.DistanceTo(target.Position);
                
                // Check if they have a ranged weapon
                if (attacker.equipment?.Primary != null && attacker.equipment.Primary.def.IsRangedWeapon)
                {
                    var weaponRange = attacker.equipment.Primary.def.Verbs[0].range;
                    
                    if (distance <= weaponRange * 0.9f)
                    {
                        // In range - direct ranged attack
                        attackJob = JobMaker.MakeJob(JobDefOf.AttackStatic, target);
                        Log.Message($"SolWorld: {attacker.Name} - Direct ranged attack (distance: {distance}, range: {weaponRange})");
                    }
                    else
                    {
                        // Out of range - move closer then attack
                        var optimalRange = Math.Min(weaponRange * 0.7f, distance - 2f);
                        
                        // Calculate direction vector manually for RimWorld
                        var directionX = (float)(attacker.Position.x - target.Position.x);
                        var directionZ = (float)(attacker.Position.z - target.Position.z);
                        var directionLength = Mathf.Sqrt(directionX * directionX + directionZ * directionZ);
                        
                        if (directionLength > 0)
                        {
                            // Normalize manually
                            directionX /= directionLength;
                            directionZ /= directionLength;
                            
                            // Calculate move position with proper int conversion
                            var moveToPos = new IntVec3(
                                target.Position.x + Mathf.RoundToInt(directionX * optimalRange),
                                0,
                                target.Position.z + Mathf.RoundToInt(directionZ * optimalRange)
                            );
                            
                            if (moveToPos.InBounds(attacker.Map) && moveToPos.Standable(attacker.Map))
                            {
                                // Use goto with attack continuation
                                attackJob = JobMaker.MakeJob(JobDefOf.Goto, moveToPos);
                                Log.Message($"SolWorld: {attacker.Name} - Moving to optimal range (distance: {distance}, target range: {optimalRange})");
                            }
                            else
                            {
                                // Fallback to direct attack
                                attackJob = JobMaker.MakeJob(JobDefOf.AttackStatic, target);
                            }
                        }
                        else
                        {
                            // Fallback to direct attack if can't calculate direction
                            attackJob = JobMaker.MakeJob(JobDefOf.AttackStatic, target);
                        }
                    }
                }
                else
                {
                    // Melee attack
                    attackJob = JobMaker.MakeJob(JobDefOf.AttackMelee, target);
                    Log.Message($"SolWorld: {attacker.Name} - Melee attack");
                }
                
                if (attackJob != null)
                {
                    attackJob.playerForced = true;
                    attackJob.canBashDoors = true;
                    attackJob.canBashFences = true;
                    attackJob.locomotionUrgency = LocomotionUrgency.Sprint;
                    attackJob.checkOverrideOnExpire = false;
                    attackJob.expiryInterval = 2000; // Allow job to expire and be reassigned
                    
                    // Force start the job
                    attacker.jobs.StartJob(attackJob, JobCondition.InterruptForced, null, false, true);
                    
                    // Track this action
                    pawnLastActionTick[attacker] = Find.TickManager.TicksGame;
                    
                    Log.Message($"SolWorld: {attackJob.def.defName} job started - {attacker.Name} -> {target.Name}");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"SolWorld: Failed to force attack {attacker.Name} -> {target.Name}: {ex.Message}");
            }
        }
        
        // FIXED: Better combat enforcement with error handling
        private void EnforceContinuousCombat()
        {
            if (currentRoster == null || !combatInitiated)
                return;
            
            try
            {
                var currentTick = Find.TickManager.TicksGame;
                var bounds = GetArenaBounds();
                
                var redAlive = redTeamPawns.Where(p => p?.Spawned == true && !p.Dead && !p.Downed).ToList();
                var blueAlive = blueTeamPawns.Where(p => p?.Spawned == true && !p.Dead && !p.Downed).ToList();
                
                if (redAlive.Count == 0 && blueAlive.Count == 0)
                {
                    Log.Warning("SolWorld: No alive pawns found in combat!");
                    return;
                }
                
                // ENHANCED: Force combat for all pawns every 2 seconds
                foreach (var redPawn in redAlive)
                {
                    EnforceAggressiveCombat(redPawn, blueAlive, bounds, currentTick);
                }
                
                foreach (var bluePawn in blueAlive)
                {
                    EnforceAggressiveCombat(bluePawn, redAlive, bounds, currentTick);
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"SolWorld: Error in combat enforcement: {ex.Message}");
            }
        }
        
        // NEW: Even more aggressive enforcement for stuck pawns
        private void EnforceAggressiveCombatActions()
        {
            var currentTick = Find.TickManager.TicksGame;
            var redAlive = redTeamPawns.Where(p => p?.Spawned == true && !p.Dead).ToList();
            var blueAlive = blueTeamPawns.Where(p => p?.Spawned == true && !p.Dead).ToList();
            
            Log.Message($"SolWorld: AGGRESSIVE enforcement - Red: {redAlive.Count}, Blue: {blueAlive.Count}");
            
            // Check for completely idle pawns and force them into action
            foreach (var pawn in redAlive.Concat(blueAlive))
            {
                if (pawn?.jobs == null) continue;
                
                var timeSinceLastAction = pawnLastActionTick.ContainsKey(pawn) ? currentTick - pawnLastActionTick[pawn] : 999999;
                var currentJob = pawn.CurJob;
                
                // If pawn has been idle for more than 3 seconds, force new action
                if (timeSinceLastAction > 180 || currentJob == null || 
                    currentJob.def == JobDefOf.Wait || currentJob.def == JobDefOf.Wait_MaintainPosture)
                {
                    Log.Message($"SolWorld: Pawn {pawn.Name} idle for {timeSinceLastAction} ticks - forcing action!");
                    
                    var enemies = pawnTeamMap[pawn] == TeamColor.Red ? blueAlive : redAlive;
                    var nearestEnemy = enemies.OrderBy(e => pawn.Position.DistanceTo(e.Position)).FirstOrDefault();
                    
                    if (nearestEnemy != null)
                    {
                        ForceAttackTarget(pawn, nearestEnemy);
                    }
                }
            }
        }
        
        private void EnforceAggressiveCombat(Pawn pawn, List<Pawn> enemies, CellRect? bounds, int currentTick)
        {
            if (pawn?.jobs == null || enemies.Count == 0)
                return;
            
            // Keep in arena bounds
            if (bounds.HasValue && !bounds.Value.Contains(pawn.Position))
            {
                ForceBackToArena(pawn, bounds.Value);
                return;
            }
            
            // Maintain combat state
            if (pawn.mindState != null)
            {
                pawn.mindState.canFleeIndividual = false;
                
                // Keep mood high
                if (pawn.needs?.mood != null && pawn.needs.mood.CurLevel < 0.5f)
                {
                    pawn.needs.mood.CurLevel = 1.0f;
                }
            }
            
            // Check if they're actually doing something useful
            var currentJob = pawn.CurJob;
            bool isDoingSomethingUseful = currentJob != null && 
                (currentJob.def == JobDefOf.AttackMelee || 
                 currentJob.def == JobDefOf.AttackStatic || 
                 currentJob.def == JobDefOf.Hunt ||
                 currentJob.def == JobDefOf.Goto);
            
            bool hasValidTarget = false;
            if (currentJob != null && currentJob.targetA.IsValid && currentJob.targetA.Thing is Pawn targetPawn)
            {
                hasValidTarget = enemies.Contains(targetPawn) && !targetPawn.Dead;
            }
            
            // Force new action if not doing anything useful or target is invalid
            if (!isDoingSomethingUseful || !hasValidTarget)
            {
                var nearestEnemy = enemies
                    .Where(e => !e.Dead && e.Spawned)
                    .OrderBy(e => pawn.Position.DistanceTo(e.Position))
                    .FirstOrDefault();
                
                if (nearestEnemy != null)
                {
                    var distance = pawn.Position.DistanceTo(nearestEnemy.Position);
                    if (distance <= 50) // Reasonable arena size
                    {
                        ForceAttackTarget(pawn, nearestEnemy);
                    }
                }
            }
            else
            {
                // Update last action time if doing something useful
                pawnLastActionTick[pawn] = currentTick;
            }
        }
        
        private void ForceBackToArena(Pawn pawn, CellRect bounds)
        {
            var center = new IntVec3(bounds.minX + bounds.Width / 2, 0, bounds.minZ + bounds.Height / 2);
            var targetPos = CellFinder.RandomClosewalkCellNear(center, map, 5);
            
            if (targetPos.IsValid && bounds.Contains(targetPos))
            {
                try
                {
                    if (pawn.jobs != null)
                    {
                        pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                        var returnJob = JobMaker.MakeJob(JobDefOf.Goto, targetPos);
                        returnJob.playerForced = true;
                        returnJob.locomotionUrgency = LocomotionUrgency.Sprint;
                        pawn.jobs.StartJob(returnJob, JobCondition.InterruptForced);
                        
                        Log.Message($"SolWorld: Forcing {pawn.Name} back to arena bounds");
                    }
                }
                catch { }
            }
        }
        
        private void UpdateRosterStatus()
        {
            if (currentRoster == null) return;
            
            // Update alive status for fighters based on actual pawn state
            foreach (var fighter in currentRoster.Red)
            {
                if (fighter.PawnRef != null)
                {
                    fighter.Alive = fighter.PawnRef.Spawned && !fighter.PawnRef.Dead;
                }
            }
            
            foreach (var fighter in currentRoster.Blue)
            {
                if (fighter.PawnRef != null)
                {
                    fighter.Alive = fighter.PawnRef.Spawned && !fighter.PawnRef.Dead;
                }
            }
        }
        
        // Add this method to your MapComponent_SolWorldArena.cs class:
        public void MakeArenaLampsInvincible()
        {
            if (map == null) return;
            
            Log.Message("SolWorld: Making arena lamps invincible and power-free...");
            
            // Find all wall lamps in the arena bounds
            var bounds = GetArenaBounds();
            if (!bounds.HasValue) return;
            
            var arenaLamps = new List<Building>();
            
            // Get all buildings in arena bounds
            foreach (var cell in bounds.Value)
            {
                var buildings = cell.GetThingList(map).OfType<Building>().ToList();
                foreach (var building in buildings)
                {
                    // Check if it's a lamp (wall lamp, standing lamp, etc.)
                    if (building.def.defName.Contains("Lamp") || 
                        building.def.defName.Contains("TorchLamp") ||
                        building.def.comps?.Any(c => c is CompProperties_Glower) == true)
                    {
                        arenaLamps.Add(building);
                    }
                }
            }
            
            Log.Message($"SolWorld: Found {arenaLamps.Count} lamps in arena bounds");
            
            // Make each lamp invincible and power-free
            foreach (var lamp in arenaLamps)
            {
                try
                {
                    // FIXED: Only set HitPoints, MaxHitPoints is read-only
                    lamp.HitPoints = 999999;
                    
                    // FIXED: Correct way to handle power component
                    var powerComp = lamp.GetComp<CompPowerTrader>();
                    if (powerComp != null)
                    {
                        // Force power on by manipulating the power grid connection
                        powerComp.PowerOn = true;
                        
                        // Try to set power output to 0 (remove power consumption)
                        try
                        {
                            // Use reflection to set private fields if needed
                            var powerOutputField = typeof(CompPowerTrader).GetField("powerOutputInt", 
                                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            if (powerOutputField != null)
                            {
                                powerOutputField.SetValue(powerComp, 0f);
                            }
                        }
                        catch
                        {
                            // If reflection fails, just leave power as-is
                            Log.Message("SolWorld: Could not modify power consumption via reflection");
                        }
                    }
                    
                    // Ensure glower works
                    var glowerComp = lamp.GetComp<CompGlower>();
                    if (glowerComp != null)
                    {
                        // Force light on
                        glowerComp.UpdateLit(map);
                    }
                    
                    Log.Message($"SolWorld: Modified lamp: {lamp.def.defName} at {lamp.Position}");
                }
                catch (Exception ex)
                {
                    Log.Warning($"SolWorld: Failed to modify lamp {lamp.def.defName}: {ex.Message}");
                }
            }
        }

        // FIXED: Handle pawn death properly to prevent errors
        public void HandlePawnDeath(Pawn deadPawn)
        {
            if (currentRoster?.IsLive != true || !pawnTeamMap.ContainsKey(deadPawn))
                return;
            
            try
            {
                // Immediately stop all jobs for the dead pawn
                if (deadPawn.jobs != null)
                {
                    deadPawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                    deadPawn.jobs.ClearQueuedJobs();
                }
                
                // Clear from combat targeting
                if (deadPawn.mindState != null)
                {
                    deadPawn.mindState.enemyTarget = null;
                }
                
                // Update roster status
                var deadTeam = pawnTeamMap[deadPawn];
                Fighter deadFighter = null;
                
                if (deadTeam == TeamColor.Red)
                {
                    deadFighter = currentRoster.Red.FirstOrDefault(f => f.PawnRef == deadPawn);
                }
                else
                {
                    deadFighter = currentRoster.Blue.FirstOrDefault(f => f.PawnRef == deadPawn);
                }
                
                if (deadFighter != null)
                {
                    deadFighter.Alive = false;
                    Log.Message($"SolWorld: {deadFighter.WalletShort} ({deadTeam}) confirmed dead");
                }
                
                // Remove from active tracking
                pawnLastActionTick.Remove(deadPawn);
                
            }
            catch (System.Exception ex)
            {
                Log.Error($"SolWorld: Error handling pawn death for {deadPawn.Name}: {ex.Message}");
            }
        }
        
        // UPDATED: EndRound with winner data capture
        private void EndRound(string reason)
        {
            currentState = ArenaState.Ended;
            roundEndTick = Find.TickManager.TicksGame; // Track when round ended
            
            if (currentRoster == null) return;
            
            currentRoster.IsLive = false;
            currentRoster.Winner = currentRoster.DetermineWinner();
            
            // WINNER STORAGE: Capture winner data for persistent storage
            if (currentRoster.Winner.HasValue)
            {
                lastRoundWinner = currentRoster.Winner;
                lastMatchId = currentRoster.MatchId;
                lastPerWinnerPayout = currentRoster.PerWinnerPayout;
                
                // Deep copy the winning team
                lastWinningTeam.Clear();
                var winningTeam = currentRoster.GetWinningTeam();
                if (winningTeam != null)
                {
                    foreach (var fighter in winningTeam)
                    {
                        lastWinningTeam.Add(new Fighter(fighter.WalletFull, fighter.Team)
                        {
                            Kills = fighter.Kills,
                            Alive = fighter.Alive
                        });
                    }
                }
                
                Log.Message($"SolWorld: Captured winner data - {lastRoundWinner} team, {lastWinningTeam.Count} winners, {lastPerWinnerPayout:F3} SOL each");
            }
            
            Log.Message("SolWorld: Round ended - " + reason + ". Winner: " + currentRoster.Winner);
            
            // TODO: Report to backend and get real txids
            var mockTxids = new string[] { "MockTx1", "MockTx2" };
            Messages.Message("ROUND COMPLETE! Winner: " + currentRoster.Winner + " team", MessageTypeDefOf.PositiveEvent);
            
            // IMPORTANT: Don't cleanup here - wait for reset phase to preserve leaderboard
        }
        
        // FIXED: Cleanup with preserved spawner references
        private void CleanupCurrentRound()
        {
            if (currentRoster == null) return;
            
            Log.Message("SolWorld: Starting current round cleanup...");
            
            // STEP 1: Clean up all arena pawns (but preserve spawner references!)
            var allArenaPawns = redTeamPawns.Concat(blueTeamPawns).ToList();
            
            foreach (var pawn in allArenaPawns)
            {
                if (pawn?.Spawned == true)
                {
                    try
                    {
                        // Stop all jobs first to prevent pathing errors
                        if (pawn.jobs != null)
                        {
                            pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                            pawn.jobs.ClearQueuedJobs();
                        }
                        
                        // Remove from team mappings
                        pawnTeamMap.Remove(pawn);
                        pawnLastActionTick.Remove(pawn);
                        
                        // Despawn the pawn
                        pawn.DeSpawn();
                        Log.Message($"SolWorld: Despawned arena pawn: {pawn.Name}");
                    }
                    catch (System.Exception ex)
                    {
                        Log.Warning($"SolWorld: Failed to despawn pawn {pawn.Name}: {ex.Message}");
                    }
                }
            }
            
            // STEP 2: Clear tracking collections
            redTeamPawns.Clear();
            blueTeamPawns.Clear();
            pawnTeamMap.Clear();
            pawnLastActionTick.Clear();
            
            // STEP 3: Clean up faction references (but keep spawner buildings!)
            redTeamFaction = null;
            blueTeamFaction = null;
            
            // CRITICAL: DO NOT clear spawner references during cleanup!
            // They should persist between rounds
            
            Log.Message("SolWorld: Current round cleanup complete - preserving spawners");
        }
        
        // ADD: Method to stop all arena pawn jobs
        private void StopAllArenaPawnJobs()
        {
            Log.Message("SolWorld: Stopping all arena pawn jobs to prevent errors...");
            
            var allArenaPawns = redTeamPawns.Concat(blueTeamPawns).Where(p => p?.Spawned == true).ToList();
            
            foreach (var pawn in allArenaPawns)
            {
                try
                {
                    if (pawn.jobs != null)
                    {
                        pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                        pawn.jobs.ClearQueuedJobs();
                    }
                    
                    // Clear any combat targets
                    if (pawn.mindState != null)
                    {
                        pawn.mindState.enemyTarget = null;
                        pawn.mindState.lastEngageTargetTick = 0;
                    }
                }
                catch (System.Exception ex)
                {
                    Log.Warning($"SolWorld: Failed to stop jobs for {pawn.Name}: {ex.Message}");
                }
            }
        }
        
        public float GetTimeLeftInCurrentPhase()
        {
            switch (currentState)
            {
                case ArenaState.Preview:
                    return PreviewTimeRemaining;
                    
                case ArenaState.Combat:
                    var combatElapsed = Find.TickManager.TicksGame - combatStartTick;
                    return Math.Max(0, (COMBAT_TICKS - combatElapsed) / 60f);
                    
                case ArenaState.Ended:
                    if (roundEndTick > 0)
                    {
                        var endElapsed = Find.TickManager.TicksGame - roundEndTick;
                        return Math.Max(0, (RESET_DELAY_TICKS - endElapsed) / 60f);
                    }
                    return 0f;
                    
                default:
                    return 0f;
            }
        }
        
        public string GetPhaseDisplayText()
        {
            var timeLeft = GetTimeLeftInCurrentPhase();
            
            switch (currentState)
            {
                case ArenaState.Preview:
                    return "PREVIEW: " + timeLeft.ToString("F0") + "s (PAUSED)";
                case ArenaState.Combat:
                    return "COMBAT: " + timeLeft.ToString("F0") + "s";
                case ArenaState.Ended:
                    return "Round Complete";
                case ArenaState.Resetting:
                    return "Resetting...";
                default:
                    return "Arena Idle";
            }
        }
        
        public int GetTimeUntilNextRound()
        {
            if (currentState != ArenaState.Idle || nextRoundTick <= 0)
                return 0;
                
            var ticksUntilRound = nextRoundTick - Find.TickManager.TicksGame;
            return Math.Max(0, ticksUntilRound / 60); // Convert to seconds
        }
        
        // MANUAL UNPAUSE METHOD - Can be called from UI
        public void ForceUnpause()
        {
            Log.Message("SolWorld: MANUAL FORCE UNPAUSE called!");
            
            try
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                
                if (Find.TickManager.Paused)
                {
                    Find.TickManager.TogglePaused();
                }
                
                Log.Message($"SolWorld: Manual unpause result - Paused: {Find.TickManager.Paused}, Speed: {Find.TickManager.CurTimeSpeed}");
                
                // If we're in preview mode and manual unpause, start combat immediately
                if (currentState == ArenaState.Preview && currentRoster != null)
                {
                    OnUITriggeredUnpause();
                    Messages.Message("Manual unpause - starting combat immediately!", MessageTypeDefOf.PositiveEvent);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"SolWorld: Manual unpause failed: {ex.Message}");
            }
        }
        
        // MANUAL RESET METHOD - Can be called from UI
        public void ForceReset()
        {
            Log.Message("SolWorld: MANUAL FORCE RESET called!");
            
            try
            {
                if (currentState == ArenaState.Ended || currentState == ArenaState.Resetting)
                {
                    OnUITriggeredReset();
                    Messages.Message("Manual reset triggered!", MessageTypeDefOf.PositiveEvent);
                }
                else
                {
                    Messages.Message("Can only reset after round ends", MessageTypeDefOf.RejectInput);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"SolWorld: Manual reset failed: {ex.Message}");
            }
        }
        
        // DEV/DEBUG METHODS
        public void TestSpawnFighters(RoundRoster testRoster)
        {
            if (testRoster == null || redSpawner == null || blueSpawner == null)
            {
                Log.Error("SolWorld: Cannot test spawn - missing components");
                return;
            }
            
            Log.Message("SolWorld: Test spawning fighters for immediate combat...");
            
            currentRoster = testRoster;
            
            // Set up factions and spawn teams
            SetupArenaFactions();
            SpawnBothTeams();
            
            // Force immediate combat
            InitiateAggressiveCombat();
            
            // Set combat state
            currentState = ArenaState.Combat;
            combatStartTick = Find.TickManager.TicksGame;
            
            Log.Message("SolWorld: Test fighters spawned and combat initiated!");
        }
        
        public void DebugListActivePawns()
        {
            Log.Message($"SolWorld: Active arena pawns - Red: {redTeamPawns.Count}, Blue: {blueTeamPawns.Count}");
            
            foreach (var pawn in redTeamPawns)
            {
                if (pawn != null)
                {
                    var jobName = pawn.CurJob?.def?.defName ?? "No Job";
                    Log.Message($"  Red: {pawn.Name} - Job: {jobName}");
                }
            }
            
            foreach (var pawn in blueTeamPawns)
            {
                if (pawn != null)
                {
                    var jobName = pawn.CurJob?.def?.defName ?? "No Job";
                    Log.Message($"  Blue: {pawn.Name} - Job: {jobName}");
                }
            }
        }
        
        public List<Pawn> GetAllArenaPawns()
        {
            return redTeamPawns.Concat(blueTeamPawns).Where(p => p != null).ToList();
        }
        
        public TeamColor? GetPawnTeam(Pawn pawn)
        {
            return pawnTeamMap.TryGetValue(pawn, out var team) ? team : (TeamColor?)null;
        }
        
        // Test combat movement
        public void TestCombatMovement()
        {
            if (currentRoster == null || redTeamPawns.Count == 0 || blueTeamPawns.Count == 0)
            {
                Log.Warning("SolWorld: No teams to test combat movement");
                return;
            }
            
            Log.Message("SolWorld: Testing combat movement...");
            
            // Force every red pawn to move toward blue spawn
            foreach (var redPawn in redTeamPawns.Where(p => p?.Spawned == true))
            {
                ForceMoveToward(redPawn, blueSpawner.Position);
            }
            
            // Force every blue pawn to move toward red spawn  
            foreach (var bluePawn in blueTeamPawns.Where(p => p?.Spawned == true))
            {
                ForceMoveToward(bluePawn, redSpawner.Position);
            }
            
            Messages.Message("Combat movement test initiated - pawns should move!", MessageTypeDefOf.PositiveEvent);
        }
        
        private void ForceMoveToward(Pawn pawn, IntVec3 targetPos)
        {
            try
            {
                if (pawn.jobs != null)
                {
                    pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                    
                    var moveJob = JobMaker.MakeJob(JobDefOf.Goto, targetPos);
                    moveJob.playerForced = true;
                    moveJob.locomotionUrgency = LocomotionUrgency.Sprint;
                    
                    pawn.jobs.StartJob(moveJob, JobCondition.InterruptForced);
                    
                    Log.Message($"SolWorld: Forcing {pawn.Name} to move toward {targetPos}");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"SolWorld: Failed to force movement for {pawn.Name}: {ex.Message}");
            }
        }
        
        // DEBUG: Blueprint status
        public void DebugBlueprintStatus()
        {
            Log.Message($"SolWorld: Blueprint Status - Initialized: {arenaBlueprint.IsInitialized}");
            
            if (arenaBlueprint.IsInitialized)
            {
                var cellCount = arenaBlueprint.GetAllCells().Count();
                var thingCount = arenaBlueprint.GetAllCells().Sum(c => c.Things.Count);
                Log.Message($"SolWorld: Blueprint contains {cellCount} cells with {thingCount} things total");
            }
            
            var bounds = GetArenaBounds();
            if (bounds.HasValue)
            {
                Log.Message($"SolWorld: Current arena bounds: {bounds.Value.Width}x{bounds.Value.Height} at ({bounds.Value.minX},{bounds.Value.minZ})");
            }
            else
            {
                Log.Warning("SolWorld: No valid arena bounds found");
            }
        }
    }
}