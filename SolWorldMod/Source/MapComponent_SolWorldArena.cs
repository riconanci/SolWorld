// solworld/SolWorldMod/Source/MapComponent_SolWorldArena.cs
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
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
        
        // REAL-TIME COUNTDOWN SYSTEM (works during pause)
        private DateTime phaseStartTime;
        private float previewDurationSeconds = 30f;
        private float combatDurationSeconds = 240f;
        
        // Team factions
        private Faction redTeamFaction;
        private Faction blueTeamFaction;
        
        public ArenaState CurrentState 
        { 
            get { return currentState; } 
            private set { currentState = value; } 
        }
        
        public bool IsActive 
        { 
            get { return isActive; } 
            private set { isActive = value; } 
        }
        
        public RoundRoster CurrentRoster 
        { 
            get { return currentRoster; } 
            private set { currentRoster = value; } 
        }
        
        // Components
        private ArenaBounds arenaBounds;
        private ArenaBlueprint arenaBlueprint;
        private ArenaReset arenaReset;
        
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
            
            // Check if it's time for the next scheduled round
            if (currentState == ArenaState.Idle && nextRoundTick > 0 && currentTick >= nextRoundTick)
            {
                StartPreviewPhase();
            }
            
            // Handle phase transitions using REAL-TIME clock (works during pause)
            HandlePhaseTransitions();
            
            // Force continuous combat during combat phase
            if (currentState == ArenaState.Combat && currentTick % 15 == 0) // Every 0.25 seconds
            {
                EnforceContinuousCombat();
            }
        }
        
        private void HandlePhaseTransitions()
        {
            if (currentState == ArenaState.Idle) return;
            
            var elapsed = (float)(DateTime.Now - phaseStartTime).TotalSeconds;
            
            switch (currentState)
            {
                case ArenaState.Preview:
                    if (elapsed >= previewDurationSeconds)
                    {
                        Log.Message("SolWorld: 30 seconds elapsed - AUTO RESUMING GAME!");
                        TransitionToCombat();
                    }
                    break;
                    
                case ArenaState.Combat:
                    if (elapsed >= combatDurationSeconds)
                    {
                        EndRound("Time limit reached");
                    }
                    else if (currentRoster != null && (currentRoster.RedAlive == 0 || currentRoster.BlueAlive == 0))
                    {
                        EndRound("Team eliminated");
                    }
                    break;
                    
                case ArenaState.Ended:
                    if (elapsed >= 3f)
                    {
                        StartResetPhase();
                    }
                    break;
                    
                case ArenaState.Resetting:
                    if (elapsed >= 2f)
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
            ScheduleNextRound();
            Messages.Message("Arena activated. Next round starts soon...", MessageTypeDefOf.PositiveEvent);
            Log.Message("SolWorld: Arena successfully started");
        }
        
        public void StopArena()
        {
            isActive = false;
            currentState = ArenaState.Idle;
            nextRoundTick = -1;
            
            if (currentRoster != null)
            {
                CleanupCurrentRound();
            }
            
            Messages.Message("Arena deactivated", MessageTypeDefOf.NeutralEvent);
        }
        
        public void ForceNextRound()
        {
            if (!HasValidSetup) return;
            
            if (currentState != ArenaState.Idle)
            {
                EndRound("Force triggered");
            }
            
            nextRoundTick = Find.TickManager.TicksGame + 60;
        }
        
        private void ScheduleNextRound()
        {
            var currentTime = Find.TickManager.TicksGame;
            var cadenceTicks = SolWorldSettings.CADENCE_SECONDS * 60;
            var previewTicks = SolWorldSettings.PREVIEW_SECONDS * 60;
            
            var nextCadenceTick = ((currentTime / cadenceTicks) + 1) * cadenceTicks;
            nextRoundTick = nextCadenceTick - previewTicks;
            
            var timeUntilRound = (nextRoundTick - currentTime) / 60f;
            Log.Message("SolWorld: Next round in " + timeUntilRound.ToString("F0") + " seconds");
        }
        
        private void StartPreviewPhase()
        {
            currentState = ArenaState.Preview;
            phaseStartTime = DateTime.Now;
            
            Log.Message("SolWorld: Starting 30-second preview phase");
            
            try
            {
                CreateRoster();
                SpawnTeamsWithProperHostility();
                
                var bounds = GetArenaBounds();
                if (bounds.HasValue)
                {
                    arenaBlueprint.InitializeBlueprint(map, bounds.Value);
                }
                
                Find.TickManager.Pause();
                
                var payoutText = currentRoster.PerWinnerPayout.ToString("F3");
                Messages.Message("30-SECOND PREVIEW: Round " + currentRoster.MatchId + " - " + payoutText + " SOL per winner", MessageTypeDefOf.PositiveEvent);
            }
            catch (Exception ex)
            {
                Log.Error("SolWorld: Error in preview phase: " + ex.Message);
                EndRound("Preview error");
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
        
        private void TransitionToCombat()
        {
            currentState = ArenaState.Combat;
            phaseStartTime = DateTime.Now;
            
            Log.Message("SolWorld: FORCING GAME TO RESUME - COMBAT STARTING!");
            
            // FORCE UNPAUSE
            if (Find.TickManager.Paused)
            {
                Find.TickManager.TogglePaused();
            }
            Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            
            currentRoster.IsLive = true;
            
            // IMMEDIATELY start aggressive combat
            InitiateAggressiveCombat();
            
            Messages.Message("COMBAT STARTED! 4 minutes of fighting!", MessageTypeDefOf.PositiveEvent);
        }
        
        private void InitiateAggressiveCombat()
        {
            if (currentRoster == null) return;
            
            Log.Message("SolWorld: INITIATING AGGRESSIVE COMBAT!");
            
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
            Log.Message("SolWorld: Setting up " + fighter.WalletShort + " for combat");
            
            // Force draft
            if (pawn.drafter != null)
            {
                pawn.drafter.Drafted = true;
            }
            
            // Disable fleeing
            if (pawn.mindState != null)
            {
                pawn.mindState.canFleeIndividual = false;
            }
            
            // Max mood
            if (pawn.needs?.mood != null)
            {
                pawn.needs.mood.CurLevel = 1.0f;
            }
            
            // Clear jobs
            if (pawn.jobs != null)
            {
                try
                {
                    pawn.jobs.ClearQueuedJobs();
                }
                catch { }
            }
            
            // Clear mental states
            if (pawn.mindState?.mentalStateHandler?.CurState != null)
            {
                try
                {
                    pawn.mindState.mentalStateHandler.Reset();
                }
                catch { }
            }
            
            // Give attack order
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
                    
                    // Maintain combat state
                    if (pawn.drafter != null)
                        pawn.drafter.Drafted = true;
                    
                    if (pawn.mindState != null)
                        pawn.mindState.canFleeIndividual = false;
                    
                    if (pawn.needs?.mood != null)
                        pawn.needs.mood.CurLevel = 1.0f;
                    
                    // Keep in arena bounds
                    if (bounds.HasValue && !bounds.Value.Contains(pawn.Position))
                    {
                        ForceBackToArena(pawn, bounds.Value);
                    }
                    
                    // Force new attacks if idle
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
            return jobDef == JobDefOf.Wait ||
                   jobDef == JobDefOf.Wait_Wander ||
                   jobDef == JobDefOf.Goto ||
                   jobDef == JobDefOf.Wait_Combat;
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
                bool hasRangedWeapon = HasRangedWeapon(pawn);
                float distance = pawn.Position.DistanceTo(target.Position);
                
                try
                {
                    if (hasRangedWeapon && distance > 3f)
                    {
                        // Ranged attack
                        var rangedJob = JobMaker.MakeJob(JobDefOf.AttackStatic, target);
                        pawn.jobs.TryTakeOrderedJob(rangedJob);
                    }
                    else
                    {
                        // Melee attack
                        var meleeJob = JobMaker.MakeJob(JobDefOf.AttackMelee, target);
                        pawn.jobs.TryTakeOrderedJob(meleeJob);
                    }
                }
                catch
                {
                    // Fallback: move toward enemy
                    try
                    {
                        var moveJob = JobMaker.MakeJob(JobDefOf.Goto, target.Position);
                        pawn.jobs.TryTakeOrderedJob(moveJob);
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
                            var moveJob = JobMaker.MakeJob(JobDefOf.Goto, targetPos);
                            pawn.jobs.TryTakeOrderedJob(moveJob);
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
                    var returnJob = JobMaker.MakeJob(JobDefOf.Goto, targetPos);
                    pawn.jobs.TryTakeOrderedJob(returnJob);
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
            phaseStartTime = DateTime.Now;
            
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
            phaseStartTime = DateTime.Now;
            
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
            
            ScheduleNextRound();
            
            Log.Message("SolWorld: Arena reset complete, next round scheduled");
        }
        
        private void SpawnTeamsWithProperHostility()
        {
            if (currentRoster == null || redSpawner == null || blueSpawner == null)
                return;
            
            Log.Message("SolWorld: Creating team factions with proper hostility");
            
            // Create hostile factions using specific defs
            redTeamFaction = CreateHostileFaction("Red Arena Team", true);
            blueTeamFaction = CreateHostileFaction("Blue Arena Team", false);
            
            if (redTeamFaction == null || blueTeamFaction == null)
            {
                Log.Error("SolWorld: Failed to create factions!");
                return;
            }
            
            // Set up all relations immediately
            SetupFactionRelations();
            
            // Spawn teams
            SpawnTeam(currentRoster.Red, redSpawner.Position, redTeamFaction, TeamColor.Red);
            SpawnTeam(currentRoster.Blue, blueSpawner.Position, blueTeamFaction, TeamColor.Blue);
            
            // Log final relations
            LogFactionRelations();
        }
        
        private Faction CreateHostileFaction(string name, bool hostileToPlayer)
        {
            // Use specific faction defs that work
            var factionDefName = hostileToPlayer ? "Pirate" : "OutlanderCivil";
            var factionDef = DefDatabase<FactionDef>.GetNamedSilentFail(factionDefName);
            
            if (factionDef == null)
            {
                // Fallback to any humanlike faction
                factionDef = DefDatabase<FactionDef>.AllDefs.FirstOrDefault(f => f.humanlikeFaction);
            }
            
            if (factionDef == null)
            {
                Log.Error("SolWorld: No faction def found!");
                return null;
            }
            
            var faction = new Faction();
            faction.def = factionDef;
            faction.Name = name;
            
            // Set color spectrum - RED team gets 0.0f (should be red), BLUE team gets 0.6f (should be blue)
            faction.colorFromSpectrum = hostileToPlayer ? 0.0f : 0.6f;
            
            // Add to world
            Find.FactionManager.Add(faction);
            
            Log.Message("SolWorld: Created " + name + " using " + factionDef.defName + " with color " + faction.colorFromSpectrum);
            
            return faction;
        }
        
        private void SetupFactionRelations()
        {
            Log.Message("SolWorld: Setting up faction relations");
            
            // Red team hostile to player
            redTeamFaction.SetRelationDirect(Faction.OfPlayer, FactionRelationKind.Hostile, false);
            Faction.OfPlayer.SetRelationDirect(redTeamFaction, FactionRelationKind.Hostile, false);
            
            // Blue team allied to player
            blueTeamFaction.SetRelationDirect(Faction.OfPlayer, FactionRelationKind.Ally, false);
            Faction.OfPlayer.SetRelationDirect(blueTeamFaction, FactionRelationKind.Ally, false);
            
            // Teams hostile to each other
            redTeamFaction.SetRelationDirect(blueTeamFaction, FactionRelationKind.Hostile, false);
            blueTeamFaction.SetRelationDirect(redTeamFaction, FactionRelationKind.Hostile, false);
            
            // Force goodwill values
            try
            {
                var redRelation = redTeamFaction.RelationWith(Faction.OfPlayer, false);
                if (redRelation != null)
                {
                    redRelation.baseGoodwill = -100;
                    redRelation.kind = FactionRelationKind.Hostile;
                }
                
                var blueRelation = blueTeamFaction.RelationWith(Faction.OfPlayer, false);
                if (blueRelation != null)
                {
                    blueRelation.baseGoodwill = 100;
                    blueRelation.kind = FactionRelationKind.Ally;
                }
                
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
            }
            catch (Exception ex)
            {
                Log.Warning("SolWorld: Error setting goodwill: " + ex.Message);
            }
        }
        
        private void LogFactionRelations()
        {
            try
            {
                var redToPlayer = redTeamFaction.RelationWith(Faction.OfPlayer).kind;
                var blueToPlayer = blueTeamFaction.RelationWith(Faction.OfPlayer).kind;
                var redToBlue = redTeamFaction.RelationWith(blueTeamFaction).kind;
                
                Log.Message("SolWorld: FACTION RELATIONS:");
                Log.Message("Red->Player: " + redToPlayer + " (should be Hostile)");
                Log.Message("Blue->Player: " + blueToPlayer + " (should be Ally)");
                Log.Message("Red->Blue: " + redToBlue + " (should be Hostile)");
                Log.Message("Red color: " + redTeamFaction.colorFromSpectrum + " (should be 0.0)");
                Log.Message("Blue color: " + blueTeamFaction.colorFromSpectrum + " (should be 0.6)");
                
                if (redToPlayer != FactionRelationKind.Hostile)
                {
                    Log.Error("SolWorld: RED TEAM IS NOT HOSTILE TO PLAYER!");
                }
            }
            catch (Exception ex)
            {
                Log.Error("SolWorld: Error logging relations: " + ex.Message);
            }
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
                    
                    Log.Message("SolWorld: Spawned " + fighter.WalletShort + " (" + teamColor + ") with faction " + teamFaction.Name);
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
            // Anti-flee
            if (pawn.mindState != null)
            {
                pawn.mindState.canFleeIndividual = false;
            }
            
            // Max mood
            if (pawn.needs?.mood != null)
            {
                pawn.needs.mood.CurLevel = 1.0f;
            }
            
            // Boost combat skills
            if (pawn.skills != null)
            {
                try
                {
                    var shooting = pawn.skills.GetSkill(SkillDefOf.Shooting);
                    var melee = pawn.skills.GetSkill(SkillDefOf.Melee);
                    
                    if (shooting != null)
                    {
                        shooting.Level = 15;
                        shooting.passion = Passion.Major;
                    }
                    
                    if (melee != null)
                    {
                        melee.Level = 15;
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
        
        // UI Methods
        public float GetTimeLeftInCurrentPhase()
        {
            if (currentState == ArenaState.Idle) return 0f;
            
            var elapsed = (float)(DateTime.Now - phaseStartTime).TotalSeconds;
            var total = currentState == ArenaState.Preview ? previewDurationSeconds : combatDurationSeconds;
            
            return Math.Max(0, total - elapsed);
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
            
            // Spawn with proper hostility
            SpawnTeamsWithProperHostility();
            
            // Force immediate combat
            InitiateAggressiveCombat();
            
            // Set combat state
            currentState = ArenaState.Combat;
            phaseStartTime = DateTime.Now;
            
            Log.Message("SolWorld: Test fighters spawned and combat initiated!");
        }
    }
}