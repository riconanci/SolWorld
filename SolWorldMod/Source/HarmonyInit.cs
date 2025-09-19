// solworld/SolWorldMod/Source/HarmonyInit.cs
using HarmonyLib;
using Verse;

namespace SolWorldMod
{
    [StaticConstructorOnStartup]
    public static class HarmonyInit
    {
        static HarmonyInit()
        {
            var harmony = new Harmony("solworld.arena.mod");
            harmony.PatchAll();
            Log.Message("SolWorld: Harmony patches applied successfully");
        }
    }
}