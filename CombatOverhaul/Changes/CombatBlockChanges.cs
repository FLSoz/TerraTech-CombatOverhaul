using BlockChangePatcher;
using CombatOverhaul.BlockModules;
using CombatOverhaul.ProjectileComponents;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace CombatOverhaul.Changes
{
    internal static class CombatBlockChanges
    {
        internal static List<Change> changes = new List<Change>();

        private class ShieldConditional : CustomConditional
        {
            private static readonly BlockTypes[] VanillaBlacklist = new BlockTypes[] {
                BlockTypes.GC_SamSite_Shield_222,
                BlockTypes.GC_SamSite_Shield_AC_222,
                BlockTypes.GC_SamSite_Shield_LaserLab_222,
                BlockTypes.GC_SamSite_Shield_TeslaTurret_222,
                BlockTypes.EXP_Shield_Charger_Lab
            };

            private static readonly string[] ModdedBlacklist = new string[] {};

            public override bool Validate(BlockMetadata blockData)
            {
                if (VanillaBlacklist.Contains(blockData.VanillaID) || ModdedBlacklist.Contains(blockData.BlockID))
                {
                    return false;
                }
                ModuleShieldGenerator shieldModule = blockData.blockPrefab.GetComponent<ModuleShieldGenerator>();
                if (shieldModule && shieldModule.m_Repulsion)
                {
                    return true;
                }
                return false;
            }
        }

        private class ProjectileConditional : CustomConditional
        {
            private static readonly BlockTypes[] VanillaBlacklist = new BlockTypes[] {
            };

            private static readonly string[] ModdedBlacklist = new string[] { };

            public override bool Validate(BlockMetadata blockData)
            {
                if (VanillaBlacklist.Contains(blockData.VanillaID) || ModdedBlacklist.Contains(blockData.BlockID))
                {
                    return false;
                }
                Transform target = blockData.blockPrefab;
                ModuleWeaponGun moduleWeaponGun = target.GetComponent<ModuleWeaponGun>();
                if (moduleWeaponGun)
                {
                    FireData fireData = target.GetComponent<FireData>();
                    if (fireData.m_BulletPrefab is Projectile projPrefab && projPrefab != null)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        private static void ReplaceProjectilePrefab(BlockMetadata block, Transform editableAncillaryPrefab)
        {
            FireData fireData = block.blockPrefab.GetComponent<FireData>();
            fireData.m_BulletPrefab = editableAncillaryPrefab.GetComponent<WeaponRound>();
            if (fireData.m_BulletPrefab == null)
            {
                ProjectileParameters.logger.Fatal($"NULL BULLET PREFAB");
            }
        }

        private static Transform GetProjectilePrefab(BlockMetadata block)
        {
            return block.blockPrefab.GetComponent<FireData>().m_BulletPrefab.transform;
        }

        private static void PatchProjectile(BlockMetadata block, Transform projectile)
        {
            if (projectile != null)
            {
                ProjectileParameters projParams = projectile.gameObject.GetComponent<ProjectileParameters>();
                if (projParams == null)
                {
                    projParams = projectile.gameObject.AddComponent<ProjectileParameters>();
                    // Console.WriteLine($"ADDED PROJECTILE PARAMETERS TO {blockData.BlockID}");
                    projParams.CalculateParameters(block.blockPrefab.GetComponent<TankBlock>());
                }
            }
        }

        private static void PatchShield(BlockMetadata blockData)
        {
            blockData.blockPrefab.gameObject.AddComponent<ModuleShieldParameters>();
            // Console.WriteLine($"ADDED SHIELD PARAMETERS TO {blockData.BlockID}");
        }

        internal static void SetupChanges()
        {
            changes.Add(new Change
            {
                id = "CombatOverhaul_Shield",
                targetType = ChangeTargetType.TRANSFORM,
                condition = new ShieldConditional(),
                patcher = new Action<BlockMetadata>(PatchShield)
            });
            changes.Add(new Change
            {
                id = "CombatOverhaul_Projectiles",
                targetType = ChangeTargetType.TRANSFORM,
                condition = new ProjectileConditional(),
                targetsAncillaryPrefabs = true,
                ancillaryChanges = new List<AncillaryChange>() {
                    new AncillaryChange
                    {
                        id = "ProjectileParams",
                        AncillaryPatcher = new Action<BlockMetadata, Transform>(PatchProjectile),
                        GetAncillaryPrefab = new Func<BlockMetadata, Transform>(GetProjectilePrefab),
                        UpdateAncillaryPrefab = new Action<BlockMetadata, Transform>(ReplaceProjectilePrefab)
                    }
                }
            });
        }

        internal static void RegisterChanges()
        {
            foreach (Change change in changes)
            {
                BlockChangePatcherMod.RegisterChange(change);
            }
        }
    }
}
