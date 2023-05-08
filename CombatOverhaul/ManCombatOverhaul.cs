using CombatOverhaul.BlockModules;
using CombatOverhaul.ProjectileComponents;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CombatOverhaul
{
    public class ManCombatOverhaul : Singleton.Manager<ManCombatOverhaul>
    {
        private Dictionary<FactionSubTypes, float> corpArmorReduction = new Dictionary<FactionSubTypes, float>();
        private Dictionary<FactionSubTypes, float> corpPiercingCoefficient = new Dictionary<FactionSubTypes, float>();
        private Dictionary<FactionSubTypes, float> corpShieldCoefficient = new Dictionary<FactionSubTypes, float>();

        public void EarlyInit()
        {

        }

        public void Init()
        {
            this.corpArmorReduction.Add(FactionSubTypes.GSO, 0.2f);
            this.corpArmorReduction.Add(FactionSubTypes.VEN, 0.1f);
            this.corpArmorReduction.Add(FactionSubTypes.GC, 0.35f);
            this.corpArmorReduction.Add(FactionSubTypes.BF, 0.25f);
            this.corpArmorReduction.Add(FactionSubTypes.EXP, 0.4f);
            this.corpArmorReduction.Add(FactionSubTypes.HE, 0.5f);

            this.corpPiercingCoefficient.Add(FactionSubTypes.GSO, 0.5f);
            this.corpPiercingCoefficient.Add(FactionSubTypes.VEN, 0.2f);
            this.corpPiercingCoefficient.Add(FactionSubTypes.GC, 0.4f);
            this.corpPiercingCoefficient.Add(FactionSubTypes.BF, 0.6f);
            this.corpPiercingCoefficient.Add(FactionSubTypes.EXP, 0.8f);
            this.corpPiercingCoefficient.Add(FactionSubTypes.HE, 1.0f);

            this.corpShieldCoefficient.Add(FactionSubTypes.GSO, 1.0f);
            this.corpShieldCoefficient.Add(FactionSubTypes.VEN, 1.0f);
            this.corpShieldCoefficient.Add(FactionSubTypes.GC, 1.0f);
            this.corpShieldCoefficient.Add(FactionSubTypes.BF, 1.0f);
            this.corpShieldCoefficient.Add(FactionSubTypes.EXP, 2.0f);
            this.corpShieldCoefficient.Add(FactionSubTypes.HE, 1.5f);
        }

        public void DeInit()
        {
            this.corpArmorReduction.Clear();
            this.corpPiercingCoefficient.Clear();
            this.corpShieldCoefficient.Clear();
        }

        public float GetArmorReduction(TankBlock block)
        {
            FactionSubTypes corporation = Singleton.Manager<ManSpawn>.inst.GetCorporation(block.BlockType);
            return this.corpArmorReduction.GetOrDefault(corporation, 0.2f);
        }

        internal float GetPiercingCoefficient(ProjectileParameters projectile)
        {
            return this.corpPiercingCoefficient.GetOrDefault(projectile.ProjectileCorp, 0.5f);
        }

        internal float GetShieldCoefficient(ModuleShieldParameters shield)
        {
            return this.corpShieldCoefficient.GetOrDefault(shield.ShieldCorp, 1.0f);
        }
    }
}
