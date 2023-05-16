using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace CombatOverhaul.Explosions
{
    internal class ExplosionParameters
    {
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

        private class ByDist : IComparer<HitDesc>
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
            UNTOUCHED
        }

        private static readonly Dictionary<int, float> DamageReductions = new Dictionary<int, float>();
        private static readonly Dictionary<int, VisibleStatus> VisibleStatusCache = new Dictionary<int, VisibleStatus>();
        private static readonly SortedSet<HitDesc> HitsInOrder = new SortedSet<HitDesc>(new ByDist());
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

        private static int GetVisibleHash(Collider collider)
        {
            if (collider == null)
            {
                return 0;
            }
            int colliderHash = collider.GetHashCode();
            if (ColliderToVisibleCache.TryGetValue(colliderHash, out int visibleHash))
            {
                return visibleHash;
            }
            else
            {
                Visible visible = Visible.FindVisibleUpwards(collider);
                if (visible != null)
                {
                    visibleHash = visible.GetHashCode();
                }
                else
                {
                    visibleHash = 0;
                }
                ColliderToVisibleCache.Add(colliderHash, visibleHash);
                return visibleHash;
            }
        }

        // return true if block is damaged, false otherwise
        private static bool ProcessHitDesc(Explosion explosion, HitDesc hitDesc, float explosionStrength)
        {
            int targetHash = hitDesc.visible.GetHashCode();
            if (VisibleStatusCache.TryGetValue(targetHash, out VisibleStatus status))
            {
                return status == VisibleStatus.DAMAGED || status == VisibleStatus.DESTROYED;
            }

            // Console.WriteLine($"  Processing hit against {hitDesc.visible.name}");
            bool toProcessDamage = true;
            float damageMultiplier = 1.0f;
            // do raycast
            Vector3 actual = explosion.transform.position - hitDesc.point;
            RaycastHit[] results = new RaycastHit[((int)actual.magnitude)];
            int hits = Physics.RaycastNonAlloc(new Ray(hitDesc.point, actual), results, actual.magnitude, Singleton.Manager<ManVisible>.inst.VisiblePickerMaskNoTechs, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hits; i++)
            {
                // DebugPrint("Loop " + i.ToString());
                RaycastHit test = results[i];
                // don't include self or direct hit
                int hitVisible = GetVisibleHash(test.collider);
                if (hitVisible != targetHash && (DirectHitVisible == null || hitVisible != DirectHitVisible.GetHashCode()))
                {
                    if (VisibleStatusCache.TryGetValue(hitVisible, out status))
                    {
                        switch (status)
                        {
                            case VisibleStatus.DESTROYED:
                                if (DamageReductions.TryGetValue(hitVisible, out float blockDamageMultiplier))
                                {
                                    damageMultiplier *= blockDamageMultiplier;
                                }
                                break;
                            case VisibleStatus.DAMAGED:
                            case VisibleStatus.INVINCIBLE:
                                VisibleStatusCache[targetHash] = VisibleStatus.INVINCIBLE;
                                return false;
                            case VisibleStatus.UNTOUCHED:
                                if (s_VisibleHits.TryGetValue(hitVisible, out HitDesc targetHitDesc))
                                {
                                    if (ProcessHitDesc(explosion, targetHitDesc, explosionStrength))
                                    {
                                        // if process damages, then it's not invincible
                                        // DamageReduction will only be set when it's fully destroyed
                                        if (DamageReductions.TryGetValue(hitVisible, out blockDamageMultiplier))
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
                        // if no status, we assume it's untouched
                        if (s_VisibleHits.TryGetValue(hitVisible, out HitDesc targetHitDesc))
                        {
                            if (ProcessHitDesc(explosion, targetHitDesc, explosionStrength))
                            {
                                // if process damages, then it's not invincible
                                if (DamageReductions.TryGetValue(hitVisible, out float blockDamageMultiplier))
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
                // only do damage if this is a direct hit
                Vector3 position = explosion.transform.position;
                Vector3 damageDirection = (hitDesc.point - position).normalized * explosionStrength * explosion.m_MaxImpulseStrength;
                float damage = explosion.DoDamage ? (explosionStrength * explosion.m_MaxDamageStrength) * damageMultiplier : 0f;

                float damageRemaining = DealDamage(explosion, damage, hitDesc.visible.damageable, hitDesc.visible.centrePosition, damageDirection);
                if (damageRemaining > 0.0f)
                {
                    VisibleStatusCache[targetHash] = VisibleStatus.DESTROYED;
                    DamageReductions[targetHash] = damageRemaining;
                }
                else
                {
                    VisibleStatusCache[targetHash] = VisibleStatus.DAMAGED;
                }
            }
            else
            {
                VisibleStatusCache[targetHash] = VisibleStatus.INVINCIBLE;
            }

            return toProcessDamage;
        }

        private static void ProcessHits(Explosion explosion)
        {
            Vector3 position = explosion.transform.position;
            float a = 1f / (explosion.m_EffectRadius * explosion.m_EffectRadius);
            float b = 1f / (explosion.m_EffectRadiusMaxStrength * explosion.m_EffectRadiusMaxStrength);
            foreach (HitDesc hitDesc in HitsInOrder)
            {
                float explosionStrength = Mathf.InverseLerp(a, b, 1f / hitDesc.sqDist);
                // If we did damage, then add impulse
                if (ProcessHitDesc(explosion, hitDesc, explosionStrength))
                {
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
            }
        }

        private static void ResetStaticClass()
        {
            s_VisibleHits.Clear();
            s_TankHits.Clear();
            DamageReductions.Clear();
            VisibleStatusCache.Clear();
            HitsInOrder.Clear();
            ColliderToVisibleCache.Clear();
            DirectHitVisible = null;
        }

        private static void Explode(Explosion explosion)
        {
            // only do explosion processing on host. Expect MP NetTech and NetBlock to sync positions, etc.
            if (ManNetwork.IsHost) {
                // Console.WriteLine($"NEW EXPLOSION: {explosion.name}");
                ResetStaticClass();
                Damageable directHit = (Damageable)m_DirectHitTarget.GetValue(explosion);
                // process damage against current damageable first.
                // Only consider going elsewhere if damageable is destroyed
                bool skipProcessing = directHit != null;
                if (skipProcessing)
                {
                    DirectHitVisible = Visible.FindVisibleUpwards(directHit);
                    // if explosion is actually damaging, calculate damage
                    if (explosion.DoDamage)
                    {
                        float damageToDeal = explosion.m_MaxDamageStrength;
                        // if we are a shield
                        if (directHit.DamageableType == ManDamage.DamageableType.Shield && directHit.Block?.visible.damageable != directHit)
                        {
                            // calculate damage to shield
                        }
                        else
                        {
                            // calculate damage to damageable

                        }
                        float damageRemaining = DealDamage(explosion, damageToDeal, directHit);
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
