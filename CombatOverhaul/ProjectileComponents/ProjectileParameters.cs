using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using HarmonyLib;
using static CompoundExpression;
using static System.Net.Mime.MediaTypeNames;

namespace CombatOverhaul.ProjectileComponents
{
    [RequireComponent(typeof(Projectile))]
    internal class ProjectileParameters : MonoBehaviour
    {
        internal static Logger logger;
        internal static void ConfigureLogger(Logger.TargetConfig target)
        {
            logger = new Logger("ProjectileOverhaul", target);
            logger.Info("Logger is setup");
        }

        public float krupp;
        public float mass;
        public float caliber;

        public int remainingDamage;
        public float armorPierce = 0.5f;
        public float speed = 100.0f;
        public FactionSubTypes ProjectileCorp = FactionSubTypes.NULL;

        private Projectile m_MyProjectile;
        internal Rigidbody rbody
        {
            get { return m_MyProjectile.rbody; }
        }

        private static readonly FieldInfo m_Damage = AccessTools.Field(typeof(WeaponRound), "m_Damage");
        private void OnPool()
        {
            logger.Trace("PRojectileParams PrePool");
            this.m_MyProjectile = base.GetComponent<Projectile>();
            this.remainingDamage = (int)m_Damage.GetValue(this.m_MyProjectile);
            this.myColliders = base.GetComponentsInChildren<Collider>();
        }

        internal void CalculateParameters(TankBlock block) {}

        // We can only penetrate if it's one of these types:
        //   - Impact
        //   - Standard
        //   - Ballistic

        // Armor pierce works as follows:
        //   We calculate how many cells projectile would move through
        //   We calculate effective health
        //   We subtract that from dmg we will deal
        //   remainder is multiplied by Coefficient
        //   If any remains, then we penetrate
        // This means it's possible to penetrate without destroying the block
        private static readonly FieldInfo m_DamageType = AccessTools.Field(typeof(Projectile), "m_DamageType");
        private static readonly FieldInfo m_Weapon = AccessTools.Field(typeof(Projectile), "m_Weapon");
        public bool DealDamageAndCheckPenetration(Damageable damageable, Vector3 hitPoint)
        {
            bool penetrated = false;
            float damageToDeal = this.remainingDamage;
            float originalDamage = this.remainingDamage;

            TankBlock block = damageable.Block;
            float origDamageableHealth = damageable.Health;
            ManDamage.DamageType damageType = this.m_MyProjectile.DamageType;
            bool canPenetrate = damageType == ManDamage.DamageType.Impact ||
                damageType == ManDamage.DamageType.Ballistic ||
                damageType == ManDamage.DamageType.Standard;
            if (block && !damageable.Invulnerable)
            {
                bool hasArmourReduction = canPenetrate || damageType == ManDamage.DamageType.Cutting;

                // Calculate damage reduction from armour
                if (hasArmourReduction && block && damageable.DamageableType == ManDamage.DamageableType.Armour)
                {
                    float armorReduction = Singleton.Manager<ManCombatOverhaul>.inst.GetArmorReduction(block);
                    int reduction = Mathf.RoundToInt(damageable.Health * damageable.Health / damageable.MaxHealth * armorReduction);
                    damageToDeal = Math.Max(Math.Max(1, damageToDeal / 10), damageToDeal - reduction);
                }

                if (canPenetrate)
                {
                    float maxDamageToDeal = damageable.Health;
                    if (block.GetComponent<DummyTarget>() == null)
                    {
                        int numCells = block.filledCells.Length;
                        float cellHealth = damageable.Health / numCells;

                        int cellsToPenetrate = 0;
                        if (damageToDeal < cellHealth)
                        {
                            // if can't even penetrate one, then don't bother
                            cellsToPenetrate = 1;
                        }
                        else
                        {
                            int expectedCellsToPenetrate = Mathf.CeilToInt(damageToDeal / cellHealth);

                            // brute force check cells
                            // TODO: optimize this
                            foreach (IntVector3 cell in block.filledCells)
                            {
                                if (IntersectsCell(rbody.position, rbody.velocity, cell))
                                {
                                    cellsToPenetrate++;
                                }
                            }
                        }
                        maxDamageToDeal = cellHealth * Math.Max(1, cellsToPenetrate);
                        logger.Trace($"Trying to penetrate {cellsToPenetrate} cells, for corresponding HP of {maxDamageToDeal}");
                    }

                    if (damageToDeal < maxDamageToDeal)
                    {
                        this.remainingDamage = 0;
                    }
                    else
                    {
                        this.remainingDamage = Math.Max(0, (int)((this.remainingDamage - maxDamageToDeal) * Singleton.Manager<ManCombatOverhaul>.inst.GetPiercingCoefficient(this)));
                        // damageToDeal = maxDamageToDeal;
                        penetrated = true;
                    }
                }
            }

            logger.Trace($"Stage 1 - create new DamageInfo");
            ManDamage.DamageInfo damageInfo = new ManDamage.DamageInfo(
                damageToDeal, (ManDamage.DamageType)m_DamageType.GetValue(this.m_MyProjectile), (ModuleWeapon)m_Weapon.GetValue(this.m_MyProjectile),
                this.m_MyProjectile.Shooter, hitPoint, this.m_MyProjectile.rbody.velocity, 0f, 0f
            );
            logger.Trace($"Stage 2 - deal the damage");
            float fracDamageRemaining = Singleton.Manager<ManDamage>.inst.DealDamage(damageInfo, damageable);

            if (!block && !damageable.Invulnerable)
            {
                if (canPenetrate)
                {
                    // this is not a block
                    this.remainingDamage = (int)Mathf.Max(0.0f, (damageToDeal * fracDamageRemaining * Singleton.Manager<ManCombatOverhaul>.inst.GetPiercingCoefficient(this)));
                    penetrated = this.remainingDamage > 0;
                }
            }

            logger.Trace($"Stage 4a - Just dealt {damageToDeal} damage to damageable {damageable.name} (HP: {origDamageableHealth}), original shell had damage {originalDamage}");
            if (!penetrated)
            {
                this.remainingDamage = 0;
            }
            return penetrated;
        }

        private static bool IntersectsCell(Vector3 point, Vector3 direction, IntVector3 cell)
        {
            float distance = Vector3.Cross(cell - point, cell - (point + direction)).magnitude / direction.magnitude;
            return distance <= Mathf.Pow(1, 1/3);
        }

        private Collider[] myColliders;

        private List<Collider> penetrated = new List<Collider>();
        public void Penetrate(Collider collider, Vector3 originalVelocity)
        {
            this.penetrated.Add(collider);
            foreach (Collider myCollider in myColliders) {
                Physics.IgnoreCollision(collider, myCollider, true);
            }
            rbody.velocity = originalVelocity;
        }
        public void ResetPenetration()
        {
            foreach (Collider collider in penetrated)
            {
                if (collider != null)
                {
                    foreach (Collider myCollider in myColliders)
                    {
                        if (myCollider != null)
                        {
                            Physics.IgnoreCollision(collider, myCollider, false);
                        }
                    }
                }
            }
            this.penetrated.Clear();
        }
        public bool HasPenetrated(Collider collider)
        {
            return penetrated.Contains(collider);
        }
    }

    [HarmonyPatch(typeof(Projectile), "OnCollisionEnter")]
    internal static class PatchProjectile
    {
        #region Reflection
        private static readonly FieldInfo m_Damage = AccessTools.Field(typeof(WeaponRound), "m_Damage");
        private static readonly FieldInfo m_DamageType = AccessTools.Field(typeof(Projectile), "m_DamageType");
        private static readonly FieldInfo m_Stuck = AccessTools.Field(typeof(Projectile), "m_Stuck");
        private static readonly FieldInfo m_SingleImpact = AccessTools.Field(typeof(Projectile), "m_SingleImpact");
        private static readonly FieldInfo m_HasSetCollisionDeathDelay = AccessTools.Field(typeof(Projectile), "m_HasSetCollisionDeathDelay");
        private static readonly FieldInfo m_Weapon = AccessTools.Field(typeof(Projectile), "m_Weapon");
        private static readonly FieldInfo m_StickOnContact = AccessTools.Field(typeof(Projectile), "m_StickOnContact");
        private static readonly FieldInfo m_ExplodeOnStick = AccessTools.Field(typeof(Projectile), "m_ExplodeOnStick");
        private static readonly FieldInfo m_VisibleStuckTo = AccessTools.Field(typeof(Projectile), "m_VisibleStuckTo");
        private static readonly FieldInfo m_Smoke = AccessTools.Field(typeof(Projectile), "m_Smoke");
        private static readonly FieldInfo m_ExplodeOnTerrain = AccessTools.Field(typeof(Projectile), "m_ExplodeOnTerrain");
        private static readonly FieldInfo m_StickOnTerrain = AccessTools.Field(typeof(Projectile), "m_StickOnTerrain");
        private static readonly FieldInfo OnParentDestroyed = AccessTools.Field(typeof(Projectile), "OnParentDestroyed");
        private static readonly FieldInfo m_StickImpactEffect = AccessTools.Field(typeof(Projectile), "m_StickImpactEffect");
        private static readonly FieldInfo m_ImpactSFXType = AccessTools.Field(typeof(Projectile), "m_ImpactSFXType");

        private static readonly MethodInfo IsProjectileArmed = AccessTools.Method(typeof(Projectile), "IsProjectileArmed");
        private static readonly MethodInfo SpawnExplosion = AccessTools.Method(typeof(Projectile), "SpawnExplosion");
        private static readonly MethodInfo SpawnStickImpactEffect = AccessTools.Method(typeof(Projectile), "SpawnStickImpactEffect");
        private static readonly MethodInfo SpawnTerrainHitEffect = AccessTools.Method(typeof(Projectile), "SpawnTerrainHitEffect");
        private static readonly MethodInfo GetDeathDelay = AccessTools.Method(typeof(Projectile), "GetDeathDelay");
        private static readonly MethodInfo OnDelayedDeathSet = AccessTools.Method(typeof(Projectile), "OnDelayedDeathSet");
        private static readonly MethodInfo SetProjectileForDelayedDestruction = AccessTools.Method(typeof(Projectile), "SetProjectileForDelayedDestruction");
        private static readonly MethodInfo StickToObject = AccessTools.Method(typeof(Projectile), "StickToObject");
        #endregion Reflection

        private static bool HandleCollisionAndCheckPenetration(Projectile __instance, ProjectileParameters projParams, Damageable damageable, Vector3 hitPoint, Collider otherCollider, bool ForceDestroy)
        {
            bool penetrated = false;
            if (!((Component)__instance).gameObject.activeInHierarchy)
            {
                ProjectileParameters.logger.Trace("projectile is inactive in hierarchy");
                return false;
            }
            if ((bool)PatchProjectile.m_Stuck.GetValue(__instance))
            {
                ProjectileParameters.logger.Trace("projectile is stuck");
                return false;
            }
            bool singleImpact = (bool)PatchProjectile.m_SingleImpact.GetValue(__instance);
            bool hasSetCollisionDeathDelay = (bool)PatchProjectile.m_HasSetCollisionDeathDelay.GetValue(__instance);
            if (singleImpact && hasSetCollisionDeathDelay)
            {
                return false;
            }

            bool hasHitTerrain = false;

            bool stickOnContact = (bool)PatchProjectile.m_StickOnContact.GetValue(__instance);
            float deathDelay = (float)PatchProjectile.GetDeathDelay.Invoke(__instance, null);

            // handle damage calculations and explosions
            if (damageable)
            {
                ProjectileParameters.logger.Trace("Projectile hit a damageable");
                float damage = projParams.remainingDamage;
                penetrated = projParams.DealDamageAndCheckPenetration(damageable, hitPoint);
                
                // block was destroyed, damage potentially leftover
                if (projParams.remainingDamage > 0)
                {
                    ProjectileParameters.logger.Trace($"Penetrated damageable {damageable.name}, SHELL DMG {damage} ==[REMAINING DMG]=> {projParams.remainingDamage}");
                }
                else
                {
                    ProjectileParameters.logger.Trace($"Failed to penetrate damageable {damageable.name}, SHELL DMG {damage} ==[REMAINING DMG]=> 0");
                }

                // no damage leftover cases:
                if (projParams.remainingDamage <= 0 && !stickOnContact)
                {
                    if (deathDelay != 0.0f)
                    {
                        // penetration fuse, but failed to kill = flattened, spawn the explosion now
                        deathDelay = 0.0f;
                        PatchProjectile.SpawnExplosion.Invoke(__instance, new object[] { hitPoint, damageable });
                    }
                    else if ((bool)PatchProjectile.IsProjectileArmed.Invoke(__instance, null))
                    {
                        // no penetration fuse, check if armed and not stick on contact - stick on contact explosions are done later
                        PatchProjectile.SpawnExplosion.Invoke(__instance, new object[] { hitPoint, damageable });
                    }
                }
            }
            else if (otherCollider.IsTerrain() || otherCollider.gameObject.layer == Globals.inst.layerLandmark || otherCollider.GetComponentInParents<TerrainObject>(true))
            {
                ProjectileParameters.logger.Trace("Stage 4b");
                hasHitTerrain = true;
                PatchProjectile.SpawnTerrainHitEffect.Invoke(__instance, new object[] { hitPoint });
                ProjectileParameters.logger.Trace("Stage 4bb");

                // if explode on terrain, explode and end, no matter death delay
                if ((bool)PatchProjectile.m_ExplodeOnTerrain.GetValue(__instance) && (bool)PatchProjectile.IsProjectileArmed.Invoke(__instance, null))
                {
                    PatchProjectile.SpawnExplosion.Invoke(__instance, new object[] { hitPoint, null });
                }
            }
            else
            {
                ProjectileParameters.logger.Error($"Hit against unknown collider {otherCollider.name}, treat as terrain");
                // destroy projectile right now just in case
                hasHitTerrain = true;
                PatchProjectile.SpawnTerrainHitEffect.Invoke(__instance, new object[] { hitPoint });
                if ((bool)PatchProjectile.m_ExplodeOnTerrain.GetValue(__instance) && (bool)PatchProjectile.IsProjectileArmed.Invoke(__instance, null))
                {
                    PatchProjectile.SpawnExplosion.Invoke(__instance, new object[] { hitPoint, null });
                }
            }

            ProjectileParameters.logger.Trace("Stage 5 - play sfx");
            Singleton.Manager<ManSFX>.inst.PlayImpactSFX(__instance.Shooter, (ManSFX.WeaponImpactSfxType)PatchProjectile.m_ImpactSFXType.GetValue(__instance), damageable, hitPoint, otherCollider);

            ProjectileParameters.logger.Trace("Stage 6 - handle recycle");
            // if here, then no stick on contact, and no damage is leftover, so start destruction sequence
            if (ForceDestroy)   // if projectile hits a shield, always destroy
            {
                ProjectileParameters.logger.Trace($"Stage 6a - hit shield, force destroy");
                __instance.Recycle(false);
            }
            else if (deathDelay <= 0f)
            {
                ProjectileParameters.logger.Trace($"Stage 6b - no delay, check if need to destroy now");
                // If hasn't hit terrain, and still damage left, return here - don't recycle
                if (!hasHitTerrain && projParams.remainingDamage > 0)
                {
                    ProjectileParameters.logger.Trace($" Projectile lives. hit terrain? {hasHitTerrain}, remaining damage: {projParams.remainingDamage}");
                    return true;
                }
                ProjectileParameters.logger.Trace($" Projectile dies now");
                __instance.Recycle(false);
            }
            else if (!hasSetCollisionDeathDelay)
            {
                ProjectileParameters.logger.Trace($"Stage 6c - Setting delayed death in {deathDelay}s");
                PatchProjectile.m_HasSetCollisionDeathDelay.SetValue(__instance, true);
                PatchProjectile.SetProjectileForDelayedDestruction.Invoke(__instance, new object[] { deathDelay });
                if (__instance.SeekingProjectile)
                {
                    __instance.SeekingProjectile.enabled = false;
                }
                PatchProjectile.OnDelayedDeathSet.Invoke(__instance, null);
            }

            ProjectileParameters.logger.Trace("Stage 7 - handle stick on terrain");
            bool stickOnTerrain = (bool)PatchProjectile.m_StickOnTerrain.GetValue(__instance);
            if (stickOnContact && (stickOnTerrain || !hasHitTerrain))
            {
                if (otherCollider.gameObject.transform.lossyScale.Approximately(Vector3.one, 0.001f))
                {
                    ProjectileParameters.logger.Trace("Stage 7a");
                    Visible stickTargetVis = Singleton.Manager<ManVisible>.inst.FindVisible(otherCollider);
                    PatchProjectile.StickToObject.Invoke(__instance, new object[] { true, otherCollider.transform, stickTargetVis, true });
                    SmokeTrail smoke = (SmokeTrail)PatchProjectile.m_Smoke.GetValue(__instance);
                    if (smoke)
                    {
                        smoke.enabled = false;
                        smoke.Reset();
                    }

                    ProjectileParameters.logger.Trace("Stage 7b");
                    if ((bool)PatchProjectile.m_ExplodeOnStick.GetValue(__instance))
                    {
                        Visible visible = (Visible)PatchProjectile.m_VisibleStuckTo.GetValue(__instance);
                        Damageable directHitTarget = visible.IsNotNull() ? visible.damageable : null;
                        PatchProjectile.SpawnExplosion.Invoke(__instance, new object[] { hitPoint, directHitTarget });
                    }
                    ProjectileParameters.logger.Trace("Stage 7c");
                    if (((Transform)PatchProjectile.m_StickImpactEffect.GetValue(__instance)).IsNotNull())
                    {
                        PatchProjectile.SpawnStickImpactEffect.Invoke(__instance, new object[] { hitPoint });
                    }
                }
                else
                {
                    ProjectileParameters.logger.Warn(string.Concat(new string[]
                    {
                        "Won't attach projectile ",
                        __instance.name,
                        " to ",
                        otherCollider.name,
                        ", as scale is not one"
                    }));
                }
            }
            ProjectileParameters.logger.Trace("FINAL");
            return penetrated;
        }

        public static bool Prefix(Projectile __instance, Collision collision)
        {
            ProjectileParameters projParams = __instance.GetComponent<ProjectileParameters>();
            if (!projParams || __instance.GetType() != typeof(Projectile) || __instance.GetType().IsSubclassOf(typeof(Projectile)))
            {
                ProjectileParameters.logger.Trace($"DETECTED BAD COLLISION:");
                ProjectileParameters.logger.Trace($"  projParams?: {projParams != null}, instance: {__instance.GetType()}");
                return true;
            }

            ContactPoint[] contacts = collision.contacts;
            if (contacts.Length == 0)
            {
                return false;
            }

            // will always be for something that we have not already penetrated
            ContactPoint contactPoint = contacts[0];

            Vector3 relativeVelocity = collision.relativeVelocity;
            Rigidbody targetRigidbody = collision.collider.attachedRigidbody;
            Vector3 targetVelocity = Vector3.zero;
            if (targetRigidbody)
            {
                targetVelocity = targetRigidbody.velocity;
            }
            // only works because projectile has no mass
            try
            {
                Damageable damageable = contactPoint.otherCollider.GetComponentInParents<Damageable>(true);
                string targetName = targetRigidbody ? targetRigidbody.name : (damageable ? damageable.name : "UNKNOWN");
                ProjectileParameters.logger.Debug($"Handle collision for {__instance.name} vs {targetName}");
                if (PatchProjectile.HandleCollisionAndCheckPenetration(__instance, projParams, damageable, contactPoint.point, collision.collider, false))
                {
                    // we penetrated - ignore collisions so we can move onto next block
                    Vector3 originalVelocity = targetVelocity - relativeVelocity;
                    projParams.Penetrate(contactPoint.otherCollider, originalVelocity);
                }
            }
            catch (Exception e)
            {
                ProjectileParameters.logger.Error("[AP-P] EXCEPTION IN HANDLE COLLISION:");
                ProjectileParameters.logger.Error(e);
                if (__instance)
                {
                    __instance.Recycle(false);
                }
            }
            return false;
        }
    }

    // Set remaining damage
    [HarmonyPatch(typeof(Projectile), "OnPool")]
    public static class PatchProjectilePool
    {
        private static readonly FieldInfo m_Damage = typeof(Projectile).GetField("m_Damage", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        public static void Postfix(ref Projectile __instance)
        {
            ProjectileParameters ProjectileParameters = __instance.GetComponent<ProjectileParameters>();
            if (ProjectileParameters != null)
            {
                ProjectileParameters.remainingDamage = (int)m_Damage.GetValue(__instance);
            }
        }
    }

    // Reset remaining damage
    [HarmonyPatch(typeof(Projectile), "OnRecycle")]
    internal static class ProjectileDamageReset
    {
        private static readonly FieldInfo m_Damage = AccessTools.Field(typeof(WeaponRound), "m_Damage");

        internal static void Postfix(Projectile __instance)
        {
            ProjectileParameters ProjectileParameters = __instance.GetComponent<ProjectileParameters>();
            if (ProjectileParameters != null)
            {
                ProjectileParameters.remainingDamage = (int)m_Damage.GetValue(__instance);
                ProjectileParameters.ResetPenetration();
            }
        }
    }

    [HarmonyPatch(typeof(Projectile), "Fire")]
    public static class GetIntendedVelocity
    {
        public static void Postfix(Projectile __instance, ModuleWeapon weapon)
        {

            ProjectileParameters projParams = __instance.GetComponent<ProjectileParameters>();
            if (projParams != null)
            {
                projParams.speed = __instance.rbody.velocity.magnitude;
                TankBlock firingBlock = weapon.block;
                projParams.ProjectileCorp = Singleton.Manager<ManSpawn>.inst.GetCorporation(firingBlock.BlockType);
            }
        }
    }
}
