using System;
using System.Collections.Generic;
using UnityEngine;
using S.Core;
namespace S.View
{
    /// <summary>Binds the authored arena. No procedural geometry is layered over the map.</summary>
    public sealed class ArenaBuilder : MonoBehaviour
    {
        ArenaDef _def = new ArenaDef();
        FoundryArena _map;
        readonly List<Pillar> _pillars = new List<Pillar>();
        public ArenaDef Def => _def;
        public FoundryArena Map => _map;
        public float HalfWidth => (_map ? _map.Width : _def.Width) * .5f;
        public float HalfDepth => (_map ? _map.Depth : _def.Depth) * .5f;
        public Vector3 PlayerSpawn => _map.PlayerSpawn.position;
        public Vector3 BossSpawn => _map.BossSpawn.position;
        public static Vector3 Center => Vector3.zero;
        public IReadOnlyList<Pillar> Pillars => _pillars;
        public void Build(ArenaDef def)
        {
            if (_map) throw new InvalidOperationException("Arena already bound.");
            if (def != null) _def = def;
            var maps = FindObjectsByType<FoundryArena>(FindObjectsInactive.Exclude);
            foreach (var map in maps)
            {
                if (map.gameObject.scene != gameObject.scene) continue;
                if (_map) throw new InvalidOperationException("More than one FoundryArena in gameplay scene.");
                _map = map;
            }
            if (!_map)
            {
                var prefab = SRes.Get<GameObject>(FoundryArena.ActiveAssetName);
                if (!prefab) throw new InvalidOperationException(SRes.Diagnose(FoundryArena.ActiveAssetName));
                _map = Instantiate(prefab, transform).GetComponent<FoundryArena>();
            }
            _map.BeginRun();
            _map.ApplyEnvironment(GameRoot.Instance != null ? GameRoot.Instance.Cam : null);
        }
        public Vector3 SpawnPoint(int slot, int totalSlots)
        {
            int n = _map.EnemySpawns.Length;
            return _map.EnemySpawns[((slot % n) + n) % n].position;
        }
        public Vector3 PickSpawnPoint(Vector3 playerPos, float minDist, System.Random rng)
        {
            int n = _map.EnemySpawns.Length;
            int start = rng != null ? rng.Next(n) : UnityEngine.Random.Range(0, n);
            Vector3 best = SpawnPoint(start, n); float bestD = -1f;
            for (int i = 0; i < n; i++)
            {
                var p = SpawnPoint(start + i, n); float d = (p - playerPos).sqrMagnitude;
                if (d > minDist * minDist) return p;
                if (d > bestD) { bestD = d; best = p; }
            }
            return best;
        }
        public void SpawnPillars()
        {
            if (_pillars.Count > 0 || _map.EnergyPillars.Length == 0) return;
            var def = GameRoot.Instance != null ? GameRoot.Instance.Data?.Boss : null;
            int count = def != null ? def.Pillars : BossState.DefaultPillars;
            float hp = def != null ? def.PillarHp : BossState.DefaultPillarHp;
            if (count != _map.EnergyPillars.Length)
                throw new InvalidOperationException("Boss pillar count does not match map sockets.");
            for (int i = 0; i < count; i++)
            {
                var pillar = _map.EnergyPillars[i];
                pillar.Index = i; pillar.Hp = pillar.MaxHp = hp;
                pillar.gameObject.SetActive(true);
                _pillars.Add(pillar);
            }
        }
    }
}
