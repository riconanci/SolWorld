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
        
        // State management - use fields to avoid ref issues
        private ArenaState currentState = ArenaState.Idle;
        private bool isActive = false;
        private RoundRoster currentRoster;
        private int nextRoundTick = -1;
        private int phaseStartTick = 0;
        
        // Faction storage for teams
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
            Scribe_Values.Look(ref phaseStartTick, "phaseStartTick", 0);
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
            
            // Handle current phase timing
            switch (currentState)
            {
                case ArenaState.Preview:
                    if (currentTick - phaseStartTick >= SolWorldSettings.PREVIEW_SECONDS * 60)
                    {
                        StartCombatPhase();
                    }
                    break;
                    
                case ArenaState.Combat:
                    // Check for combat end conditions
                    if (currentTick - phaseStartTick >= SolWorldSettings.COMBAT_SECONDS * 60)
                    {
                        EndRound("Time limit reached");
                    }
                    else if (currentRoster != null && (currentRoster.RedAlive == 0 || currentRoster.BlueAlive == 0))
                    {
                        EndRound("Team eliminated");
                    }
                    break;
                    
                case ArenaState.Ended:
                    if (currentTick - phaseStartTick >= 180) // 3 seconds
                    {
                        StartResetPhase();
                    }
                    break;
                    
                case ArenaState.Resetting:
                    if (currentTick - phaseStartTick >= 120) // 2 seconds for reset
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
            
            Log.Message("SolWorld: Starting spawner refresh...");
            
            redSpawner = null;
            blueSpawner = null;
            
            var allBuildings = map.listerBuildings.allBuildingsColonist;
            Log.Message("SolWorld: Searching through " + allBuildings.Count.ToString() + " colonist buildings");
            
            foreach (var building in allBuildings)
            {
                if (building.def?.defName == "SolWorld_RedSpawn")
                {
                    redSpawner = building;
                    Log.Message("SolWorld: Found Red spawner at " + building.Position.ToString());
                }
                else if (building.def?.defName == "SolWorld_BlueSpawn")
                {
                    blueSpawner = building;
                    Log.Message("SolWorld: Found Blue spawner at " + building.Position.ToString());
                }
            }
            
            Log.Message("SolWorld: RefreshSpawners complete - Red: " + (redSpawner != null).ToString() + ", Blue: " + (blueSpawner != null).ToString());
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
                if (currentState == ArenaState.Combat || currentState == ArenaState.Preview)
                {
                    EndRound("Force triggered");
                }
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
            Log.Message("SolWorld: Next round scheduled for tick " + nextRoundTick.ToString() + " (in " + timeUntilRound.ToString("F0") + "s)");
        }
        
        private void StartPreviewPhase()
        {
            currentState = ArenaState.Preview;
            phaseStartTick = Find.TickManager.TicksGame;
            
            Log.Message("SolWorld: Starting preview phase");
            
            try
            {
                var mockHolders = GenerateMockHolders();
                
                if (mockHolders?.Length == 20)
                {
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
                    
                    SpawnFighters();
                    
                    var bounds = GetArenaBounds();
                    if (bounds.HasValue)
                    {
                        arenaBlueprint.InitializeBlueprint(map, bounds.Value);
                    }
                    
                    Find.TickManager.Pause();
                    
                    var payoutText = currentRoster.PerWinnerPayout.ToString("F3");
                    Messages.Message("Round " + currentRoster.MatchId + " preview started - " + payoutText + " SOL per winner", MessageTypeDefOf.PositiveEvent);
                }
                else
                {
                    Log.Error("SolWorld: Failed to generate mock holders");
                    EndRound("Failed to fetch holders");
                }
            }
            catch (Exception ex)
            {
                Log.Error("SolWorld: Error in preview phase: " + ex.Message);
                EndRound("Preview error: " + ex.Message);
            }
        }
        
        private string[] GenerateMockHolders()
        {
            var holders = new string[20];
            for (int i = 0; i < 20; i++)
            {
                holders[i] = "11111" + i.ToString("D3") + "MockWallet" + i.ToString("D3") + "Address" + i.ToString("D3") + "11111";
            }
            return holders;
        }
        
        private void StartCombatPhase()
        {
            currentState = ArenaState.Combat;
            phaseStartTick = Find.TickManager.TicksGame;
            
            Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            currentRoster.IsLive = true;
            
            OrderTeamsToAttack();
            
            Log.Message("SolWorld: Combat phase started - teams ordered to attack!");
            Messages.Message("Combat begins! Teams are attacking!", MessageTypeDefOf.PositiveEvent);
        }
        
        private void OrderTeamsToAttack()
        {
            if (currentRoster == null || redSpawner == null || blueSpawner == null)
                return;
                
            // Order red team to attack blue spawn area
            foreach (var redFighter in currentRoster.Red)
            {
                if (redFighter.PawnRef?.Spawned == true && !redFighter.PawnRef.Dead)
                {
                    var pawn = redFighter.PawnRef;
                    pawn.drafter.Drafted = true;
                    
                    var attackPos = CellFinder.RandomClosewalkCellNear(blueSpawner.Position, map, 8);
                    if (!attackPos.IsValid)
                        attackPos = blueSpawner.Position;
                        
                    var gotoJob = JobMaker.MakeJob(JobDefOf.Goto, attackPos);
                    pawn.jobs.TryTakeOrderedJob(gotoJob);
                    
                    Log.Message("SolWorld: " + redFighter.WalletShort + " (Red) ordered to attack Blue spawn at " + attackPos.ToString());
                }
            }
            
            // Order blue team to attack red spawn area
            foreach (var blueFighter in currentRoster.Blue)
            {
                if (blueFighter.PawnRef?.Spawned == true && !blueFighter.PawnRef.Dead)
                {
                    var pawn = blueFighter.PawnRef;
                    pawn.drafter.Drafted = true;
                    
                    var attackPos = CellFinder.RandomClosewalkCellNear(redSpawner.Position, map, 8);
                    if (!attackPos.IsValid)
                        attackPos = redSpawner.Position;
                        
                    var gotoJob = JobMaker.MakeJob(JobDefOf.Goto, attackPos);
                    pawn.jobs.TryTakeOrderedJob(gotoJob);
                    
                    Log.Message("SolWorld: " + blueFighter.WalletShort + " (Blue) ordered to attack Red spawn at " + attackPos.ToString());
                }
            }
        }
        
        private void EndRound(string reason)
        {
            currentState = ArenaState.Ended;
            phaseStartTick = Find.TickManager.TicksGame;
            
            if (currentRoster == null)
            {
                Log.Error("SolWorld: EndRound called with null roster");
                return;
            }
            
            currentRoster.IsLive = false;
            currentRoster.Winner = currentRoster.DetermineWinner();
            
            Log.Message("SolWorld: Round ended - " + reason + ". Winner: " + currentRoster.Winner.ToString());
            
            try
            {
                var mockTxids = new string[] { "MockTxid1234567890", "MockTxid0987654321" };
                Messages.Message("Round Complete! Winner: " + currentRoster.Winner.ToString() + " team. Mock payout txids generated.", MessageTypeDefOf.PositiveEvent);
                Log.Message("SolWorld: Mock txids: " + string.Join(", ", mockTxids));
            }
            catch (Exception ex)
            {
                Log.Error("SolWorld: Error reporting results: " + ex.Message);
                Messages.Message("Round ended: " + reason + ". Payout failed: " + ex.Message, MessageTypeDefOf.NegativeEvent);
            }
        }
        
        private void StartResetPhase()
        {
            currentState = ArenaState.Resetting;
            phaseStartTick = Find.TickManager.TicksGame;
            
            Log.Message("SolWorld: Starting arena reset");
            
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
        
        private void SpawnFighters()
        {
            if (currentRoster == null || redSpawner == null || blueSpawner == null)
            {
                Log.Error("SolWorld: Cannot spawn fighters - missing roster or spawners");
                return;
            }
            
            Log.Message("SolWorld: Spawning fighters with proper team factions - no friendly fire");
            
            // Create temporary factions that are hostile to each other but not to themselves
            redTeamFaction = CreateProperTeamFaction("Red Arena Team", 0.0f);
            blueTeamFaction = CreateProperTeamFaction("Blue Arena Team", 0.6f);
            
            // Set faction relations properly
            // Blue team friendly to player (so we can see them as blue)
            blueTeamFaction.SetRelationDirect(Faction.OfPlayer, FactionRelationKind.Ally, false);
            
            // Red team hostile to player (so we can see them as red)
            redTeamFaction.SetRelationDirect(Faction.OfPlayer, FactionRelationKind.Hostile, false);
            
            // Make teams hostile to each other
            redTeamFaction.SetRelationDirect(blueTeamFaction, FactionRelationKind.Hostile, false);
            blueTeamFaction.SetRelationDirect(redTeamFaction, FactionRelationKind.Hostile, false);
            
            SpawnTeam(currentRoster.Red, redSpawner.Position, redTeamFaction, TeamColor.Red);
            SpawnTeam(currentRoster.Blue, blueSpawner.Position, blueTeamFaction, TeamColor.Blue);
            
            Log.Message("SolWorld: Teams spawned - Red (hostile to player), Blue (allied to player)");
        }
        
        private Faction CreateProperTeamFaction(string name, float colorSpectrum)
        {
            // Find a suitable faction def
            var factionDef = DefDatabase<FactionDef>.AllDefs
                .FirstOrDefault(f => f.humanlikeFaction && !f.hidden) ?? FactionDefOf.PlayerColony;
                
            var faction = new Faction();
            faction.def = factionDef;
            faction.Name = name;
            faction.colorFromSpectrum = colorSpectrum;
            
            return faction;
        }
        
        private void SpawnTeam(System.Collections.Generic.List<Fighter> fighters, IntVec3 spawnerPos, Faction teamFaction, TeamColor teamColor)
        {
            for (int i = 0; i < fighters.Count; i++)
            {
                var fighter = fighters[i];
                
                var spawnPos = CellFinder.RandomClosewalkCellNear(spawnerPos, map, 5);
                if (!spawnPos.IsValid)
                    spawnPos = spawnerPos;
                
                var pawn = GenerateFighter(fighter, teamFaction, teamColor);
                
                GenSpawn.Spawn(pawn, spawnPos, map);
                fighter.PawnRef = pawn;
                
                Log.Message("SolWorld: Spawned " + fighter.WalletShort + " (" + fighter.Team.ToString() + ") at " + spawnPos.ToString() + " with faction " + teamFaction.Name);
            }
        }
        
        private Pawn GenerateFighter(Fighter fighter, Faction teamFaction, TeamColor teamColor)
        {
            var pawnKind = PawnKindDefOf.Colonist;
            var pawn = PawnGenerator.GeneratePawn(pawnKind, teamFaction);
            
            // Set name to wallet short
            pawn.Name = new NameSingle(fighter.WalletShort);
            
            // Give them weapons
            var weaponDef = DefDatabase<ThingDef>.GetNamedSilentFail("Gun_Autopistol") ?? 
                           DefDatabase<ThingDef>.GetNamedSilentFail("Gun_Revolver") ?? 
                           DefDatabase<ThingDef>.GetNamedSilentFail("Gun_Pistol") ?? 
                           DefDatabase<ThingDef>.GetNamedSilentFail("MeleeWeapon_Knife");
                           
            if (weaponDef != null)
            {
                if (pawn.equipment?.Primary != null)
                {
                    pawn.equipment.Remove(pawn.equipment.Primary);
                }
                
                var weapon = ThingMaker.MakeThing(weaponDef);
                pawn.equipment.AddEquipment((ThingWithComps)weapon);
            }
            
            // Set faction
            pawn.SetFaction(teamFaction);
            
            // Make them fearless fighters who never flee
            MakeFearlessFighter(pawn);
            
            return pawn;
        }
        
        private void MakeFearlessFighter(Pawn pawn)
        {
            // Set them to never flee individually
            if (pawn.mindState != null)
            {
                pawn.mindState.canFleeIndividual = false;
            }
            
            // Set high mood to prevent mental breaks
            if (pawn.needs?.mood != null)
            {
                pawn.needs.mood.CurLevel = 0.8f; // Keep mood high to prevent breaks
            }
            
            // Remove any existing mental states that could interfere
            if (pawn.mindState?.mentalStateHandler != null)
            {
                pawn.mindState.mentalStateHandler.Reset();
            }
            
            // Try to add brave/aggressive traits if possible
            if (pawn.story?.traits != null)
            {
                // Try to add brawler trait for aggressiveness
                var brawlerTrait = DefDatabase<TraitDef>.GetNamedSilentFail("Brawler");
                if (brawlerTrait != null && !pawn.story.traits.HasTrait(brawlerTrait))
                {
                    try
                    {
                        pawn.story.traits.GainTrait(new Trait(brawlerTrait));
                    }
                    catch
                    {
                        // Ignore if trait can't be added
                    }
                }
            }
            
            // Set combat stats to be more aggressive
            if (pawn.skills != null)
            {
                // Boost shooting and melee skills
                var shootingSkill = pawn.skills.GetSkill(SkillDefOf.Shooting);
                var meleeSkill = pawn.skills.GetSkill(SkillDefOf.Melee);
                
                if (shootingSkill != null)
                {
                    shootingSkill.Level = Math.Max(shootingSkill.Level, 8); // Minimum skill 8
                }
                
                if (meleeSkill != null)
                {
                    meleeSkill.Level = Math.Max(meleeSkill.Level, 8); // Minimum skill 8
                }
            }
            
            Log.Message("SolWorld: Made " + pawn.Name.ToStringShort + " a fearless fighter (no fleeing, boosted combat)");
        }
        
        private void CleanupCurrentRound()
        {
            if (currentRoster == null) return;
            
            var allFighters = currentRoster.Red.Concat(currentRoster.Blue);
            
            foreach (var fighter in allFighters)
            {
                if (fighter.PawnRef?.Spawned == true)
                {
                    fighter.PawnRef.DeSpawn();
                    Log.Message("SolWorld: Despawned " + fighter.WalletShort);
                }
            }
        }
        
        public float GetTimeLeftInCurrentPhase()
        {
            if (currentState == ArenaState.Idle) return 0f;
            
            var elapsed = Find.TickManager.TicksGame - phaseStartTick;
            var total = currentState == ArenaState.Preview ? 
                SolWorldSettings.PREVIEW_SECONDS * 60 : 
                SolWorldSettings.COMBAT_SECONDS * 60;
                
            return Math.Max(0, (total - elapsed) / 60f);
        }
        
        public void TestSpawnFighters(RoundRoster testRoster)
        {
            if (testRoster == null || redSpawner == null || blueSpawner == null)
            {
                Log.Error("SolWorld: Cannot test spawn - missing roster or spawners");
                return;
            }
            
            Log.Message("SolWorld: Test spawning fighters with proper team combat...");
            
            // Use the same faction system as real rounds
            redTeamFaction = CreateProperTeamFaction("Test Red Team", 0.0f);
            blueTeamFaction = CreateProperTeamFaction("Test Blue Team", 0.6f);
            
            // Set relations
            blueTeamFaction.SetRelationDirect(Faction.OfPlayer, FactionRelationKind.Ally, false);
            redTeamFaction.SetRelationDirect(Faction.OfPlayer, FactionRelationKind.Hostile, false);
            redTeamFaction.SetRelationDirect(blueTeamFaction, FactionRelationKind.Hostile, false);
            blueTeamFaction.SetRelationDirect(redTeamFaction, FactionRelationKind.Hostile, false);
            
            SpawnTeam(testRoster.Red, redSpawner.Position, redTeamFaction, TeamColor.Red);
            SpawnTeam(testRoster.Blue, blueSpawner.Position, blueTeamFaction, TeamColor.Blue);
            
            // Give immediate attack orders
            foreach (var redFighter in testRoster.Red)
            {
                if (redFighter.PawnRef?.Spawned == true)
                {
                    redFighter.PawnRef.drafter.Drafted = true;
                    var attackPos = CellFinder.RandomClosewalkCellNear(blueSpawner.Position, map, 8);
                    var gotoJob = JobMaker.MakeJob(JobDefOf.Goto, attackPos);
                    redFighter.PawnRef.jobs.TryTakeOrderedJob(gotoJob);
                }
            }
            
            foreach (var blueFighter in testRoster.Blue)
            {
                if (blueFighter.PawnRef?.Spawned == true)
                {
                    blueFighter.PawnRef.drafter.Drafted = true;
                    var attackPos = CellFinder.RandomClosewalkCellNear(redSpawner.Position, map, 8);
                    var gotoJob = JobMaker.MakeJob(JobDefOf.Goto, attackPos);
                    blueFighter.PawnRef.jobs.TryTakeOrderedJob(gotoJob);
                }
            }
        }
    }
}