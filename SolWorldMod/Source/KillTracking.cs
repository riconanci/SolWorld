// solworld/SolWorldMod/Source/KillTracking.cs
using HarmonyLib;
using Verse;
using RimWorld;
using System.Linq;

namespace SolWorldMod
{
    [HarmonyPatch]
    public static class KillTracking
    {
        // Patch the main pawn death method
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

        // Alternative patch for PostApplyDamage in case Kill isn't called in all scenarios
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

        // FIXED: Patch downed pawn job assignment to prevent pathing errors
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Verse.AI.JobDriver), "DriverTick")]
        public static bool JobDriver_DriverTick_Prefix(Verse.AI.JobDriver __instance)
        {
            try
            {
                // Prevent downed arena pawns from trying to path
                if (__instance?.pawn != null && __instance.pawn.Downed)
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
            }
            return true; // Continue with original method
        }

        private static void HandlePawnDeath(Pawn victim, DamageInfo? damageInfo)
        {
            if (victim?.Map == null)
                return;

            // Get the arena component
            var arenaComp = victim.Map.GetComponent<MapComponent_SolWorldArena>();
            if (arenaComp?.CurrentRoster == null || !arenaComp.CurrentRoster.IsLive)
                return;

            // Find the victim in our roster
            var victimFighter = FindFighterByPawn(arenaComp.CurrentRoster, victim);
            if (victimFighter == null)
                return; // Not an arena fighter

            // Mark victim as dead and stop all their jobs
            victimFighter.Alive = false;
            
            // FIXED: Immediately stop all jobs for dead pawns to prevent pathing errors
            if (victim.jobs != null)
            {
                victim.jobs.EndCurrentJob(Verse.AI.JobCondition.InterruptForced);
                victim.jobs.ClearQueuedJobs();
            }
            
            Log.Message("SolWorld: " + victimFighter.WalletShort + " (" + victimFighter.Team.ToString() + ") was killed");

            // Try to identify the killer and award kill credit
            Pawn killer = null;
            if (damageInfo.HasValue && damageInfo.Value.Instigator is Pawn instigatorPawn)
            {
                killer = instigatorPawn;
            }
            else if (damageInfo.HasValue && damageInfo.Value.Weapon != null)
            {
                // damageInfo.Value.Weapon is already a ThingDef in RimWorld 1.6
                ThingDef weaponDef = damageInfo.Value.Weapon;
                killer = TryFindWeaponOwner(victim, weaponDef);
            }

            if (killer != null)
            {
                var killerFighter = FindFighterByPawn(arenaComp.CurrentRoster, killer);
                if (killerFighter != null && killerFighter.Team != victimFighter.Team)
                {
                    killerFighter.Kills++;
                    Log.Message("SolWorld: Kill credited to " + killerFighter.WalletShort + " (" + killerFighter.Team.ToString() + ") - Total: " + killerFighter.Kills);
                }
            }

            // Check if this death ends the round (team elimination)
            CheckForRoundEnd(arenaComp);
        }

        private static Fighter FindFighterByPawn(RoundRoster roster, Pawn pawn)
        {
            // Use LINQ (available in RimWorld 1.6)
            return roster.Red.FirstOrDefault(f => f.PawnRef == pawn) ??
                   roster.Blue.FirstOrDefault(f => f.PawnRef == pawn);
        }

        private static Pawn TryFindWeaponOwner(Pawn victim, ThingDef weaponDef)
        {
            // This is a best-effort attempt to find who owns a weapon
            if (victim?.Map == null)
                return null;

            // Look for pawns near the victim who have this weapon equipped
            var nearbyPawns = victim.Map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer)
                .Where(p => p.Position.DistanceTo(victim.Position) < 20);

            return nearbyPawns.FirstOrDefault(pawn => pawn.equipment?.Primary?.def == weaponDef);
        }

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
                Log.Message("SolWorld: Team elimination detected - Red: " + roster.RedAlive + ", Blue: " + roster.BlueAlive);
                
                // The MapComponent will handle the actual round ending in its next tick
                // We just log here to track the trigger condition
            }
        }

        // Utility method for manual kill attribution (if needed)
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
                Log.Message("SolWorld: Manual kill attribution - " + killerFighter.WalletShort + " killed " + victimFighter.WalletShort);
            }
        }
    }
}