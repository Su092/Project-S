using System;
using System.Collections.Generic;

namespace S.Core
{
    /// <summary>三选一候选。</summary>
    public struct Offer
    {
        public string Id;
        public bool IsBullet;
        public Rarity Rarity;
    }

    /// <summary>
    /// 局内状态：经验、等级、威胁值、背包、掉落。
    /// 零 Unity 依赖、只用 mulberry32 + 种子 —— 同种子同操作结果一致（技术设计 §4）。
    /// 规则出处：docs/gameplay-spec.md §4 §5 §6
    /// </summary>
    public sealed class RunState
    {
        public const int MaxLevel = 20;
        public const float ThreatMax = 100f;

        public uint Seed;
        uint _rng;

        public int Level = 1;
        public int Xp;                 // 当前等级内已积累
        public float Threat;           // 0..100
        public int Kills;
        public bool ReachedMaxLevel => Level >= MaxLevel;

        public readonly List<ChainItem> Bag = new List<ChainItem>();
        public int BagCapacity = 24;

        /// <summary>本局已出现过的史诗件（全局唯一）。</summary>
        public readonly HashSet<string> UsedEpics = new HashSet<string>();

        readonly SData _data;

        public RunState(SData data, uint seed, int bagCapacity = 24)
        {
            _data = data;
            Seed = seed;
            _rng = seed == 0 ? 0x9E3779B9u : seed;
            BagCapacity = bagCapacity;
        }

        // ---------- 随机 ----------

        /// <summary>mulberry32。确定性来源，不要换成 UnityEngine.Random。</summary>
        public float NextFloat()
        {
            _rng += 0x6D2B79F5u;
            uint t = _rng;
            t = (t ^ (t >> 15)) * (t | 1u);
            t ^= t + (t ^ (t >> 7)) * (t | 61u);
            return ((t ^ (t >> 14)) >> 8) / 16777216f;
        }

        public int NextInt(int maxExclusive) =>
            maxExclusive <= 0 ? 0 : (int)(NextFloat() * maxExclusive) % maxExclusive;

        // ---------- 经验与等级 ----------

        /// <summary>从 level 升到 level+1 所需经验：need(L) = 2L² + 5L + 8。</summary>
        public static int NeedFor(int level) => 2 * level * level + 5 * level + 8;

        /// <summary>吃经验。返回本次升了几级（调用方据此弹几次三选一）。</summary>
        public int AddXp(int amount)
        {
            if (amount <= 0 || ReachedMaxLevel) return 0;
            Xp += amount;
            int ups = 0;
            while (!ReachedMaxLevel && Xp >= NeedFor(Level))
            {
                Xp -= NeedFor(Level);
                Level++;
                ups++;
            }
            if (ReachedMaxLevel) Xp = 0;
            return ups;
        }

        /// <summary>到 level 级所需的累计经验（用于 UI 与验收）。</summary>
        public static int TotalXpFor(int level)
        {
            int sum = 0;
            for (int l = 1; l < level; l++) sum += NeedFor(l);
            return sum;
        }

        // ---------- 威胁 ----------

        public void ThreatTick(float dt)
        {
            if (dt <= 0f) return;
            Threat += 1.2f * dt;
            if (Threat > ThreatMax) Threat = ThreatMax;
        }

        public void AddThreat(float v)
        {
            Threat += v;
            if (Threat < 0f) Threat = 0f;
            if (Threat > ThreatMax) Threat = ThreatMax;
        }

        /// <summary>敌人缩放系数（gameplay-spec §3）。</summary>
        public float HpScale => 1f + 0.004f * Threat;
        public float DmgScale => 1f + 0.0025f * Threat;

        public bool EliteWaveUnlocked => Threat >= 35f;
        public bool EliteEveryWave => Threat >= 65f;
        public bool HuntActive => Threat >= 88f;

        // ---------- 背包 ----------

        public bool TryPick(string id)
        {
            var item = _data.MakeItem(id);
            if (item == null) return false;
            if (Bag.Count >= BagCapacity) return false;
            Bag.Add(item);
            return true;
        }

        public bool RemoveFromBag(ChainItem item) => Bag.Remove(item);

        public ChainItem FindInBag(string id)
        {
            for (int i = 0; i < Bag.Count; i++)
                if (Bag[i].Id == id) return Bag[i];
            return null;
        }

        /// <summary>丢弃一个背包件（背包满时顶替用）。</summary>
        public bool Discard(ChainItem item) => Bag.Remove(item);

        // ---------- 三选一 ----------

        /// <summary>
        /// 生成一次三选一。不重复同一个 id；史诗全局唯一（本局只出现一次）。
        /// 权重来自 sdata.rarityWeights（gameplay-spec §5.3 / loadout-spec §7.4）。
        /// </summary>
        public List<Offer> Roll(int count = 3)
        {
            var offers = new List<Offer>(count);
            var picked = new HashSet<string>();

            for (int attempt = 0; attempt < count * 12 && offers.Count < count; attempt++)
            {
                var pool = BuildWeightedPool(picked);
                if (pool.Count == 0) break;
                int total = 0;
                for (int i = 0; i < pool.Count; i++) total += pool[i].Weight;
                if (total <= 0) break;

                int roll = NextInt(total);
                int acc = 0;
                for (int i = 0; i < pool.Count; i++)
                {
                    acc += pool[i].Weight;
                    if (roll < acc)
                    {
                        var o = pool[i].Offer;
                        if (picked.Add(o.Id))
                        {
                            offers.Add(o);
                            if (o.Rarity == Rarity.Epic) UsedEpics.Add(o.Id);
                        }
                        break;
                    }
                }
            }
            return offers;
        }

        struct PoolEntry
        {
            public Offer Offer;
            public int Weight;
        }

        List<PoolEntry> BuildWeightedPool(HashSet<string> exclude)
        {
            var list = new List<PoolEntry>();

            foreach (var kv in _data.Bullets)
            {
                var b = kv.Value;
                if (b.Rarity == Rarity.Starter) continue;      // 起手件不进抽取池
                if (exclude.Contains(b.Id)) continue;
                if (b.Rarity == Rarity.Epic && UsedEpics.Contains(b.Id)) continue;
                list.Add(new PoolEntry
                {
                    Offer = new Offer { Id = b.Id, IsBullet = true, Rarity = b.Rarity },
                    Weight = WeightOf(b.Rarity),
                });
            }

            foreach (var kv in _data.Modifiers)
            {
                var m = kv.Value;
                if (exclude.Contains(m.Id)) continue;
                if (m.Rarity == Rarity.Epic && UsedEpics.Contains(m.Id)) continue;
                list.Add(new PoolEntry
                {
                    Offer = new Offer { Id = m.Id, IsBullet = false, Rarity = m.Rarity },
                    Weight = WeightOf(m.Rarity),
                });
            }

            return list;
        }

        int WeightOf(Rarity r) =>
            _data.RarityWeights.TryGetValue(r, out var w) ? w : 10;
    }
}
