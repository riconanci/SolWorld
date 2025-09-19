// solworld/SolWorldMod/Source/MapComponent_SolWorldArena.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
        public ArenaState CurrentState { get; private set; } = ArenaState.Idle;
        public bool IsActive { get; private set; } = false;
        public RoundRoster CurrentRoster { get; private set; }
        
        // Timing
        private int nextRoundTick = -1;
        private int currentPhaseTicks = 0;
        private int phaseStartTick = 0;
        
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
        
        public override void MapComponentTick()
        {
            base.MapComponentTick();
            
            if (!IsActive || arenaCore?.IsOperational != true)
                return;
                
            var currentTick = Find.TickManager.TicksGame;
            
            // Check if it's time for the next scheduled round
            if (CurrentState == ArenaState.Idle && nextRoundTick > 0 && currentTick >= nextRoundTick)
            {
                StartPreviewPhase();
            }
            
            // Handle current phase timing
            switch (CurrentState)
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
                    else if (CurrentRoster != null && (CurrentRoster.RedAlive == 0 || CurrentRoster.BlueAlive == 0))
                    {
                        EndRound("Team eliminated");
                    }
                    break;
                    
                case ArenaState.Ended:
                    // Wait a few seconds before starting reset
                    if (currentTick - phaseStartTick >= 180) // 3 seconds
                    {
                        StartResetPhase();
                    }
                    break;
                    
                case ArenaState.Resetting:
                    // Reset should complete quickly, then schedule next round
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
            RefreshSpawners();
        }
        
        public void UnregisterArenaCore()
        {
            StopArena();
            arenaCore = null;
        }
        
        private void RefreshSpawners()
        {
            if (map == null) return;
            
            redSpawner = map.listerBuildings.AllBuildingsColonistOfDef(DefDatabase<ThingDef>.GetNamed("SolWorld_RedSpawn")).FirstOrDefault();
            blueSpawner = map.listerBuildings.AllBuildingsColonistOfDef(DefDatabase<ThingDef>.GetNamed("SolWorld_BlueSpawn")).FirstOrDefault();
        }
        
        public bool HasValidSetup => arenaCore?.IsOperational == true && redSpawner != null && blueSpawner != null;
        
        public CellRect? GetArenaBounds()
        {
            return arenaBounds.CalculateBounds(arenaCore, redSpawner, blueSpawner);
        }
        
        public void StartArena()
        {
            if (!HasValidSetup)
            {
                Messages.Message("Cannot start arena: missing Arena Core or team spawners", MessageTypeDefOf.RejectInput);
                return;
            }
            
            IsActive = true;
            ScheduleNextRound();
            Messages.Message("Arena activated. Next round starts soon...", MessageTypeDefOf.PositiveEvent);
        }
        
        public void StopArena()
        {
            IsActive = false;
            CurrentState = ArenaState.Idle;
            nextRoundTick = -1;
            
            // Clean up any active round
            if (CurrentRoster != null)
            {
                CleanupCurrentRound();
            }
            
            Messages.Message("Arena deactivated", MessageTypeDefOf.NeutralEvent);
        }
        
        public void ForceNextRound()
        {
            if (!HasValidSetup) return;
            
            if (CurrentState != ArenaState.Idle)
            {
                // Force end current round first
                if (CurrentState == ArenaState.Combat || CurrentState == ArenaState.Preview)
                {
                    EndRound("Force triggered");
                }
            }
            
            nextRoundTick = Find.TickManager.TicksGame + 60; // 1 second delay
        }
        
        private void ScheduleNextRound()
        {
            var currentTime = Find.TickManager.TicksGame;
            var cadenceTicks = SolWorldSettings.CADENCE_SECONDS * 60;
            var previewTicks = SolWorldSettings.PREVIEW_SECONDS * 60;
            
            // Schedule next round to start preview phase 30s before the next 5-minute mark
            var nextCadenceTick = ((currentTime / cadenceTicks) + 1) * cadenceTicks;
            nextRoundTick = nextCadenceTick - previewTicks;
            
            Log.Message($"SolWorld: Next round scheduled for tick {nextRoundTick} (in {(nextRoundTick - currentTime) / 60f:F0}s)");
        }
        
        private async void StartPreviewPhase()
        {
            CurrentState = ArenaState.Preview;
            phaseStartTick = Find.TickManager.TicksGame;
            
            Log.Message("SolWorld: Starting preview phase");
            
            try
            {
                // TODO: Implement CryptoReporter.FetchHoldersAsync() - for now use mock data
                var mockHolders = GenerateMockHolders();
                
                if (mockHolders?.Length == 20)
                {
                    // Create roster
                    CurrentRoster = new RoundRoster
                    {
                        RoundRewardTotalSol = SolWorldMod.Settings.roundPoolSol,
                        PayoutPercent = SolWorldMod.Settings.payoutPercent
                    };
                    
                    // Assign teams (first 10 = Red, last 10 = Blue)
                    for (int i = 0; i < 10; i++)
                    {
                        CurrentRoster.Red.Add(new Fighter(mockHolders[i], TeamColor.Red));
                        CurrentRoster.Blue.Add(new Fighter(mockHolders[i + 10], TeamColor.Blue));
                    }
                    
                    // Spawn pawns
                    SpawnFighters();
                    
                    // Initialize arena blueprint on first activation
                    var bounds = GetArenaBounds();
                    if (bounds.HasValue)
                    {
                        arenaBlueprint.InitializeBlueprint(map, bounds.Value);
                    }
                    
                    // Pause game for preview
                    Find.TickManager.Pause();
                    
                    Messages.Message($"Round {CurrentRoster.MatchId} preview started - {CurrentRoster.PerWinnerPayout:F3} SOL per winner", MessageTypeDefOf.PositiveEvent);
                }
                else
                {
                    Log.Error("SolWorld: Failed to generate mock holders");
                    EndRound("Failed to fetch holders");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"SolWorld: Error in preview phase: {ex.Message}");
                EndRound($"Preview error: {ex.Message}");
            }
        }
        
        private string[] GenerateMockHolders()
        {
            // Generate 20 mock wallet addresses for testing
            var holders = new string[20];
            for (int i = 0; i < 20; i++)
            {
                holders[i] = $"11111{i:D3}MockWallet{i:D3}Address{i:D3}11111";
            }
            return holders;
        }
        
        private void StartCombatPhase()
        {
            CurrentState = ArenaState.Combat;
            phaseStartTick = Find.TickManager.TicksGame;
            
            // Unpause game
            Find.TickManager.Pause();
            CurrentRoster.IsLive = true;
            
            Log.Message("SolWorld: Combat phase started");
            Messages.Message("Combat begins! Fight!", MessageTypeDefOf.PositiveEvent);
        }
        
        private async void EndRound(string reason)
        {
            CurrentState = ArenaState.Ended;
            phaseStartTick = Find.TickManager.TicksGame;
            
            if (CurrentRoster == null)
            {
                Log.Error("SolWorld: EndRound called with null roster");
                return;
            }
            
            CurrentRoster.IsLive = false;
            CurrentRoster.Winner = CurrentRoster.DetermineWinner();
            
            Log.Message($"SolWorld: Round ended - {reason}. Winner: {CurrentRoster.Winner}");
            
            try
            {
                // TODO: Implement CryptoReporter.ReportResultAsync() - for now show mock results
                var mockTxids = new string[] { "MockTxid1234567890", "MockTxid0987654321" };
                
                // Show results - simplified for now
                Messages.Message($"Round Complete! Winner: {CurrentRoster.Winner} team. Mock payout txids generated.", MessageTypeDefOf.PositiveEvent);
                Log.Message($"SolWorld: Mock txids: {string.Join(", ", mockTxids)}");
            }
            catch (Exception ex)
            {
                Log.Error($"SolWorld: Error reporting results: {ex.Message}");
                Messages.Message($"Round ended: {reason}. Payout failed: {ex.Message}", MessageTypeDefOf.NegativeEvent);
            }
        }
        
        private void StartResetPhase()
        {
            CurrentState = ArenaState.Resetting;
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
            CurrentState = ArenaState.Idle;
            CurrentRoster = null;
            
            // Schedule next round
            ScheduleNextRound();
            
            Log.Message("SolWorld: Arena reset complete, next round scheduled");
        }
        
        private void SpawnFighters()
        {
            if (CurrentRoster == null || redSpawner == null || blueSpawner == null)
                return;
                
            // Spawn Red team around red spawner
            SpawnTeam(CurrentRoster.Red, redSpawner.Position);
            
            // Spawn Blue team around blue spawner  
            SpawnTeam(CurrentRoster.Blue, blueSpawner.Position);
        }
        
        private void SpawnTeam(List<Fighter> fighters, IntVec3 spawnerPos)
        {
            for (int i = 0; i < fighters.Count; i++)
            {
                var fighter = fighters[i];
                
                // Find spawn position around spawner
                var spawnPos = CellFinder.RandomClosewalkCellNear(spawnerPos, map, 3);
                if (!spawnPos.IsValid)
                    spawnPos = spawnerPos;
                
                // Generate pawn
                var pawn = GenerateFighter(fighter);
                
                // Spawn pawn
                GenSpawn.Spawn(pawn, spawnPos, map);
                fighter.PawnRef = pawn;
                
                // Set faction and draft
                pawn.SetFaction(Faction.OfPlayer);
                pawn.drafter.Drafted = true;
            }
        }
        
        private Pawn GenerateFighter(Fighter fighter)
        {
            var pawnKind = PawnKindDefOf.Colonist;
            var pawn = PawnGenerator.GeneratePawn(pawnKind, Faction.OfPlayer);
            
            // Set name to wallet short
            pawn.Name = new NameSingle(fighter.WalletShort);
            
            // Give basic equipment - use a weapon that exists in RimWorld 1.5
            var weaponDef = DefDatabase<ThingDef>.GetNamedSilentFail("Gun_Autopistol") ?? 
                           DefDatabase<ThingDef>.GetNamedSilentFail("Gun_Pistol") ?? 
                           DefDatabase<ThingDef>.GetNamedSilentFail("MeleeWeapon_Knife");
                           
            if (weaponDef != null)
            {
                var weapon = ThingMaker.MakeThing(weaponDef);
                pawn.equipment.AddEquipment((ThingWithComps)weapon);
            }
            
            // Set team color (for identification)
            // This could be done with apparel coloring if desired
            
            return pawn;
        }
        
        private void CleanupCurrentRound()
        {
            if (CurrentRoster == null) return;
            
            // Despawn all fighter pawns
            foreach (var fighter in CurrentRoster.Red.Concat(CurrentRoster.Blue))
            {
                if (fighter.PawnRef?.Spawned == true)
                {
                    fighter.PawnRef.DeSpawn();
                }
            }
        }
        
        public float GetTimeLeftInCurrentPhase()
        {
            if (CurrentState == ArenaState.Idle) return 0f;
            
            var elapsed = Find.TickManager.TicksGame - phaseStartTick;
            var total = CurrentState == ArenaState.Preview ? 
                SolWorldSettings.PREVIEW_SECONDS * 60 : 
                SolWorldSettings.COMBAT_SECONDS * 60;
                
            return Math.Max(0, (total - elapsed) / 60f);
        }
    }
}