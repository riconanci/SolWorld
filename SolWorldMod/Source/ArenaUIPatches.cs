// solworld/SolWorldMod/Source/ArenaUIPatches.cs
using HarmonyLib;
using UnityEngine;
using Verse;
using RimWorld;

namespace SolWorldMod
{
    /// <summary>
    /// Safe Harmony patches for clean arena viewing experience
    /// Only patches methods that definitely exist in RimWorld 1.6
    /// </summary>
    [HarmonyPatch]
    public static class ArenaUIPatches
    {
        /// <summary>
        /// Check if arena is currently active on the current map
        /// </summary>
        private static bool IsArenaActive()
        {
            try
            {
                if (Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null)
                    return false;
                
                var arenaComp = Find.CurrentMap.GetComponent<MapComponent_SolWorldArena>();
                return arenaComp?.IsActive == true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Hide the colonist bar during arena combat - this definitely exists
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(ColonistBar), "ColonistBarOnGUI")]
        public static bool ColonistBar_Prefix()
        {
            return !IsArenaActive();
        }

        /// <summary>
        /// Hide notification messages during arena combat - this definitely exists
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Messages), "MessagesDoGUI")]
        public static bool Messages_MessagesDoGUI_Prefix()
        {
            return !IsArenaActive();
        }

        /// <summary>
        /// Add arena status indicator next to time controls - this definitely exists
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GlobalControlsUtility), "DoTimespeedControls")]
        public static void GlobalControls_DoTimespeedControls_Postfix(float leftX, float width, ref float curBaseY)
        {
            if (!IsArenaActive())
                return;
            
            try
            {
                var arenaComp = Find.CurrentMap?.GetComponent<MapComponent_SolWorldArena>();
                if (arenaComp == null) return;

                var rect = new Rect(leftX + width + 10f, curBaseY - 26f, 200f, 24f);
                
                GUI.color = Color.yellow;
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                
                string stateText = "ARENA ACTIVE";
                switch (arenaComp.CurrentState)
                {
                    case ArenaState.Preview:
                        stateText = "🎬 PREVIEW";
                        GUI.color = Color.cyan;
                        break;
                    case ArenaState.Combat:
                        stateText = "⚔️ COMBAT";
                        GUI.color = Color.red;
                        break;
                    case ArenaState.Ended:
                        stateText = "🏆 ROUND END";
                        GUI.color = Color.green;
                        break;
                    case ArenaState.Resetting:
                        stateText = "🔄 RESETTING";
                        GUI.color = Color.yellow;
                        break;
                }
                
                Widgets.Label(rect, stateText);
                
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
            }
            catch
            {
                // Ignore any drawing errors
            }
        }

        /// <summary>
        /// Ensure our arena UI draws after main UI elements
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapInterface), "MapInterfaceOnGUI_AfterMainTabs")]
        public static void MapInterface_AfterMainTabs_Postfix()
        {
            if (!IsArenaActive())
                return;
            
            try
            {
                var arenaComp = Find.CurrentMap?.GetComponent<MapComponent_SolWorldArena>();
                if (arenaComp == null) return;

                // Draw our arena UI on top
                GUI.depth = -1000;
                
                ScoreboardUI.DrawScoreboard();
                
                if (arenaComp.IsPreviewActive)
                {
                    ScoreboardUI.DrawPreviewOverlay(arenaComp);
                }
                
                GUI.depth = 0;
            }
            catch
            {
                // Ignore drawing errors
            }
        }
    }

    /// <summary>
    /// Manual UI patches that are applied conditionally to avoid errors
    /// These patches are applied manually after checking if the target methods exist
    /// </summary>
    [StaticConstructorOnStartup]
    public static class ConditionalUIPatches
    {
        static ConditionalUIPatches()
        {
            var harmony = new Harmony("solworld.arena.conditionalui");
            
            // Try to patch ResourceReadout if it exists
            TryPatchResourceReadout(harmony);
            
            // Try to patch AlertsReadout if it exists  
            TryPatchAlertsReadout(harmony);
            
            // Try to patch LetterStack if it exists
            TryPatchLetterStack(harmony);
            
            // Try to patch PlaySettings if it exists
            TryPatchPlaySettings(harmony);
            
            // Try to patch MainTabsRoot if it exists
            TryPatchMainTabs(harmony);
            
            // Try to patch Selector if it exists
            TryPatchSelector(harmony);
            
            Log.Message("SolWorld: Conditional UI patches applied successfully");
        }

        private static void TryPatchResourceReadout(Harmony harmony)
        {
            try
            {
                var targetType = typeof(ResourceReadout);
                var targetMethod = targetType.GetMethod("ResourceReadoutOnGUI");
                
                if (targetMethod != null)
                {
                    var prefixMethod = typeof(ConditionalUIPatches).GetMethod(nameof(ResourceReadout_Prefix), 
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                    
                    harmony.Patch(targetMethod, new HarmonyMethod(prefixMethod));
                    Log.Message("SolWorld: Successfully patched ResourceReadout");
                }
                else
                {
                    Log.Message("SolWorld: ResourceReadout method not found - skipping patch");
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"SolWorld: Could not patch ResourceReadout: {ex.Message}");
            }
        }

        private static void TryPatchAlertsReadout(Harmony harmony)
        {
            try
            {
                var targetType = typeof(AlertsReadout);
                var targetMethod = targetType.GetMethod("AlertsReadoutOnGUI");
                
                if (targetMethod != null)
                {
                    var prefixMethod = typeof(ConditionalUIPatches).GetMethod(nameof(AlertsReadout_Prefix), 
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                    
                    harmony.Patch(targetMethod, new HarmonyMethod(prefixMethod));
                    Log.Message("SolWorld: Successfully patched AlertsReadout");
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"SolWorld: Could not patch AlertsReadout: {ex.Message}");
            }
        }

        private static void TryPatchLetterStack(Harmony harmony)
        {
            try
            {
                var targetType = typeof(LetterStack);
                var targetMethod = targetType.GetMethod("LettersOnGUI");
                
                if (targetMethod != null)
                {
                    var prefixMethod = typeof(ConditionalUIPatches).GetMethod(nameof(LetterStack_Prefix), 
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                    
                    harmony.Patch(targetMethod, new HarmonyMethod(prefixMethod));
                    Log.Message("SolWorld: Successfully patched LetterStack");
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"SolWorld: Could not patch LetterStack: {ex.Message}");
            }
        }

        private static void TryPatchPlaySettings(Harmony harmony)
        {
            try
            {
                var targetType = typeof(PlaySettings);
                var targetMethod = targetType.GetMethod("DoPlaySettingsGlobalControls");
                
                if (targetMethod != null)
                {
                    var prefixMethod = typeof(ConditionalUIPatches).GetMethod(nameof(PlaySettings_Prefix), 
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                    
                    harmony.Patch(targetMethod, new HarmonyMethod(prefixMethod));
                    Log.Message("SolWorld: Successfully patched PlaySettings");
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"SolWorld: Could not patch PlaySettings: {ex.Message}");
            }
        }

        private static void TryPatchMainTabs(Harmony harmony)
        {
            try
            {
                var targetType = typeof(MainTabsRoot);
                var targetMethod = targetType.GetMethod("MainTabsOnGUI");
                
                if (targetMethod != null)
                {
                    var prefixMethod = typeof(ConditionalUIPatches).GetMethod(nameof(MainTabsRoot_Prefix), 
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                    
                    harmony.Patch(targetMethod, new HarmonyMethod(prefixMethod));
                    Log.Message("SolWorld: Successfully patched MainTabsRoot");
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"SolWorld: Could not patch MainTabsRoot: {ex.Message}");
            }
        }

        private static void TryPatchSelector(Harmony harmony)
        {
            try
            {
                var targetType = typeof(Selector);
                var targetMethod = targetType.GetMethod("SelectionDrawerOnGUI");
                
                if (targetMethod != null)
                {
                    var prefixMethod = typeof(ConditionalUIPatches).GetMethod(nameof(Selector_Prefix), 
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                    
                    harmony.Patch(targetMethod, new HarmonyMethod(prefixMethod));
                    Log.Message("SolWorld: Successfully patched Selector");
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"SolWorld: Could not patch Selector: {ex.Message}");
            }
        }

        // Patch methods - these will only be called if the corresponding patches succeed
        private static bool IsArenaActive()
        {
            try
            {
                if (Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null)
                    return false;
                
                var arenaComp = Find.CurrentMap.GetComponent<MapComponent_SolWorldArena>();
                return arenaComp?.IsActive == true;
            }
            catch
            {
                return false;
            }
        }

        private static bool ResourceReadout_Prefix()
        {
            return !IsArenaActive();
        }

        private static bool AlertsReadout_Prefix()
        {
            return !IsArenaActive();
        }

        private static bool LetterStack_Prefix()
        {
            return !IsArenaActive();
        }

        private static bool PlaySettings_Prefix()
        {
            return !IsArenaActive();
        }

        private static bool MainTabsRoot_Prefix()
        {
            return !IsArenaActive();
        }

        private static bool Selector_Prefix()
        {
            return !IsArenaActive();
        }
    }
}