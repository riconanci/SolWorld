// solworld/SolWorldMod/Source/PlaceWorker_TeamSpawners.cs
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace SolWorldMod
{
    public class PlaceWorker_RedSpawner : PlaceWorker
    {
        public override void DrawGhost(ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol, Thing thing = null)
        {
            base.DrawGhost(def, center, rot, ghostCol, thing);
            SpawnerPlaceWorkerHelper.DrawSpawnerGhost(Find.CurrentMap, center, TeamColor.Red);
        }
        
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 center, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            var baseResult = base.AllowsPlacing(checkingDef, center, rot, map, thingToIgnore, thing);
            if (!baseResult.Accepted)
                return baseResult;
                
            // Check if there's already a red spawner on this map
            var existingRedSpawner = map.listerBuildings.allBuildingsColonist
                .FirstOrDefault(b => b.def.defName == "SolWorld_RedSpawn" && b != thingToIgnore);
                
            if (existingRedSpawner != null)
            {
                return new AcceptanceReport("Only one Red Team Spawner allowed per map");
            }
            
            return AcceptanceReport.WasAccepted;
        }
    }
    
    public class PlaceWorker_BlueSpawner : PlaceWorker
    {
        public override void DrawGhost(ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol, Thing thing = null)
        {
            base.DrawGhost(def, center, rot, ghostCol, thing);
            SpawnerPlaceWorkerHelper.DrawSpawnerGhost(Find.CurrentMap, center, TeamColor.Blue);
        }
        
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 center, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            var baseResult = base.AllowsPlacing(checkingDef, center, rot, map, thingToIgnore, thing);
            if (!baseResult.Accepted)
                return baseResult;
                
            // Check if there's already a blue spawner on this map
            var existingBlueSpawner = map.listerBuildings.allBuildingsColonist
                .FirstOrDefault(b => b.def.defName == "SolWorld_BlueSpawn" && b != thingToIgnore);
                
            if (existingBlueSpawner != null)
            {
                return new AcceptanceReport("Only one Blue Team Spawner allowed per map");
            }
            
            return AcceptanceReport.WasAccepted;
        }
    }
    
    // FIXED: No GUI calls in PlaceWorker
    public static class SpawnerPlaceWorkerHelper
    {
        public static void DrawSpawnerGhost(Map map, IntVec3 spawnerPos, TeamColor team)
        {
            if (map == null) return;
            
            // Find existing buildings
            Thing_ArenaCore arenaCore = null;
            Building otherSpawner = null;
            
            var allBuildings = map.listerBuildings.allBuildingsColonist;
            foreach (var building in allBuildings)
            {
                if (building.def?.defName == "SolWorld_ArenaCore" && building is Thing_ArenaCore core)
                {
                    arenaCore = core;
                }
                else if (team == TeamColor.Red && building.def?.defName == "SolWorld_BlueSpawn")
                {
                    otherSpawner = building;
                }
                else if (team == TeamColor.Blue && building.def?.defName == "SolWorld_RedSpawn")
                {
                    otherSpawner = building;
                }
            }
            
            // Draw spawn radius around this spawner position
            var spawnerColor = team == TeamColor.Red ? Color.red : Color.blue;
            GenDraw.DrawRadiusRing(spawnerPos, 5f, spawnerColor);
            
            // If we have all three positions, show the potential arena
            if (arenaCore != null && otherSpawner != null)
            {
                var bounds = CalculatePotentialBounds(arenaCore.Position, 
                    team == TeamColor.Red ? spawnerPos : otherSpawner.Position,
                    team == TeamColor.Blue ? spawnerPos : otherSpawner.Position, 
                    map);
                
                if (bounds.HasValue)
                {
                    // Draw arena bounds in magenta to distinguish from other visualizations
                    GenDraw.DrawFieldEdges(bounds.Value.Cells.ToList(), Color.magenta);
                    
                    // Draw connection lines
                    var coreCenter = arenaCore.Position.ToVector3Shifted();
                    var spawnerCenter = spawnerPos.ToVector3Shifted();
                    var otherCenter = otherSpawner.Position.ToVector3Shifted();
                    
                    GenDraw.DrawLineBetween(coreCenter, spawnerCenter, team == TeamColor.Red ? SimpleColor.Red : SimpleColor.Blue);
                    GenDraw.DrawLineBetween(coreCenter, otherCenter, team == TeamColor.Red ? SimpleColor.Blue : SimpleColor.Red);
                    
                    // Highlight the arena core and other spawner
                    GenDraw.DrawRadiusRing(arenaCore.Position, 3f, Color.green);
                    GenDraw.DrawRadiusRing(otherSpawner.Position, 3f, team == TeamColor.Red ? Color.blue : Color.red);
                }
            }
            else
            {
                // REMOVED: All GUI.Label calls that caused the error
                // Just show visual indicators without text
                if (arenaCore == null)
                {
                    // Draw red ring to indicate missing arena core
                    GenDraw.DrawRadiusRing(spawnerPos, 8f, Color.red);
                }
                
                if (otherSpawner == null)
                {
                    // Draw yellow ring to indicate missing other spawner
                    GenDraw.DrawRadiusRing(spawnerPos, 6f, Color.yellow);
                }
            }
        }
        
        private static CellRect? CalculatePotentialBounds(IntVec3 corePos, IntVec3 redPos, IntVec3 bluePos, Map map)
        {
            // Same logic as ArenaBounds.CalculateBounds
            var minX = System.Math.Min(System.Math.Min(corePos.x, redPos.x), bluePos.x);
            var maxX = System.Math.Max(System.Math.Max(corePos.x, redPos.x), bluePos.x);
            var minZ = System.Math.Min(System.Math.Min(corePos.z, redPos.z), bluePos.z);
            var maxZ = System.Math.Max(System.Math.Max(corePos.z, redPos.z), bluePos.z);

            const int padding = 10;
            minX -= padding;
            maxX += padding;
            minZ -= padding;
            maxZ += padding;

            const int minSize = 20;
            var width = maxX - minX + 1;
            var height = maxZ - minZ + 1;

            if (width < minSize)
            {
                var expansion = (minSize - width) / 2;
                minX -= expansion;
                maxX += expansion;
            }

            if (height < minSize)
            {
                var expansion = (minSize - height) / 2;
                minZ -= expansion;
                maxZ += expansion;
            }

            minX = System.Math.Max(0, minX);
            maxX = System.Math.Min(map.Size.x - 1, maxX);
            minZ = System.Math.Max(0, minZ);
            maxZ = System.Math.Min(map.Size.z - 1, maxZ);

            return new CellRect(minX, minZ, maxX - minX + 1, maxZ - minZ + 1);
        }
    }
}