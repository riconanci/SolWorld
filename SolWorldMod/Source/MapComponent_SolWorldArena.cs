// solworld/SolWorldMod/Source/MapComponent_SolWorldArena.cs
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
        
        // Team management - use existing factions
        private Faction redTeamFaction;
        private Faction blueTeamFaction;
        private List<Pawn> redTeamPawns = new List<Pawn>();
        private List<Pawn> blueTeamPawns = new List<Pawn>();
        private Dictionary<Pawn, TeamColor> pawnTeamMap = new Dictionary<Pawn, TeamColor>();
        
        // UI-TRIGGERED SYSTEMS - Same approach as unpause fix
        private bool previewCompleted = false;
        private bool uiShouldTriggerUnpause = false;
        private bool uiShouldTriggerReset = false;
        
        // Enhanced combat enforcement
        private bool combatInitiated = false;
        private int lastCombatEnforcementTick = -1;
        private int lastAggressiveEnforcementTick = -1;
        private Dictionary<Pawn, int> pawnLastActionTick = new Dictionary<Pawn, int>();
        
        // Components
        private ArenaBounds arenaBounds;
        private ArenaBlueprint arenaBlueprint;
        private ArenaReset arenaReset;
        
        // Accessors - ALWAYS AVAILABLE to prevent button disappearing
        public ArenaState CurrentState => currentState;
        public bool IsActive => isActive;
        public RoundRoster CurrentRoster => currentRoster;
        public bool HasValidSetup => arenaCore?.IsOperational == true && redSpawner != null && blueSpawner != null;
        
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
            
            // Rebuild team map after loading
            if (redTeamPawns == null) redTeamPawns = new List<Pawn>();
            if (blueTeamPawns == null) blueTeamPawns = new List<Pawn>();
            
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
        
        public override void MapComponentTick()
        {
            base.MapComponentTick();
            
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
            }
        }
        
        // Execute arena reset from UI context
        private void ExecuteArenaReset()
        {
            Log.Message("SolWorld: ===== EXECUTING ARENA RESET FROM UI CONTEXT =====");
            
            try
            {
                // FIRST: Cleanup current round pawns
                CleanupCurrentRound();
                
                // SECOND: Perform arena reset if blueprint exists
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
                
                // THIRD: Reset all state to idle and schedule next round
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
                
                // FOURTH: Schedule next round
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
        
        private void RefreshSpawners()
        {
            if (map == null) 
            {
                return;
            }
            
            // ALWAYS refresh spawners to prevent button disappearing
            var prevRed = redSpawner;
            var prevBlue = blueSpawner;
            
            redSpawner = null;
            blueSpawner = null;
            
            var allBuildings = map.listerBuildings.allBuildingsColonist;
            
            foreach (var building in allBuildings)
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
            
            // Log changes to spawner availability
            if (prevRed != redSpawner || prevBlue != blueSpawner)
            {
                Log.Message("SolWorld: Spawner refresh - Red: " + (redSpawner != null) + ", Blue: " + (blueSpawner != null));
            }
        }
        
        public CellRect? GetArenaBounds()
        {
            return arenaBounds.CalculateBounds(arenaCore, redSpawner, blueSpawner);
        }
        
        public void StartArena()
        {
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
        
        private void StartNewRound()
        {
            Log.Message("SolWorld: ===== STARTING NEW ROUND =====");
            
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
                
                // Step 3: Initialize blueprint BEFORE spawning (if not already done)
                var bounds = GetArenaBounds();
                if (bounds.HasValue && !arenaBlueprint.IsInitialized)
                {
                    Log.Message("SolWorld: Initializing blueprint...");
                    arenaBlueprint.InitializeBlueprint(map, bounds.Value);
                }
                
                // Step 4: Spawn teams
                Log.Message("SolWorld: ===== SPAWNING TEAMS =====");
                SpawnBothTeams();
                
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
            var mockHolders = GenerateMockHolders();
            
            currentRoster = new RoundRoster
            {
                RoundRewardTotalSol = SolWorldMod.Settings.roundPoolSol,
                PayoutPercent = SolWorldMod.Settings.payoutPercent
            };
            
            for (int i = 0; i < 10; i++)
            {
                currentRoster.Red.Add(new Fighter(mockHolders[i], TeamColor.Red));
                currentRoster.Blue.Add(new Fighter(mockHolders[i + 10], TeamColor.Blue));
            }
            
            Log.Message("SolWorld: Created roster with 20 fighters (10 red, 10 blue)");
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
            Log.Message("SolWorld: Setting up arena factions using existing hostile/friendly factions");
            
            // Find an existing hostile faction for red team
            redTeamFaction = Find.FactionManager.AllFactions
                .Where(f => f != null && !f.IsPlayer && f.HostileTo(Faction.OfPlayer) && f.def.humanlikeFaction)
                .FirstOrDefault();
            
            if (redTeamFaction == null)
            {
                Log.Warning("SolWorld: No hostile faction found, red team will use player faction");
                redTeamFaction = Faction.OfPlayer;
            }
            else
            {
                Log.Message($"SolWorld: Using hostile faction '{redTeamFaction.Name}' for red team");
            }
            
            // Find an existing friendly faction for blue team
            blueTeamFaction = Find.FactionManager.AllFactions
                .Where(f => f != null && !f.IsPlayer && !f.HostileTo(Faction.OfPlayer) && f.def.humanlikeFaction)
                .FirstOrDefault();
            
            if (blueTeamFaction == null)
            {
                Log.Warning("SolWorld: No friendly faction found, using player faction for blue team");
                blueTeamFaction = Faction.OfPlayer;
            }
            else
            {
                Log.Message($"SolWorld: Using friendly faction '{blueTeamFaction.Name}' for blue team");
                
                // Make blue team hostile to red team so they fight each other
                if (redTeamFaction != blueTeamFaction && redTeamFaction != Faction.OfPlayer)
                {
                    try
                    {
                        // Set mutual hostility between the teams
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
                        
                        Log.Message($"SolWorld: Set {redTeamFaction.Name} and {blueTeamFaction.Name} as hostile to each other");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"SolWorld: Failed to set faction hostility: {ex.Message}");
                    }
                }
            }
        }
        
        private void SpawnBothTeams()
        {
            Log.Message("SolWorld: ===== SPAWNING BOTH TEAMS =====");
            
            // Clear previous pawn lists and mappings
            redTeamPawns.Clear();
            blueTeamPawns.Clear();
            pawnTeamMap.Clear();
            pawnLastActionTick.Clear();
            
            // Spawn red team (hostile)
            Log.Message("SolWorld: Spawning red team (HOSTILE)...");
            SpawnTeam(currentRoster.Red, redSpawner.Position, TeamColor.Red, redTeamFaction);
            
            // Spawn blue team (friendly)
            Log.Message("SolWorld: Spawning blue team (FRIENDLY)...");
            SpawnTeam(currentRoster.Blue, blueSpawner.Position, TeamColor.Blue, blueTeamFaction);
            
            Log.Message("SolWorld: ===== BOTH TEAMS SPAWNED SUCCESSFULLY =====");
            
            // Count spawned pawns for verification
            var redSpawned = currentRoster.Red.Count(f => f.PawnRef?.Spawned == true);
            var blueSpawned = currentRoster.Blue.Count(f => f.PawnRef?.Spawned == true);
            Log.Message($"SolWorld: Verification - Red spawned: {redSpawned}/10, Blue spawned: {blueSpawned}/10");
        }
        
        private void SpawnTeam(List<Fighter> fighters, IntVec3 spawnerPos, TeamColor teamColor, Faction teamFaction)
        {
            Log.Message($"SolWorld: Spawning {teamColor} team at {spawnerPos} with faction {teamFaction.Name}...");
            
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
                        
                        // Add to team tracking
                        if (teamColor == TeamColor.Red)
                            redTeamPawns.Add(pawn);
                        else
                            blueTeamPawns.Add(pawn);
                        
                        pawnTeamMap[pawn] = teamColor;
                        pawnLastActionTick[pawn] = -1;
                        
                        // Apply team visual styling
                        ApplyTeamStyling(pawn, teamColor);
                        
                        Log.Message($"SolWorld: Spawned {fighter.WalletShort} ({teamColor})");
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"SolWorld: Failed to spawn {fighter.WalletShort}: {ex.Message}");
                    }
                }
            }
        }
        
        // FIXED: Pawn generation to avoid cast errors
        private Pawn GenerateWarrior(Fighter fighter, TeamColor teamColor, Faction teamFaction)
        {
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
                
                EnsurePawnMindStateSetup(pawn);
                GiveWeapon(pawn);
                MakeWarrior(pawn);
                
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
                if (pawn.story != null)
                {
                    if (teamColor == TeamColor.Red)
                        pawn.story.HairColor = Color.red;
                    else
                        pawn.story.HairColor = Color.blue;
                }
                
                if (pawn.Drawer?.renderer != null)
                {
                    pawn.Drawer.renderer.SetAllGraphicsDirty();
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"SolWorld: Failed to apply team styling: {ex.Message}");
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
        
        private void InitiateAggressiveCombat()
        {
            Log.Message("SolWorld: ===== INITIATING AGGRESSIVE COMBAT =====");
            
            combatInitiated = true;
            lastCombatEnforcementTick = Find.TickManager.TicksGame;
            lastAggressiveEnforcementTick = Find.TickManager.TicksGame;
            
            // Make teams TRULY hostile to each other
            SetupProperFactionHostility();
            
            // Force immediate combat engagement
            foreach (var redPawn in redTeamPawns.Where(p => p?.Spawned == true && !p.Dead))
            {
                SetupAggressiveCombatant(redPawn, TeamColor.Red);
            }
            
            foreach (var bluePawn in blueTeamPawns.Where(p => p?.Spawned == true && !p.Dead))
            {
                SetupAggressiveCombatant(bluePawn, TeamColor.Blue);
            }
            
            // Force initial attack orders
            ForceInitialCombatEngagement();
        }
        
        private void SetupProperFactionHostility()
        {
            if (redTeamFaction != null && blueTeamFaction != null && redTeamFaction != blueTeamFaction)
            {
                try
                {
                    // Force mutual hostility
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
                    
                    Log.Message($"SolWorld: Forced hostility between {redTeamFaction.Name} and {blueTeamFaction.Name}");
                }
                catch (Exception ex)
                {
                    Log.Warning($"SolWorld: Failed to force faction hostility: {ex.Message}");
                }
            }
            
            // Manually set pawn hostilities if faction hostility fails
            foreach (var redPawn in redTeamPawns.Where(p => p?.Spawned == true))
            {
                foreach (var bluePawn in blueTeamPawns.Where(p => p?.Spawned == true))
                {
                    try
                    {
                        // Force them to see each other as enemies
                        if (redPawn.mindState != null)
                        {
                            redPawn.mindState.enemyTarget = bluePawn;
                            redPawn.mindState.lastEngageTargetTick = Find.TickManager.TicksGame;
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
                        // Note: maxNumStaticAttacks might not be available in RimWorld 1.6
                        // attackJob.maxNumStaticAttacks = 999;
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
                                // Note: maxNumStaticAttacks might not be available in RimWorld 1.6
                                // attackJob.maxNumStaticAttacks = 999;
                            }
                        }
                        else
                        {
                            // Fallback to direct attack if can't calculate direction
                            attackJob = JobMaker.MakeJob(JobDefOf.AttackStatic, target);
                            // Note: maxNumStaticAttacks might not be available in RimWorld 1.6
                            // attackJob.maxNumStaticAttacks = 999;
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
        
        // ENHANCED: More aggressive combat enforcement
        private void EnforceContinuousCombat()
        {
            if (currentRoster == null || !combatInitiated)
                return;
            
            var currentTick = Find.TickManager.TicksGame;
            var bounds = GetArenaBounds();
            
            var redAlive = redTeamPawns.Where(p => p?.Spawned == true && !p.Dead).ToList();
            var blueAlive = blueTeamPawns.Where(p => p?.Spawned == true && !p.Dead).ToList();
            
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
        
        // PAWN DEATH TRACKING - Called from KillTracking.cs
        public void OnPawnDeath(Pawn deadPawn, Pawn killer)
        {
            if (currentRoster?.IsLive != true || !pawnTeamMap.ContainsKey(deadPawn))
                return;
                
            var deadTeam = pawnTeamMap[deadPawn];
            
            // Find the dead fighter in our roster
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
                Log.Message($"SolWorld: {deadFighter.WalletShort} ({deadTeam}) was killed");
                
                // Credit the kill if we have a valid killer
                if (killer != null && pawnTeamMap.ContainsKey(killer))
                {
                    var killerTeam = pawnTeamMap[killer];
                    if (killerTeam != deadTeam) // Only credit cross-team kills
                    {
                        Fighter killerFighter = null;
                        if (killerTeam == TeamColor.Red)
                        {
                            killerFighter = currentRoster.Red.FirstOrDefault(f => f.PawnRef == killer);
                        }
                        else
                        {
                            killerFighter = currentRoster.Blue.FirstOrDefault(f => f.PawnRef == killer);
                        }
                        
                        if (killerFighter != null)
                        {
                            killerFighter.Kills++;
                            Log.Message($"SolWorld: Kill credited to {killerFighter.WalletShort} ({killerTeam}) - Total: {killerFighter.Kills}");
                        }
                    }
                }
            }
        }
        
        private void EndRound(string reason)
        {
            currentState = ArenaState.Ended;
            roundEndTick = Find.TickManager.TicksGame; // Track when round ended
            
            if (currentRoster == null) return;
            
            currentRoster.IsLive = false;
            currentRoster.Winner = currentRoster.DetermineWinner();
            
            Log.Message("SolWorld: Round ended - " + reason + ". Winner: " + currentRoster.Winner);
            
            // KEEP LEADERBOARD VISIBLE - Don't cleanup roster yet, let UI show results
            
            // TODO: Report to backend and get real txids
            var mockTxids = new string[] { "MockTx1", "MockTx2" };
            Messages.Message("ROUND COMPLETE! Winner: " + currentRoster.Winner + " team", MessageTypeDefOf.PositiveEvent);
            
            // IMPORTANT: Don't cleanup here - wait for reset phase
        }
        
        private void CleanupCurrentRound()
        {
            if (currentRoster == null) return;
            
            // Clean up all arena pawns
            var allArenaPawns = redTeamPawns.Concat(blueTeamPawns).ToList();
            
            foreach (var pawn in allArenaPawns)
            {
                if (pawn?.Spawned == true)
                {
                    try
                    {
                        pawn.DeSpawn();
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"SolWorld: Failed to despawn pawn {pawn.Name}: {ex.Message}");
                    }
                }
            }
            
            // Clear tracking collections
            redTeamPawns.Clear();
            blueTeamPawns.Clear();
            pawnTeamMap.Clear();
            pawnLastActionTick.Clear();
            
            // Clean up faction references
            redTeamFaction = null;
            blueTeamFaction = null;
            
            Log.Message("SolWorld: Current round cleanup complete");
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
    }
}