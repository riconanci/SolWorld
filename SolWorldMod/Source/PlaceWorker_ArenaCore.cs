// solworld/SolWorldMod/Source/PlaceWorker_ArenaCore.cs
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace SolWorldMod
{
    public class PlaceWorker_ArenaCore : PlaceWorker
    {
        public override void DrawGhost(ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol, Thing thing = null)
        {
            base.DrawGhost(def, center, rot, ghostCol, thing);
            
            // Draw potential arena bounds during placement - only visual elements, no GUI
            DrawPotentialArenaBounds(Find.CurrentMap, center);
        }
        
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 center, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            // Basic placement validation
            var baseResult = base.AllowsPlacing(checkingDef, center, rot, map, thingToIgnore, thing);
            if (!baseResult.Accepted)
                return baseResult;
                
            // Check if there is already an arena core on this map
            var existingCore = map.listerBuildings.allBuildingsColonist
                .FirstOrDefault(b => b.def.defName == "SolWorld_ArenaCore");
                
            if (existingCore != null)
            {
                return new AcceptanceReport("Only one Arena Core allowed per map");
            }
            
            return AcceptanceReport.WasAccepted;
        }
        
        private void DrawPotentialArenaBounds(Map map, IntVec3 corePos)
        {
            if (map == null) return;
            
            // Find existing spawners on the map
            Building redSpawner = null;
            Building blueSpawner = null;
            
            var allBuildings = map.listerBuildings.allBuildingsColonist;
            foreach (var building in allBuildings)
            {
                if (building.def?.defName == "SolWorld_RedSpawn")
                    redSpawner = building;
                else if (building.def?.defName == "SolWorld_BlueSpawn")
                    blueSpawner = building;
            }
            
            if (redSpawner != null && blueSpawner != null)
            {
                // Calculate potential bounds using the same logic as ArenaBounds
                var bounds = CalculatePotentialBounds(corePos, redSpawner.Position, blueSpawner.Position, map);
                
                if (bounds.HasValue)
                {
                    // Draw the potential arena perimeter in cyan (different from selection color)
                    GenDraw.DrawFieldEdges(bounds.Value.Cells.ToList(), Color.cyan);
                    
                    // Draw corner markers
                    var corners = new IntVec3[]
                    {
                        new IntVec3(bounds.Value.minX, 0, bounds.Value.minZ),
                        new IntVec3(bounds.Value.maxX, 0, bounds.Value.minZ),
                        new IntVec3(bounds.Value.maxX, 0, bounds.Value.maxZ),
                        new IntVec3(bounds.Value.minX, 0, bounds.Value.maxZ)
                    };
                    
                    foreach (var corner in corners)
                    {
                        GenDraw.DrawRadiusRing(corner, 1.5f, Color.cyan);
                    }
                    
                    // Draw center point
                    var center = new IntVec3(
                        bounds.Value.minX + bounds.Value.Width / 2,
                        0,
                        bounds.Value.minZ + bounds.Value.Height / 2
                    );
                    GenDraw.DrawRadiusRing(center, 2f, Color.cyan);
                    
                    // Draw connection lines to spawners
                    var coreCenterVec = corePos.ToVector3Shifted();
                    var redCenterVec = redSpawner.Position.ToVector3Shifted();
                    var blueCenterVec = blueSpawner.Position.ToVector3Shifted();
                    
                    GenDraw.DrawLineBetween(coreCenterVec, redCenterVec, SimpleColor.Red);
                    GenDraw.DrawLineBetween(coreCenterVec, blueCenterVec, SimpleColor.Blue);
                    
                    // Highlight the spawners
                    GenDraw.DrawRadiusRing(redSpawner.Position, 3f, Color.red);
                    GenDraw.DrawRadiusRing(blueSpawner.Position, 3f, Color.blue);
                }
            }
            else
            {
                // Show a default radius if spawners are not placed yet - NO GUI CALLS
                GenDraw.DrawRadiusRing(corePos, 15f, Color.gray);
                
                // NO TEXT RENDERING IN PLACEWORKER - causes GUI context errors
                // Text messages will be shown in the building inspect string instead
            }
        }
        
        private CellRect? CalculatePotentialBounds(IntVec3 corePos, IntVec3 redPos, IntVec3 bluePos, Map map)
        {
            // Same logic as ArenaBounds.CalculateBounds but with potential core position
            var minX = System.Math.Min(System.Math.Min(corePos.x, redPos.x), bluePos.x);
            var maxX = System.Math.Max(System.Math.Max(corePos.x, redPos.x), bluePos.x);
            var minZ = System.Math.Min(System.Math.Min(corePos.z, redPos.z), bluePos.z);
            var maxZ = System.Math.Max(System.Math.Max(corePos.z, redPos.z), bluePos.z);

            // Add padding around the perimeter for combat space
            const int padding = 10;
            minX -= padding;
            maxX += padding;
            minZ -= padding;
            maxZ += padding;

            // Ensure minimum arena size
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

            // Clamp to map bounds
            minX = System.Math.Max(0, minX);
            maxX = System.Math.Min(map.Size.x - 1, maxX);
            minZ = System.Math.Max(0, minZ);
            maxZ = System.Math.Min(map.Size.z - 1, maxZ);

            return new CellRect(minX, minZ, maxX - minX + 1, maxZ - minZ + 1);
        }
    }
}