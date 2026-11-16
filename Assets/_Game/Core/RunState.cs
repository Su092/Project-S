using System;
using System.Collections.Generic;

namespace S.Core
{
    /// <summary>三选一候选。</summary>
    public struct Offer
    {
        public string Id;
        public bool IsBullet;
        public bool IsGun;
        public Rarity Rarity;
    }

    /// <summary>
    /// 局内状态：经验、等级、威胁值、背包、抽取。
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
        public int Xp;
        public float Threat;
        public int Kills;
        public bool ReachedMaxLevel => Level >= MaxLevel;

        public readonly List<ChainItem> Bag = new List<ChainItem>();
        public int BagCapacity = 44;

        /// <summary>本局已出现过的史诗件（记录用途，允许再次抽取用于合成）。</summary>
        public readonly HashSet<string> UsedEpics = new HashSet<string>();

        readonly SData _data;
        readonly float _hpScalePerThreat;
        readonly float _dmgScalePerThreat;
        readonly float _huntAtThreat;
        readonly int _salvageCommon;
        readonly int _salvageRare;
        readonly int _salvageEpic;

        public RunState(SData data, uint seed, int bagCapacity = 44)
        {
            _data = data;
            Seed = seed;
            _rng = seed == 0 ? 0x9E3779B9u : seed;

            // 这些系数原先写死在这里。搬进 Player.tsv 后仍要走默认值：
            // 断言用的 SData 可能是手工拼的最小集，缺字段不该让规则变成 0。
            var p = data != null ? data.Player : null;
            BagCapacity = bagCapacity > 0 ? bagCapacity : (p != null && p.BagSize > 0 ? p.BagSize : 44);
            _hpScalePerThreat = p != null && p.HpScalePerThreat > 0f ? p.HpScalePerThreat : 0.008f;
            _dmgScalePerThreat = p != null && p.DmgScalePerThreat > 0f ? p.DmgScalePerThreat : 0.004f;
            _huntAtThreat = p != null && p.HuntAtThreat > 0f ? p.HuntAtThreat : 88f;
            _salvageCommon = p != null && p.SalvageCommon > 0 ? p.SalvageCommon : 3;
            _salvageRare = p != null && p.SalvageRare > 0 ? p.SalvageRare : 6;
            _salvageEpic = p != null && p.SalvageEpic > 0 ? p.SalvageEpic : 9;
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

        /// <summary>
        /// 从 level 升到 level+1 所需经验：`A·L² + B·L + C`，系数来自 Player.tsv。
        /// 默认 2/5/8 与原 `2L² + 5L + 8` 完全一致。
        /// </summary>
        public int NeedFor(int level) => _data != null ? _data.XpNeedFor(level) : 2 * level * level + 5 * level + 8;

        /// <summary>吃经验。返回本次升了几级。</summary>
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

        public int TotalXpFor(int level)
        {
            int sum = 0;
            for (int l = 1; l < level; l++) sum += NeedFor(l);
            return sum;
        }

        // ---------- 威胁 ----------

        /// <summary>把威胁值同步为时间轴给出的值（gameplay-spec §4）。</summary>
        public void SyncThreat(float value)
        {
            Threat = value < 0f ? 0f : value > ThreatMax ? ThreatMax : value;
        }

        /// <summary>
        /// 敌人缩放系数（gameplay-spec §3）。系数可在 Player.tsv 覆盖（`hpScalePerThreat`）。
        /// </summary>
        public float HpScale => 1f + _hpScalePerThreat * Threat;
        public float DmgScale => 1f + _dmgScalePerThreat * Threat;

        public bool HuntActive => Threat >= _huntAtThreat;

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

        public bool Discard(ChainItem item) => Bag.Remove(item);

        /// <summary>
        /// 分解配件换经验（对齐原型：普通 3 / 稀有 6 / 史诗 9）。
        /// 玩家清背包的唯一手段 —— 不设「丢弃即消失」，否则垃圾件会一直占格。
        /// </summary>
        public int Salvage(ChainItem item)
        {
            if (item == null) return 0;
            if (!Bag.Remove(item)) return 0;
            int xp = item.Rarity switch
            {
                Rarity.Epic => _salvageEpic,
                Rarity.Rare => _salvageRare,
                _ => _salvageCommon,
            };
            AddXp(xp);
            return xp;
        }

        // ---------- 三选一 ----------

        /// <summary>
        /// 生成一次三选一。不重复同一个 id；史诗允许跨轮重复，以支持合成。
        /// </summary>
        public const float GunOfferChance = 0.10f;

        public List<Offer> Roll(int count = 3, ISet<string> ownedGuns = null)
        {
            var offers = new List<Offer>(count);
            var picked = new HashSet<string>();
            if (count > 0 && ownedGuns != null)
            {
                var guns = _data.GunOrder.FindAll(g => !ownedGuns.Contains(g.Id));
                if (guns.Count > 0 && NextFloat() < GunOfferChance)
                    offers.Add(new Offer { Id = guns[NextInt(guns.Count)].Id, IsGun = true, Rarity = Rarity.Rare });
            }

            for (int attempt = 0; attempt < count * 16 && offers.Count < count; attempt++)
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

            for (int i = 0; i < _data.BulletOrder.Count; i++)
            {
                var b = _data.BulletOrder[i];
                if (exclude.Contains(b.Id)) continue;
                list.Add(new PoolEntry
                {
                    Offer = new Offer { Id = b.Id, IsBullet = true, Rarity = b.Rarity },
                    Weight = WeightOf(b.Rarity),
                });
            }

            for (int i = 0; i < _data.ModifierOrder.Count; i++)
            {
                var m = _data.ModifierOrder[i];
                if (exclude.Contains(m.Id)) continue;
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
