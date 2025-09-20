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
        
        // CORRECT TIMING: Preview uses REAL TIME (works during pause), Combat uses GAME TIME
        private DateTime previewStartTime;
        private int combatStartTick;
        private const float PREVIEW_SECONDS = 30f; // Real-time during pause - ALWAYS WORKS
        private const int COMBAT_TICKS = 240 * 60; // 4 minutes (240 seconds) game time
        private const int CADENCE_TICKS = 300 * 60; // 5 minutes between rounds
        
        // FIXED: Use existing hostile factions instead of creating custom ones
        private Faction redTeamFaction; // Will be hostile to player
        private Faction blueTeamFaction; // Will be friendly to player
        private List<Pawn> redTeamPawns = new List<Pawn>();
        private List<Pawn> blueTeamPawns = new List<Pawn>();
        private Dictionary<Pawn, TeamColor> pawnTeamMap = new Dictionary<Pawn, TeamColor>();
        
        // FIXED: Direct unpause system
        private bool needsUnpause = false;
        private int unpauseAttempts = 0;
        private const int MAX_UNPAUSE_ATTEMPTS = 30; // Try for 30 ticks
        
        // Combat enforcement tracking
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
            Scribe_Values.Look(ref needsUnpause, "needsUnpause", false);
            Scribe_Values.Look(ref unpauseAttempts, "unpauseAttempts", 0);
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
            
            // CRITICAL: Handle unpause attempts FIRST every tick
            if (needsUnpause && unpauseAttempts < MAX_UNPAUSE_ATTEMPTS)
            {
                HandleUnpauseAttempt();
                return; // Focus only on unpausing
            }
            
            // Check if it's time for the next scheduled round
            if (currentState == ArenaState.Idle && nextRoundTick > 0 && currentTick >= nextRoundTick)
            {
                Log.Message("SolWorld: TIME TO START NEW ROUND! Current: " + currentTick + ", Next: " + nextRoundTick);
                StartNewRound();
                return;
            }
            
            // Handle phase transitions - CRITICAL: Different timing systems
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
        
        private void HandleUnpauseAttempt()
        {
            unpauseAttempts++;
            
            Log.Message($"SolWorld: Unpause attempt {unpauseAttempts}/{MAX_UNPAUSE_ATTEMPTS} - Current state: Paused={Find.TickManager.Paused}, Speed={Find.TickManager.CurTimeSpeed}");
            
            // DIRECT: Set time speed to Normal (this should force unpause)
            if (Find.TickManager.CurTimeSpeed != TimeSpeed.Normal)
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                Log.Message($"SolWorld: Forced TimeSpeed to Normal - Result: Paused={Find.TickManager.Paused}, Speed={Find.TickManager.CurTimeSpeed}");
            }
            
            // If still paused after setting speed, use toggle
            if (Find.TickManager.Paused)
            {
                Find.TickManager.TogglePaused();
                Log.Message($"SolWorld: Used TogglePaused - Result: Paused={Find.TickManager.Paused}, Speed={Find.TickManager.CurTimeSpeed}");
            }
            
            // Success check
            if (!Find.TickManager.Paused && Find.TickManager.CurTimeSpeed == TimeSpeed.Normal)
            {
                needsUnpause = false;
                unpauseAttempts = 0;
                Log.Message("SolWorld: ===== UNPAUSE SUCCESS! =====");
                
                // NOW start combat
                currentRoster.IsLive = true;
                InitiateNaturalCombat();
                Messages.Message("COMBAT STARTED! 4 minutes to fight!", MessageTypeDefOf.PositiveEvent);
            }
            else if (unpauseAttempts >= MAX_UNPAUSE_ATTEMPTS)
            {
                Log.Error("SolWorld: ===== UNPAUSE FAILED AFTER 30 ATTEMPTS! =====");
                needsUnpause = false;
                unpauseAttempts = 0;
                
                // Force combat anyway
                currentRoster.IsLive = true;
                InitiateNaturalCombat();
                Messages.Message("COMBAT STARTED (paused)! Manual unpause required!", MessageTypeDefOf.RejectInput);
            }
        }
        
        private void HandlePhaseTransitions()
        {
            switch (currentState)
            {
                case ArenaState.Preview:
                    // CRITICAL: Use REAL TIME for preview (works during pause)
                    var previewElapsed = (float)(DateTime.Now - previewStartTime).TotalSeconds;
                    
                    // Log every 5 seconds during preview
                    if ((int)previewElapsed % 5 == 0 && (int)previewElapsed != 0)
                    {
                        var timeLeft = PREVIEW_SECONDS - previewElapsed;
                        Log.Message($"SolWorld: Preview time remaining: {timeLeft:F0} seconds");
                    }
                    
                    if (previewElapsed >= PREVIEW_SECONDS)
                    {
                        Log.Message("SolWorld: 30 seconds elapsed - TRIGGERING UNPAUSE SYSTEM!");
                        TransitionToCombat();
                    }
                    break;
                    
                case ArenaState.Combat:
                    // Use GAME TIME for combat
                    var combatElapsed = Find.TickManager.TicksGame - combatStartTick;
                    if (combatElapsed >= COMBAT_TICKS)
                    {
                        EndRound("Time limit reached (4 minutes)");
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
            
            // Start first round immediately instead of waiting
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
            needsUnpause = false;
            unpauseAttempts = 0;
            
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
            combatInitiated = false;
            lastCombatEnforcementTick = -1;
            needsUnpause = false;
            unpauseAttempts = 0;
            
            try
            {
                // Step 1: Create roster
                Log.Message("SolWorld: Creating roster...");
                CreateRoster();
                
                // Step 2: Get or create proper factions
                Log.Message("SolWorld: Setting up arena factions...");
                SetupArenaFactions();
                
                // Step 3: Initialize blueprint BEFORE spawning
                var bounds = GetArenaBounds();
                if (bounds.HasValue)
                {
                    Log.Message("SolWorld: Initializing blueprint...");
                    arenaBlueprint.InitializeBlueprint(map, bounds.Value);
                }
                
                // Step 4: Spawn teams with proper factions
                Log.Message("SolWorld: ===== SPAWNING TEAMS =====");
                SpawnBothTeams();
                
                // Step 5: IMMEDIATELY pause after spawning
                Log.Message("SolWorld: ===== PAUSING GAME =====");
                if (!Find.TickManager.Paused)
                {
                    Find.TickManager.Pause();
                    Log.Message("SolWorld: Game paused for 30-second preview");
                }
                
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
            
            // FIXED: Use existing factions to avoid relation errors
            
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
                
                // CRITICAL: Make blue team hostile to red team so they fight each other
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
                
                var pawn = PawnGenerator.GeneratePawn(pawnKind, teamFaction);
                
                pawn.Name = new NameSingle(fighter.WalletShort);
                
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
        
        private void ApplyTeamStyling(Pawn pawn, TeamColor teamColor)
        {
            try
            {
                // Set hair color for team identification using RimWorld 1.6 API
                if (pawn.story != null && pawn.story.HairColor != null)
                {
                    if (teamColor == TeamColor.Red)
                        pawn.story.HairColor = Color.red;
                    else
                        pawn.story.HairColor = Color.blue;
                }
                
                // Force graphics refresh using RimWorld 1.6 API
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
        
        private void TransitionToCombat()
        {
            currentState = ArenaState.Combat;
            combatStartTick = Find.TickManager.TicksGame; // Start game-time tracking
            combatInitiated = false; // Will be set to true after unpause
            
            Log.Message("SolWorld: ===== TRANSITION TO COMBAT =====");
            Log.Message("SolWorld: ===== ACTIVATING UNPAUSE SYSTEM =====");
            
            // FIXED: Use persistent unpause system
            needsUnpause = true;
            unpauseAttempts = 0;
            
            // Don't start combat here - wait for successful unpause
            Log.Message("SolWorld: Unpause system activated - will attempt unpause every tick");
        }
        
        private void InitiateNaturalCombat()
        {
            Log.Message("SolWorld: ===== INITIATING NATURAL COMBAT =====");
            
            combatInitiated = true;
            lastCombatEnforcementTick = Find.TickManager.TicksGame + 180; // Start enforcement in 3 seconds
            
            // Since pawns are in hostile factions, they should naturally engage
            // Just set them up for optimal combat without forcing specific targets
            foreach (var pawn in redTeamPawns.Concat(blueTeamPawns))
            {
                if (pawn?.Spawned == true && !pawn.Dead)
                {
                    SetupPawnForNaturalCombat(pawn);
                }
            }
            
            Log.Message("SolWorld: ===== NATURAL COMBAT INITIATION COMPLETE =====");
        }
        
        private void SetupPawnForNaturalCombat(Pawn pawn)
        {
            var team = pawnTeamMap.TryGetValue(pawn, out var teamColor) ? teamColor : TeamColor.Red;
            
            Log.Message($"SolWorld: Setting up {pawn.Name} ({team}) for natural combat");
            
            // Basic combat readiness - let faction hostility handle targeting
            if (pawn.mindState != null)
            {
                pawn.mindState.canFleeIndividual = false;
                pawn.mindState.duty = new PawnDuty(DutyDefOf.AssaultColony);
            }
            
            // Ensure good combat state
            if (pawn.needs?.mood != null)
            {
                pawn.needs.mood.CurLevel = 1.0f;
            }
            
            // Don't force specific jobs - let natural faction hostility work
        }
        
        private void EnforceContinuousCombat()
        {
            if (currentRoster == null || !combatInitiated)
                return;
            
            var currentTick = Find.TickManager.TicksGame;
            var bounds = GetArenaBounds();
            
            // Light enforcement - mainly just keep pawns in bounds and maintain combat readiness
            var allAlivePawns = redTeamPawns.Concat(blueTeamPawns)
                .Where(p => p?.Spawned == true && !p.Dead)
                .ToList();
            
            if (allAlivePawns.Count == 0)
            {
                Log.Warning("SolWorld: No alive pawns found in combat!");
                return;
            }
            
            foreach (var pawn in allAlivePawns)
            {
                if (!pawnTeamMap.ContainsKey(pawn))
                    continue;
                    
                var team = pawnTeamMap[pawn];
                
                // Maintain basic combat state
                if (pawn.mindState != null)
                {
                    pawn.mindState.canFleeIndividual = false;
                    if (pawn.mindState.duty?.def != DutyDefOf.AssaultColony)
                    {
                        pawn.mindState.duty = new PawnDuty(DutyDefOf.AssaultColony);
                    }
                }
                
                // Keep mood high
                if (pawn.needs?.mood != null && pawn.needs.mood.CurLevel < 0.5f)
                {
                    pawn.needs.mood.CurLevel = 1.0f;
                }
                
                // Keep in arena bounds
                if (bounds.HasValue && !bounds.Value.Contains(pawn.Position))
                {
                    ForceBackToArena(pawn, bounds.Value);
                }
            }
            
            // Log combat status every 10 seconds
            if (currentTick % 600 == 0)
            {
                var redAlive = redTeamPawns.Count(p => p?.Spawned == true && !p.Dead);
                var blueAlive = blueTeamPawns.Count(p => p?.Spawned == true && !p.Dead);
                Log.Message($"SolWorld: Combat status - Red: {redAlive} alive, Blue: {blueAlive} alive");
                
                // Check faction hostilities
                if (redTeamPawns.Count > 0 && blueTeamPawns.Count > 0)
                {
                    var redPawn = redTeamPawns.FirstOrDefault(p => p?.Spawned == true);
                    var bluePawn = blueTeamPawns.FirstOrDefault(p => p?.Spawned == true);
                    if (redPawn != null && bluePawn != null)
                    {
                        Log.Message($"SolWorld: Faction hostility check - Red hostile to Blue: {redPawn.HostileTo(bluePawn)}");
                    }
                }
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
            needsUnpause = false;
            unpauseAttempts = 0;
            
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
        
        // UI SUPPORT METHODS
        public float GetTimeLeftInCurrentPhase()
        {
            switch (currentState)
            {
                case ArenaState.Preview:
                    var previewElapsed = (float)(DateTime.Now - previewStartTime).TotalSeconds;
                    return Math.Max(0, PREVIEW_SECONDS - previewElapsed);
                    
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
            InitiateNaturalCombat();
            
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
        
        // MANUAL UNPAUSE METHOD - Can be called from UI
        public void ForceUnpause()
        {
            Log.Message("SolWorld: MANUAL FORCE UNPAUSE called!");
            
            Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            
            if (Find.TickManager.Paused)
            {
                Find.TickManager.TogglePaused();
            }
            
            Log.Message($"SolWorld: Manual unpause result - Paused: {Find.TickManager.Paused}, Speed: {Find.TickManager.CurTimeSpeed}");
        }
    }
}