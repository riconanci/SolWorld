// solworld/SolWorldMod/Source/ArenaBlueprint.cs
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace SolWorldMod
{
    public class ArenaBlueprint
    {
        private Dictionary<IntVec3, BlueprintCell> blueprint;
        private bool isInitialized = false;

        public bool IsInitialized => isInitialized;

        public void InitializeBlueprint(Map map, CellRect bounds)
        {
            if (isInitialized)
                return;

            Log.Message($"SolWorld: Initializing arena blueprint for bounds {bounds}");
            
            blueprint = new Dictionary<IntVec3, BlueprintCell>();

            foreach (var cell in bounds.Cells)
            {
                if (!cell.InBounds(map))
                    continue;

                var blueprintCell = new BlueprintCell
                {
                    Position = cell,
                    OriginalTerrain = map.terrainGrid.TerrainAt(cell),
                    OriginalRoof = map.roofGrid.RoofAt(cell),
                    OriginalSnowDepth = map.snowGrid.GetDepth(cell),
                    Things = new List<BlueprintThing>()
                };

                // Capture all non-temporary things at this cell
                var thingsAtCell = map.thingGrid.ThingsAt(cell).ToList();
                foreach (var thing in thingsAtCell)
                {
                    // Skip temporary combat items and pawns
                    if (ShouldIncludeInBlueprint(thing))
                    {
                        blueprintCell.Things.Add(new BlueprintThing
                        {
                            DefName = thing.def.defName,
                            StuffDefName = thing.Stuff?.defName,
                            Position = thing.Position,
                            Rotation = thing.Rotation,
                            MaxHitPoints = thing.MaxHitPoints,
                            CurrentHitPoints = thing.HitPoints,
                            Quality = thing.TryGetQuality(out var quality) ? (QualityCategory?)quality : null
                        });
                    }
                }

                blueprint[cell] = blueprintCell;
            }

            isInitialized = true;
            Log.Message($"SolWorld: Blueprint captured {blueprint.Count} cells with {blueprint.Values.Sum(c => c.Things.Count)} things");
        }

        private bool ShouldIncludeInBlueprint(Thing thing)
        {
            // Don't include pawns
            if (thing is Pawn)
                return false;

            // Don't include temporary combat debris
            if (thing is Corpse || thing.def.category == ThingCategory.Filth)
                return false;

            // Don't include dropped items/weapons
            if (thing.def.category == ThingCategory.Item && thing.Position.GetFirstItem(thing.Map) == thing)
                return false;

            // Don't include projectiles, gas, fire
            if (thing.def.category == ThingCategory.Projectile || 
                thing.def.category == ThingCategory.Gas ||
                thing.def.defName == "Fire")
                return false;

            // Don't include temporary designations or jobs
            if (thing.def.category == ThingCategory.Ethereal)
                return false;

            // Include buildings, plants, and other permanent structures
            return thing.def.category == ThingCategory.Building || 
                   thing.def.category == ThingCategory.Plant ||
                   thing.def.building != null;  // Fixed: use 'building' instead of 'buildingProperties'
        }

        public BlueprintCell GetBlueprintCell(IntVec3 position)
        {
            blueprint?.TryGetValue(position, out var cell);
            return cell;
        }

        public IEnumerable<BlueprintCell> GetAllCells()
        {
            return blueprint?.Values ?? Enumerable.Empty<BlueprintCell>();
        }

        public void ClearBlueprint()
        {
            blueprint?.Clear();
            isInitialized = false;
        }
    }

    public class BlueprintCell
    {
        public IntVec3 Position;
        public TerrainDef OriginalTerrain;
        public RoofDef OriginalRoof;
        public float OriginalSnowDepth;
        public List<BlueprintThing> Things;
    }

    public class BlueprintThing
    {
        public string DefName;
        public string StuffDefName; // Material/stuff if applicable
        public IntVec3 Position;
        public Rot4 Rotation;
        public int MaxHitPoints;
        public int CurrentHitPoints;
        public QualityCategory? Quality;

        public ThingDef GetThingDef()
        {
            return DefDatabase<ThingDef>.GetNamedSilentFail(DefName);
        }

        public ThingDef GetStuffDef()
        {
            return string.IsNullOrEmpty(StuffDefName) ? null : DefDatabase<ThingDef>.GetNamedSilentFail(StuffDefName);
        }

        public Thing CreateThing()
        {
            var thingDef = GetThingDef();
            if (thingDef == null)
            {
                Log.Warning($"SolWorld: Could not find ThingDef for {DefName}");
                return null;
            }

            var stuffDef = GetStuffDef();
            var thing = ThingMaker.MakeThing(thingDef, stuffDef);
            
            if (thing == null)
                return null;

            // Restore rotation
            thing.Rotation = Rotation;

            // Restore hit points
            if (thing.def.useHitPoints && MaxHitPoints > 0)
            {
                // Direct hit points assignment for RimWorld 1.5
                if (thing.HitPoints != CurrentHitPoints && CurrentHitPoints > 0)
                {
                    thing.HitPoints = CurrentHitPoints;
                }
            }

            // Restore quality if applicable
            if (Quality.HasValue && thing.TryGetComp<CompQuality>() is CompQuality qualityComp)
            {
                qualityComp.SetQuality(Quality.Value, ArtGenerationContext.Colony);
            }

            return thing;
        }
    }
}