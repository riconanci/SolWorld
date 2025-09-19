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

            Log.Message($"SolWorld: Starting arena reset for {bounds.Area} cells");

            // Phase 1: Cleanup - Remove all temporary combat debris
            CleanupArena(map, bounds);

            // Phase 2: Restore - Rebuild to original blueprint state  
            RestoreArena(map, bounds, blueprint);

            Log.Message("SolWorld: Arena reset complete");
        }

        private void CleanupArena(Map map, CellRect bounds)
        {
            var thingsToDestroy = new List<Thing>();

            foreach (var cell in bounds.Cells)
            {
                if (!cell.InBounds(map))
                    continue;

                // Collect all things that need to be removed
                var thingsAtCell = map.thingGrid.ThingsAt(cell).ToList();
                foreach (var thing in thingsAtCell)
                {
                    if (ShouldRemoveDuringCleanup(thing))
                    {
                        thingsToDestroy.Add(thing);
                    }
                }

                // Clear filth - RimWorld 1.5 API
                var filthList = map.thingGrid.ThingsAt(cell).Where(t => t.def.category == ThingCategory.Filth).ToList();
                foreach (var filth in filthList)
                {
                    if (!filth.Destroyed)
                        filth.Destroy();
                }

                // Clear fire
                var fire = map.thingGrid.ThingsAt(cell).FirstOrDefault(t => t.def.defName == "Fire");
                if (fire != null && !fire.Destroyed)
                {
                    fire.Destroy();
                }

                // Clear gas - simplified for RimWorld 1.5
                // Note: Gas system may vary by RimWorld version
                
                // Clear snow
                map.snowGrid.SetDepth(cell, 0f);

                // Clear designations
                var designations = map.designationManager.AllDesignationsAt(cell).ToList();
                foreach (var designation in designations)
                {
                    map.designationManager.RemoveDesignation(designation);
                }
            }
                
                // Clear scorch marks and burn damage from terrain
                var terrain = map.terrainGrid.TerrainAt(cell);
                if (terrain.defName.Contains("Burned") || terrain.defName.Contains("Scorch"))
                {
                    // This will be fixed in the restore phase
                }
            }

            // Destroy collected things
            foreach (var thing in thingsToDestroy)
            {
                if (!thing.Destroyed && thing.Spawned)
                {
                    thing.Destroy();
                }
            }

            Log.Message($"SolWorld: Cleanup removed {thingsToDestroy.Count} temporary objects");
        }

        private bool ShouldRemoveDuringCleanup(Thing thing)
        {
            // Always preserve Arena Core and team spawners
            if (thing.def.defName == "SolWorld_ArenaCore" || 
                thing.def.defName == "SolWorld_RedSpawn" || 
                thing.def.defName == "SolWorld_BlueSpawn")
                return false;

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

            // Keep everything else (buildings, plants, etc.) - they'll be restored if damaged
            return false;
        }

        private void RestoreArena(Map map, CellRect bounds, ArenaBlueprint blueprint)
        {
            int restoredCells = 0;
            int restoredThings = 0;

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
                    map.terrainGrid.SetTerrain(cell, blueprintCell.OriginalTerrain);
                }

                // Restore roof
                var currentRoof = map.roofGrid.RoofAt(cell);
                if (currentRoof != blueprintCell.OriginalRoof)
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

                // Restore things
                RestoreThingsAtCell(map, cell, blueprintCell, ref restoredThings);

                restoredCells++;
            }

            Log.Message($"SolWorld: Restored {restoredCells} cells and {restoredThings} things");
        }

        private void RestoreThingsAtCell(Map map, IntVec3 cell, BlueprintCell blueprintCell, ref int restoredThings)
        {
            var currentThings = map.thingGrid.ThingsAt(cell).Where(t => 
                t.def.defName != "SolWorld_ArenaCore" && 
                t.def.defName != "SolWorld_RedSpawn" && 
                t.def.defName != "SolWorld_BlueSpawn").ToList();

            // Create a set of expected things from blueprint
            var expectedThings = new HashSet<string>();
            foreach (var blueprintThing in blueprintCell.Things)
            {
                var key = $"{blueprintThing.DefName}_{blueprintThing.StuffDefName}_{blueprintThing.Rotation}";
                expectedThings.Add(key);
            }

            // Remove things that shouldn't be there
            foreach (var currentThing in currentThings)
            {
                var currentKey = $"{currentThing.def.defName}_{currentThing.Stuff?.defName}_{currentThing.Rotation}";
                if (!expectedThings.Contains(currentKey))
                {
                    currentThing.Destroy();
                }
            }

            // Add/repair things that should be there
            foreach (var blueprintThing in blueprintCell.Things)
            {
                var existingThing = currentThings.FirstOrDefault(t => 
                    t.def.defName == blueprintThing.DefName &&
                    t.Stuff?.defName == blueprintThing.StuffDefName &&
                    t.Rotation == blueprintThing.Rotation);

                if (existingThing != null)
                {
                    // Repair existing thing to full health
                    if (existingThing.def.useHitPoints && existingThing.HitPoints < existingThing.MaxHitPoints)
                    {
                        existingThing.HitPoints = existingThing.MaxHitPoints;
                        restoredThings++;
                    }
                }
                else
                {
                    // Spawn missing thing
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
            }
        }
    }
}