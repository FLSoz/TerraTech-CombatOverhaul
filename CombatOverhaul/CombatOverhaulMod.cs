using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CombatOverhaul
{
    public class CombatOverhaulMod : ModBase
    {
        private const string HarmonyID = "com.flsoz.ttmods.CombatOverhaul";
        internal static Harmony harmony;
        public override void DeInit()
        {
            harmony.UnpatchAll(HarmonyID);
        }

        public override void Init()
        {
            harmony = new Harmony(HarmonyID);
            harmony.PatchAll();
        }
    }
}
