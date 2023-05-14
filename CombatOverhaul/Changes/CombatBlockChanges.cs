using BlockChangePatcher;
using CombatOverhaul.BlockModules;
using CombatOverhaul.ProjectileComponents;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using HarmonyLib;

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

        private static readonly FieldInfo m_Explosion = AccessTools.Field(typeof(Projectile), "m_Explosion");
        private static readonly FieldInfo m_DamageType = AccessTools.Field(typeof(WeaponRound), "m_DamageType");
        private static void PatchProjectileAddParams(BlockMetadata block, Transform projectilePrefab)
        {
            if (projectilePrefab != null)
            {
                ProjectileParameters projParams = projectilePrefab.gameObject.GetComponent<ProjectileParameters>();
                if (projParams == null)
                {
                    projParams = projectilePrefab.gameObject.AddComponent<ProjectileParameters>();
                    // Console.WriteLine($"ADDED PROJECTILE PARAMETERS TO {blockData.BlockID}");
                    projParams.CalculateParameters(block.blockPrefab.GetComponent<TankBlock>());
                }
            }
        }

        private static void PatchProjectileAddPiercing(BlockMetadata block, Transform projectilePrefab)
        {
            if (projectilePrefab != null)
            {
                Projectile projectile = projectilePrefab.GetComponent<Projectile>();
                m_DamageType.SetValue(projectile, ManDamage.DamageType.Ballistic);
            }
        }

        private static readonly BlockTypes[] VanillaBlocksEnablePiercing = new BlockTypes[]
        {
            BlockTypes.GSOBigBertha_845,
            BlockTypes.GSOMegatonLong_242,
            BlockTypes.HE_CannonBattleship_216,
            BlockTypes.HE_Cannon_Naval_826,
            // BlockTypes.HE_Cannon_Naval_AC_NPC_826,
            BlockTypes.HE_CannonTurret_Short_525,
            // BlockTypes.HE_CannonTurret_327,
        };

        private static readonly string[] ModdedBlocksEnablePiercing = new string[]
        {
            "RabisBlocks:GSO_LongBertha",
            "RabisBlocks:HE_ShredderCannonSingle",
            "RabisBlocks:HE_HeavyMG",
            "Black Labs:HE_Muspell_Railgun",
            "Black Labs:HE_Mjolnir_Railgun",
            "Black Labs:HE_Gungnir_Railgun",
            "Black Labs:HE_Jormungand_Railgun",
            "Black Labs:HE_Blutgang_Howitzer",
            "Black Labs:HE_cannon_naglfaar2",
            "Black Labs:HE_Nidhogg_Railcannon",
            "Black Labs:HE_Ragnarok_Railcannon_Battery",
            "HE Plus Additional Block Pack:HE+_DualCannon",
            "HE Plus Additional Block Pack:HE+_FortCannon",
            "HE Plus Additional Block Pack:HE+_Fixed_Gatring",
            "Naval Guns:HE_Triple_Barrel_Large_FX_Update",
            "Air Guns:Phalanx_Broadside",
            "Air Guns:HE_Broadside"
        };

        private static void PatchShield(BlockMetadata blockData)
        {
            ModuleShieldParameters shieldParams = blockData.blockPrefab.GetComponent<ModuleShieldParameters>();
            if (shieldParams == null)
            {
                blockData.blockPrefab.gameObject.AddComponent<ModuleShieldParameters>();
                // Console.WriteLine($"ADDED SHIELD PARAMETERS TO {blockData.BlockID}");
            }
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
                        AncillaryPatcher = new Action<BlockMetadata, Transform>(PatchProjectileAddParams),
                        GetAncillaryPrefab = new Func<BlockMetadata, Transform>(GetProjectilePrefab),
                        UpdateAncillaryPrefab = new Action<BlockMetadata, Transform>(ReplaceProjectilePrefab)
                    }
                }
            });
            foreach (BlockTypes vanillaBlock in VanillaBlocksEnablePiercing)
            {
                changes.Add(new Change
                {
                    id = $"CombatOverhaul_Piercing_{vanillaBlock}",
                    targetType = ChangeTargetType.VANILLA_ID,
                    condition = new VanillaIDConditional(vanillaBlock),
                    targetsAncillaryPrefabs = true,
                    ancillaryChanges = new List<AncillaryChange>() {
                    new AncillaryChange
                    {
                        id = "ProjectileDamageType",
                        AncillaryPatcher = new Action<BlockMetadata, Transform>(PatchProjectileAddPiercing),
                        GetAncillaryPrefab = new Func<BlockMetadata, Transform>(GetProjectilePrefab),
                        UpdateAncillaryPrefab = new Action<BlockMetadata, Transform>(ReplaceProjectilePrefab)
                    }
                }
                });
            }
            foreach (string blockID in ModdedBlocksEnablePiercing)
            {
                changes.Add(new Change
                {
                    id = $"CombatOverhaul_Piercing_{blockID}",
                    targetType = ChangeTargetType.BLOCK_ID,
                    condition = new BlockIDConditional(blockID),
                    targetsAncillaryPrefabs = true,
                    ancillaryChanges = new List<AncillaryChange>() {
                    new AncillaryChange
                    {
                        id = "ProjectileDamageType",
                        AncillaryPatcher = new Action<BlockMetadata, Transform>(PatchProjectileAddPiercing),
                        GetAncillaryPrefab = new Func<BlockMetadata, Transform>(GetProjectilePrefab),
                        UpdateAncillaryPrefab = new Action<BlockMetadata, Transform>(ReplaceProjectilePrefab)
                    }
                }
                });
            }
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
