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
        private const float PREVIEW_SECONDS = 30f; // Real-time during pause
        private const int COMBAT_TICKS = 90 * 60; // 1.5 minutes (90 seconds) game time
        private const int CADENCE_TICKS = 300 * 60; // 5 minutes between rounds
        
        // Team management - use existing factions
        private Faction redTeamFaction;
        private Faction blueTeamFaction;
        private List<Pawn> redTeamPawns = new List<Pawn>();
        private List<Pawn> blueTeamPawns = new List<Pawn>();
        private Dictionary<Pawn, TeamColor> pawnTeamMap = new Dictionary<Pawn, TeamColor>();
        
        // NEW: UI-TRIGGERED UNPAUSE SYSTEM - Let UI handle the unpause directly
        private bool previewCompleted = false;
        private bool uiShouldTriggerUnpause = false; // NEW: Flag for UI to trigger unpause
        
        // Combat enforcement
        private bool combatInitiated = false;
        private int lastCombatEnforcementTick = -1;
        
        // Components
        private ArenaBounds arenaBounds;
        private ArenaBlueprint arenaBlueprint;
        private ArenaReset arenaReset;
        
        // Accessors
        public ArenaState CurrentState => currentState;
        public bool IsActive => isActive;
        public RoundRoster CurrentRoster => currentRoster;
        
        // Preview timing accessors for UI
        public DateTime PreviewStartTime => previewStartTime;
        public bool IsPreviewActive => currentState == ArenaState.Preview && !previewCompleted;
        public bool ShouldUITriggerUnpause => uiShouldTriggerUnpause; // NEW: UI checks this
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
            Scribe_Values.Look(ref combatInitiated, "combatInitiated", false);
            Scribe_Values.Look(ref lastCombatEnforcementTick, "lastCombatEnforcementTick", -1);
            Scribe_Values.Look(ref previewCompleted, "previewCompleted", false);
            Scribe_Values.Look(ref uiShouldTriggerUnpause, "uiShouldTriggerUnpause", false);
            Scribe_Deep.Look(ref currentRoster, "currentRoster");
            Scribe_References.Look(ref redTeamFaction, "redTeamFaction");
            Scribe_References.Look(ref blueTeamFaction, "blueTeamFaction");
            Scribe_Collections.Look(ref redTeamPawns, "redTeamPawns", LookMode.Reference);
            Scribe_Collections.Look(ref blueTeamPawns, "blueTeamPawns", LookMode.Reference);
            
            // Rebuild team map after loading
            if (redTeamPawns == null) redTeamPawns = new List<Pawn>();
            if (blueTeamPawns == null) blueTeamPawns = new List<Pawn>();
            
            pawnTeamMap.Clear();
            foreach (var pawn in redTeamPawns.Where(p => p != null))
            {
                pawnTeamMap[pawn] = TeamColor.Red;
            }
            foreach (var pawn in blueTeamPawns.Where(p => p != null))
            {
                pawnTeamMap[pawn] = TeamColor.Blue;
            }
            
            // Clean up null references
            redTeamPawns.RemoveAll(p => p == null);
            blueTeamPawns.RemoveAll(p => p == null);
        }
        
        public override void MapComponentTick()
        {
            base.MapComponentTick();
            
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
            
            // Handle phase transitions (NO automatic unpause logic here anymore)
            HandlePhaseTransitions();
            
            // Combat enforcement every 3 seconds during combat
            if (currentState == ArenaState.Combat && (currentTick - lastCombatEnforcementTick) >= 180)
            {
                lastCombatEnforcementTick = currentTick;
                EnforceContinuousCombat();
            }
            
            // Update roster status every 30 ticks during combat
            if (currentState == ArenaState.Combat && currentTick % 30 == 0)
            {
                UpdateRosterStatus();
            }
        }
        
        // NEW: Called by UI when preview timer expires - just sets flag for ArenaCore to handle
        public void RequestCombatTransition()
        {
            if (currentState == ArenaState.Preview && !previewCompleted)
            {
                Log.Message("SolWorld: UI requested combat transition - flagging for Arena Core to handle!");
                previewCompleted = true;
                uiShouldTriggerUnpause = true; // UI will see this and trigger the manual unpause
            }
        }
        
        // NEW: Called by ArenaCore when UI triggers the manual unpause
        public void OnUITriggeredUnpause()
        {
            Log.Message("SolWorld: ===== UI TRIGGERED UNPAUSE SUCCESS =====");
            
            uiShouldTriggerUnpause = false; // Clear the flag
            
            // Now start combat from the SAME context as manual button (UI/Gizmo context)
            ExecuteCombatTransition();
        }
        
        private void ExecuteCombatTransition()
        {
            Log.Message("SolWorld: ===== EXECUTING COMBAT TRANSITION FROM UI CONTEXT =====");
            
            currentState = ArenaState.Combat;
            combatStartTick = Find.TickManager.TicksGame;
            combatInitiated = false;
            
            if (currentRoster != null)
            {
                currentRoster.IsLive = true;
                InitiateAggressiveCombat();
                Messages.Message("COMBAT STARTED! 90 seconds to fight!", MessageTypeDefOf.PositiveEvent);
            }
        }
        
        private void HandlePhaseTransitions()
        {
            switch (currentState)
            {
                case ArenaState.Preview:
                    // Preview timing is now handled by UI layer - NO automatic unpause here
                    break;
                    
                case ArenaState.Combat:
                    // Use GAME TIME for combat
                    var combatElapsed = Find.TickManager.TicksGame - combatStartTick;
                    if (combatElapsed >= COMBAT_TICKS)
                    {
                        EndRound("Time limit reached (90 seconds)");
                    }
                    else if (currentRoster != null && (currentRoster.RedAlive == 0 || currentRoster.BlueAlive == 0))
                    {
                        EndRound("Team eliminated");
                    }
                    break;
                    
                case ArenaState.Ended:
                    // Quick transition to reset after 3 seconds
                    var endElapsed = Find.TickManager.TicksGame - combatStartTick - COMBAT_TICKS;
                    if (endElapsed >= 180) // 3 seconds
                    {
                        StartResetPhase();
                    }
                    break;
                    
                case ArenaState.Resetting:
                    // Quick reset after 2 seconds, then schedule next round
                    var resetElapsed = Find.TickManager.TicksGame - combatStartTick - COMBAT_TICKS;
                    if (resetElapsed >= 300) // 5 seconds total
                    {
                        CompleteReset();
                    }
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
                Log.Warning("SolWorld: RefreshSpawners called but map is null");
                return;
            }
            
            redSpawner = null;
            blueSpawner = null;
            
            var allBuildings = map.listerBuildings.allBuildingsColonist;
            
            foreach (var building in allBuildings)
            {
                if (building.def?.defName == "SolWorld_RedSpawn")
                {
                    redSpawner = building;
                }
                else if (building.def?.defName == "SolWorld_BlueSpawn")
                {
                    blueSpawner = building;
                }
            }
            
            Log.Message("SolWorld: Spawners found - Red: " + (redSpawner != null) + ", Blue: " + (blueSpawner != null));
        }
        
        public bool HasValidSetup => arenaCore?.IsOperational == true && redSpawner != null && blueSpawner != null;
        
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
            combatInitiated = false;
            lastCombatEnforcementTick = -1;
            previewCompleted = false;
            uiShouldTriggerUnpause = false;
            
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
            nextRoundTick = currentTime + CADENCE_TICKS; // 5 minutes from now
            
            var timeUntilRound = CADENCE_TICKS / 60f;
            Log.Message("SolWorld: Next round scheduled in " + timeUntilRound.ToString("F0") + " seconds (tick " + nextRoundTick + ")");
        }
        
        private void StartNewRound()
        {
            Log.Message("SolWorld: ===== STARTING NEW ROUND =====");
            
            currentState = ArenaState.Preview;
            previewStartTime = DateTime.Now; // Real-time tracking for paused preview
            previewCompleted = false;
            uiShouldTriggerUnpause = false;
            combatInitiated = false;
            lastCombatEnforcementTick = -1;
            
            try
            {
                // Step 1: Create roster
                Log.Message("SolWorld: Creating roster...");
                CreateRoster();
                
                // Step 2: Set up factions
                Log.Message("SolWorld: Setting up arena factions...");
                SetupArenaFactions();
                
                // Step 3: Initialize blueprint BEFORE spawning
                var bounds = GetArenaBounds();
                if (bounds.HasValue)
                {
                    Log.Message("SolWorld: Initializing blueprint...");
                    arenaBlueprint.InitializeBlueprint(map, bounds.Value);
                }
                
                // Step 4: Spawn teams
                Log.Message("SolWorld: ===== SPAWNING TEAMS =====");
                SpawnBothTeams();
                
                // Step 5: PAUSE using the correct RimWorld 1.6 method
                Log.Message("SolWorld: ===== PAUSING GAME =====");
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                Log.Message($"SolWorld: Game paused for 30-second preview - Speed now: {Find.TickManager.CurTimeSpeed}, Paused: {Find.TickManager.Paused}");
                
                var payoutText = currentRoster.PerWinnerPayout.ToString("F3");
                Messages.Message("30-SECOND PREVIEW: Round " + currentRoster.MatchId + " - " + payoutText + " SOL per winner", MessageTypeDefOf.PositiveEvent);
                
                Log.Message("SolWorld: ===== ROUND STARTED SUCCESSFULLY =====");
                Log.Message("SolWorld: UI will handle countdown and trigger Arena Core unpause button automatically");
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
            
            Log.Message($"SolWorld: Red team faction: {redTeamFaction.Name} (hostile to player: {redTeamFaction.HostileTo(Faction.OfPlayer)})");
            Log.Message($"SolWorld: Blue team faction: {blueTeamFaction.Name} (hostile to player: {blueTeamFaction.HostileTo(Faction.OfPlayer)})");
            Log.Message($"SolWorld: Teams hostile to each other: {redTeamFaction.HostileTo(blueTeamFaction)}");
        }
        
        private void SpawnBothTeams()
        {
            Log.Message("SolWorld: ===== SPAWNING BOTH TEAMS =====");
            
            // Clear previous pawn lists and mappings
            redTeamPawns.Clear();
            blueTeamPawns.Clear();
            pawnTeamMap.Clear();
            
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
            Log.Message($"SolWorld: Team lists - Red pawns: {redTeamPawns.Count}, Blue pawns: {blueTeamPawns.Count}");
            
            // Verify faction hostilities
            if (redTeamPawns.Count > 0 && blueTeamPawns.Count > 0)
            {
                var redPawn = redTeamPawns[0];
                var bluePawn = blueTeamPawns[0];
                Log.Message($"SolWorld: Red pawn faction: {redPawn.Faction?.Name}, hostile to player: {redPawn.HostileTo(Faction.OfPlayer)}");
                Log.Message($"SolWorld: Blue pawn faction: {bluePawn.Faction?.Name}, hostile to player: {bluePawn.HostileTo(Faction.OfPlayer)}");
                Log.Message($"SolWorld: Red hostile to Blue: {redPawn.HostileTo(bluePawn)}");
            }
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
                
                Log.Message($"SolWorld: Generating warrior {i+1}/10 for {teamColor} team...");
                var pawn = GenerateWarrior(fighter, teamColor, teamFaction);
                
                if (pawn != null)
                {
                    try
                    {
                        Log.Message($"SolWorld: Spawning {fighter.WalletShort} at {spawnPos}...");
                        GenSpawn.Spawn(pawn, spawnPos, map);
                        fighter.PawnRef = pawn;
                        
                        // Add to team tracking
                        if (teamColor == TeamColor.Red)
                            redTeamPawns.Add(pawn);
                        else
                            blueTeamPawns.Add(pawn);
                        
                        pawnTeamMap[pawn] = teamColor;
                        
                        // Apply team visual styling
                        ApplyTeamStyling(pawn, teamColor);
                        
                        Log.Message($"SolWorld: Spawned {fighter.WalletShort} ({teamColor}) - Faction: {pawn.Faction?.Name}, Hostile to player: {pawn.HostileTo(Faction.OfPlayer)}");
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"SolWorld: Failed to spawn {fighter.WalletShort}: {ex.Message}");
                    }
                }
                else
                {
                    Log.Error($"SolWorld: Failed to generate warrior for {fighter.WalletShort}");
                }
            }
            
            Log.Message($"SolWorld: {teamColor} team spawn complete");
        }
        
        // FIXED: Pawn generation to avoid cast errors
        private Pawn GenerateWarrior(Fighter fighter, TeamColor teamColor, Faction teamFaction)
        {
            try
            {
                Log.Message($"SolWorld: Generating warrior for {fighter.WalletShort} with faction {teamFaction.Name}...");
                
                // Use appropriate pawn kind based on faction
                PawnKindDef pawnKind;
                if (teamFaction.def.pawnGroupMakers?.Any() == true)
                {
                    // Use faction's natural pawn kinds
                    var pawnGroupMaker = teamFaction.def.pawnGroupMakers.FirstOrDefault();
                    pawnKind = pawnGroupMaker?.options?.FirstOrDefault()?.kind ?? PawnKindDefOf.Colonist;
                }
                else
                {
                    pawnKind = PawnKindDefOf.Colonist;
                }
                
                // FIXED: Create PawnGenerationRequest that avoids cast errors
                var request = new PawnGenerationRequest(
                    kind: pawnKind,
                    faction: teamFaction,
                    context: PawnGenerationContext.NonPlayer,
                    tile: map.Tile,
                    forceGenerateNewPawn: true,
                    allowDead: false,
                    allowDowned: false,
                    canGeneratePawnRelations: false, // CRITICAL: Prevents cast errors in pawn relations
                    mustBeCapableOfViolence: true,
                    colonistRelationChanceFactor: 0f, // No relation generation
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
                
                // CRITICAL FIX: Ensure pawn has proper MindState components
                EnsurePawnMindStateSetup(pawn);
                
                GiveWeapon(pawn);
                MakeWarrior(pawn);
                
                Log.Message($"SolWorld: Generated warrior {fighter.WalletShort} successfully");
                return pawn;
            }
            catch (Exception ex)
            {
                Log.Error("SolWorld: Failed to generate warrior: " + ex.Message);
                return null;
            }
        }
        
        // CRITICAL FIX: Ensure pawns have proper MindState setup to prevent NullReferenceException
        private void EnsurePawnMindStateSetup(Pawn pawn)
        {
            try
            {
                // Ensure mindState is not null and properly initialized
                if (pawn.mindState == null)
                {
                    Log.Warning($"SolWorld: Pawn {pawn.Name} has null mindState - this should not happen");
                    return;
                }
                
                // Ensure mentalBreaker is not null (this was likely causing the NRE)
                if (pawn.mindState.mentalBreaker == null)
                {
                    Log.Message($"SolWorld: Initializing mentalBreaker for {pawn.Name}");
                    // RimWorld should auto-initialize this, but let's be safe
                    // DON'T manually create it - just log and let RimWorld handle it
                }
                
                // Set basic combat-ready mental state
                pawn.mindState.canFleeIndividual = false;
                
                // Ensure pawn has proper job capacity
                if (pawn.jobs == null)
                {
                    Log.Warning($"SolWorld: Pawn {pawn.Name} has null jobs - this should not happen");
                    return;
                }
                
                // Set a basic duty for now
                pawn.mindState.duty = new PawnDuty(DutyDefOf.AssaultColony);
                
                Log.Message($"SolWorld: Mind state setup complete for {pawn.Name}");
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
                // Set hair color for team identification
                if (pawn.story != null)
                {
                    if (teamColor == TeamColor.Red)
                        pawn.story.HairColor = Color.red;
                    else
                        pawn.story.HairColor = Color.blue;
                }
                
                // Force graphics refresh
                if (pawn.Drawer?.renderer != null)
                {
                    pawn.Drawer.renderer.SetAllGraphicsDirty();
                }
                
                Log.Message($"SolWorld: Applied {teamColor} team styling to {pawn.Name}");
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
            
            Log.Warning($"SolWorld: Could not give any weapon to pawn {pawn.Name}");
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
            if (pawn.needs?.mood != null)
            {
                pawn.needs.mood.CurLevel = 1.0f;
            }
            if (pawn.needs?.rest != null)
            {
                pawn.needs.rest.CurLevel = 1.0f;
            }
            if (pawn.needs?.food != null)
            {
                pawn.needs.food.CurLevel = 1.0f;
            }
            
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
                    // Remove pacifist
                    var pacifistTrait = DefDatabase<TraitDef>.GetNamedSilentFail("Pacifist");
                    if (pacifistTrait != null && pawn.story.traits.HasTrait(pacifistTrait))
                    {
                        pawn.story.traits.allTraits.RemoveAll(t => t.def == pacifistTrait);
                    }
                    
                    // Add brawler if space
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
            
            Log.Message("SolWorld: ===== AGGRESSIVE COMBAT INITIATION COMPLETE =====");
        }
        
        private void SetupProperFactionHostility()
        {
            Log.Message("SolWorld: Setting up AGGRESSIVE faction hostility...");
            
            // Make sure red and blue factions are DEFINITELY hostile
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
                        if (redPawn.mindState != null && redPawn.mindState.enemyTarget != bluePawn)
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
            Log.Message($"SolWorld: Setting up AGGRESSIVE combatant {pawn.Name} ({team})");
            
            // Make them fearless and aggressive
            if (pawn.mindState != null)
            {
                pawn.mindState.canFleeIndividual = false;
                
                // Force aggressive duty
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
                // Remove any incapacitating hediffs
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
            
            var alivePawns = redTeamPawns.Concat(blueTeamPawns)
                .Where(p => p?.Spawned == true && !p.Dead)
                .ToList();
            
            if (alivePawns.Count < 2)
            {
                Log.Warning("SolWorld: Not enough pawns for combat engagement");
                return;
            }
            
            // Force red team to attack blue team
            foreach (var redPawn in redTeamPawns.Where(p => p?.Spawned == true && !p.Dead))
            {
                var nearestBlue = blueTeamPawns
                    .Where(p => p?.Spawned == true && !p.Dead)
                    .OrderBy(p => redPawn.Position.DistanceTo(p.Position))
                    .FirstOrDefault();
                
                if (nearestBlue != null)
                {
                    ForceAttackTarget(redPawn, nearestBlue);
                }
            }
            
            // Force blue team to attack red team
            foreach (var bluePawn in blueTeamPawns.Where(p => p?.Spawned == true && !p.Dead))
            {
                var nearestRed = redTeamPawns
                    .Where(p => p?.Spawned == true && !p.Dead)
                    .OrderBy(p => bluePawn.Position.DistanceTo(p.Position))
                    .FirstOrDefault();
                
                if (nearestRed != null)
                {
                    ForceAttackTarget(bluePawn, nearestRed);
                }
            }
            
            Log.Message("SolWorld: Initial combat engagement orders issued");
        }
        
        private void ForceAttackTarget(Pawn attacker, Pawn target)
        {
            try
            {
                Log.Message($"SolWorld: Forcing {attacker.Name} to attack {target.Name}");
                
                // Clear current job forcefully
                if (attacker.jobs != null)
                {
                    attacker.jobs.EndCurrentJob(JobCondition.InterruptForced);
                    attacker.jobs.ClearQueuedJobs();
                }
                
                // Set enemy target directly
                if (attacker.mindState != null)
                {
                    attacker.mindState.enemyTarget = target;
                    attacker.mindState.lastEngageTargetTick = Find.TickManager.TicksGame;
                }
                
                // Create appropriate attack job
                Job attackJob = null;
                
                // Check if they have a ranged weapon
                if (attacker.equipment?.Primary != null && attacker.equipment.Primary.def.IsRangedWeapon)
                {
                    // Ranged attack - but first move closer if too far
                    var distance = attacker.Position.DistanceTo(target.Position);
                    if (distance > attacker.equipment.Primary.def.Verbs[0].range * 0.8f)
                    {
                        // Move closer first
                        var moveToPos = CellFinder.RandomClosewalkCellNear(target.Position, attacker.Map, 3);
                        if (moveToPos.IsValid)
                        {
                            attackJob = JobMaker.MakeJob(JobDefOf.Goto, moveToPos);
                        }
                    }
                    else
                    {
                        attackJob = JobMaker.MakeJob(JobDefOf.AttackStatic, target);
                        attackJob.maxNumStaticAttacks = 999;
                    }
                }
                else
                {
                    // Melee attack - move to target
                    attackJob = JobMaker.MakeJob(JobDefOf.AttackMelee, target);
                }
                
                // If no attack job was created, force a goto
                if (attackJob == null)
                {
                    var movePos = CellFinder.RandomClosewalkCellNear(target.Position, attacker.Map, 2);
                    if (movePos.IsValid)
                    {
                        attackJob = JobMaker.MakeJob(JobDefOf.Goto, movePos);
                    }
                }
                
                if (attackJob != null)
                {
                    attackJob.playerForced = true;
                    attackJob.canBashDoors = true;
                    attackJob.canBashFences = true;
                    attackJob.locomotionUrgency = LocomotionUrgency.Sprint;
                    attackJob.checkOverrideOnExpire = false;
                    attackJob.expiryInterval = 999999; // Don't let it expire
                    
                    // Force start the job
                    attacker.jobs.StartJob(attackJob, JobCondition.InterruptForced, null, false, true);
                    
                    Log.Message($"SolWorld: {attackJob.def.defName} job started - {attacker.Name} -> {target.Name}");
                }
                else
                {
                    Log.Warning($"SolWorld: Could not create attack job for {attacker.Name} -> {target.Name}");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"SolWorld: Failed to force attack {attacker.Name} -> {target.Name}: {ex.Message}");
            }
        }
        
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
            
            // AGGRESSIVE ENFORCEMENT: Force combat every 3 seconds
            foreach (var redPawn in redAlive)
            {
                EnforceAggressiveCombat(redPawn, blueAlive, bounds);
            }
            
            foreach (var bluePawn in blueAlive)
            {
                EnforceAggressiveCombat(bluePawn, redAlive, bounds);
            }
            
            // Log combat status every 10 seconds
            if (currentTick % 600 == 0)
            {
                Log.Message($"SolWorld: Combat enforcement - Red: {redAlive.Count} alive, Blue: {blueAlive.Count} alive");
                
                // Debug: Show what pawns are doing
                foreach (var pawn in redAlive.Take(3))
                {
                    var jobName = pawn.CurJob?.def?.defName ?? "No Job";
                    var targetName = "No Target";
                    if (pawn.CurJob != null && pawn.CurJob.targetA.IsValid && pawn.CurJob.targetA.Thing != null)
                    {
                        targetName = pawn.CurJob.targetA.Thing.LabelShort;
                    }
                    Log.Message($"  Red {pawn.Name}: Job={jobName}, Target={targetName}");
                }
                
                foreach (var pawn in blueAlive.Take(3))
                {
                    var jobName = pawn.CurJob?.def?.defName ?? "No Job";
                    var targetName = "No Target";
                    if (pawn.CurJob != null && pawn.CurJob.targetA.IsValid && pawn.CurJob.targetA.Thing != null)
                    {
                        targetName = pawn.CurJob.targetA.Thing.LabelShort;
                    }
                    Log.Message($"  Blue {pawn.Name}: Job={jobName}, Target={targetName}");
                }
            }
        }
        
        private void EnforceAggressiveCombat(Pawn pawn, List<Pawn> enemies, CellRect? bounds)
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
            
            // Check if they're actually fighting
            var currentJob = pawn.CurJob;
            bool isAttacking = currentJob != null && 
                (currentJob.def == JobDefOf.AttackMelee || 
                 currentJob.def == JobDefOf.AttackStatic || 
                 currentJob.def == JobDefOf.Hunt);
            
            bool hasValidTarget = false;
            if (currentJob != null && currentJob.targetA.IsValid && currentJob.targetA.Thing is Pawn targetPawn)
            {
                hasValidTarget = enemies.Contains(targetPawn) && !targetPawn.Dead;
            }
            
            // Force new attack if not currently attacking or target is invalid
            if (!isAttacking || !hasValidTarget)
            {
                var nearestEnemy = enemies
                    .Where(e => !e.Dead && e.Spawned)
                    .OrderBy(e => pawn.Position.DistanceTo(e.Position))
                    .FirstOrDefault();
                
                if (nearestEnemy != null)
                {
                    // Only force new attack if distance is reasonable (within arena)
                    var distance = pawn.Position.DistanceTo(nearestEnemy.Position);
                    if (distance <= 50) // Reasonable arena size
                    {
                        ForceAttackTarget(pawn, nearestEnemy);
                    }
                    else
                    {
                        // If too far, force them to move toward enemy spawn
                        var enemyTeam = enemies == redTeamPawns ? TeamColor.Red : TeamColor.Blue;
                        var enemySpawner = enemyTeam == TeamColor.Red ? redSpawner : blueSpawner;
                        
                        if (enemySpawner != null)
                        {
                            ForceMoveToward(pawn, enemySpawner.Position);
                        }
                    }
                }
            }
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
                
                // Check if this death should end the round
                if (currentRoster.RedAlive == 0 || currentRoster.BlueAlive == 0)
                {
                    Log.Message($"SolWorld: Team elimination detected - Red: {currentRoster.RedAlive}, Blue: {currentRoster.BlueAlive}");
                }
            }
        }
        
        private void EndRound(string reason)
        {
            currentState = ArenaState.Ended;
            
            if (currentRoster == null) return;
            
            currentRoster.IsLive = false;
            currentRoster.Winner = currentRoster.DetermineWinner();
            
            Log.Message("SolWorld: Round ended - " + reason + ". Winner: " + currentRoster.Winner);
            
            // TODO: Report to backend and get real txids
            var mockTxids = new string[] { "MockTx1", "MockTx2" };
            Messages.Message("ROUND COMPLETE! Winner: " + currentRoster.Winner + " team", MessageTypeDefOf.PositiveEvent);
        }
        
        private void StartResetPhase()
        {
            currentState = ArenaState.Resetting;
            
            Log.Message("SolWorld: Starting reset phase");
            
            var bounds = GetArenaBounds();
            if (bounds.HasValue)
            {
                arenaReset.ResetArena(map, bounds.Value, arenaBlueprint);
            }
            
            CleanupCurrentRound();
        }
        
        private void CompleteReset()
        {
            currentState = ArenaState.Idle;
            currentRoster = null;
            combatStartTick = -1;
            combatInitiated = false;
            lastCombatEnforcementTick = -1;
            previewCompleted = false;
            uiShouldTriggerUnpause = false;
            
            ScheduleNextRound(); // Schedule next round in 5 minutes
            
            Log.Message("SolWorld: Arena reset complete, next round scheduled in 5 minutes");
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
                    Log.Message($"  Red: {pawn.Name} - Faction: {pawn.Faction?.Name}, Hostile to player: {pawn.HostileTo(Faction.OfPlayer)}, Job: {jobName}");
                }
            }
            
            foreach (var pawn in blueTeamPawns)
            {
                if (pawn != null)
                {
                    var jobName = pawn.CurJob?.def?.defName ?? "No Job";
                    Log.Message($"  Blue: {pawn.Name} - Faction: {pawn.Faction?.Name}, Hostile to player: {pawn.HostileTo(Faction.OfPlayer)}, Job: {jobName}");
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
        
        // MANUAL UNPAUSE METHOD - Can be called from UI (SAME context as manual button)
        public void ForceUnpause()
        {
            Log.Message("SolWorld: MANUAL FORCE UNPAUSE called!");
            
            try
            {
                // Use the exact same logic as always
                Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                
                if (Find.TickManager.Paused)
                {
                    Find.TickManager.TogglePaused();
                }
                
                Log.Message($"SolWorld: Manual unpause result - Paused: {Find.TickManager.Paused}, Speed: {Find.TickManager.CurTimeSpeed}");
                
                // If we're in preview mode and manual unpause, start combat immediately
                if (currentState == ArenaState.Preview && currentRoster != null)
                {
                    // Notify MapComponent that UI triggered the unpause (from same context as manual button)
                    OnUITriggeredUnpause();
                    Messages.Message("Manual unpause - starting combat immediately!", MessageTypeDefOf.PositiveEvent);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"SolWorld: Manual unpause failed: {ex.Message}");
            }
        }
        
        // Add a simpler combat test method
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
    }
}