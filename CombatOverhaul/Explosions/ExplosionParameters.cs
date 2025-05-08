using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace CombatOverhaul.Explosions
{
    internal class ExplosionParameters
    {
        internal static Logger logger;
    }

    // Override explode function isntead
    [HarmonyPatch(typeof(Explosion), "Explode")]
    public static class PatchExplosionDmg
    {
        public static readonly FieldInfo DirectHit = AccessTools.Field(typeof(Explosion), "m_DirectHitTarget");
        private static readonly FieldInfo m_AoEDamageBlockPercent = AccessTools.Field(typeof(Damageable), "m_AoEDamageBlockPercent");
        private static readonly FieldInfo m_DirectHitTarget = AccessTools.Field(typeof(Explosion), "m_DirectHitTarget");
        private static readonly FieldInfo m_DamageType = AccessTools.Field(typeof(Explosion), "m_DamageType");
        private static readonly FieldInfo m_DamageSource = AccessTools.Field(typeof(Explosion), "m_DamageSource");
        private static readonly FieldInfo m_ExplosionType = AccessTools.Field(typeof(Explosion), "m_ExplosionType");
        private static readonly FieldInfo m_ExplosionSize = AccessTools.Field(typeof(Explosion), "m_ExplosionSize");
        private static readonly FieldInfo m_CorpExplosionAudioType = AccessTools.Field(typeof(Explosion), "m_CorpExplosionAudioType");

        private struct HitDesc : IEquatable<HitDesc>
        {
            public HitDesc(Visible v, Vector3 p, float sd, Vector3 i)
            {
                this.visible = v;
                this.point = p;
                this.sqDist = sd;
                this.impulse = i;
            }
            public Visible visible;
            public Vector3 point;
            public Vector3 impulse;
            public float sqDist;

            public bool Equals(HitDesc other)
            {
                return other.visible == this.visible;
            }
        }

        private class HitDescByDist : IComparer<HitDesc>
        {
            public int Compare(HitDesc x, HitDesc y)
            {
                return x.sqDist.CompareTo(y.sqDist);
            }
        }

        private static readonly Dictionary<int, HitDesc> s_VisibleHits = new Dictionary<int, HitDesc>();
        private static readonly Dictionary<int, HitDesc> s_TankHits = new Dictionary<int, HitDesc>();
        private static Collider[] s_SphereOverlapResultBuffer = new Collider[64];

        private enum VisibleStatus
        {
            INVINCIBLE,
            DAMAGED,
            DESTROYED,
            UNTOUCHED,
            SHIELD
        }

        private static readonly Dictionary<int, float> DamageReductions = new Dictionary<int, float>();
        private static readonly HashSet<int> VisitedSet = new HashSet<int>();
        private static readonly Dictionary<int, VisibleStatus> VisibleStatusCache = new Dictionary<int, VisibleStatus>();
        private static readonly SortedSet<HitDesc> HitsInOrder = new SortedSet<HitDesc>(new HitDescByDist());
        private static readonly Dictionary<int, Visible> VisibleCache = new Dictionary<int, Visible>();
        private static Visible DirectHitVisible = null;

        internal static Dictionary<int, int> ColliderToVisibleCache = new Dictionary<int, int>();

        private static void StoreVisibleHit(Visible visible, Vector3 point, float sqDist, Vector3 impulse)
        {
            HitDesc hitDesc;
            if (!s_VisibleHits.TryGetValue(visible.GetHashCode(), out hitDesc))
            {
                HitDesc newHitDesc = new HitDesc(visible, point, sqDist, impulse);
                s_VisibleHits[visible.GetHashCode()] = newHitDesc;
                HitsInOrder.Add(newHitDesc);
            }
            else if (sqDist < hitDesc.sqDist)
            {
                HitsInOrder.Remove(hitDesc);
                HitDesc newHitDesc = new HitDesc(visible, point, sqDist, impulse);
                s_VisibleHits[visible.GetHashCode()] = newHitDesc;
                HitsInOrder.Add(newHitDesc);
            }
        }
        private static void StoreTankHit(Visible visible, Vector3 point, float sqDist, Vector3 impulse)
        {
            HitDesc hitDesc;
            if (!s_TankHits.TryGetValue(visible.GetHashCode(), out hitDesc))
            {
                HitDesc newHitDesc = new HitDesc(visible, point, sqDist, impulse);
                s_TankHits[visible.GetHashCode()] = newHitDesc;
            }
            else if (sqDist < hitDesc.sqDist)
            {
                HitDesc newHitDesc = new HitDesc(visible, point, sqDist, impulse);
                s_TankHits[visible.GetHashCode()] = newHitDesc;
            }
        }

        private static void GatherVisibleHits(Explosion explosion, Visible directHit)
        {
            int num = 0;
            do
            {
                if (num >= s_SphereOverlapResultBuffer.Length)
                {
                    Array.Resize<Collider>(ref s_SphereOverlapResultBuffer, s_SphereOverlapResultBuffer.Length * 2);
                }
                num = Physics.OverlapSphereNonAlloc(explosion.transform.position, explosion.m_EffectRadius, s_SphereOverlapResultBuffer, Singleton.Manager<ManVisible>.inst.VisiblePickerMaskNoTechs, QueryTriggerInteraction.Ignore);
            }
            while (num >= s_SphereOverlapResultBuffer.Length);
            for (int i = 0; i < num; i++)
            {
                Visible visible = Visible.FindVisibleUpwards(s_SphereOverlapResultBuffer[i]);
                if (visible != null && visible != directHit)
                {
                    Vector3 centrePosition = visible.centrePosition;
                    float sqrMagnitude = (centrePosition - explosion.transform.position).sqrMagnitude;
                    if (visible && visible.damageable)
                    {
                        StoreVisibleHit(visible, centrePosition, sqrMagnitude, Vector3.zero);
                    }
                }
            }
        }

        private static float DealDamage(Explosion explosion, float damage, Damageable damageable, Vector3 hitPosition, Vector3 damageDirection)
        {
            float damageRemaining = Singleton.Manager<ManDamage>.inst.DealDamage(
                damageable,
                damage,
                (ManDamage.DamageType)m_DamageType.GetValue(explosion),
                explosion,
                (Tank)m_DamageSource.GetValue(explosion),
                hitPosition,
                damageDirection,
                0f,
                0f
            );
            return damageRemaining;
        }
        private static float DealDamage(Explosion explosion, float damage, Damageable damageable, float explosionStrength = 1.0f)
        {
            Vector3 direction = damageable.transform.position - explosion.transform.position;
            return DealDamage(explosion, damage, damageable, explosion.transform.position, direction.normalized * explosionStrength * explosion.m_MaxImpulseStrength);
        }

        private static int GetVisibleHash(Collider collider, out Visible visible)
        {
            if (collider == null)
            {
                visible = null;
                return 0;
            }
            int colliderHash = collider.GetHashCode();
            if (ColliderToVisibleCache.TryGetValue(colliderHash, out int visibleHash))
            {
                visible = VisibleCache[visibleHash];
                return visibleHash;
            }
            else
            {
                visible = Visible.FindVisibleUpwards(collider);
                if (visible != null)
                {
                    visibleHash = visible.GetHashCode();
                }
                else
                {
                    visibleHash = 0;
                }
                VisibleCache[visibleHash] = visible;
                ColliderToVisibleCache.Add(colliderHash, visibleHash);
                return visibleHash;
            }
        }

        private class RaycastByDist : IComparer<RaycastHit>
        {
            public int Compare(RaycastHit x, RaycastHit y)
            {
                return x.distance.CompareTo(y.distance);
            }
        }

        // we don't want to be obstructed by chunks
        public static int ExplosionRaycastMask = Globals.inst.layerTank.mask | Globals.inst.layerScenery.mask | Globals.inst.layerShield.mask;
        // return true if block is damaged, false otherwise
        private static bool ProcessHitDesc(Explosion explosion, HitDesc hitDesc, float explosionStrength)
        {
            Visible hitDescVisible = hitDesc.visible;
            int targetHash = hitDescVisible.GetHashCode();
            VisitedSet.Add(targetHash);
            ExplosionParameters.logger.Trace($"    Processing recursive hit {hitDescVisible.name} ({targetHash})");
            if (VisibleStatusCache.TryGetValue(targetHash, out VisibleStatus status))
            {
                ExplosionParameters.logger.Trace($"    Already processed. Status: {status}");
                return status == VisibleStatus.DAMAGED || status == VisibleStatus.DESTROYED;
            }

            // Console.WriteLine($"  Processing hit against {hitDescVisible.name}");
            bool toProcessDamage = true;
            float damageMultiplier = 1.0f;
            // do raycast
            Vector3 actual = hitDesc.point - explosion.transform.position;
            RaycastHit[] results = new RaycastHit[((int)actual.magnitude)];
            Physics.RaycastNonAlloc(new Ray(explosion.transform.position, actual), results, actual.magnitude, ExplosionRaycastMask, QueryTriggerInteraction.Ignore);
            // sort results, so we're going in order from origin
            SortedSet<RaycastHit> sortedResults = new SortedSet<RaycastHit>(new RaycastByDist());
            foreach (RaycastHit hit in results)
            {
                sortedResults.Add(hit);
            }

            foreach (RaycastHit hit in sortedResults)
            {
                // don't include self or direct hit
                int rayHitVisible = GetVisibleHash(hit.collider, out Visible visible);
                ExplosionParameters.logger.Trace($"    Raycast hit {rayHitVisible}, distance {hit.distance}");
                if (rayHitVisible == targetHash)
                {
                    // This is same block, ignore hit
                    ExplosionParameters.logger.Trace($"     Visible blocks itself, ignore");
                    continue;
                }

                // don't consider the direct hit
                if ((DirectHitVisible == null || rayHitVisible != DirectHitVisible.GetHashCode()))
                {

                    if (visible == null || (visible.type == ObjectTypes.Block && visible.block.IsNotNull() && visible.block.tank.IsNull()))
                    {
                        // if this is a loose block, then ignore raycast
                        ExplosionParameters.logger.Trace($"     Is loose block, ignore");
                        continue;
                    }

                    if (VisibleStatusCache.TryGetValue(rayHitVisible, out status))
                    {
                        float blockDamageMultiplier;
                        switch (status)
                        {
                            case VisibleStatus.SHIELD:
                                ExplosionParameters.logger.Trace($"     Is shield, attenuate");
                                if (DamageReductions.TryGetValue(rayHitVisible, out blockDamageMultiplier))
                                {
                                    ExplosionParameters.logger.Trace($"     Damage multiplier {blockDamageMultiplier}");
                                    damageMultiplier *= blockDamageMultiplier;
                                }
                                break;
                            case VisibleStatus.DESTROYED:
                                ExplosionParameters.logger.Trace($"     Is destroyed");
                                if (DamageReductions.TryGetValue(rayHitVisible, out blockDamageMultiplier))
                                {
                                    ExplosionParameters.logger.Trace($"     Damage multiplier {blockDamageMultiplier}");
                                    damageMultiplier *= blockDamageMultiplier;
                                }
                                break;
                            case VisibleStatus.DAMAGED:
                            case VisibleStatus.INVINCIBLE:
                                ExplosionParameters.logger.Trace($"     Survived or invincible");
                                VisibleStatusCache[targetHash] = VisibleStatus.INVINCIBLE;
                                return false;
                            case VisibleStatus.UNTOUCHED:
                                ExplosionParameters.logger.Trace($"     Is Untouched, recursing");
                                if (s_VisibleHits.TryGetValue(rayHitVisible, out HitDesc targetHitDesc))
                                {
                                    ExplosionParameters.logger.Trace($"     Found visible name {targetHitDesc.visible.name}");
                                    if (VisitedSet.Contains(rayHitVisible))
                                    {
                                        ExplosionParameters.logger.Trace($"     Recursive loop detected, ignore this hit and try to process damage now");
                                        continue;
                                    }
                                    if (ProcessHitDesc(explosion, targetHitDesc, explosionStrength))
                                    {
                                        // if process damages, then it's not invincible
                                        // DamageReduction will only be set when it's fully destroyed
                                        if (DamageReductions.TryGetValue(rayHitVisible, out blockDamageMultiplier))
                                        {
                                            damageMultiplier *= blockDamageMultiplier;
                                        }
                                        else
                                        {
                                            // we failed to fully destroy the block
                                            VisibleStatusCache[targetHash] = VisibleStatus.INVINCIBLE;
                                            return false;
                                        }
                                    }
                                    else
                                    {
                                        // we hit an invincible block in the way
                                        VisibleStatusCache[targetHash] = VisibleStatus.INVINCIBLE;
                                        return false;
                                    }
                                }
                                break;
                        }
                    }
                    else
                    {
                        ExplosionParameters.logger.Trace($"     Is untouched, recursing");
                        // if no status, we assume it's untouched
                        if (s_VisibleHits.TryGetValue(rayHitVisible, out HitDesc targetHitDesc))
                        {
                            ExplosionParameters.logger.Trace($"     Found visible name {targetHitDesc.visible.name}");
                            if (VisitedSet.Contains(rayHitVisible))
                            {
                                ExplosionParameters.logger.Trace($"     Recursive loop detected, ignore this hit and try to process damage now");
                                continue;
                            }
                            if (ProcessHitDesc(explosion, targetHitDesc, explosionStrength))
                            {
                                // if process damages, then it's not invincible
                                if (DamageReductions.TryGetValue(rayHitVisible, out float blockDamageMultiplier))
                                {
                                    damageMultiplier *= blockDamageMultiplier;
                                }
                                else
                                {
                                    // we failed to fully destroy the block
                                    VisibleStatusCache[targetHash] = VisibleStatus.INVINCIBLE;
                                    return false;
                                }
                            }
                            else
                            {
                                // we hit an invincible block in the way
                                VisibleStatusCache[targetHash] = VisibleStatus.INVINCIBLE;
                                return false;
                            }
                        }
                    }
                }
            }

            if (toProcessDamage)
            {
                ExplosionParameters.logger.Trace($"    Processing dmg");
                float damageRemaining = 1.0f;
                if (hitDescVisible.damageable.Health > 0.0f)
                {
                    // only do damage if this actually has health
                    Vector3 position = explosion.transform.position;
                    Vector3 damageDirection = (hitDesc.point - position).normalized * explosionStrength * explosion.m_MaxImpulseStrength;
                    float damage = explosion.DoDamage ? (explosionStrength * explosion.m_MaxDamageStrength) * damageMultiplier : 0f;
                    damageRemaining = DealDamage(explosion, damage, hitDescVisible.damageable, hitDescVisible.centrePosition, damageDirection);
                }
                if (hitDescVisible.damageable.DamageableType == ManDamage.DamageableType.Shield && hitDescVisible.block.IsNotNull() && hitDescVisible.block.visible.damageable != hitDescVisible.damageable)
                {
                    ExplosionParameters.logger.Trace($"    Is shield - add attenuation");
                    VisibleStatusCache[targetHash] = VisibleStatus.SHIELD;
                    // damage attenuation through shield
                    DamageReductions[targetHash] = GetShieldBleedthrough(hitDescVisible.block);
                }
                else if (damageRemaining > 0.0f)
                {
                    ExplosionParameters.logger.Trace($"    Visible destroyed {damageRemaining} * {explosion.m_MaxDamageStrength} dmg remaining");
                    VisibleStatusCache[targetHash] = VisibleStatus.DESTROYED;
                    DamageReductions[targetHash] = damageRemaining;
                }
                else
                {
                    ExplosionParameters.logger.Trace($"    Visible survived");
                    VisibleStatusCache[targetHash] = VisibleStatus.DAMAGED;
                }
            }
            else
            {
                ExplosionParameters.logger.Trace($"    Force setting to invincible");
                VisibleStatusCache[targetHash] = VisibleStatus.INVINCIBLE;
            }

            return toProcessDamage;
        }

        private static void ProcessHits(Explosion explosion)
        {
            Vector3 position = explosion.transform.position;
            float a = 1f / (explosion.m_EffectRadius * explosion.m_EffectRadius);
            float b = 1f / (explosion.m_EffectRadiusMaxStrength * explosion.m_EffectRadiusMaxStrength);
            ExplosionParameters.logger.Trace(" Processing hits");
            foreach (HitDesc hitDesc in HitsInOrder)
            {
                ExplosionParameters.logger.Trace($"  Processing hit {hitDesc.visible.name}");
                float explosionStrength = Mathf.InverseLerp(a, b, 1f / hitDesc.sqDist);
                // If we did damage, then add impulse
                if (ProcessHitDesc(explosion, hitDesc, explosionStrength))
                {
                    ExplosionParameters.logger.Trace($"  damage dealt");
                    if (explosionStrength > 0f)
                    {
                        Vector3 damageDirection = (hitDesc.point - position).normalized * explosionStrength * explosion.m_MaxImpulseStrength;

                        // else, calculate force
                        if (hitDesc.visible.type == ObjectTypes.Block && hitDesc.visible.block.tank)
                        {
                            StoreTankHit(hitDesc.visible.block.tank.visible, hitDesc.point, hitDesc.sqDist, damageDirection);
                        }
                        else if (hitDesc.visible.type != ObjectTypes.Scenery && hitDesc.visible.rbody)
                        {
                            hitDesc.visible.rbody.AddForceAtPosition(damageDirection, hitDesc.point, ForceMode.Impulse);
                        }
                    }
                }
                else
                {
                    ExplosionParameters.logger.Trace($"  no damage dealt");
                }
            }
        }

        private static float GetShieldBleedthrough(TankBlock block)
        {
            return 0.5f;
        }

        private static void ResetStaticClass()
        {
            s_VisibleHits.Clear();
            s_TankHits.Clear();
            DamageReductions.Clear();
            VisibleCache.Clear();
            VisibleStatusCache.Clear();
            HitsInOrder.Clear();
            ColliderToVisibleCache.Clear();
            VisitedSet.Clear();
            DirectHitVisible = null;
        }

        private static void Explode(Explosion explosion)
        {
            // only do explosion processing on host. Expect MP NetTech and NetBlock to sync positions, etc.
            if (ManNetwork.IsHost) {
                ExplosionParameters.logger.Debug($"NEW EXPLOSION: {explosion.name}");
                ResetStaticClass();
                Damageable directHit = (Damageable)m_DirectHitTarget.GetValue(explosion);
                // process damage against current damageable first.
                // Only consider going elsewhere if damageable is destroyed
                bool skipProcessing = directHit != null;
                if (skipProcessing)
                {
                    ExplosionParameters.logger.Trace($" direct hit detected: {directHit.name}");
                    DirectHitVisible = Visible.FindVisibleUpwards(directHit);
                    // if explosion is actually damaging, calculate damage
                    if (explosion.DoDamage)
                    {
                        float damageRemaining = 1.0f;
                        // potential edge case where we hit dead block/damageable?
                        if (directHit.Health > 0.0f)
                        {
                            float damageToDeal = explosion.m_MaxDamageStrength;
                            // if we are a shield
                            if (directHit.DamageableType == ManDamage.DamageableType.Shield && directHit.Block.IsNotNull() && directHit.Block.visible.damageable != directHit)
                            {
                                ExplosionParameters.logger.Trace($" Is shield, let damage through");
                                // calculate damage to
                                int hitHash = DirectHitVisible.GetHashCode();
                                VisibleStatusCache[hitHash] = VisibleStatus.SHIELD;
                                float shieldBleedthrough = GetShieldBleedthrough(directHit.Block);
                                DamageReductions[hitHash] = shieldBleedthrough;
                                damageRemaining = shieldBleedthrough;
                                skipProcessing = false;
                            }
                            else
                            {
                                // calculate damage to damageable
                                damageRemaining = DealDamage(explosion, damageToDeal, directHit);
                            }
                        }
                        if (damageRemaining > 0)
                        {
                            skipProcessing = false;
                            explosion.m_MaxDamageStrength *= damageRemaining;
                            VisibleStatusCache[DirectHitVisible.GetHashCode()] = VisibleStatus.DESTROYED;
                        }
                    }
                    // only add impulse to direct impact
                    else
                    {
                        Vector3 direction = directHit.transform.position - explosion.transform.position;
                        if (DirectHitVisible.type == ObjectTypes.Block && DirectHitVisible.block.tank)
                        {
                            StoreTankHit(DirectHitVisible.block.tank.visible, directHit.transform.position, direction.sqrMagnitude, direction);
                        }
                        else if (DirectHitVisible.type != ObjectTypes.Scenery && DirectHitVisible.rbody)
                        {
                            DirectHitVisible.rbody.AddForceAtPosition(direction, directHit.transform.position, ForceMode.Impulse);
                        }
                    }
                }
                // Only do advanced processing if we killed impact damageable, or we did not impact damageable
                if (!skipProcessing)
                {
                    GatherVisibleHits(explosion, DirectHitVisible);
                    ProcessHits(explosion);
                }
                foreach (KeyValuePair<int, HitDesc> keyValuePair in s_TankHits)
                {
                    HitDesc hitDesc = keyValuePair.Value;
                    hitDesc.visible.rbody.AddForceAtPosition(hitDesc.impulse, hitDesc.point, ForceMode.Impulse);
                }
                ResetStaticClass();
            }
            // play SFX
            Vector3 position = explosion.transform.position;
            Singleton.Manager<ManSFX>.inst.PlayExplosionSFX(
                position,
                (ManSFX.ExplosionType)m_ExplosionType.GetValue(explosion),
                (ManSFX.ExplosionSize)m_ExplosionSize.GetValue(explosion),
                (FactionSubTypes)m_CorpExplosionAudioType.GetValue(explosion)
            );
        }

        public static bool Prefix(Explosion __instance, out float __state)
        {
            __state = __instance.m_MaxDamageStrength;
            Explode(__instance);
            return false;
        }

        public static void Postfix(Explosion __instance, ref float __state)
        {
            __instance.m_MaxDamageStrength = __state;
        }
    }
}
