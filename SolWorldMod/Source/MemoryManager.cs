// solworld/SolWorldMod/Source/MemoryManager.cs
// ULTRA-SIMPLE VERSION - No complex RimWorld internals
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SolWorldMod
{
    /// <summary>
    /// Ultra-simple memory management - no complex RimWorld internals
    /// </summary>
    public static class MemoryManager
    {
        // Performance tracking
        private static long lastGCMemory = 0;
        private static int consecutiveLowFPSCount = 0;
        private static int lastReportTick = 0;
        
        // Memory thresholds
        private static readonly long MEMORY_WARNING_THRESHOLD = 1024L * 1024L * 1024L; // 1GB
        private static readonly long MEMORY_CRITICAL_THRESHOLD = 2048L * 1024L * 1024L; // 2GB
        
        /// <summary>
        /// Simple arena cleanup after each round
        /// </summary>
        public static void PerformArenaCleanup(Map map, List<Pawn> allArenaPawns = null)
        {
            Log.Message("SolWorld: Starting simple memory cleanup...");
            
            try
            {
                // Clean up arena pawns if provided
                if (allArenaPawns?.Any() == true)
                {
                    CleanupArenaPawns(allArenaPawns);
                }
                
                // Basic Unity cleanup
                Resources.UnloadUnusedAssets();
                
                // Force garbage collection if memory is high
                var currentMemory = GC.GetTotalMemory(false);
                if (currentMemory > MEMORY_WARNING_THRESHOLD)
                {
                    GC.Collect();
                    var memoryAfter = GC.GetTotalMemory(true);
                    var freed = (currentMemory - memoryAfter) / 1024f / 1024f;
                    
                    if (freed > 0)
                    {
                        Log.Message($"SolWorld: GC freed {freed:F1} MB");
                    }
                }
                
                Log.Message("SolWorld: Simple memory cleanup completed");
            }
            catch (Exception ex)
            {
                Log.Error($"SolWorld: Memory cleanup failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Clean up specific arena pawns
        /// </summary>
        private static void CleanupArenaPawns(List<Pawn> pawns)
        {
            foreach (var pawn in pawns.Where(p => p != null))
            {
                try
                {
                    // Basic job cleanup
                    if (pawn.jobs != null)
                    {
                        pawn.jobs.EndCurrentJob(Verse.AI.JobCondition.InterruptForced);
                        pawn.jobs.ClearQueuedJobs();
                    }
                    
                    // Basic pathfinding cleanup
                    if (pawn.pather != null)
                    {
                        pawn.pather.StopDead();
                    }
                    
                    // Basic stance cleanup
                    if (pawn.stances?.curStance != null)
                    {
                        pawn.stances.SetStance(new Stance_Mobile());
                    }
                    
                }
                catch (Exception ex)
                {
                    Log.Warning($"SolWorld: Failed to cleanup pawn {pawn?.Name}: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Monitor performance and memory usage
        /// </summary>
        public static void MonitorPerformance()
        {
            var currentTick = Find.TickManager.TicksGame;
            
            // Only check every 5 seconds to avoid overhead
            if (currentTick - lastReportTick < 300) return;
            lastReportTick = currentTick;
            
            try
            {
                // Check memory usage
                var currentMemory = GC.GetTotalMemory(false);
                CheckMemoryUsage(currentMemory);
                
                // Check frame rate
                var currentFPS = Mathf.RoundToInt(1f / Time.deltaTime);
                CheckFrameRate(currentFPS);
                
                // Auto-cleanup if performance is bad
                if (ShouldPerformEmergencyCleanup(currentMemory, currentFPS))
                {
                    Log.Warning("SolWorld: Performance degraded, performing emergency cleanup...");
                    PerformEmergencyCleanup();
                }
                
            }
            catch (Exception ex)
            {
                Log.Warning($"SolWorld: Performance monitoring failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Check if memory usage is concerning
        /// </summary>
        private static void CheckMemoryUsage(long currentMemory)
        {
            if (currentMemory > MEMORY_CRITICAL_THRESHOLD)
            {
                Log.Error($"SolWorld: CRITICAL memory usage: {currentMemory / 1024 / 1024:F1} MB");
            }
            else if (currentMemory > MEMORY_WARNING_THRESHOLD)
            {
                Log.Warning($"SolWorld: High memory usage: {currentMemory / 1024 / 1024:F1} MB");
            }
        }
        
        /// <summary>
        /// Check frame rate and track performance issues
        /// </summary>
        private static void CheckFrameRate(int currentFPS)
        {
            if (currentFPS < 30)
            {
                consecutiveLowFPSCount++;
                Log.Message($"SolWorld: Low FPS detected: {currentFPS} ({consecutiveLowFPSCount} consecutive)");
            }
            else
            {
                consecutiveLowFPSCount = 0;
            }
        }
        
        /// <summary>
        /// Determine if emergency cleanup is needed
        /// </summary>
        private static bool ShouldPerformEmergencyCleanup(long memory, int fps)
        {
            return memory > MEMORY_CRITICAL_THRESHOLD || consecutiveLowFPSCount >= 5;
        }
        
        /// <summary>
        /// Emergency cleanup for severe performance issues
        /// </summary>
        private static void PerformEmergencyCleanup()
        {
            try
            {
                // Force full garbage collection
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                
                // Unload Unity resources
                Resources.UnloadUnusedAssets();
                
                // Reset counters
                consecutiveLowFPSCount = 0;
                lastGCMemory = GC.GetTotalMemory(false);
                
                Log.Message("SolWorld: Emergency cleanup completed");
                
            }
            catch (Exception ex)
            {
                Log.Error($"SolWorld: Emergency cleanup failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Get simple performance report
        /// </summary>
        public static string GetPerformanceReport()
        {
            var currentMemory = GC.GetTotalMemory(false);
            var currentFPS = Mathf.RoundToInt(1f / Time.deltaTime);
            
            return $"Memory: {currentMemory / 1024 / 1024:F1} MB, FPS: {currentFPS}, Low FPS Count: {consecutiveLowFPSCount}";
        }
    }
}