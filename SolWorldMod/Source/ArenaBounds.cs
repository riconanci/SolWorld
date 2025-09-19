// solworld/SolWorldMod/Source/ArenaBounds.cs
using System;
using UnityEngine;
using Verse;

namespace SolWorldMod
{
    public class ArenaBounds
    {
        public CellRect? CalculateBounds(Thing_ArenaCore arenaCore, Building redSpawner, Building blueSpawner)
        {
            if (arenaCore == null || redSpawner == null || blueSpawner == null)
                return null;

            // Get all three positions
            var corePos = arenaCore.Position;
            var redPos = redSpawner.Position;
            var bluePos = blueSpawner.Position;

            // Calculate bounding rectangle that encompasses all three buildings
            var minX = Math.Min(Math.Min(corePos.x, redPos.x), bluePos.x);
            var maxX = Math.Max(Math.Max(corePos.x, redPos.x), bluePos.x);
            var minZ = Math.Min(Math.Min(corePos.z, redPos.z), bluePos.z);
            var maxZ = Math.Max(Math.Max(corePos.z, redPos.z), bluePos.z);

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
            var map = arenaCore.Map;
            minX = Math.Max(0, minX);
            maxX = Math.Min(map.Size.x - 1, maxX);
            minZ = Math.Max(0, minZ);
            maxZ = Math.Min(map.Size.z - 1, maxZ);

            return new CellRect(minX, minZ, maxX - minX + 1, maxZ - minZ + 1);
        }

        public void DrawBounds(CellRect bounds, Color color)
        {
            // Draw the perimeter of the arena bounds
            GenDraw.DrawFieldEdges(bounds.Cells.ToList(), color);
            
            // Draw corner markers for better visibility
            var corners = new[]
            {
                new IntVec3(bounds.minX, 0, bounds.minZ),
                new IntVec3(bounds.maxX, 0, bounds.minZ), 
                new IntVec3(bounds.maxX, 0, bounds.maxZ),
                new IntVec3(bounds.minX, 0, bounds.maxZ)
            };

            foreach (var corner in corners)
            {
                GenDraw.DrawRadiusRing(corner, 1f, color);
            }
        }

        public bool IsWithinBounds(CellRect bounds, IntVec3 position)
        {
            return bounds.Contains(position);
        }

        public float GetArenaArea(CellRect bounds)
        {
            return bounds.Width * bounds.Height;
        }

        public IntVec3 GetArenaCenter(CellRect bounds)
        {
            return new IntVec3(
                bounds.minX + bounds.Width / 2,
                0,
                bounds.minZ + bounds.Height / 2
            );
        }

        // Helper for finding valid positions within arena bounds
        public IntVec3? FindRandomValidPositionInBounds(Map map, CellRect bounds, int maxAttempts = 20)
        {
            for (int i = 0; i < maxAttempts; i++)
            {
                var randomPos = new IntVec3(
                    Rand.Range(bounds.minX, bounds.maxX + 1),
                    0,
                    Rand.Range(bounds.minZ, bounds.maxZ + 1)
                );

                if (randomPos.InBounds(map) && randomPos.Standable(map) && !randomPos.Fogged(map))
                {
                    return randomPos;
                }
            }
            return null;
        }
    }
}