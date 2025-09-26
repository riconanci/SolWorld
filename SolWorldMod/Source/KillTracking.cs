// Simplified KillTracking.cs - Only patches public accessible methods
// REPLACE your existing KillTracking.cs with this version

using HarmonyLib;
using Verse;
using RimWorld;
using UnityEngine;
using System.Linq;
using System;
using Verse.AI;

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
                // Only handle if pawn actually died from this damage
                if (__instance.Dead)
                {
                    HandlePawnDeath(__instance, dinfo);
                }
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
                // Only trigger when pawn just became downed
                if (__result && __instance.Spawned)
                {
                    HandlePawnDown(__instance);
                }
            }
            catch (System.Exception ex)
            {
                Log.Error("SolWorld: Error in down tracking: " + ex.Message);
            }
        }

        // PREVENT DOWNED PAWN PATHFINDING ERRORS
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Verse.AI.JobDriver), "DriverTick")]
        public static bool JobDriver_DriverTick_Prefix(Verse.AI.JobDriver __instance)
        {
            try
            {
                // Comprehensive null checks to prevent the errors you're seeing
                if (__instance?.pawn == null || __instance.pawn.Destroyed) return false;
                
                // Prevent downed arena pawns from trying to path
                if (__instance.pawn.Downed)
                {
                    var arenaComp = __instance.pawn.Map?.GetComponent<MapComponent_SolWorldArena>();
                    if (arenaComp?.IsActive == true && arenaComp.GetPawnTeam(__instance.pawn).HasValue)
                    {
                        // This is a downed arena pawn - end their job to prevent pathing errors
                        if (__instance.pawn.jobs?.curJob != null)
                        {
                            __instance.pawn.jobs.EndCurrentJob(Verse.AI.JobCondition.InterruptForced);
                        }
                        return false; // Skip the original DriverTick
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning("SolWorld: Error in downed pawn job prevention: " + ex.Message);
                return false; // Skip on error to prevent cascading failures
            }
            return true; // Continue with original method
        }
        
        // SIMPLE FIX: Prevent bullets from impacting destroyed things
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Bullet), "Impact")]
        public static bool Bullet_Impact_Prefix(Bullet __instance, Thing hitThing)
        {
            try
            {
                // Allow impact if no specific hit thing
                if (hitThing == null) return true;

                // Check if the hit thing is valid
                if (hitThing.Destroyed) return false; // Skip impact on destroyed things

                // Special handling for pawn targets
                if (hitThing is Pawn hitPawn)
                {
                    if (hitPawn.Dead || !hitPawn.Spawned)
                    {
                        return false; // Skip impact on dead/despawned pawns
                    }
                }

                return true; // Continue with original impact
            }
            catch (System.Exception ex)
            {
                Log.Warning($"SolWorld: Bullet impact safety error: {ex.Message}");
                return false; // Skip impact on error
            }
        }

        // NEW: Handle pawn going down for instant death
        private static void HandlePawnDown(Pawn pawn)
        {
            if (pawn?.Map == null || pawn.Dead) return;

            var arenaComp = pawn.Map.GetComponent<MapComponent_SolWorldArena>();
            if (arenaComp?.IsActive != true) return;

            // Check if this is an arena fighter
            var teamColor = arenaComp.GetPawnTeam(pawn);
            if (!teamColor.HasValue) return;

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
                pawn.health.hediffSet.GetBrain() // Target brain for instant death
            );

            // Kill the pawn immediately
            pawn.Kill(executionDamage);

            // Add dramatic visual effects (FIXED - using correct RimWorld 1.6 API)
            if (pawn.Spawned)
            {
                // Blood splatter effect - FIXED method call
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

            // Dramatic elimination message
            var teamName = teamColor.Value.ToString().ToUpper();
            Messages.Message($"{teamName} fighter eliminated in arena!", MessageTypeDefOf.PawnDeath);

            // Clean up tracking
            lastDownTick.Remove(pawn);
        }

        // MAIN DEATH HANDLER
        private static void HandlePawnDeath(Pawn victim, DamageInfo? dinfo)
        {
            if (victim?.Map == null)
                return;

            var arenaComp = victim.Map.GetComponent<MapComponent_SolWorldArena>();
            if (arenaComp?.IsActive != true || arenaComp.CurrentState != ArenaState.Combat)
                return;

            var roster = arenaComp.CurrentRoster;
            if (roster?.IsLive != true)
                return;

            Log.Message($"SolWorld: Processing death of {victim.Name?.ToStringShort ?? "Unknown"}");

            // Find the victim in our roster
            var victimFighter = FindFighterByPawn(roster, victim);
            if (victimFighter == null)
            {
                Log.Warning("SolWorld: Dead pawn not found in roster: " + victim.Name);
                return;
            }

            // Mark victim as dead
            victimFighter.Alive = false;
            Log.Message($"SolWorld: {victimFighter.Team} fighter {victimFighter.WalletShort} eliminated");

            // Try to find the killer and attribute the kill
            var killer = FindKiller(victim, dinfo);
            if (killer != null)
            {
                var killerFighter = FindFighterByPawn(roster, killer);
                if (killerFighter != null && killerFighter.Team != victimFighter.Team)
                {
                    killerFighter.Kills++;
                    Log.Message($"SolWorld: Kill attributed - {killerFighter.WalletShort} ({killerFighter.Team}) killed {victimFighter.WalletShort} ({victimFighter.Team})");
                    
                    // Add kill effect
                    if (killer.Spawned)
                    {
                        MoteMaker.ThrowText(killer.Position.ToVector3Shifted(), killer.Map, "+1 KILL", Color.yellow, 2f);
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

            // Check if this death ends the round
            CheckForRoundEnd(arenaComp);

            // Clean up any tracking for this pawn
            lastDownTick.Remove(victim);
        }

        // KILLER IDENTIFICATION (FIXED - removed invalid properties)
        private static Pawn FindKiller(Pawn victim, DamageInfo? dinfo)
        {
            if (!dinfo.HasValue)
                return null;

            var damageInfo = dinfo.Value;

            // Direct instigator (ranged weapons, direct melee)
            if (damageInfo.Instigator is Pawn directKiller)
            {
                return directKiller;
            }

            // Weapon-based killing - try to find weapon owner
            if (damageInfo.Weapon != null)
            {
                var weaponOwner = TryFindWeaponOwner(victim, damageInfo.Weapon);
                if (weaponOwner != null)
                {
                    return weaponOwner;
                }
            }

            // Check for nearby arena pawns as potential killers
            var arenaComp = victim.Map?.GetComponent<MapComponent_SolWorldArena>();
            if (arenaComp?.IsActive == true)
            {
                var allArenaPawns = arenaComp.GetAllArenaPawns();
                var nearbyPawns = allArenaPawns.Where(p => 
                    p?.Spawned == true && 
                    p != victim &&
                    p.Position.DistanceTo(victim.Position) < 5
                );

                // Return the closest hostile pawn
                var hostilePawns = nearbyPawns.Where(p => {
                    var pawnTeam = arenaComp.GetPawnTeam(p);
                    var victimTeam = arenaComp.GetPawnTeam(victim);
                    return pawnTeam.HasValue && victimTeam.HasValue && pawnTeam != victimTeam;
                });

                return hostilePawns.OrderBy(p => p.Position.DistanceTo(victim.Position)).FirstOrDefault();
            }

            return null;
        }

        // FIGHTER LOOKUP UTILITIES
        private static Fighter FindFighterByPawn(RoundRoster roster, Pawn pawn)
        {
            if (roster == null || pawn == null)
                return null;

            return roster.Red.FirstOrDefault(f => f.PawnRef == pawn) ??
                   roster.Blue.FirstOrDefault(f => f.PawnRef == pawn);
        }

        private static Pawn TryFindWeaponOwner(Pawn victim, ThingDef weaponDef)
        {
            if (victim?.Map == null)
                return null;

            // Look for arena pawns near the victim who have this weapon equipped
            var arenaComp = victim.Map.GetComponent<MapComponent_SolWorldArena>();
            if (arenaComp?.IsActive != true)
                return null;

            var allArenaPawns = arenaComp.GetAllArenaPawns();
            var nearbyArenaPawns = allArenaPawns.Where(p => 
                p?.Spawned == true && 
                p != victim &&
                p.Position.DistanceTo(victim.Position) < 25 &&
                p.equipment?.Primary?.def == weaponDef
            );

            return nearbyArenaPawns.FirstOrDefault();
        }

        // ROUND END DETECTION
        private static void CheckForRoundEnd(MapComponent_SolWorldArena arenaComp)
        {
            if (arenaComp.CurrentState != ArenaState.Combat)
                return;

            var roster = arenaComp.CurrentRoster;
            if (roster == null)
                return;

            // Check for team elimination
            if (roster.RedAlive == 0 || roster.BlueAlive == 0)
            {
                Log.Message($"SolWorld: Team elimination detected - Red: {roster.RedAlive}/10, Blue: {roster.BlueAlive}/10");
                
                // Determine winner
                if (roster.RedAlive > 0)
                {
                    Log.Message("SolWorld: RED TEAM WINS by elimination!");
                }
                else if (roster.BlueAlive > 0)
                {
                    Log.Message("SolWorld: BLUE TEAM WINS by elimination!");
                }
                else
                {
                    Log.Message("SolWorld: MUTUAL ELIMINATION - both teams eliminated!");
                }
            }
            else
            {
                Log.Message($"SolWorld: Combat continues - Red: {roster.RedAlive}/10, Blue: {roster.BlueAlive}/10");
            }
        }

        // MANUAL KILL ATTRIBUTION (utility method)
        public static void AttributeKill(RoundRoster roster, Pawn killer, Pawn victim)
        {
            if (roster?.IsLive != true)
                return;

            var killerFighter = FindFighterByPawn(roster, killer);
            var victimFighter = FindFighterByPawn(roster, victim);

            if (killerFighter != null && victimFighter != null && 
                killerFighter.Team != victimFighter.Team)
            {
                killerFighter.Kills++;
                victimFighter.Alive = false;
                Log.Message($"SolWorld: Manual kill attribution - {killerFighter.WalletShort} killed {victimFighter.WalletShort}");
            }
        }

        // CLEANUP METHOD (called when arena stops)
        public static void CleanupTracking()
        {
            lastDownTick.Clear();
            Log.Message("SolWorld: Kill tracking cleanup completed");
        }
    }
}