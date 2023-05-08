using CombatOverhaul.BlockModules;
using CombatOverhaul.ProjectileComponents;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BlockChangePatcher;
using CombatOverhaul.Changes;
using UnityEngine;

namespace CombatOverhaul
{
    public class CombatOverhaulMod : ModBase
    {
        private const string HarmonyID = "com.flsoz.ttmods.CombatOverhaul";
        internal static Harmony harmony;
        private static bool Inited = false;

        internal static bool DEBUG = true;

        internal static Logger logger;
        internal static void ConfigureLogger()
        {
            Logger.TargetConfig target = new Logger.TargetConfig
            {
                path = "Combat_Overhaul"
            };
            logger = new Logger("CombatOverhaulMod", target);
            logger.Info("Logger is setup");
        }

        public override void EarlyInit()
        {
            if (!Inited)
            {
                Inited = true;
                ConfigureLogger();

                Logger.TargetConfig target = new Logger.TargetConfig
                {
                    path = "Combat_Overhaul"
                };
                ProjectileParameters.ConfigureLogger(target);
                ModuleShieldParameters.ConfigureLogger(target);

                // setup
                Singleton.instance.gameObject.AddComponent<ManCombatOverhaul>();
                Singleton.Manager<ManCombatOverhaul>.inst.EarlyInit();

                // do changes
                CombatBlockChanges.SetupChanges();
            }
        }

        public override bool HasEarlyInit()
        {
            return true;
        }

        public override void DeInit()
        {
            Singleton.Manager<ManCombatOverhaul>.inst.DeInit();
            harmony.UnpatchAll(HarmonyID);
        }

        public override void Init()
        {
            harmony = new Harmony(HarmonyID);
            harmony.PatchAll();
            Singleton.Manager<ManCombatOverhaul>.inst.Init();
            CombatBlockChanges.RegisterChanges();
        }

        public static Type[] LoadBefore()
        {
            return new Type[] { typeof(BlockChangePatcherMod) };
        }

        // debug failure
        [HarmonyPatch(typeof(ComponentPool), "Recycle")]
        internal static class PatchComponentRecycle
        {
            internal static Exception Finalizer(UnityEngine.Component item, Exception __exception)
            {
                if (__exception != null)
                {
                    Console.WriteLine(__exception);
                    if (item != null)
                    {
                        Console.WriteLine($"Failed for: {item.name}");
                        UnityEngine.Object.Destroy(item);
                    }
                    else
                    {
                        Console.WriteLine("NULL ITEM!!");
                    }
                }
                return null;
            }
        }
    }
}
