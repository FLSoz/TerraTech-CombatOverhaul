using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;

namespace CombatOverhaul.Patches
{
    [HarmonyPatch(typeof(ModuleShieldGenerator), "OnPool")]
    internal static class PatchHealingPassiveDrain
    {
        [HarmonyPostfix]
        internal static void Postfix(ref ModuleShieldGenerator __instance)
        {
            if (__instance.m_Healing)
            {
                // No energy consumption allowed
                __instance.m_EnergyConsumptionPerSec = 0;
            }
        }
    }
}
