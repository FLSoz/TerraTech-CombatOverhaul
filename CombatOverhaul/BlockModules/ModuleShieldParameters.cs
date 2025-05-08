using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using HarmonyLib;
using System.Reflection;
using static CompoundExpression;

namespace CombatOverhaul.BlockModules
{
    [RequireComponent(typeof(ModuleShieldGenerator))]
    internal class ModuleShieldParameters : MonoBehaviour
    {
        internal static Logger logger;
        internal static void ConfigureLogger(Logger.TargetConfig target)
        {
            logger = new Logger("ShieldOverhaul", target);
            logger.Info("Logger is setup");
        }

        public FactionSubTypes ShieldCorp = FactionSubTypes.NULL;
        internal ModuleShieldGenerator GeneratorModule;
        private bool isShield = false;
        private ManDamage.DamageableType oldDamageableType;
        internal BubbleShield Bubble;
        internal Damageable Damageable
        {
            get { return this.Bubble.Damageable; }
        }

        private static readonly FieldInfo m_OrigMaxHealth = AccessTools.Field(typeof(Damageable), "m_OrigMaxHealth");
        private static readonly FieldInfo m_MaxHealthFixed = AccessTools.Field(typeof(Damageable), "m_MaxHealthFixed");
        private float originalShieldHealth;
        private void SetShieldHealth(float newHealth)
        {
            this.Damageable.SetMaxHealth(newHealth);
            // for whatever reason setting health to this force sets it to max health
            this.Damageable.InitHealth(this.Damageable.MaxHealth);
            logger.Debug($"Initializing shield health to {newHealth}. Shield health currently at: {this.Damageable.Health}");
        }

        public float ShieldRegen { get; private set; }
        public float MaxShieldHealth { get; private set; }

        [SerializeField]
        private float BubbleCoefficient = 1.0f;

        private float CalculateBubbleCoefficient()
        {
            BubbleShield bubble = base.GetComponentsInChildren<BubbleShield>(true).FirstOrDefault<BubbleShield>();
            Collider[] colliders = bubble.RepulsorBulletTrigger.transform.GetComponentsInChildren<Collider>();
            float coefficient = 1.0f;

            logger.Debug($"Calculating bubble coefficient for {base.name}");
            // we will assume generator scale is accurate
            ModuleShieldGenerator generator = base.GetComponent<ModuleShieldGenerator>();
            foreach (Collider collider in colliders)
            {
                float localX = collider.transform.lossyScale.x / bubble.transform.lossyScale.x;
                float localY = collider.transform.lossyScale.y / bubble.transform.lossyScale.y;
                float localZ = collider.transform.lossyScale.z / bubble.transform.lossyScale.z;
                float approximateLocalRadius = Mathf.Pow(localX * localY * localZ, 1 / 3);
                float radius = approximateLocalRadius * generator.m_Radius;

                float scale = radius * radius;
                logger.Trace($"Approximate scale {scale} (generator radius {generator.m_Radius}, local radius scale {approximateLocalRadius})");

                if (collider is SphereCollider sphereCollider) {
                    float area = 4 * sphereCollider.radius * sphereCollider.radius * Mathf.PI;
                    logger.Trace($"Found sphere collider with area {area}");
                    coefficient += area * scale;
                }
                else if (collider is BoxCollider boxCollider)
                {
                    Vector3 size = boxCollider.size;
                    float a = size.x;
                    float b = size.y;
                    float c = size.z;
                    float area = 2 * (a * b + a * b + b * c);
                    logger.Trace($"Found box collider with area {area}");
                    coefficient += area * scale;
                }
                else if (collider is CapsuleCollider capsuleCollider) {
                    float area =  4 * capsuleCollider.radius * capsuleCollider.radius * Mathf.PI +  capsuleCollider.height * 2 * Mathf.PI * capsuleCollider.radius;
                    logger.Trace($"Found capsule collider with area {area}");
                    coefficient += area * scale;
                }
                else if (collider is MeshCollider meshCollider)
                {
                    float area = meshCollider.sharedMesh.CalculateSurfaceArea();
                    logger.Trace($"Found mesh collider with area {area}");
                    coefficient += area * scale;
                }
                else
                {
                    // we assume this is an ellipsoid, calculate surface area
                    Vector3 extents = collider.bounds.extents;
                    float a = extents.x;
                    float b = extents.y;
                    float c = extents.z;
                    float p = 8 / 5;
                    float area = 4 * Mathf.PI * Mathf.Pow((Mathf.Pow(a * b, p) + Mathf.Pow(a * c, p) + Mathf.Pow(b * c, p)) / 3, 1 / p);
                    logger.Trace($"Found unknown collider with area {area}");
                    coefficient += area * scale;
                }
            }
            logger.Debug($"Bubble coefficient is: {coefficient}");
            return coefficient;
        }

        private void PrePool()
        {
            this.BubbleCoefficient = this.CalculateBubbleCoefficient();
        }

        private static readonly FieldInfo m_PowerUpDelay = AccessTools.Field(typeof(ModuleShieldGenerator), "m_PowerUpDelay");
        internal void CalculateNewShieldParams()
        {
            this.MaxShieldHealth = 10000.0f;
            this.ShieldRegen = 100.0f;
            // WE assume power up time corresponds with full regen time
            float chargeTime = (float)m_PowerUpDelay.GetValue(this.GeneratorModule);

            // we convert energy per damage to an estimation of the charge power
            float energyToMax = Mathf.Max(this.GeneratorModule.m_InitialChargeEnergy, this.GeneratorModule.m_EnergyConsumptionPerSec * chargeTime);
            float rawMaxHealth = energyToMax / this.GeneratorModule.m_EnergyConsumedPerDamagePoint;

            // Apply modifiers based on bubble size and corp
            float corpCoefficient = Singleton.Manager<ManCombatOverhaul>.inst.GetShieldCoefficient(this);
            float bubbleSizeCoefficient = this.BubbleCoefficient;
            this.MaxShieldHealth = rawMaxHealth * corpCoefficient * Mathf.Sqrt(bubbleSizeCoefficient);

            // Shield regen is determined based on charge time
            this.ShieldRegen = this.MaxShieldHealth / chargeTime;
            if (logger.LogDebug())
            {
                logger.Debug($"Pooling {this.GeneratorModule.name}:");
                logger.Trace($"  chargeTime: {chargeTime}");
                logger.Trace($"  energyToMax: {energyToMax}");
                logger.Trace($"  energyPerHP: {this.GeneratorModule.m_EnergyConsumedPerDamagePoint}");
                logger.Trace($"  rawMaxHP: {rawMaxHealth}");
                logger.Trace($"  corpCoeff: {corpCoefficient}");
                logger.Trace($"  bubbleCoeff: {bubbleSizeCoefficient}");
                logger.Debug($"  Final shield Health: {this.MaxShieldHealth}");
                logger.Debug($"  Final shield Regen: {this.ShieldRegen}");
            }
            return;
        }

        private void OnPool()
        {
            logger.Trace($"ShieldParams OnPool {base.name}");
            this.ShieldRegen = 0.0f;
            this.MaxShieldHealth = 0.0f;

            logger.Trace("OnPool 1");
            this.GeneratorModule = base.GetComponent<ModuleShieldGenerator>();
            this.isShield = this.GeneratorModule.m_Repulsion;
            logger.Trace("OnPool 1a");
            TankBlock forceBlock = this.GeneratorModule.block;
            if (forceBlock == null)
            {
                logger.Fatal("NULL BLOCK?");
            }
            this.ShieldCorp = Singleton.Manager<ManSpawn>.inst.GetCorporation(forceBlock.BlockType);
            logger.Trace("OnPool 2");

            // Forcibly set damageable type back to shield
            this.Bubble = base.GetComponentsInChildren<BubbleShield>(true).FirstOrDefault<BubbleShield>();
            this.oldDamageableType = this.Damageable.DamageableType;
            this.Damageable.DamageableType = ManDamage.DamageableType.Shield;
            logger.Trace("OnPool 3");

            // set damageable health
            if (this.isShield)
            {
                logger.Trace("OnPool 4");
                this.originalShieldHealth = (float) m_OrigMaxHealth.GetValue(this.Damageable);
                logger.Trace("OnPool 5");
                if (Mathf.Approximately(this.MaxShieldHealth, 0.0f))
                {
                    this.CalculateNewShieldParams();
                }
                this.SetShieldHealth(this.MaxShieldHealth);

                this.GeneratorModule.m_EnergyConsumedPerDamagePoint /= 2;
            }
        }

        private void OnSpawn()
        {
            this.SetShieldHealth(this.MaxShieldHealth);
            logger.Trace($"OnSpawn {base.name}, target health is {this.MaxShieldHealth}, actual health is {this.Damageable.Health}");
        }

        private void OnRecycle()
        {
            // reset Damageable type
            this.Damageable.DamageableType = this.oldDamageableType;
            if (this.isShield)
            {
                this.SetShieldHealth(this.originalShieldHealth);
            }
        }

        internal void AttemptRegen()
        {
            if (this.impactCooldown <= 0.0f)
            {
                float hpToRegen = this.ShieldRegen * Time.deltaTime;
                logger.Trace($"  Regen {hpToRegen} health");
                this.Damageable.Repair(hpToRegen, false);
            }
            else
            {
                logger.Trace($"  Regen BLOCKED for {this.impactCooldown} more seconds");
            }
        }

        internal void Update()
        {
            if (this.impactCooldown > 0.0f)
            {
                this.impactCooldown -= Time.deltaTime;
            }
        }

        internal void RegisterImpact(ManDamage.DamageInfo info)
        {
            Transform transform = this.GeneratorModule.hitEffect.Spawn(info.HitPosition);
            Vector3 normalized = (this.Bubble.RepulsorBulletTrigger.transform.position - info.HitPosition).normalized;
            transform.rotation = Quaternion.LookRotation(normalized);

            // override particle layer
            Material[] materials = transform.GetComponentsInChildren<Material>();
            foreach (Material material in materials)
            {
                material.renderQueue = 2;
            }

            this.SetCooldown(this.minCooldown);
        }

        public void SetCooldown(float newCooldown)
        {
            this.impactCooldown = Mathf.Max(this.impactCooldown, newCooldown);
        }

        private float minCooldown = 0.2f;
        private float impactCooldown = 0.0f;
    }

    // patch Update step. This is where we regen the shield
    [HarmonyPatch(typeof(ModuleShieldGenerator), "OnUpdateConsumeEnergy")]
    internal static class PatchShieldState
    {
        private static readonly FieldInfo m_State = AccessTools.Field(typeof(ModuleShieldGenerator), "m_State");
        private static readonly FieldInfo m_PowerUpTimer = AccessTools.Field(typeof(ModuleShieldGenerator), "m_PowerUpTimer");

        internal struct ShieldState
        {
            public bool scriptDisabled;
            public bool isPowered;
        }

        internal static void Prefix(ModuleShieldGenerator __instance, out ShieldState __state)
        {
            __state = new ShieldState {
                scriptDisabled = __instance.m_ScriptDisabled,
                isPowered = __instance.IsPowered
            };
            // is a shield
            if (__instance.m_Repulsion && !__instance.m_ScriptDisabled)
            {
                ModuleShieldParameters shieldParams = __instance.GetComponent<ModuleShieldParameters>();
                if (shieldParams != null)
                {
                    // we double energy consumption
                    // __instance.m_EnergyConsumptionPerSec = __instance.m_EnergyConsumptionPerSec * 2;

                    ModuleShieldParameters.logger.Trace($"Updating shield {__instance.name} ({__instance.gameObject.GetInstanceID()})");
                    if (shieldParams.Damageable.Health <= 0.0f)
                    {
                        ModuleShieldParameters.logger.Trace("  Shield has lost all health - disable for recharge");
                        // reset health to max, pop shield
                        shieldParams.Bubble.SetTargetScale(0.0f);
                        __instance.m_ScriptDisabled = true;
                    }
                    else
                    {
                        ModuleShieldParameters.logger.Trace($"  Shield is alive, attempt to regen health (currently {shieldParams.Damageable.Health})");
                        shieldParams.AttemptRegen();
                    }
                }
            }
        }

        internal static void Postfix(ModuleShieldGenerator __instance, ShieldState __state)
        {
            // we forcibly flipped the shield state to pop the shield
            if (__instance.m_Repulsion && __instance.m_ScriptDisabled != __state.scriptDisabled)
            {
                __instance.m_ScriptDisabled = __state.scriptDisabled;
                // add power down time to shield cycle time so it stays down a bit longer
                float currentDelay = (float)m_PowerUpTimer.GetValue(__instance);
                m_PowerUpTimer.SetValue(__instance, currentDelay + __instance.m_InterpTimeOff);
            }
            ModuleShieldParameters shieldParams = __instance.GetComponent<ModuleShieldParameters>();
            if (shieldParams != null)
            {
                // __instance.m_EnergyConsumptionPerSec = __instance.m_EnergyConsumptionPerSec / 2;

                // shield went from charging to powered
                if (__instance.m_Repulsion && __state.isPowered != __instance.IsPowered)
                {
                    shieldParams.Damageable.InitHealth(shieldParams.Damageable.MaxHealth);
                }
            }
        }
    }

    // make shield damageables invulnerable right before Damageable.Damage(), and remove invulnerability right after
    // This is so Damageable.TryToDamage() still executes Damageable.Damage(), but Damageable.Damage() does not destroy the block
    [HarmonyPatch(typeof(Damageable), "Damage")]
    internal static class PatchInvulnerableShield
    {
        private static readonly FieldInfo m_OrigMaxHealth = AccessTools.Field(typeof(Damageable), "m_OrigMaxHealth");
        internal static void Prefix(Damageable __instance, out bool __state)
        {
            __state = __instance.Invulnerable;
            if (__instance.DamageableType == ManDamage.DamageableType.Shield && !__state)
            {
                // we are only shields if original max health != max health
                if (!Mathf.Approximately((float)m_OrigMaxHealth.GetValue(__instance), __instance.MaxHealth))
                {
                    __instance.SetInvulnerable(true, true);
                }
            }
        }
        internal static void Postfix(Damageable __instance, bool __state)
        {
            if (__state != __instance.Invulnerable)
            {
                __instance.SetInvulnerable(__state, true);
            }
        }
    }
    // Setting max health forcibly sets original max health, as that's used in ModuleDamage pool, so should be forcibly synced for all blocks
    [HarmonyPatch(typeof(Damageable), "SetMaxHealth")]
    internal static class ForcePatchMaxHealth
    {
        private static readonly FieldInfo m_OrigMaxHealth = AccessTools.Field(typeof(Damageable), "m_OrigMaxHealth");
        internal static void Postfix(Damageable __instance) {
            m_OrigMaxHealth.SetValue(__instance, __instance.MaxHealth);
        }
    }

    // this is only set for shield bubbles, so we actually do damage
    [HarmonyPatch(typeof(ModuleShieldGenerator), "OnRejectShieldDamage")]
    internal static class PatchShieldDamage
    {
        private static readonly FieldInfo m_EnergyDeficit = AccessTools.Field(typeof(ModuleShieldGenerator), "m_EnergyDeficit");
        private static readonly MethodInfo OnServerSetEnergyDeficit = AccessTools.Method(typeof(ModuleShieldGenerator), "OnServerSetEnergyDeficit");
        internal static readonly FieldInfo m_Shield = AccessTools.Field(typeof(ModuleShieldGenerator), "m_Shield");
        internal static bool Prefix(ModuleShieldGenerator __instance, ManDamage.DamageInfo info, bool actuallyDealDamage, ref bool __result)
        {
            __result = true;
            ModuleShieldParameters shieldParams = __instance.GetComponent<ModuleShieldParameters>();
            if (shieldParams != null)
            {
                if (__instance.IsPowered && actuallyDealDamage)
                {
                    __result = false;
                    shieldParams.RegisterImpact(info);
                    // drain energy at reduced rate
                    float currDeficit = (float)m_EnergyDeficit.GetValue(__instance);
                    OnServerSetEnergyDeficit.Invoke(__instance, new object[] { currDeficit + (__instance.m_EnergyConsumedPerDamagePoint * info.Damage) });
                }
                return false;
            }
            else
            {
                return true;
            }
        }
    }
}
