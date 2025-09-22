// solworld/SolWorldMod/Source/LoadoutManager.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace SolWorldMod
{
    public static class LoadoutManager
    {
        // CONFIGURABLE LOADOUT PRESETS
        public static readonly LoadoutPreset[] AVAILABLE_PRESETS = new LoadoutPreset[]
        {
            // PRESET 1: Mixed Combat (default)
            new LoadoutPreset
            {
                Name = "Mixed Combat",
                Description = "Balanced mix of ranged and melee fighters",
                Weapons = new WeaponLoadout[]
                {
                    new WeaponLoadout { DefName = "Gun_AssaultRifle", Count = 3, Description = "Assault Rifles" },
                    new WeaponLoadout { DefName = "Gun_SniperRifle", Count = 2, Description = "Sniper Rifles" },
                    new WeaponLoadout { DefName = "Gun_Autopistol", Count = 2, Description = "Autopistols" },
                    new WeaponLoadout { DefName = "MeleeWeapon_LongSword", Count = 2, Description = "Longswords" },
                    new WeaponLoadout { DefName = "MeleeWeapon_Knife", Count = 1, Description = "Combat Knives" }
                }
            },

            // PRESET 2: Ranged Focus
            new LoadoutPreset
            {
                Name = "Ranged Focus",
                Description = "Primarily ranged combat with backup melee",
                Weapons = new WeaponLoadout[]
                {
                    new WeaponLoadout { DefName = "Gun_AssaultRifle", Count = 4, Description = "Assault Rifles" },
                    new WeaponLoadout { DefName = "Gun_SniperRifle", Count = 3, Description = "Sniper Rifles" },
                    new WeaponLoadout { DefName = "Gun_Autopistol", Count = 2, Description = "Autopistols" },
                    new WeaponLoadout { DefName = "MeleeWeapon_LongSword", Count = 1, Description = "Longsword" }
                }
            },

            // PRESET 3: Close Combat
            new LoadoutPreset
            {
                Name = "Close Combat",
                Description = "Melee-focused with some ranged support",
                Weapons = new WeaponLoadout[]
                {
                    new WeaponLoadout { DefName = "MeleeWeapon_LongSword", Count = 4, Description = "Longswords" },
                    new WeaponLoadout { DefName = "MeleeWeapon_Knife", Count = 3, Description = "Combat Knives" },
                    new WeaponLoadout { DefName = "Gun_Autopistol", Count = 2, Description = "Autopistols" },
                    new WeaponLoadout { DefName = "Gun_Revolver", Count = 1, Description = "Revolver" }
                }
            },

            // PRESET 4: Elite Warriors
            new LoadoutPreset
            {
                Name = "Elite Warriors",
                Description = "High-tier weapons for experienced fighters",
                Weapons = new WeaponLoadout[]
                {
                    new WeaponLoadout { DefName = "Gun_ChargeRifle", Count = 3, Description = "Charge Rifles" },
                    new WeaponLoadout { DefName = "Gun_SniperRifle", Count = 2, Description = "Sniper Rifles" },
                    new WeaponLoadout { DefName = "MeleeWeapon_Plasteel_LongSword", Count = 3, Description = "Plasteel Swords" },
                    new WeaponLoadout { DefName = "Gun_AssaultRifle", Count = 2, Description = "Assault Rifles" }
                }
            },

            // PRESET 5: Pistols & Blades
            new LoadoutPreset
            {
                Name = "Pistols & Blades",
                Description = "Classic dueling setup",
                Weapons = new WeaponLoadout[]
                {
                    new WeaponLoadout { DefName = "Gun_Autopistol", Count = 4, Description = "Autopistols" },
                    new WeaponLoadout { DefName = "Gun_Revolver", Count = 3, Description = "Revolvers" },
                    new WeaponLoadout { DefName = "MeleeWeapon_Knife", Count = 2, Description = "Combat Knives" },
                    new WeaponLoadout { DefName = "MeleeWeapon_LongSword", Count = 1, Description = "Longsword" }
                }
            }
        };

        public static LoadoutPreset GetPreset(int index)
        {
            if (index < 0 || index >= AVAILABLE_PRESETS.Length)
                return AVAILABLE_PRESETS[0]; // Default to Mixed Combat
            
            return AVAILABLE_PRESETS[index];
        }

        public static LoadoutPreset GetPreset(string name)
        {
            return AVAILABLE_PRESETS.FirstOrDefault(p => p.Name == name) ?? AVAILABLE_PRESETS[0];
        }

        // MAIN METHOD: Generate balanced weapon assignments for both teams
        public static (string[] redWeapons, string[] blueWeapons) GenerateBalancedLoadouts(LoadoutPreset preset)
        {
            Log.Message($"SolWorld: Generating balanced loadouts using preset '{preset.Name}'");

            // Create weapon pool from preset
            var weaponPool = new List<string>();
            foreach (var weapon in preset.Weapons)
            {
                var weaponDef = DefDatabase<ThingDef>.GetNamedSilentFail(weapon.DefName);
                if (weaponDef != null)
                {
                    // Add this weapon type 'Count' times to the pool
                    for (int i = 0; i < weapon.Count; i++)
                    {
                        weaponPool.Add(weapon.DefName);
                    }
                    Log.Message($"SolWorld: Added {weapon.Count}x {weapon.Description} to weapon pool");
                }
                else
                {
                    Log.Warning($"SolWorld: Weapon def '{weapon.DefName}' not found - skipping");
                }
            }

            // Ensure we have exactly 10 weapons (pad with fallback if needed)
            while (weaponPool.Count < 10)
            {
                weaponPool.Add("Gun_Autopistol"); // Fallback weapon
                Log.Warning("SolWorld: Padding weapon pool with fallback autopistol");
            }

            // Trim to exactly 10 if we have too many
            if (weaponPool.Count > 10)
            {
                weaponPool = weaponPool.Take(10).ToList();
                Log.Warning($"SolWorld: Trimmed weapon pool to exactly 10 weapons");
            }

            // Shuffle the weapon pool for randomness
            var shuffledPool = weaponPool.OrderBy(x => Rand.Value).ToList();

            // Assign identical loadouts to both teams
            var redWeapons = shuffledPool.ToArray();
            var blueWeapons = shuffledPool.ToArray(); // Identical copy

            Log.Message($"SolWorld: Generated balanced loadouts - both teams get identical weapon distribution");
            LogLoadoutDistribution(redWeapons, "Red");
            LogLoadoutDistribution(blueWeapons, "Blue");

            return (redWeapons, blueWeapons);
        }

        private static void LogLoadoutDistribution(string[] weapons, string team)
        {
            var weaponCounts = weapons.GroupBy(w => w).ToDictionary(g => g.Key, g => g.Count());
            Log.Message($"SolWorld: {team} Team Loadout:");
            foreach (var kvp in weaponCounts)
            {
                var weaponDef = DefDatabase<ThingDef>.GetNamedSilentFail(kvp.Key);
                var displayName = weaponDef?.label ?? kvp.Key;
                Log.Message($"  - {kvp.Value}x {displayName}");
            }
        }

        // IMPROVED: Give specific weapon to pawn with better error handling
        public static bool GiveWeaponToPawn(Pawn pawn, string weaponDefName)
        {
            try
            {
                var weaponDef = DefDatabase<ThingDef>.GetNamedSilentFail(weaponDefName);
                if (weaponDef == null)
                {
                    Log.Warning($"SolWorld: Weapon def '{weaponDefName}' not found for {pawn.Name}");
                    return GiveFallbackWeapon(pawn);
                }

                // Remove existing equipment
                if (pawn.equipment?.Primary != null)
                {
                    pawn.equipment.Remove(pawn.equipment.Primary);
                }

                // Create and equip new weapon
                var weapon = ThingMaker.MakeThing(weaponDef);
                if (weapon is ThingWithComps weaponWithComps && pawn.equipment != null)
                {
                    pawn.equipment.AddEquipment(weaponWithComps);
                    Log.Message($"SolWorld: Equipped {pawn.Name} with {weaponDef.label}");
                    return true;
                }
                else
                {
                    Log.Warning($"SolWorld: Failed to create weapon '{weaponDefName}' for {pawn.Name}");
                    return GiveFallbackWeapon(pawn);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"SolWorld: Error equipping weapon '{weaponDefName}' to {pawn.Name}: {ex.Message}");
                return GiveFallbackWeapon(pawn);
            }
        }

        private static bool GiveFallbackWeapon(Pawn pawn)
        {
            Log.Message($"SolWorld: Giving fallback weapon to {pawn.Name}");
            
            var fallbackWeapons = new string[] { "Gun_Autopistol", "Gun_Pistol", "MeleeWeapon_Knife" };
            
            foreach (var fallback in fallbackWeapons)
            {
                var weaponDef = DefDatabase<ThingDef>.GetNamedSilentFail(fallback);
                if (weaponDef != null)
                {
                    try
                    {
                        var weapon = ThingMaker.MakeThing(weaponDef);
                        if (weapon is ThingWithComps weaponWithComps && pawn.equipment != null)
                        {
                            pawn.equipment.AddEquipment(weaponWithComps);
                            Log.Message($"SolWorld: Equipped {pawn.Name} with fallback {weaponDef.label}");
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"SolWorld: Fallback weapon {fallback} also failed: {ex.Message}");
                    }
                }
            }
            
            Log.Error($"SolWorld: All fallback weapons failed for {pawn.Name}");
            return false;
        }
    }

    // DATA STRUCTURES for loadout system
    public class LoadoutPreset
    {
        public string Name;
        public string Description;
        public WeaponLoadout[] Weapons;

        public int TotalWeapons => Weapons?.Sum(w => w.Count) ?? 0;

        public string GetSummary()
        {
            if (Weapons == null || Weapons.Length == 0)
                return "No weapons configured";

            var summary = string.Join(", ", Weapons.Select(w => $"{w.Count}x {w.Description}"));
            return $"{Name}: {summary} (Total: {TotalWeapons})";
        }
    }

    public class WeaponLoadout
    {
        public string DefName;     // RimWorld weapon def name
        public int Count;          // How many fighters get this weapon
        public string Description; // Human-readable name for UI
    }
}