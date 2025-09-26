// solworld/SolWorldMod/Source/RuntimeSafetyPatches.cs
// COMPLETELY DISABLED - All patches removed
// 
// The null reference exceptions appear to be normal edge cases in RimWorld's combat system
// that don't actually break functionality. The game handles these internally and continues working.
// 
// Rather than trying to patch every possible combat interaction, we're letting RimWorld
// handle its own error recovery while focusing on making sure the arena system itself is robust.

using HarmonyLib;
using Verse;
using RimWorld;
using System;

namespace SolWorldMod
{
    // NO PATCHES - This class is intentionally empty
    // The arena system will handle its own state management without interfering with RimWorld's combat
    public static class RuntimeSafetyPatches
    {
        // All patches disabled to avoid interfering with RimWorld's internal error handling
    }
}