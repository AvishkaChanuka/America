using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace AmericanBattlefront.Core
{
    // ── Nested data classes (Inspector-friendly) ─────────────────────────────

    [Serializable]
    public class WaveDefinition
    {
        [Tooltip("How many enemies to spawn in this wave total.")]
        public int TotalEnemies = 5;

        [Tooltip("Maximum enemies alive at once during this wave.")]
        public int MaxAliveAtOnce = 3;

        [Tooltip("Seconds between each individual spawn event.")]
        public float SpawnInterval = 3f;

        [Tooltip("Seconds to wait before this wave begins (after previous wave cleared).")]
        public float PreWaveDelay = 2f;
    }

    [Serializable]
    public class EnemyEntry
    {
        [Tooltip("Enemy prefab. Must have EnemyHealth, EnemyBrain, NavMeshAgent.")]
        public GameObject Prefab;

        [Tooltip("Relative spawn weight (higher = chosen more often).")]
        [Range(1, 10)] public int Weight = 1;

        [Header("Combat Stats")]
        public float MaxHealth = 50f;
        public float MoveSpeed = 2.5f;
        [Tooltip("Distance at which this enemy begins navigating toward the player.")]
        public float DetectionRange = 15f;
        [Tooltip("Distance at which melee attack triggers.")]
        public float AttackRange = 1.8f;
        public float AttackDamage = 10f;
        [Tooltip("Seconds between attacks.")]
        public float AttackCooldown = 2f;
        [Tooltip("Seconds to wait after attacking before approaching again.")]
        public float RecoverDuration = 0.5f;
        [Tooltip("XP awarded on death. 0 = use LevelConfig.DefaultXPPerKill.")]
        public float XPReward = 0f;
        [Tooltip("If true, enemy must have clear line-of-sight to attack.")]
        public bool RequireLineOfSight = false;
    }


    [Serializable]
    public class PropEntry
    {
        [Tooltip("Static prop prefab (cover, barricade, etc.).")]
        public GameObject Prefab;

        [Tooltip("Minimum number to place.")]
        public int MinCount = 1;

        [Tooltip("Maximum number to place.")]
        public int MaxCount = 5;

        [Tooltip("Uniform scale range applied at placement.")]
        public Vector2 ScaleRange = new Vector2(0.9f, 1.1f);

        [Tooltip("Minimum distance this prop must keep from all other props.")]
        public float MinSeparation = 1.5f;

        [Tooltip("If true, include in NavMesh static bake.")]
        public bool IsNavigationStatic = true;
    }

    [Serializable]
    public class ProtectedZone
    {
        [Tooltip("World-space centre of the protected zone (relative to arena origin).")]
        public Vector3 Centre;

        [Tooltip("No prop may be placed within this radius.")]
        public float Radius = 2f;
    }

    [Serializable]
    public class WeaponConfig
    {
        [Tooltip("Weapon prefab. Must have WeaponController component.")]
        public GameObject Prefab;

        [Tooltip("Damage per hit.")]
        public float Damage = 25f;

        [Tooltip("Maximum raycast range in metres.")]
        public float Range = 50f;

        [Tooltip("Minimum seconds between shots.")]
        public float FireInterval = 0.5f;

        [Tooltip("Rounds per magazine.")]
        public int MagazineSize = 8;

        [Tooltip("Total reserve ammo (not counting current magazine).")]
        public int ReserveAmmo = 24;

        [Tooltip("Seconds to complete a reload.")]
        public float ReloadTime = 2.5f;

        [Tooltip("Half-angle spread in degrees (0 = perfectly accurate).")]
        [Range(0f, 15f)] public float SpreadAngle = 1f;

        [Tooltip("LayerMask for valid hit targets. Exclude player, UI, panorama.")]
        public LayerMask HitLayers = Physics.AllLayers;
    }

    [Serializable]
    public class PlayerConfig
    {
        [Tooltip("Maximum player health.")]
        public float MaxHealth = 100f;

        [Tooltip("Optional invincibility window after taking damage (seconds). 0 = none.")]
        public float DamageCooldown = 0f;

        /// <summary>Restart policy: restore health to max, or keep current health.</summary>
        public bool RestoreHealthOnRestart = true;

        /// <summary>Restart policy: refill ammo on restart.</summary>
        public bool RestoreAmmoOnRestart = true;
    }

    [Serializable]
    public class ArenaConfig
    {
        [Tooltip("Prefab containing the arena floor, walls and fixed geometry. Required.")]
        public GameObject ArenaPrefab;

        [Tooltip("World-space player start position within the arena.")]
        public Vector3 PlayerStartPosition = Vector3.zero;

        [Tooltip("Horizontal dimensions of the playable area (width x depth).")]
        public Vector2 ArenaDimensions = new Vector2(20f, 20f);
    }

    [Serializable]
    public class RestartConfig
    {
        public enum SeedPolicy { ReuseSameSeed, GenerateNewSeed }

        [Tooltip("What to do with the random seed when restarting.")]
        public SeedPolicy LayoutSeedPolicy = SeedPolicy.ReuseSameSeed;
    }

    // ── Main ScriptableObject ────────────────────────────────────────────────

    /// <summary>
    /// One ScriptableObject = one playable level.
    /// Create via: Assets/Game/Configs/Levels → right-click → Create → AmericanBattlefront → Level Config
    /// </summary>
    [CreateAssetMenu(
        fileName = "CFG_Level_01",
        menuName = "AmericanBattlefront/Level Config",
        order = 1)]
    public class LevelConfig : ScriptableObject
    {
        // ── Identity ─────────────────────────────────────────────────────────
        [Header("─── Identity ───────────────────────────────────────")]
        [Tooltip("Unique identifier. e.g. 'level_01_colonial'. Must not be empty.")]
        public string LevelId = "level_01";

        [Tooltip("Shown in briefing panel.")]
        public string DisplayName = "New Level";

        public string HistoricalPeriod = "";
        public string DateRange = "";
        public string Location = "";

        [TextArea(3, 6)]
        public string ShortBriefing = "";

        [TextArea(2, 4)]
        public string ObjectiveText = "";

        [TextArea(2, 4)]
        public string ControlHelp = "Left stick: move  |  Right stick: turn  |  Trigger: fire  |  B/Y: reload";

        [TextArea(3, 8)]
        public string HistoricalNotes = "";

        [TextArea(2, 5)]
        public string SourceNotes = "";

        // ── Backdrop ─────────────────────────────────────────────────────────
        [Header("─── Backdrop ───────────────────────────────────────")]
        [Tooltip("Material applied to the panorama sphere. Visual only — no collision.")]
        public Material PanoramaMaterial;

        [Tooltip("Y-axis rotation offset for the panorama (degrees).")]
        public float PanoramaRotation = 0f;

        [Tooltip("Exposure multiplier for the panorama material.")]
        [Range(0.1f, 3f)] public float PanoramaExposure = 1f;

        // ── Arena ─────────────────────────────────────────────────────────────
        [Header("─── Arena ──────────────────────────────────────────")]
        public ArenaConfig Arena = new ArenaConfig();

        // ── Randomized Layout ─────────────────────────────────────────────────
        [Header("─── Randomized Layout ──────────────────────────────")]
        [Tooltip("Deterministic random seed for prop placement.")]
        public int Seed = 42;

        [Tooltip("Maximum placement attempts per prop instance before skipping.")]
        public int MaxAttemptsPerProp = 30;

        [Tooltip("Maximum total props to place (hard cap for performance).")]
        public int MaxTotalProps = 40;

        [Tooltip("Props to place in this level.")]
        public List<PropEntry> Props = new List<PropEntry>();

        // ── Protected Zones ───────────────────────────────────────────────────
        [Header("─── Protected Zones ────────────────────────────────")]
        [Tooltip("Areas where no prop may be placed (player start, spawn zones, nav lanes).")]
        public List<ProtectedZone> ProtectedZones = new List<ProtectedZone>();

        // ── Weapon ────────────────────────────────────────────────────────────
        [Header("─── Weapon ─────────────────────────────────────────")]
        public WeaponConfig Weapon = new WeaponConfig();

        // ── Enemies ───────────────────────────────────────────────────────────
        [Header("─── Enemies ────────────────────────────────────────")]
        [Tooltip("Pool of enemy types that can appear in this level.")]
        public List<EnemyEntry> Enemies = new List<EnemyEntry>();

        // ── Waves ─────────────────────────────────────────────────────────────
        [Header("─── Waves ──────────────────────────────────────────")]
        public List<WaveDefinition> Waves = new List<WaveDefinition>();

        // ── Player ────────────────────────────────────────────────────────────
        [Header("─── Player ─────────────────────────────────────────")]
        public PlayerConfig Player = new PlayerConfig();

        // ── XP / Rewards ──────────────────────────────────────────────────────
        [Header("─── Rewards ────────────────────────────────────────")]
        [Tooltip("Default XP per enemy kill (enemy can override this in its own config).")]
        public float DefaultXPPerKill = 10f;

        // ── Restart ───────────────────────────────────────────────────────────
        [Header("─── Restart ────────────────────────────────────────")]
        public RestartConfig Restart = new RestartConfig();

        // ── Performance Caps ──────────────────────────────────────────────────
        [Header("─── Performance Caps ────────────────────────────────")]
        [Tooltip("Hard cap on simultaneous active enemies (Quest budget).")]
        public int MaxActiveEnemies = 8;

        // ── Validation ────────────────────────────────────────────────────────
        /// <summary>
        /// Returns null if valid, or a human-readable error string if not.
        /// Called by GameBootstrap before starting setup.
        /// </summary>
        public string Validate()
        {
            if (string.IsNullOrWhiteSpace(LevelId))
                return "LevelConfig.LevelId must not be empty.";

            if (string.IsNullOrWhiteSpace(DisplayName))
                return "LevelConfig.DisplayName must not be empty.";

            if (Arena.ArenaPrefab == null)
                return $"[{LevelId}] Arena.ArenaPrefab is not assigned.";
            
            if (Weapon.Prefab == null)
                return $"[{LevelId}] Weapon.Prefab is not assigned.";

            if (Weapon.Damage <= 0f)
                return $"[{LevelId}] Weapon.Damage must be > 0.";

            if (Weapon.MagazineSize <= 0)
                return $"[{LevelId}] Weapon.MagazineSize must be > 0.";

            if (Weapon.Range <= 0f)
                return $"[{LevelId}] Weapon.Range must be > 0.";

            if (Player.MaxHealth <= 0f)
                return $"[{LevelId}] Player.MaxHealth must be > 0.";

            if (Waves == null || Waves.Count == 0)
                return $"[{LevelId}] At least one WaveDefinition is required.";

            for (int i = 0; i < Waves.Count; i++)
            {
                if (Waves[i].MaxAliveAtOnce <= 0)
                    return $"[{LevelId}] Wave[{i}].MaxAliveAtOnce must be > 0.";

                if (Waves[i].TotalEnemies <= 0)
                    return $"[{LevelId}] Wave[{i}].TotalEnemies must be > 0.";
            }

            /*if (Enemies == null || Enemies.Count == 0)
                return $"[{LevelId}] At least one EnemyEntry is required.";

            foreach (var e in Enemies)
            {
                if (e.Prefab == null)
                    return $"[{LevelId}] An EnemyEntry has a null Prefab.";
            }
            */

            if (MaxActiveEnemies <= 0)
                return $"[{LevelId}] MaxActiveEnemies must be > 0.";

            return null; // valid
        }
    }
}
