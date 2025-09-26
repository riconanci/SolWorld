// solworld/SolWorldMod/Source/KillTracking.cs
using HarmonyLib;
using Verse;
using RimWorld;
using UnityEngine;
using System.Linq;
using System;

namespace SolWorldMod
{
    [HarmonyPatch]
    public static class KillTracking
    {
        // Track last down time to prevent double-processing
        private static readonly System.Collections.Generic.Dictionary<Pawn, int> lastDownTick = 
            new System.Collections.Generic.Dictionary<Pawn, int>();
        
        // MAIN KILL TRACKING - Patch the main pawn death method
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Pawn), "Kill")]
        public static void Pawn_Kill_Postfix(Pawn __instance, DamageInfo? dinfo)
        {
            try
            {
                // SIMPLE NULL CHECK - Skip if pawn is invalid
                if (__instance?.Map == null || __instance.Destroyed)
                    return;
                    
                HandlePawnDeath(__instance, dinfo);
            }
            catch (System.Exception ex)
            {
                Log.Error("SolWorld: Error in kill tracking: " + ex.Message);
            }
        }

        // BACKUP KILL TRACKING - Alternative patch for PostApplyDamage
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Pawn), "PostApplyDamage")]
        public static void Pawn_PostApplyDamage_Postfix(Pawn __instance, DamageInfo dinfo, float totalDamageDealt)
        {
            try
            {
                // SIMPLE NULL CHECKS - Skip if pawn is invalid
                if (__instance?.Map == null || __instance.Destroyed || !__instance.Dead)
                    return;
                    
                HandlePawnDeath(__instance, dinfo);
            }
            catch (System.Exception ex)
            {
                Log.Error("SolWorld: Error in damage tracking: " + ex.Message);
            }
        }

        // INSTANT DEATH ON DOWN - Patch Pawn.Downed property getter
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Pawn), "get_Downed")]
        public static void Pawn_get_Downed_Postfix(Pawn __instance, bool __result)
        {
            try
            {
                // SIMPLE NULL CHECKS - Skip if pawn is invalid
                if (!__result || __instance?.Map == null || __instance.Destroyed || __instance.Dead)
                    return;
                    
                if (__instance.Spawned)
                {
                    HandlePawnDown(__instance);
                }
            }
            catch (System.Exception ex)
            {
                Log.Error("SolWorld: Error in down tracking: " + ex.Message);
            }
        }

        // PREVENT DOWNED PAWN PATHFINDING ERRORS - SIMPLIFIED
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Verse.AI.JobDriver), "DriverTick")]
        public static bool JobDriver_DriverTick_Prefix(Verse.AI.JobDriver __instance)
        {
            try
            {
                // COMPREHENSIVE NULL CHECKS
                if (__instance?.pawn == null || __instance.pawn.Map == null || __instance.pawn.Destroyed)
                    return false; // Skip entirely if pawn is invalid
                
                // Only process downed pawns in active arenas
                if (__instance.pawn.Downed)
                {
                    var arenaComp = __instance.pawn.Map.GetComponent<MapComponent_SolWorldArena>();
                    if (arenaComp?.IsActive == true && arenaComp.GetPawnTeam(__instance.pawn).HasValue)
                    {
                        // This is a downed arena pawn - safely end their job
                        try
                        {
                            if (__instance.pawn.jobs?.curJob != null)
                            {
                                __instance.pawn.jobs.EndCurrentJob(Verse.AI.JobCondition.InterruptForced);
                            }
                        }
                        catch (System.Exception)
                        {
                            // Ignore job ending errors - pawn might be in weird state
                        }
                        return false; // Skip the original DriverTick
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning("SolWorld: Error in downed pawn job prevention: " + ex.Message);
            }
            return true; // Continue with original method
        }

        // ADDITIONAL SAFETY: Prevent null reference errors in AttackMelee job drivers
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Verse.AI.JobDriver_AttackMelee), "TryMakePreToilReservations")]
        public static bool AttackMelee_TryMakePreToilReservations_Prefix(Verse.AI.JobDriver_AttackMelee __instance, ref bool __result)
        {
            try
            {
                // NULL SAFETY CHECK
                if (__instance?.pawn == null || __instance.job?.targetA == null)
                {
                    __result = false;
                    return false; // Skip original method
                }
                
                // Check if pawn or target is invalid
                var pawn = __instance.pawn;
                var target = __instance.job.targetA.Thing as Pawn;
                
                if (pawn.Destroyed || pawn.Map == null || target?.Destroyed == true || target?.Map == null)
                {
                    __result = false;
                    return false; // Skip original method
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning("SolWorld: Error in AttackMelee safety check: " + ex.Message);
                __result = false;
                return false;
            }
            
            return true; // Continue with original method
        }

        // NEW: Handle pawn going down for instant death
        private static void HandlePawnDown(Pawn pawn)
        {
            // COMPREHENSIVE NULL CHECKS
            if (pawn?.Map == null || pawn.Dead || pawn.Destroyed) 
                return;

            var arenaComp = pawn.Map.GetComponent<MapComponent_SolWorldArena>();
            if (arenaComp?.IsActive != true) 
                return;

            // Check if this is an arena fighter
            var teamColor = arenaComp.GetPawnTeam(pawn);
            if (!teamColor.HasValue) 
                return;

            // Prevent double-processing
            var currentTick = Find.TickManager.TicksGame;
            if (lastDownTick.TryGetValue(pawn, out var lastTick) && (currentTick - lastTick) < 30)
            {
                return; // Too soon since last down event
            }
            lastDownTick[pawn] = currentTick;

            Log.Message($"SolWorld: Arena fighter {pawn.Name?.ToStringShort ?? "Unknown"} went down - executing instantly!");

            // Create dramatic execution damage
            var executionDamage = new DamageInfo(
                DamageDefOf.ExecutionCut,
                9999, // Massive damage to ensure death
                999f, // High armor penetration
                -1f,  // Random angle
                null, // No specific instigator
                pawn.health?.hediffSet?.GetBrain() // Target brain for instant death - NULL SAFE
            );

            // Kill the pawn immediately
            pawn.Kill(executionDamage);

            // Add dramatic visual effects - NULL SAFE
            if (pawn.Spawned && pawn.Map != null)
            {
                try
                {
                    // Blood splatter effect
                    FleckMaker.ThrowDustPuffThick(pawn.Position.ToVector3Shifted(), pawn.Map, 2.5f, Color.red);
                    
                    // Additional blood spatter
                    for (int i = 0; i < 3; i++)
                    {
                        var randomOffset = new Vector3(
                            UnityEngine.Random.Range(-1f, 1f),
                            0f,
                            UnityEngine.Random.Range(-1f, 1f)
                        );
                        
                        FleckMaker.ThrowDustPuff(
                            pawn.Position.ToVector3Shifted() + randomOffset,
                            pawn.Map,
                            1f
                        );
                    }

                    // Dev mode elimination text
                    if (Prefs.DevMode)
                    {
                        MoteMaker.ThrowText(pawn.Position.ToVector3Shifted(), pawn.Map, "ELIMINATED!", Color.red, 3f);
                    }
                }
                catch (System.Exception ex)
                {
                    Log.Warning("SolWorld: Error creating visual effects: " + ex.Message);
                }
            }

            // Dramatic elimination message
            var teamName = teamColor.Value.ToString().ToUpper();
            Messages.Message($"{teamName} fighter eliminated in arena!", MessageTypeDefOf.PawnDeath);

            // Clean up tracking
            lastDownTick.Remove(pawn);
        }

        // MAIN DEATH HANDLER - SIMPLIFIED AND NULL SAFE
        private static void HandlePawnDeath(Pawn victim, DamageInfo? dinfo)
        {
            // COMPREHENSIVE NULL CHECKS
            if (victim?.Map == null || victim.Destroyed)
                return;

            var arenaComp = victim.Map.GetComponent<MapComponent_SolWorldArena>();
            if (arenaComp?.IsActive != true || arenaComp.CurrentState != ArenaState.Combat)
                return;

            var roster = arenaComp.CurrentRoster;
            if (roster?.IsLive != true)
                return;

            Log.Message($"SolWorld: Processing death of {victim.Name?.ToStringShort ?? "Unknown"}");

            // Find the victim in our roster - NULL SAFE
            var victimFighter = FindFighterByPawn(roster, victim);
            if (victimFighter == null)
            {
                Log.Warning("SolWorld: Dead pawn not found in roster: " + (victim.Name?.ToStringShort ?? "Unknown"));
                return;
            }

            // Mark victim as dead
            victimFighter.Alive = false;
            Log.Message($"SolWorld: {victimFighter.Team} fighter {victimFighter.WalletShort} eliminated");

            // Try to find the killer and attribute the kill - NULL SAFE
            var killer = FindKiller(victim, dinfo);
            if (killer?.Map != null && !killer.Destroyed)
            {
                var killerFighter = FindFighterByPawn(roster, killer);
                if (killerFighter != null && killerFighter.Team != victimFighter.Team)
                {
                    killerFighter.Kills++;
                    Log.Message($"SolWorld: Kill attributed - {killerFighter.WalletShort} ({killerFighter.Team}) killed {victimFighter.WalletShort} ({victimFighter.Team})");
                    
                    // Add kill effect - NULL SAFE
                    if (killer.Spawned && killer.Map != null)
                    {
                        try
                        {
                            MoteMaker.ThrowText(killer.Position.ToVector3Shifted(), killer.Map, "+1 KILL", Color.yellow, 2f);
                        }
                        catch (System.Exception ex)
                        {
                            Log.Warning("SolWorld: Error creating kill text: " + ex.Message);
                        }
                    }
                }
                else if (killerFighter != null)
                {
                    Log.Message($"SolWorld: Friendly fire - {killerFighter.WalletShort} killed teammate {victimFighter.WalletShort}");
                }
            }
            else
            {
                Log.Message($"SolWorld: Death with unknown cause - {victimFighter.WalletShort} eliminated");
            }

            // NOTE: Round end checking removed - MapComponent handles this automatically via UpdateRosterStatus()
            // The arena component already checks team elimination every 30 ticks during combat

            // Clean up any tracking for this pawn
            lastDownTick.Remove(victim);
        }

        // KILLER IDENTIFICATION - SIMPLIFIED VERSION (FIXED)
        private static Pawn FindKiller(Pawn victim, DamageInfo? dinfo)
        {
            try
            {
                // Check DamageInfo instigator first - NULL SAFE
                if (dinfo?.Instigator is Pawn directKiller && !directKiller.Destroyed && directKiller.Map != null)
                {
                    return directKiller;
                }

                // REMOVED: Battle log access - causes compilation errors in RimWorld 1.6
                // Instead, use simpler nearby hostile detection

                // Fallback: Look for nearby hostiles - NULL SAFE
                if (victim?.Map != null)
                {
                    var nearbyPawns = GenRadial.RadialDistinctThingsAround(victim.Position, victim.Map, 5f, true)
                        ?.OfType<Pawn>()
                        ?.Where(p => p != null && !p.Destroyed && p.Map != null && p != victim && p.Spawned);

                    if (nearbyPawns != null)
                    {
                        foreach (var pawn in nearbyPawns)
                        {
                            try
                            {
                                if (pawn.mindState?.enemyTarget == victim || 
                                    pawn.CurJob?.targetA.Thing == victim)
                                {
                                    return pawn;
                                }
                            }
                            catch (System.Exception)
                            {
                                // Ignore individual pawn check errors
                                continue;
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning("SolWorld: Error finding killer: " + ex.Message);
            }

            return null;
        }

        // NULL SAFE fighter finder
        private static Fighter FindFighterByPawn(RoundRoster roster, Pawn pawn)
        {
            if (roster?.Red == null || roster.Blue == null || pawn == null)
                return null;

            try
            {
                var redFighter = roster.Red.FirstOrDefault(f => f?.PawnRef == pawn);
                if (redFighter != null) return redFighter;

                var blueFighter = roster.Blue.FirstOrDefault(f => f?.PawnRef == pawn);
                if (blueFighter != null) return blueFighter;
            }
            catch (System.Exception ex)
            {
                Log.Warning("SolWorld: Error finding fighter by pawn: " + ex.Message);
            }

            return null;
        }
    }
}