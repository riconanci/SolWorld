// solworld/SolWorldMod/Source/UIOverlayHook.cs
using HarmonyLib;
using UnityEngine;
using Verse;
using RimWorld;

namespace SolWorldMod
{
    [HarmonyPatch]
    public static class UIOverlayHook
    {
        // Use a more reliable UI hook - patch the main game OnGUI method
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Root), "OnGUI")]
        public static void Root_OnGUI_Postfix()
        {
            try
            {
                // Only draw during play mode with a valid map
                if (Current.ProgramState == ProgramState.Playing && 
                    Find.CurrentMap != null && 
                    Event.current.type == EventType.Repaint)
                {
                    var arenaComp = Find.CurrentMap.GetComponent<MapComponent_SolWorldArena>();
                    if (arenaComp?.IsActive == true)
                    {
                        ScoreboardUI.DrawScoreboard();
                        
                        // Draw large preview overlay during preview phase
                        if (arenaComp.IsPreviewActive)
                        {
                            ScoreboardUI.DrawPreviewOverlay(arenaComp);
                        }
                    }
                }
            }
            catch (System.Exception)
            {
                // Silently ignore UI errors to prevent log spam
            }
        }
    }
    
    // Alternative approach: Hook into Game updates
    [HarmonyPatch]
    public static class AlternativeUIHook
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Game), "UpdatePlay")]
        public static void Game_UpdatePlay_Postfix()
        {
            try
            {
                // This runs every frame during play mode
                if (Find.CurrentMap != null)
                {
                    var arenaComp = Find.CurrentMap.GetComponent<MapComponent_SolWorldArena>();
                    if (arenaComp?.IsActive == true)
                    {
                        // Store reference for UI to access
                        CurrentArenaComponent = arenaComp;
                    }
                }
            }
            catch (System.Exception)
            {
                // Ignore errors silently
            }
        }
        
        public static MapComponent_SolWorldArena CurrentArenaComponent;
    }
    
    // Simple OnGUI method that doesn't require Harmony patches
    public static class SimpleUIDrawer
    {
        private static bool initialized = false;
        
        public static void Initialize()
        {
            if (!initialized)
            {
                // Hook into Unity's OnGUI system directly
                Camera.onPostRender += DrawArenaUI;
                initialized = true;
                Log.Message("SolWorld: Simple UI drawer initialized");
            }
        }
        
        private static void DrawArenaUI(Camera camera)
        {
            try
            {
                if (Current.ProgramState == ProgramState.Playing && 
                    Find.CurrentMap != null &&
                    camera == Find.Camera)
                {
                    var arenaComp = Find.CurrentMap.GetComponent<MapComponent_SolWorldArena>();
                    if (arenaComp?.IsActive == true)
                    {
                        // Use immediate mode GUI
                        GUI.depth = -1000; // Draw on top
                        
                        ScoreboardUI.DrawScoreboard();
                        
                        if (arenaComp.IsPreviewActive)
                        {
                            ScoreboardUI.DrawPreviewOverlay(arenaComp);
                        }
                    }
                }
            }
            catch (System.Exception)
            {
                // Ignore errors silently
            }
        }
        
        public static void Cleanup()
        {
            if (initialized)
            {
                Camera.onPostRender -= DrawArenaUI;
                initialized = false;
            }
        }
    }
}