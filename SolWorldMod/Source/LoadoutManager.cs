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
        // CUSTOM LOADOUT PRESETS - Feel free to modify these!
        public static readonly LoadoutPreset[] AVAILABLE_PRESETS = new LoadoutPreset[]
        {
            // PRESET 1: Assault Squad
            new LoadoutPreset
            {
                Name = "Assault Squad",
                Description = "Heavy assault rifles with melee backup",
                Weapons = new WeaponLoadout[]
                {
                    new WeaponLoadout { DefName = "Gun_AssaultRifle", Count = 6, Description = "Assault Rifles" },
                    new WeaponLoadout { DefName = "Gun_Autopistol", Count = 2, Description = "Autopistols" },
                    new WeaponLoadout { DefName = "MeleeWeapon_LongSword", Count = 2, Description = "Longswords" }
                }
            },

            // PRESET 2: Berserker Horde
            new LoadoutPreset
            {
                Name = "Berserker Horde",
                Description = "Pure melee chaos with minimal ranged",
                Weapons = new WeaponLoadout[]
                {
                    new WeaponLoadout { DefName = "MeleeWeapon_LongSword", Count = 5, Description = "Longswords" },
                    new WeaponLoadout { DefName = "MeleeWeapon_Knife", Count = 4, Description = "Combat Knives" },
                    new WeaponLoadout { DefName = "Gun_Autopistol", Count = 1, Description = "Emergency Pistol" }
                }
            },

            // PRESET 3: Gunslinger Duel
            new LoadoutPreset
            {
                Name = "Gunslinger Duel",
                Description = "Classic pistol showdown",
                Weapons = new WeaponLoadout[]
                {
                    new WeaponLoadout { DefName = "Gun_Revolver", Count = 5, Description = "Revolvers" },
                    new WeaponLoadout { DefName = "Gun_Autopistol", Count = 4, Description = "Autopistols" },
                    new WeaponLoadout { DefName = "MeleeWeapon_Knife", Count = 1, Description = "Last Resort Knife" }
                }
            },

            // PRESET 4: Tactical Strike
            new LoadoutPreset
            {
                Name = "Tactical Strike",
                Description = "Mixed tactical combat setup",
                Weapons = new WeaponLoadout[]
                {
                    new WeaponLoadout { DefName = "Gun_AssaultRifle", Count = 4, Description = "Assault Rifles" },
                    new WeaponLoadout { DefName = "Gun_Autopistol", Count = 3, Description = "Autopistols" },
                    new WeaponLoadout { DefName = "MeleeWeapon_LongSword", Count = 3, Description = "Longswords" }
                }
            },

            // PRESET 5: Shotgun Rush
            new LoadoutPreset
            {
                Name = "Shotgun Rush",
                Description = "Close-range devastation",
                Weapons = new WeaponLoadout[]
                {
                    new WeaponLoadout { DefName = "Gun_PumpShotgun", Count = 4, Description = "Pump Shotguns" },
                    new WeaponLoadout { DefName = "Gun_ChainShotgun", Count = 2, Description = "Chain Shotguns" },
                    new WeaponLoadout { DefName = "Gun_Revolver", Count = 2, Description = "Revolvers" },
                    new WeaponLoadout { DefName = "MeleeWeapon_LongSword", Count = 2, Description = "Longswords" }
                }
            },

            // PRESET 6: Medieval Warriors (NEW - Pure melee)
            new LoadoutPreset
            {
                Name = "Medieval Warriors",
                Description = "Pure blade combat - no ranged weapons",
                Weapons = new WeaponLoadout[]
                {
                    new WeaponLoadout { DefName = "MeleeWeapon_LongSword", Count = 6, Description = "Longswords" },
                    new WeaponLoadout { DefName = "MeleeWeapon_Knife", Count = 4, Description = "Combat Knives" }
                }
            },

            // PRESET 7: Grenadier Squad (NEW - One grenadier per team)
            new LoadoutPreset
            {
                Name = "Grenadier Squad",
                Description = "Explosive warfare with one grenadier per team",
                Weapons = new WeaponLoadout[]
                {
                    new WeaponLoadout { DefName = "Gun_AssaultRifle", Count = 2, Description = "Assault Rifles" },
                    new WeaponLoadout { DefName = "Gun_Autopistol", Count = 3, Description = "Autopistols" },
                    new WeaponLoadout { DefName = "MeleeWeapon_LongSword", Count = 3, Description = "Longswords" },
                    new WeaponLoadout { DefName = "Gun_IncendiaryLauncher", Count = 2, Description = "Grenade Launcher" }
                }
            },

            // PRESET 8: Energy Weapons
            new LoadoutPreset
            {
                Name = "Energy Weapons",
                Description = "High-tech energy combat",
                Weapons = new WeaponLoadout[]
                {
                    new WeaponLoadout { DefName = "Gun_ChargeRifle", Count = 4, Description = "Charge Rifles" },
                    new WeaponLoadout { DefName = "Gun_Autopistol", Count = 2, Description = "Backup Pistols" },
                    new WeaponLoadout { DefName = "MeleeWeapon_Knife", Count = 4, Description = "Combat Knives" }
                }
            }
        };

        // NEW: Get random preset for variety each round
        public static LoadoutPreset GetRandomPreset()
        {
            var randomIndex = Rand.Range(0, AVAILABLE_PRESETS.Length);
            var selectedPreset = AVAILABLE_PRESETS[randomIndex];
            Log.Message($"SolWorld: Randomly selected loadout preset '{selectedPreset.Name}' for this round");
            return selectedPreset;
        }

        // NEW: Get random preset but exclude recently used ones
        public static LoadoutPreset GetRandomPreset(string[] excludeNames)
        {
            var availablePresets = AVAILABLE_PRESETS.Where(p => !excludeNames.Contains(p.Name)).ToArray();
            
            if (availablePresets.Length == 0)
            {
                // If we've excluded everything, reset and pick from all
                availablePresets = AVAILABLE_PRESETS;
            }
            
            var randomIndex = Rand.Range(0, availablePresets.Length);
            var selectedPreset = availablePresets[randomIndex];
            Log.Message($"SolWorld: Randomly selected loadout preset '{selectedPreset.Name}' (excluded: {string.Join(", ", excludeNames)})");
            return selectedPreset;
        }

        public static LoadoutPreset GetPreset(int index)
        {
            if (index < 0 || index >= AVAILABLE_PRESETS.Length)
                return AVAILABLE_PRESETS[0]; // Default to first preset
            
            return AVAILABLE_PRESETS[index];
        }

        public static LoadoutPreset GetPreset(string name)
        {
            return AVAILABLE_PRESETS.FirstOrDefault(p => p.Name == name) ?? AVAILABLE_PRESETS[0];
        }

        // UPDATED: Generate balanced loadouts with random preset selection
        public static (string[] redWeapons, string[] blueWeapons, LoadoutPreset usedPreset) GenerateBalancedLoadouts(bool useRandomPreset = true, LoadoutPreset specificPreset = null)
        {
            LoadoutPreset preset;
            
            if (useRandomPreset)
            {
                preset = GetRandomPreset();
            }
            else if (specificPreset != null)
            {
                preset = specificPreset;
            }
            else
            {
                preset = AVAILABLE_PRESETS[0]; // Fallback
            }
            
            Log.Message($"SolWorld: Generating balanced loadouts using preset '{preset.Name}' - {preset.Description}");

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

            return (redWeapons, blueWeapons, preset); // Return the preset that was used
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