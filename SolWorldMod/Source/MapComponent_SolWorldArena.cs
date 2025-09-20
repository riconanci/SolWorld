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
        
        // UPDATED TIMING: 30s preview + 90s combat = 2min rounds, 3min cadence
        private DateTime previewStartTime;
        private int combatStartTick;
        private const float PREVIEW_SECONDS = 30f; // Real-time during pause
        private const int COMBAT_TICKS = 90 * 60; // 1.5 minutes (90 seconds) game time
        private const int CADENCE_TICKS = 180 * 60; // 3 minutes between rounds
        
        // Team factions
        private Faction redTeamFaction;
        private Faction blueTeamFaction;
        
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
            Scribe_Deep.Look(ref currentRoster, "currentRoster");
            Scribe_References.Look(ref redTeamFaction, "redTeamFaction");
            Scribe_References.Look(ref blueTeamFaction, "blueTeamFaction");
        }
        
        public override void MapComponentTick()
        {
            base.MapComponentTick();
            
            if (!isActive || arenaCore?.IsOperational != true)
                return;
                
            var currentTick = Find.TickManager.TicksGame;
            
            // FIXED: Check if it's time for the next scheduled round
            if (currentState == ArenaState.Idle && nextRoundTick > 0 && currentTick >= nextRoundTick)
            {
                Log.Message("SolWorld: TIME TO START NEW ROUND! Current: " + currentTick + ", Next: " + nextRoundTick);
                StartNewRound();
                return;
            }
            
            // Handle phase transitions
            HandlePhaseTransitions();
            
            // CRITICAL: Force continuous combat during combat phase
            if (currentState == ArenaState.Combat && currentTick % 15 == 0) // Every 0.25 seconds
            {
                EnforceContinuousCombat();
            }
        }
        
        private void HandlePhaseTransitions()
        {
            switch (currentState)
            {
                case ArenaState.Preview:
                    // Use REAL TIME for preview (works during pause)
                    var previewElapsed = (float)(DateTime.Now - previewStartTime).TotalSeconds;
                    if (previewElapsed >= PREVIEW_SECONDS)
                    {
                        Log.Message("SolWorld: 30 seconds elapsed - FORCING UNPAUSE AND COMBAT!");
                        TransitionToCombat();
                    }
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
            
            // FIXED: Start first round immediately instead of waiting
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
            Log.Message("SolWorld: STARTING NEW ROUND - SPAWN + PAUSE SIMULTANEOUSLY");
            
            currentState = ArenaState.Preview;
            previewStartTime = DateTime.Now; // Real-time tracking for paused preview
            
            try
            {
                // Step 1: Create roster and factions
                CreateRoster();
                CreateArenaFactions();
                
                // Step 2: Initialize blueprint BEFORE spawning
                var bounds = GetArenaBounds();
                if (bounds.HasValue)
                {
                    arenaBlueprint.InitializeBlueprint(map, bounds.Value);
                }
                
                // Step 3: Spawn teams (this happens instantly)
                SpawnBothTeams();
                
                // Step 4: IMMEDIATELY pause after spawning
                if (!Find.TickManager.Paused)
                {
                    Find.TickManager.Pause();
                    Log.Message("SolWorld: Game paused for 30-second preview");
                }
                
                var payoutText = currentRoster.PerWinnerPayout.ToString("F3");
                Messages.Message("30-SECOND PREVIEW: Round " + currentRoster.MatchId + " - " + payoutText + " SOL per winner", MessageTypeDefOf.PositiveEvent);
                
                Log.Message("SolWorld: Round started successfully - 20 fighters spawned and game paused");
            }
            catch (Exception ex)
            {
                Log.Error("SolWorld: Error starting round: " + ex.Message);
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
        
        private void CreateArenaFactions()
        {
            Log.Message("SolWorld: Creating arena factions with proper hostility");
            
            // RED TEAM FACTION (Hostile to player - shows as RED)
            var pirateDef = DefDatabase<FactionDef>.GetNamed("Pirate");
            var redParms = new FactionGeneratorParms(pirateDef);
            redTeamFaction = FactionGenerator.NewGeneratedFaction(redParms);
            redTeamFaction.Name = "Red Arena Team";
            redTeamFaction.colorFromSpectrum = 0.0f; // Red color
            
            // Force hostile to player
            redTeamFaction.SetRelationDirect(Faction.OfPlayer, FactionRelationKind.Hostile, false);
            var redRelation = redTeamFaction.RelationWith(Faction.OfPlayer, false);
            if (redRelation != null)
            {
                redRelation.baseGoodwill = -100;
                redRelation.kind = FactionRelationKind.Hostile;
            }
            
            Find.FactionManager.Add(redTeamFaction);
            
            // BLUE TEAM FACTION (Allied to player - shows as BLUE)
            var civilDef = DefDatabase<FactionDef>.GetNamed("OutlanderCivil");
            var blueParms = new FactionGeneratorParms(civilDef);
            blueTeamFaction = FactionGenerator.NewGeneratedFaction(blueParms);
            blueTeamFaction.Name = "Blue Arena Team";
            blueTeamFaction.colorFromSpectrum = 0.6f; // Blue color
            
            // Force allied to player
            blueTeamFaction.SetRelationDirect(Faction.OfPlayer, FactionRelationKind.Ally, false);
            var blueRelation = blueTeamFaction.RelationWith(Faction.OfPlayer, false);
            if (blueRelation != null)
            {
                blueRelation.baseGoodwill = 75;
                blueRelation.kind = FactionRelationKind.Ally;
            }
            
            Find.FactionManager.Add(blueTeamFaction);
            
            // Make teams hostile to each other
            redTeamFaction.SetRelationDirect(blueTeamFaction, FactionRelationKind.Hostile, false);
            blueTeamFaction.SetRelationDirect(redTeamFaction, FactionRelationKind.Hostile, false);
            
            var redToBlue = redTeamFaction.RelationWith(blueTeamFaction, false);
            if (redToBlue != null)
            {
                redToBlue.baseGoodwill = -100;
                redToBlue.kind = FactionRelationKind.Hostile;
            }
            
            var blueToRed = blueTeamFaction.RelationWith(redTeamFaction, false);
            if (blueToRed != null)
            {
                blueToRed.baseGoodwill = -100;
                blueToRed.kind = FactionRelationKind.Hostile;
            }
            
            Log.Message("SolWorld: Factions created - Red hostile: " + redTeamFaction.HostileTo(Faction.OfPlayer) + ", Blue hostile: " + blueTeamFaction.HostileTo(Faction.OfPlayer));
        }
        
        private void SpawnBothTeams()
        {
            Log.Message("SolWorld: Spawning both teams instantly");
            
            // Spawn red team (hostile)
            SpawnTeam(currentRoster.Red, redSpawner.Position, redTeamFaction, TeamColor.Red);
            
            // Spawn blue team (allied)
            SpawnTeam(currentRoster.Blue, blueSpawner.Position, blueTeamFaction, TeamColor.Blue);
            
            Log.Message("SolWorld: Both teams spawned successfully - 20 fighters on map");
        }
        
        private void SpawnTeam(List<Fighter> fighters, IntVec3 spawnerPos, Faction teamFaction, TeamColor teamColor)
        {
            for (int i = 0; i < fighters.Count; i++)
            {
                var fighter = fighters[i];
                
                var spawnPos = CellFinder.RandomClosewalkCellNear(spawnerPos, map, 5);
                if (!spawnPos.IsValid)
                    spawnPos = spawnerPos;
                
                var pawn = GenerateWarrior(fighter, teamFaction);
                
                if (pawn != null)
                {
                    GenSpawn.Spawn(pawn, spawnPos, map);
                    fighter.PawnRef = pawn;
                    
                    Log.Message("SolWorld: Spawned " + fighter.WalletShort + " (" + teamColor + ") with faction " + teamFaction.Name + " (hostile to player: " + pawn.HostileTo(Faction.OfPlayer) + ")");
                }
            }
        }
        
        private Pawn GenerateWarrior(Fighter fighter, Faction teamFaction)
        {
            try
            {
                var pawnKind = PawnKindDefOf.Villager ?? PawnKindDefOf.Colonist;
                var pawn = PawnGenerator.GeneratePawn(pawnKind, teamFaction);
                
                pawn.Name = new NameSingle(fighter.WalletShort);
                
                GiveWeapon(pawn);
                pawn.SetFaction(teamFaction);
                MakeWarrior(pawn);
                
                return pawn;
            }
            catch (Exception ex)
            {
                Log.Error("SolWorld: Failed to generate warrior: " + ex.Message);
                return null;
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
                        if (pawn.equipment?.Primary != null)
                        {
                            pawn.equipment.Remove(pawn.equipment.Primary);
                        }
                        
                        var weapon = ThingMaker.MakeThing(weaponDef);
                        pawn.equipment.AddEquipment((ThingWithComps)weapon);
                        return;
                    }
                    catch { }
                }
            }
        }
        
        private void MakeWarrior(Pawn pawn)
        {
            // CRITICAL: Set up for extreme aggression
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
            
            Log.Message("SolWorld: TRANSITION TO COMBAT - UNPAUSING AND FORCING FIGHT!");
            
            // CRITICAL: Aggressive unpause with multiple attempts
            int attempts = 0;
            while (Find.TickManager.Paused && attempts < 10)
            {
                attempts++;
                Find.TickManager.TogglePaused();
                Log.Message("SolWorld: Unpause attempt " + attempts + " - Still paused: " + Find.TickManager.Paused);
            }
            
            // Force normal speed
            Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            
            currentRoster.IsLive = true;
            
            // CRITICAL: Force all pawns into immediate combat mode
            InitiateInstantCombat();
            
            Messages.Message("COMBAT STARTED! 90 seconds to fight!", MessageTypeDefOf.PositiveEvent);
        }
        
        private void InitiateInstantCombat()
        {
            Log.Message("SolWorld: INITIATING INSTANT COMBAT - ALL PAWNS TO BATTLE STATIONS!");
            
            var allFighters = currentRoster.Red.Concat(currentRoster.Blue);
            
            foreach (var fighter in allFighters)
            {
                if (fighter.PawnRef?.Spawned == true && !fighter.PawnRef.Dead)
                {
                    SetupFighterForCombat(fighter.PawnRef, fighter);
                }
            }
        }
        
        private void SetupFighterForCombat(Pawn pawn, Fighter fighter)
        {
            Log.Message("SolWorld: Setting up " + fighter.WalletShort + " for EXTREME combat");
            
            // Force draft
            if (pawn.drafter != null)
            {
                pawn.drafter.Drafted = true;
            }
            
            // Ultra-aggressive mindstate
            if (pawn.mindState != null)
            {
                pawn.mindState.canFleeIndividual = false;
                pawn.mindState.duty = new PawnDuty(DutyDefOf.AssaultColony);
                pawn.mindState.enemyTarget = null; // Clear to allow new targeting
            }
            
            // Max mood and needs
            if (pawn.needs?.mood != null)
            {
                pawn.needs.mood.CurLevel = 1.0f;
            }
            
            // Clear all current jobs
            if (pawn.jobs != null)
            {
                try
                {
                    pawn.jobs.ClearQueuedJobs();
                    pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                }
                catch { }
            }
            
            // Clear mental states that might interfere
            if (pawn.mindState?.mentalStateHandler?.CurState != null)
            {
                try
                {
                    pawn.mindState.mentalStateHandler.Reset();
                }
                catch { }
            }
            
            // Give immediate attack order
            GiveAttackOrder(pawn, fighter);
        }
        
        private void EnforceContinuousCombat()
        {
            if (currentRoster == null) return;
            
            var allFighters = currentRoster.Red.Concat(currentRoster.Blue);
            var bounds = GetArenaBounds();
            
            foreach (var fighter in allFighters)
            {
                if (fighter.PawnRef?.Spawned == true && !fighter.PawnRef.Dead && fighter.Alive)
                {
                    var pawn = fighter.PawnRef;
                    
                    // Maintain combat state every check
                    if (pawn.drafter != null)
                        pawn.drafter.Drafted = true;
                    
                    if (pawn.mindState != null)
                    {
                        pawn.mindState.canFleeIndividual = false;
                        if (pawn.mindState.duty?.def != DutyDefOf.AssaultColony)
                            pawn.mindState.duty = new PawnDuty(DutyDefOf.AssaultColony);
                    }
                    
                    if (pawn.needs?.mood != null && pawn.needs.mood.CurLevel < 0.5f)
                        pawn.needs.mood.CurLevel = 1.0f;
                    
                    // Keep in arena bounds
                    if (bounds.HasValue && !bounds.Value.Contains(pawn.Position))
                    {
                        ForceBackToArena(pawn, bounds.Value);
                    }
                    
                    // Force new attacks if idle or not fighting
                    if (IsIdle(pawn))
                    {
                        GiveAttackOrder(pawn, fighter);
                    }
                }
            }
        }
        
        private bool IsIdle(Pawn pawn)
        {
            if (pawn.CurJob == null) return true;
            
            var jobDef = pawn.CurJob.def;
            
            // Only consider actual attack jobs as "not idle"
            if (jobDef == JobDefOf.AttackMelee || jobDef == JobDefOf.AttackStatic)
                return false;
            
            // Everything else is considered idle and needs new orders
            return true;
        }
        
        private void GiveAttackOrder(Pawn pawn, Fighter fighter)
        {
            if (pawn?.Spawned != true || currentRoster == null)
                return;
            
            // Find enemy team
            var enemyTeam = fighter.Team == TeamColor.Red ? currentRoster.Blue : currentRoster.Red;
            
            // Find closest alive enemy
            var target = FindClosestAliveEnemy(pawn, enemyTeam);
            
            if (target != null)
            {
                try
                {
                    // Force end current job
                    pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                    
                    Job attackJob;
                    bool hasRangedWeapon = HasRangedWeapon(pawn);
                    float distance = pawn.Position.DistanceTo(target.Position);
                    
                    if (hasRangedWeapon && distance > 3f)
                    {
                        // Ranged attack
                        attackJob = JobMaker.MakeJob(JobDefOf.AttackStatic, target);
                    }
                    else
                    {
                        // Melee attack
                        attackJob = JobMaker.MakeJob(JobDefOf.AttackMelee, target);
                    }
                    
                    // Force start the attack
                    pawn.jobs.StartJob(attackJob, JobCondition.InterruptForced);
                    
                    // Reduced logging to avoid spam
                    if (Find.TickManager.TicksGame % 60 == 0)
                    {
                        Log.Message("SolWorld: " + fighter.WalletShort + " attacking " + target.Name);
                    }
                }
                catch (Exception ex)
                {
                    if (Find.TickManager.TicksGame % 300 == 0) // Log errors every 5 seconds
                    {
                        Log.Warning("SolWorld: Failed to give attack order - " + ex.Message);
                    }
                    // Fallback: move toward enemy
                    try
                    {
                        pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                        var moveJob = JobMaker.MakeJob(JobDefOf.Goto, target.Position);
                        pawn.jobs.StartJob(moveJob, JobCondition.InterruptForced);
                    }
                    catch { }
                }
            }
            else
            {
                // No target - move toward enemy spawn
                var enemySpawner = fighter.Team == TeamColor.Red ? blueSpawner : redSpawner;
                if (enemySpawner != null)
                {
                    var targetPos = CellFinder.RandomClosewalkCellNear(enemySpawner.Position, map, 8);
                    if (targetPos.IsValid)
                    {
                        try
                        {
                            pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                            var moveJob = JobMaker.MakeJob(JobDefOf.Goto, targetPos);
                            pawn.jobs.StartJob(moveJob, JobCondition.InterruptForced);
                            
                            if (Find.TickManager.TicksGame % 180 == 0) // Log every 3 seconds
                            {
                                Log.Message("SolWorld: " + fighter.WalletShort + " moving toward enemy spawn");
                            }
                        }
                        catch { }
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
                    pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                    var returnJob = JobMaker.MakeJob(JobDefOf.Goto, targetPos);
                    pawn.jobs.StartJob(returnJob, JobCondition.InterruptForced);
                }
                catch { }
            }
        }
        
        private Pawn FindClosestAliveEnemy(Pawn attacker, List<Fighter> enemyTeam)
        {
            if (attacker?.Spawned != true || enemyTeam == null)
                return null;
            
            Pawn closestEnemy = null;
            float closestDistance = float.MaxValue;
            
            foreach (var enemy in enemyTeam)
            {
                if (enemy.PawnRef?.Spawned == true && enemy.Alive && !enemy.PawnRef.Dead)
                {
                    var distance = attacker.Position.DistanceTo(enemy.PawnRef.Position);
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        closestEnemy = enemy.PawnRef;
                    }
                }
            }
            
            return closestEnemy;
        }
        
        private bool HasRangedWeapon(Pawn pawn)
        {
            if (pawn?.equipment?.Primary == null)
                return false;
            
            var weapon = pawn.equipment.Primary;
            
            if (weapon.def.Verbs != null)
            {
                foreach (var verb in weapon.def.Verbs)
                {
                    if (verb.range > 1.5f)
                        return true;
                }
            }
            
            return weapon.def.weaponTags?.Any(tag =>
                tag.Contains("Gun") ||
                tag.Contains("Ranged") ||
                tag.Contains("Rifle") ||
                tag.Contains("Pistol")) == true;
        }
        
        private void EndRound(string reason)
        {
            currentState = ArenaState.Ended;
            
            if (currentRoster == null) return;
            
            currentRoster.IsLive = false;
            currentRoster.Winner = currentRoster.DetermineWinner();
            
            Log.Message("SolWorld: Round ended - " + reason + ". Winner: " + currentRoster.Winner);
            
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
            
            ScheduleNextRound(); // Schedule next round in 3 minutes
            
            Log.Message("SolWorld: Arena reset complete, next round scheduled in 3 minutes");
        }
        
        private void CleanupCurrentRound()
        {
            if (currentRoster == null) return;
            
            var allFighters = currentRoster.Red.Concat(currentRoster.Blue);
            
            foreach (var fighter in allFighters)
            {
                if (fighter.PawnRef?.Spawned == true)
                {
                    try
                    {
                        fighter.PawnRef.DeSpawn();
                    }
                    catch { }
                }
            }
            
            if (redTeamFaction != null)
            {
                redTeamFaction.hidden = true;
                redTeamFaction = null;
            }
            
            if (blueTeamFaction != null)
            {
                blueTeamFaction.hidden = true;
                blueTeamFaction = null;
            }
        }
        
        // UI Methods - Complete from original
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
        
        public void TestSpawnFighters(RoundRoster testRoster)
        {
            if (testRoster == null || redSpawner == null || blueSpawner == null)
            {
                Log.Error("SolWorld: Cannot test spawn - missing components");
                return;
            }
            
            Log.Message("SolWorld: Test spawning fighters for immediate combat...");
            
            currentRoster = testRoster;
            
            // Spawn with proper factions
            CreateArenaFactions();
            SpawnBothTeams();
            
            // Force immediate combat
            InitiateInstantCombat();
            
            // Set combat state
            currentState = ArenaState.Combat;
            combatStartTick = Find.TickManager.TicksGame;
            
            Log.Message("SolWorld: Test fighters spawned and combat initiated!");
        }
    }
}