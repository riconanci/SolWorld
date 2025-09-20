using System.Collections.Generic;
using Verse;
using RimWorld;

namespace SolWorldMod
{
    public class Thing_ArenaCore : Building
    {
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            Log.Message("SolWorld: Arena Core spawned");
        }
        
        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            Log.Message("SolWorld: Arena Core despawned");
            base.DeSpawn(mode);
        }
        
        public bool IsOperational 
        { 
            get 
            {
                return Spawned && !Destroyed;
            }
        }
        
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }
            
            if (Faction == Faction.OfPlayer)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Test Button",
                    defaultDesc = "Test description",
                    icon = BaseContent.BadTex,
                    action = () => {
                        Log.Message("Test button clicked");
                    }
                };
            }
        }
        
        public override string GetInspectString()
        {
            var text = base.GetInspectString();
            if (!string.IsNullOrEmpty(text))
                text += "\n";
            text += "Status: " + (IsOperational ? "Operational" : "Not Operational");
            return text;
        }
    }
}