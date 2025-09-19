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

            // Mark victim as dead
            victimFighter.Alive = false;
            
            Log.Message("SolWorld: " + victimFighter.WalletShort + " (" + victimFighter.Team.ToString() + ") was killed");

            // Try to identify the killer and award kill credit
            Pawn killer = null;
            if (damageInfo.HasValue && damageInfo.Value.Instigator is Pawn instigatorPawn)
            {
                killer = instigatorPawn;
            }
            else if (damageInfo.HasValue && damageInfo.Value.Weapon != null)
            {
                // FIXED: damageInfo.Value.Weapon is already a ThingDef, not a Thing!
                // No need to access .def - it IS the def
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

            // Look for pawns near the victim who have this weapon equipped - use LINQ
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