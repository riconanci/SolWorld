// solworld/SolWorldMod/Source/ArenaUIPatches.cs
using HarmonyLib;
using UnityEngine;
using Verse;
using RimWorld;

namespace SolWorldMod
{
    /// <summary>
    /// Nuclear approach - draw a full-screen overlay that only shows arena content
    /// Instead of trying to hide individual UI elements, we draw our own clean interface
    /// </summary>
    [HarmonyPatch]
    public static class ArenaUIPatches
    {
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
        /// Nuclear option - draw full screen overlay that replaces ALL UI during arena
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Root), "OnGUI")]
        public static void DrawFullArenaOverlay()
        {
            if (!IsArenaActive() || Event.current.type != EventType.Repaint)
                return;

            try
            {
                var arenaComp = Find.CurrentMap?.GetComponent<MapComponent_SolWorldArena>();
                if (arenaComp == null) return;

                // Set GUI depth to draw over EVERYTHING
                GUI.depth = -2000;

                // Draw semi-transparent overlay over entire screen to dim default UI
                var fullScreenRect = new Rect(0, 0, UI.screenWidth, UI.screenHeight);
                GUI.color = new Color(0, 0, 0, 0.0f); // 30% black overlay
                GUI.DrawTexture(fullScreenRect, BaseContent.WhiteTex);
                GUI.color = Color.white;

                // Draw solid black bars to completely hide problem areas
                DrawUIBlockingBars();

                // Draw our clean arena interface
                DrawCleanArenaInterface(arenaComp);

                GUI.depth = 0;
            }
            catch (System.Exception ex)
            {
                Log.Warning($"SolWorld: Full arena overlay failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Draw solid black bars over all the UI areas we want to hide
        /// </summary>
        private static void DrawUIBlockingBars()
        {
            var blackColor = Color.black;

            // Top bar - hide resources, alerts, letters
            var topBar = new Rect(0, 0, UI.screenWidth, 120f);
            GUI.color = blackColor;
            GUI.DrawTexture(topBar, BaseContent.WhiteTex);

            // Bottom bar - hide ALL bottom UI elements
            var bottomBar = new Rect(0, UI.screenHeight - 120f, UI.screenWidth, 80f);
            GUI.color = blackColor;
            GUI.DrawTexture(bottomBar, BaseContent.WhiteTex);

            // Left bar - hide inspect panel area
            var leftBar = new Rect(0, 120f, 200f, UI.screenHeight - 200f);
            GUI.color = blackColor;
            GUI.DrawTexture(leftBar, BaseContent.WhiteTex);

            // Right bar - hide inspect panel area
            var rightBar = new Rect(UI.screenWidth - 200f, 120f, 200f, UI.screenHeight - 200f);
            GUI.color = blackColor;
            GUI.DrawTexture(rightBar, BaseContent.WhiteTex);

            GUI.color = Color.white;
        }

        /// <summary>
        /// Draw our custom clean arena interface
        /// </summary>
        private static void DrawCleanArenaInterface(MapComponent_SolWorldArena arenaComp)
        {
            // Draw minimal time controls in top-left
            DrawMinimalTimeControls();

            // Draw arena status in top-center
            DrawArenaStatus(arenaComp);

            // Draw compact controls in top-right
            DrawCompactControls();

            // Draw the main scoreboard (this should still work)
            ScoreboardUI.DrawScoreboard();

            if (arenaComp.IsPreviewActive)
            {
                ScoreboardUI.DrawPreviewOverlay(arenaComp);
            }
        }

        /// <summary>
        /// Draw minimal time controls
        /// </summary>
        private static void DrawMinimalTimeControls()
        {
            float controlWidth = 180f;
            float controlHeight = 30f;
            var controlRect = new Rect(10f, 10f, controlWidth, controlHeight);

            // Dark background
            GUI.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);
            GUI.DrawTexture(controlRect, BaseContent.WhiteTex);
            GUI.color = Color.white;
            Widgets.DrawBox(controlRect, 1);

            // Buttons
            float buttonSize = 26f;
            float spacing = 2f;
            float x = controlRect.x + 4f;
            float y = controlRect.y + 2f;

            // Pause/Play
            var pauseRect = new Rect(x, y, buttonSize, buttonSize);
            string pauseText = Find.TickManager.Paused ? ">" : "||";
            GUI.color = Find.TickManager.Paused ? Color.green : Color.yellow;
            if (GUI.Button(pauseRect, pauseText))
            {
                Find.TickManager.TogglePaused();
            }
            GUI.color = Color.white;
            x += buttonSize + spacing;

            // Speed controls
            for (int i = 0; i < 4; i++)
            {
                var speedRect = new Rect(x, y, buttonSize, buttonSize);
                var timeSpeed = (TimeSpeed)i;
                
                bool isCurrentSpeed = !Find.TickManager.Paused && Find.TickManager.CurTimeSpeed == timeSpeed;
                GUI.color = isCurrentSpeed ? Color.green : Color.white;
                
                if (GUI.Button(speedRect, (i + 1).ToString()))
                {
                    Find.TickManager.CurTimeSpeed = timeSpeed;
                    if (Find.TickManager.Paused)
                        Find.TickManager.TogglePaused();
                }
                
                x += buttonSize + spacing;
                GUI.color = Color.white;
            }
        }

        /// <summary>
        /// Draw arena status indicator
        /// </summary>
        private static void DrawArenaStatus(MapComponent_SolWorldArena arenaComp)
        {
            float statusWidth = 200f;
            float statusHeight = 30f;
            var statusRect = new Rect((UI.screenWidth - statusWidth) / 2f, 10f, statusWidth, statusHeight);

            // Background
            GUI.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);
            GUI.DrawTexture(statusRect, BaseContent.WhiteTex);
            
            // Determine status
            string stateText = "ARENA ACTIVE";
            Color stateColor = Color.yellow;
            
            switch (arenaComp.CurrentState)
            {
                case ArenaState.Preview:
                    stateText = "PREVIEW MODE";
                    stateColor = Color.cyan;
                    break;
                case ArenaState.Combat:
                    stateText = "COMBAT ACTIVE";
                    stateColor = Color.red;
                    break;
                case ArenaState.Ended:
                    stateText = "ROUND COMPLETE";
                    stateColor = Color.green;
                    break;
                case ArenaState.Resetting:
                    stateText = "RESETTING ARENA";
                    stateColor = Color.yellow;
                    break;
            }

            GUI.color = stateColor;
            Widgets.DrawBox(statusRect, 2);
            
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(statusRect, stateText);
            Text.Anchor = TextAnchor.UpperLeft;
            
            GUI.color = Color.white;
        }

        /// <summary>
        /// Draw compact controls
        /// </summary>
        private static void DrawCompactControls()
        {
            float controlWidth = 120f;
            float controlHeight = 30f;
            var controlRect = new Rect(UI.screenWidth - controlWidth - 10f, 10f, controlWidth, controlHeight);

            // Dark background
            GUI.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);
            GUI.DrawTexture(controlRect, BaseContent.WhiteTex);
            GUI.color = Color.white;
            Widgets.DrawBox(controlRect, 1);

            // Exit arena button
            var exitRect = new Rect(controlRect.x + 5f, controlRect.y + 5f, 50f, 20f);
            GUI.color = Color.red;
            if (GUI.Button(exitRect, "EXIT"))
            {
                var arenaComp = Find.CurrentMap?.GetComponent<MapComponent_SolWorldArena>();
                if (arenaComp != null)
                {
                    arenaComp.StopArena();
                    Messages.Message("Arena stopped by user", MessageTypeDefOf.NeutralEvent);
                }
            }

            // Settings button
            var settingsRect = new Rect(controlRect.x + 60f, controlRect.y + 5f, 50f, 20f);
            GUI.color = Color.cyan;
            if (GUI.Button(settingsRect, "SET"))
            {
                Find.WindowStack.Add(new Dialog_ModSettings(LoadedModManager.GetMod<SolWorldMod>()));
            }

            GUI.color = Color.white;
        }

        /// <summary>
        /// Hide colonist bar (this one should definitely work)
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(ColonistBar), "ColonistBarOnGUI")]
        public static bool HideColonistBar()
        {
            return !IsArenaActive();
        }

        /// <summary>
        /// Hide messages (this should also work)
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Messages), "MessagesDoGUI")]
        public static bool HideMessages()
        {
            return !IsArenaActive();
        }
    }

    /// <summary>
    /// Aggressive patches to completely suppress UI drawing
    /// If methods exist, prevent them from drawing anything
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AggressiveUISupression
    {
        static AggressiveUISupression()
        {
            var harmony = new Harmony("solworld.arena.aggressive");
            
            Log.Message("SolWorld: Applying aggressive UI suppression patches...");
            
            // Use reflection to find and patch ANY method that might draw UI
            SuppressAllUITypes(harmony);
            
            Log.Message("SolWorld: Aggressive suppression complete");
        }

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

        private static void SuppressAllUITypes(Harmony harmony)
        {
            // List of UI types to completely suppress
            var uiTypes = new[]
            {
                typeof(ResourceReadout),
                typeof(AlertsReadout), 
                typeof(LetterStack),
                typeof(MainTabsRoot),
                typeof(Selector),
                typeof(MouseoverReadout),
                typeof(PlaySettings)
            };

            foreach (var uiType in uiTypes)
            {
                try
                {
                    var methods = uiType.GetMethods();
                    foreach (var method in methods)
                    {
                        if (method.Name.Contains("OnGUI") || 
                            method.Name.Contains("Draw") || 
                            method.Name.Contains("Render"))
                        {
                            harmony.Patch(method, prefix: new HarmonyMethod(typeof(AggressiveUISupression), nameof(SuppressUI)));
                            Log.Message($"SolWorld: Suppressed {uiType.Name}.{method.Name}");
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Log.Warning($"SolWorld: Could not suppress {uiType.Name}: {ex.Message}");
                }
            }
        }

        private static bool SuppressUI()
        {
            return !IsArenaActive();
        }
    }
}