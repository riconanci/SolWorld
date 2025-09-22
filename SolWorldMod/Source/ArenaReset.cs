// solworld/SolWorldMod/Source/ArenaReset.cs
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace SolWorldMod
{
    public class ArenaReset
    {
        public void ResetArena(Map map, CellRect bounds, ArenaBlueprint blueprint)
        {
            if (!blueprint.IsInitialized)
            {
                Log.Warning("SolWorld: Cannot reset arena - blueprint not initialized");
                return;
            }

            Log.Message("SolWorld: Starting arena reset for " + bounds.Area + " cells");

            // CRITICAL: Verify spawners exist BEFORE starting reset
            var spawnerCheck = VerifySpawnersBeforeReset(map);
            if (!spawnerCheck.success)
            {
                Log.Error("SolWorld: ABORTING RESET - " + spawnerCheck.error);
                return;
            }

            // Phase 1: Cleanup - Remove all temporary combat debris (WITH BULLETPROOF PROTECTION)
            CleanupArena(map, bounds);

            // Phase 2: Restore - Rebuild to original blueprint state  
            RestoreArena(map, bounds, blueprint);

            // CRITICAL: Verify spawners still exist AFTER reset
            var postResetCheck = VerifySpawnersAfterReset(map);
            if (!postResetCheck.success)
            {
                Log.Error("SolWorld: RESET DAMAGED SPAWNERS - " + postResetCheck.error);
                // Try emergency recovery
                AttemptSpawnerRecovery(map);
            }

            Log.Message("SolWorld: Arena reset complete");
        }

        // NEW: Pre-reset spawner verification
        private (bool success, string error) VerifySpawnersBeforeReset(Map map)
        {
            var redSpawner = FindSpawnerOnMap(map, "Red");
            var blueSpawner = FindSpawnerOnMap(map, "Blue");
            var arenaCore = FindArenaCoreOnMap(map);

            if (redSpawner == null)
                return (false, "Red spawner not found before reset");
            if (blueSpawner == null)
                return (false, "Blue spawner not found before reset");
            if (arenaCore == null)
                return (false, "Arena core not found before reset");

            Log.Message($"SolWorld: Pre-reset verification - Red: {redSpawner.Position}, Blue: {blueSpawner.Position}, Core: {arenaCore.Position}");
            return (true, "All spawners verified");
        }

        // NEW: Post-reset spawner verification
        private (bool success, string error) VerifySpawnersAfterReset(Map map)
        {
            var redSpawner = FindSpawnerOnMap(map, "Red");
            var blueSpawner = FindSpawnerOnMap(map, "Blue");
            var arenaCore = FindArenaCoreOnMap(map);

            if (redSpawner == null)
                return (false, "Red spawner DESTROYED during reset!");
            if (blueSpawner == null)
                return (false, "Blue spawner DESTROYED during reset!");
            if (arenaCore == null)
                return (false, "Arena core DESTROYED during reset!");

            Log.Message($"SolWorld: Post-reset verification - Red: {redSpawner.Position}, Blue: {blueSpawner.Position}, Core: {arenaCore.Position}");
            return (true, "All spawners survived reset");
        }

        // NEW: Emergency spawner recovery
        private void AttemptSpawnerRecovery(Map map)
        {
            Log.Message("SolWorld: Attempting emergency spawner recovery...");
            
            // Try to find any remaining SolWorld buildings
            var allBuildings = map.listerBuildings.allBuildingsColonist.ToList();
            var solWorldBuildings = allBuildings.Where(b => 
                b?.def?.defName != null && b.def.defName.Contains("SolWorld")).ToList();

            Log.Message($"SolWorld: Found {solWorldBuildings.Count} SolWorld buildings during recovery:");
            foreach (var building in solWorldBuildings)
            {
                Log.Message($"  - {building.def.defName} at {building.Position}");
            }

            // TODO: In a future version, we could try to respawn missing spawners here
            // For now, just log the issue for manual intervention
        }

        // NEW: Robust spawner finding methods
        private Building FindSpawnerOnMap(Map map, string teamColor)
        {
            var expectedDefName = $"SolWorld_{teamColor}Spawn";
            
            try
            {
                var allBuildings = map.listerBuildings.allBuildingsColonist.ToList();
                
                // Try exact match first
                var exactMatch = allBuildings.FirstOrDefault(b => 
                    b?.def?.defName == expectedDefName);
                if (exactMatch != null)
                {
                    Log.Message($"SolWorld: Found {teamColor} spawner (exact): {expectedDefName}");
                    return exactMatch;
                }

                // Try partial match as fallback
                var partialMatch = allBuildings.FirstOrDefault(b => 
                    b?.def?.defName != null && 
                    b.def.defName.Contains("SolWorld") && 
                    b.def.defName.Contains(teamColor) && 
                    b.def.defName.Contains("Spawn"));
                if (partialMatch != null)
                {
                    Log.Warning($"SolWorld: Found {teamColor} spawner (partial): {partialMatch.def.defName}");
                    return partialMatch;
                }

                Log.Warning($"SolWorld: No {teamColor} spawner found on map!");
                return null;
            }
            catch (System.Exception ex)
            {
                Log.Error($"SolWorld: Error finding {teamColor} spawner: {ex.Message}");
                return null;
            }
        }

        private Building FindArenaCoreOnMap(Map map)
        {
            try
            {
                var allBuildings = map.listerBuildings.allBuildingsColonist.ToList();
                
                var arenaCore = allBuildings.FirstOrDefault(b => 
                    b?.def?.defName != null && 
                    (b.def.defName == "SolWorld_ArenaCore" || 
                     (b.def.defName.Contains("SolWorld") && b.def.defName.Contains("Arena"))));

                if (arenaCore != null)
                {
                    Log.Message($"SolWorld: Found arena core: {arenaCore.def.defName}");
                }
                else
                {
                    Log.Warning("SolWorld: No arena core found on map!");
                }

                return arenaCore;
            }
            catch (System.Exception ex)
            {
                Log.Error($"SolWorld: Error finding arena core: {ex.Message}");
                return null;
            }
        }

        private void CleanupArena(Map map, CellRect bounds)
        {
            var thingsToDestroy = new List<Thing>();

            Log.Message("SolWorld: Starting cleanup phase with BULLETPROOF spawner protection...");

            foreach (var cell in bounds.Cells)
            {
                if (!cell.InBounds(map))
                    continue;

                // Collect all things that need to be removed - can use LINQ in RimWorld 1.6
                var thingsAtCell = map.thingGrid.ThingsAt(cell).ToList();
                
                foreach (var thing in thingsAtCell)
                {
                    if (ShouldRemoveDuringCleanup(thing))
                    {
                        thingsToDestroy.Add(thing);
                    }
                    else if (IsSolWorldBuilding(thing))
                    {
                        Log.Message($"SolWorld: PROTECTING {thing.def.defName} at {thing.Position} from cleanup");
                    }
                }

                // Clear filth - use LINQ
                var filthList = map.thingGrid.ThingsAt(cell)
                    .Where(t => t.def.category == ThingCategory.Filth)
                    .ToList();
                foreach (var filth in filthList)
                {
                    if (!filth.Destroyed)
                        filth.Destroy();
                }

                // Clear fire - use LINQ
                var fireList = map.thingGrid.ThingsAt(cell)
                    .Where(t => t.def.defName == "Fire")
                    .ToList();
                foreach (var fire in fireList)
                {
                    if (!fire.Destroyed)
                        fire.Destroy();
                }

                // Clear snow
                map.snowGrid.SetDepth(cell, 0f);

                // Clear designations - use LINQ
                var designations = map.designationManager.AllDesignationsAt(cell).ToList();
                foreach (var designation in designations)
                {
                    map.designationManager.RemoveDesignation(designation);
                }
            }

            // ENHANCED: Double-check we're not destroying any SolWorld buildings
            var solWorldItemsToDestroy = thingsToDestroy.Where(t => IsSolWorldBuilding(t)).ToList();
            if (solWorldItemsToDestroy.Any())
            {
                Log.Error($"SolWorld: CRITICAL - About to destroy {solWorldItemsToDestroy.Count} SolWorld buildings!");
                foreach (var item in solWorldItemsToDestroy)
                {
                    Log.Error($"  - Would destroy: {item.def.defName} at {item.Position}");
                    thingsToDestroy.Remove(item); // Remove from destruction list
                }
            }

            // Destroy collected things (now guaranteed not to include SolWorld buildings)
            foreach (var thing in thingsToDestroy)
            {
                if (!thing.Destroyed && thing.Spawned)
                {
                    try
                    {
                        thing.Destroy();
                    }
                    catch (System.Exception ex)
                    {
                        Log.Warning($"SolWorld: Failed to destroy {thing.def.defName}: {ex.Message}");
                    }
                }
            }

            Log.Message("SolWorld: Cleanup removed " + thingsToDestroy.Count + " temporary objects (SolWorld buildings protected)");
        }

        // BULLETPROOF: Multiple layers of protection for SolWorld buildings
        private bool ShouldRemoveDuringCleanup(Thing thing)
        {
            // LAYER 1: Direct SolWorld building protection
            if (IsSolWorldBuilding(thing))
            {
                return false; // NEVER remove SolWorld buildings
            }

            // LAYER 2: Category-based protection for critical buildings
            if (thing.def.category == ThingCategory.Building && thing.def.building?.isInert == false)
            {
                // Don't remove active/functional buildings during cleanup
                return false;
            }

            // LAYER 3: Specific type removal (only remove known temporary items)
            
            // Remove corpses
            if (thing is Corpse)
                return true;

            // Remove dropped weapons and items
            if (thing.def.category == ThingCategory.Item)
                return true;

            // Remove filth (blood, dirt, etc.)
            if (thing.def.category == ThingCategory.Filth)
                return true;

            // Remove fire and gas
            if (thing.def.defName == "Fire" || thing.def.category == ThingCategory.Gas)
                return true;

            // Remove projectiles and other ethereal things
            if (thing.def.category == ThingCategory.Projectile || 
                thing.def.category == ThingCategory.Ethereal)
                return true;

            // Remove shell casings and other debris
            if (thing.def.defName.Contains("Bullet") || thing.def.defName.Contains("Shell"))
                return true;

            // CONSERVATIVE: Keep everything else (buildings, plants, etc.)
            return false;
        }

        // BULLETPROOF: Multiple ways to identify SolWorld buildings
        private bool IsSolWorldBuilding(Thing thing)
        {
            if (thing?.def?.defName == null)
                return false;

            var defName = thing.def.defName;

            // METHOD 1: Exact defName matches
            if (defName == "SolWorld_ArenaCore" || 
                defName == "SolWorld_RedSpawn" || 
                defName == "SolWorld_BlueSpawn")
            {
                return true;
            }

            // METHOD 2: Pattern matching for any SolWorld building
            if (defName.StartsWith("SolWorld") || defName.Contains("SolWorld"))
            {
                return true;
            }

            // METHOD 3: Check if it's a known arena building type
            if (thing is Building && 
                (defName.Contains("Arena") || defName.Contains("Spawn")) && 
                defName.Contains("Sol"))
            {
                return true;
            }

            // METHOD 4: Check ThingClass for SolWorld types
            if (thing.GetType().Namespace?.Contains("SolWorldMod") == true)
            {
                return true;
            }

            return false;
        }

        private void RestoreArena(Map map, CellRect bounds, ArenaBlueprint blueprint)
        {
            int restoredCells = 0;
            int restoredThings = 0;

            Log.Message("SolWorld: Starting restoration phase...");

            foreach (var cell in bounds.Cells)
            {
                if (!cell.InBounds(map))
                    continue;

                var blueprintCell = blueprint.GetBlueprintCell(cell);
                if (blueprintCell == null)
                    continue;

                // Restore terrain
                var currentTerrain = map.terrainGrid.TerrainAt(cell);
                if (currentTerrain != blueprintCell.OriginalTerrain)
                {
                    try
                    {
                        map.terrainGrid.SetTerrain(cell, blueprintCell.OriginalTerrain);
                    }
                    catch (System.Exception ex)
                    {
                        Log.Warning($"SolWorld: Failed to restore terrain at {cell}: {ex.Message}");
                    }
                }

                // Restore roof
                var currentRoof = map.roofGrid.RoofAt(cell);
                if (currentRoof != blueprintCell.OriginalRoof)
                {
                    try
                    {
                        if (blueprintCell.OriginalRoof != null)
                        {
                            map.roofGrid.SetRoof(cell, blueprintCell.OriginalRoof);
                        }
                        else if (currentRoof != null)
                        {
                            map.roofGrid.SetRoof(cell, null);
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Log.Warning($"SolWorld: Failed to restore roof at {cell}: {ex.Message}");
                    }
                }

                // Restore things (but NEVER touch SolWorld buildings)
                RestoreThingsAtCell(map, cell, blueprintCell, ref restoredThings);

                restoredCells++;
            }

            Log.Message("SolWorld: Restored " + restoredCells + " cells and " + restoredThings + " things");
        }

        private void RestoreThingsAtCell(Map map, IntVec3 cell, BlueprintCell blueprintCell, ref int restoredThings)
        {
            // Get current things - use LINQ in RimWorld 1.6
            var currentThings = map.thingGrid.ThingsAt(cell).ToList();
            
            // CRITICAL: Separate SolWorld buildings from other things
            var solWorldBuildings = currentThings.Where(t => IsSolWorldBuilding(t)).ToList();
            var otherThings = currentThings.Where(t => !IsSolWorldBuilding(t)).ToList();

            // NEVER TOUCH SolWorld buildings during restoration
            foreach (var solWorldBuilding in solWorldBuildings)
            {
                Log.Message($"SolWorld: Preserving {solWorldBuilding.def.defName} during restoration at {cell}");
            }

            // Create a set of expected things from blueprint (excluding SolWorld buildings)
            var expectedThings = new HashSet<string>();
            foreach (var blueprintThing in blueprintCell.Things)
            {
                // Skip SolWorld buildings in blueprint - they should already exist
                if (blueprintThing.DefName.Contains("SolWorld"))
                {
                    continue;
                }
                
                var key = blueprintThing.DefName + "_" + blueprintThing.StuffDefName + "_" + blueprintThing.Rotation.ToString();
                expectedThings.Add(key);
            }

            // Remove non-SolWorld things that shouldn't be there
            foreach (var currentThing in otherThings)
            {
                var currentKey = currentThing.def.defName + "_" + (currentThing.Stuff?.defName ?? "") + "_" + currentThing.Rotation.ToString();
                if (!expectedThings.Contains(currentKey))
                {
                    try
                    {
                        currentThing.Destroy();
                    }
                    catch (System.Exception ex)
                    {
                        Log.Warning($"SolWorld: Failed to remove {currentThing.def.defName}: {ex.Message}");
                    }
                }
            }

            // Add/repair things that should be there (excluding SolWorld buildings)
            foreach (var blueprintThing in blueprintCell.Things)
            {
                // SKIP SolWorld buildings - they should already exist and be preserved
                if (blueprintThing.DefName.Contains("SolWorld"))
                {
                    continue;
                }

                var existingThing = otherThings.FirstOrDefault(t => 
                    t.def.defName == blueprintThing.DefName &&
                    (t.Stuff?.defName ?? "") == (blueprintThing.StuffDefName ?? "") &&
                    t.Rotation == blueprintThing.Rotation);

                if (existingThing != null)
                {
                    // Repair existing thing to full health
                    if (existingThing.def.useHitPoints && existingThing.HitPoints < existingThing.MaxHitPoints)
                    {
                        try
                        {
                            existingThing.HitPoints = existingThing.MaxHitPoints;
                            restoredThings++;
                        }
                        catch (System.Exception ex)
                        {
                            Log.Warning($"SolWorld: Failed to repair {existingThing.def.defName}: {ex.Message}");
                        }
                    }
                }
                else
                {
                    // Spawn missing thing
                    try
                    {
                        var newThing = blueprintThing.CreateThing();
                        if (newThing != null)
                        {
                            // Check if position is valid for spawning
                            if (cell.Standable(map) || newThing.def.category == ThingCategory.Building)
                            {
                                var spawnedThing = GenSpawn.Spawn(newThing, cell, map, blueprintThing.Rotation);
                                if (spawnedThing != null)
                                {
                                    restoredThings++;
                                }
                            }
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Log.Warning($"SolWorld: Failed to spawn {blueprintThing.DefName}: {ex.Message}");
                    }
                }
            }
        }

        // NEW: Debug method to list all SolWorld buildings
        public void DebugListSolWorldBuildings(Map map)
        {
            Log.Message("SolWorld: === DEBUG - ALL SOLWORLD BUILDINGS ON MAP ===");
            
            var allBuildings = map.listerBuildings.allBuildingsColonist.ToList();
            var solWorldBuildings = allBuildings.Where(b => IsSolWorldBuilding(b)).ToList();
            
            Log.Message($"SolWorld: Found {solWorldBuildings.Count} SolWorld buildings:");
            foreach (var building in solWorldBuildings)
            {
                Log.Message($"  - {building.def.defName} at {building.Position} (ID: {building.thingIDNumber})");
            }
            
            if (solWorldBuildings.Count == 0)
            {
                Log.Error("SolWorld: NO SOLWORLD BUILDINGS FOUND! This explains the missing buttons!");
            }
        }
    }
}