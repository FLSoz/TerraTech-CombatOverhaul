using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
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

        private struct HitDesc
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
        }

        private static readonly Dictionary<int, HitDesc> s_VisibleHits = new Dictionary<int, HitDesc>();
        private static readonly Dictionary<int, HitDesc> s_TankHits = new Dictionary<int, HitDesc>();
        private static Collider[] s_SphereOverlapResultBuffer = new Collider[64];

        private static readonly Dictionary<int, HashSet<int>> BlockDependencies = new Dictionary<int, HashSet<int>>();
        private static readonly HashSet<int> DestroyedBlocks = new HashSet<int>();
        private static readonly List<HitDesc> HitsInOrder = new List<HitDesc>();

        private static void StoreHit(Dictionary<int, HitDesc> hitDict, Visible visible, Vector3 point, float sqDist, Vector3 impulse)
        {
            HitDesc hitDesc;
            if (!hitDict.TryGetValue(visible.GetHashCode(), out hitDesc) || sqDist < hitDesc.sqDist)
            {
                hitDict[visible.GetHashCode()] = new HitDesc(visible, point, sqDist, impulse);
            }
        }

        private static void GatherVisibleHits(Explosion explosion)
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
                if (visible != null)
                {
                    Vector3 centrePosition = visible.centrePosition;
                    float sqrMagnitude = (centrePosition - explosion.transform.position).sqrMagnitude;
                    if (visible && visible.damageable)
                    {
                        StoreHit(s_VisibleHits, visible, centrePosition, sqrMagnitude, Vector3.zero);
                    }
                }
            }
        }

        private static void AnalyzeHits()
        {

        }

        private static void Explode(Explosion explosion)
        {
            float a = 1f / (explosion.m_EffectRadius * explosion.m_EffectRadius);
            float b = 1f / (explosion.m_EffectRadiusMaxStrength * explosion.m_EffectRadiusMaxStrength);
            s_VisibleHits.Clear();
            s_TankHits.Clear();
            GatherVisibleHits(explosion);
            Vector3 position = explosion.transform.position;
            foreach (KeyValuePair<int, HitDesc> keyValuePair in s_VisibleHits)
            {
                HitDesc hitDesc = keyValuePair.Value;
                float explosionStrength;
                Damageable directHit = (Damageable)m_DirectHitTarget.GetValue(explosion);
                if (directHit != null && hitDesc.visible.damageable == directHit)
                {
                    explosionStrength = 1f;
                }
                else
                {
                    explosionStrength = Mathf.InverseLerp(a, b, 1f / hitDesc.sqDist);
                }
                if (explosionStrength > 0f)
                {
                    Vector3 damageDirection = (hitDesc.point - position).normalized * explosionStrength * explosion.m_MaxImpulseStrength;
                    float damage = explosion.DoDamage ? (explosionStrength * explosion.m_MaxDamageStrength) : 0f;
                    if (ManNetwork.IsHost)
                    {
                        Singleton.Manager<ManDamage>.inst.DealDamage(
                            hitDesc.visible.damageable,
                            damage,
                            (ManDamage.DamageType)m_DamageType.GetValue(explosion),
                            explosion,
                            (Tank)m_DamageSource.GetValue(explosion),
                            hitDesc.visible.centrePosition,
                            damageDirection,
                            0f,
                            0f
                        );
                    }
                    if (hitDesc.visible.type == ObjectTypes.Block && hitDesc.visible.block.tank)
                    {
                        StoreHit(s_TankHits, hitDesc.visible.block.tank.visible, hitDesc.point, hitDesc.sqDist, damageDirection);
                    }
                    else if (hitDesc.visible.type != ObjectTypes.Scenery && hitDesc.visible.rbody)
                    {
                        hitDesc.visible.rbody.AddForceAtPosition(damageDirection, hitDesc.point, ForceMode.Impulse);
                    }
                }
            }
            foreach (KeyValuePair<int, HitDesc> keyValuePair in s_TankHits)
            {
                HitDesc hitDesc = keyValuePair.Value;
                hitDesc.visible.rbody.AddForceAtPosition(hitDesc.impulse, hitDesc.point, ForceMode.Impulse);
            }
            s_VisibleHits.Clear();
            s_TankHits.Clear();
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
