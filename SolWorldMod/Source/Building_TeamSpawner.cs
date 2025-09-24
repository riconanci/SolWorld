// solworld/SolWorldMod/Source/Building_TeamSpawner.cs
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace SolWorldMod
{
    public class Building_TeamSpawner : Building
    {
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            
            // Log spawner placement
            var teamType = def.defName == "SolWorld_RedSpawn" ? "Red" : "Blue";
            Log.Message($"SolWorld: {teamType} Team Spawner placed at {Position}");
        }
        
        // INVINCIBILITY IMPLEMENTATION - Using PostApplyDamage to nullify damage after it's applied
        public override void PostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            // Always show impact effects when hit
            if (dinfo.Amount > 0)
            {
                // Create visual/audio feedback
                if (Map != null)
                {
                    // Create sparks effect
                    FleckMaker.ThrowMicroSparks(DrawPos, Map);
                    
                    // Optional: Show "INVULNERABLE" text
                    if (Prefs.DevMode)
                    {
                        MoteMaker.ThrowText(DrawPos, Map, "INVULNERABLE", Color.cyan, 2f);
                    }
                }
            }
            
            // Restore full health after any damage
            if (HitPoints < MaxHitPoints)
            {
                HitPoints = MaxHitPoints;
            }
            
            base.PostApplyDamage(dinfo, 0f); // Pass 0 damage to base
        }
        
        public override string GetInspectString()
        {
            var text = base.GetInspectString();
            
            // Add team identification
            var teamType = def.defName == "SolWorld_RedSpawn" ? "Red" : "Blue";
            text += $"\nTeam: {teamType}";
            text += "\nStatus: INVULNERABLE";
            
            // Show arena setup status if we can find the arena core
            var arenaCore = Map?.listerBuildings.allBuildingsColonist
                .FirstOrDefault(b => b.def?.defName == "SolWorld_ArenaCore") as Thing_ArenaCore;
                
            if (arenaCore != null)
            {
                var arenaComp = Map.GetComponent<MapComponent_SolWorldArena>();
                if (arenaComp != null)
                {
                    arenaComp.ForceRefreshSpawners();
                    
                    if (arenaComp.HasValidSetup)
                    {
                        text += "\nArena: READY";
                        var bounds = arenaComp.GetArenaBounds();
                        if (bounds.HasValue)
                        {
                            text += $"\nBounds: {bounds.Value.Width}x{bounds.Value.Height}";
                        }
                    }
                    else
                    {
                        text += "\nArena: INCOMPLETE SETUP";
                        
                        // Check what's missing
                        var redSpawner = Map.listerBuildings.allBuildingsColonist
                            .FirstOrDefault(b => b.def?.defName == "SolWorld_RedSpawn");
                        var blueSpawner = Map.listerBuildings.allBuildingsColonist
                            .FirstOrDefault(b => b.def?.defName == "SolWorld_BlueSpawn");
                            
                        if (redSpawner == null)
                            text += "\nNeed: Red Team Spawner";
                        if (blueSpawner == null)
                            text += "\nNeed: Blue Team Spawner";
                    }
                }
            }
            else
            {
                text += "\nArena: No Arena Core found";
            }
            
            return text;
        }
    }
}