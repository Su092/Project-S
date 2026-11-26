using System.Collections.Generic;

namespace S.Core
{
    /// <summary>
    /// 局外成长存档：`shard` 货币 + 已解锁项 + 图鉴。
    ///
    /// 谱桌等级由所有角色共享。
    ///
    /// 序列化用极简 key=value 文本，不引 JSON 库（Core 需零 Unity 依赖，
    /// 且存档结构简单，手写读写比引第三方更可控）。
    /// </summary>
    public sealed class MetaProgress
    {
        public const string ShardKey = "shard";
        public const string UnlockPrefix = "unlock.";
        public const string CodexPrefix = "codex.";

        /// <summary>局外货币。按每局进度结算。</summary>
        public int Shard;
        public int Performer;
        public const int MaxTraining = 5;
        public readonly int[] Training = new int[3];

        public int TrainingCost(int track) => 2 + Training[track] * 2;

        public bool Train(int track)
        {
            if (track < 0 || track >= Training.Length || Training[track] >= MaxTraining) return false;
            int price = TrainingCost(track);
            if (Shard < price) return false;
            Shard -= price;
            Training[track]++;
            return true;
        }

        /// <summary>已解锁项 id 集合。</summary>
        public readonly HashSet<string> Unlocked = new HashSet<string>();

        /// <summary>图鉴：见过的配件 id → 见过的次数（仅展示用）。</summary>
        public readonly Dictionary<string, int> Codex = new Dictionary<string, int>();

        /// <summary>历史最高单发伤害（图鉴展示用）。</summary>
        public float BestSingleHit;

        /// <summary>通关次数。</summary>
        public int Clears;

        // ---------------- 解锁表 ----------------

        public struct UnlockDef
        {
            public string Id;
            public string Name;
            public int Cost;
            public string Desc;
        }

        /// <summary>
        /// 全部可解锁项。成本按规格 §8：起手枪 3 shard、起始配件 2 shard。
        ///
        /// 注意：规格原文是「起手枪池 +2（3 把扩到 5 把）」，但初版只有 3 把枪，
        /// 而玩家已能选 2 把起手。所以这里实现为「解锁第 3 把枪可入起手池」，
        /// 效果等价（起手可选数 +1），且不需要凭空多造两把枪。
        /// </summary>
        public static readonly UnlockDef[] Catalog =
        {
            new UnlockDef
            {
                Id = "gun_third", Name = "第三把起手枪", Cost = 3,
                Desc = "起手可选枪从 2 把扩到 3 把（局内仍只带 2 把）",
            },
            new UnlockDef
            {
                Id = "start_dmg_up", Name = "起始：校准套件", Cost = 2,
                Desc = "开局背包自带 1 个校准套件（伤害 ×1.35）",
            },
            new UnlockDef
            {
                Id = "start_spread_down", Name = "起始：稳定器", Cost = 2,
                Desc = "开局背包自带 1 个稳定器（散射 ×0.60）",
            },
            new UnlockDef
            {
                Id = "start_mag", Name = "起始：弹匣扩容", Cost = 2,
                Desc = "开局背包自带 1 个弹匣扩容（弹匣 +12）",
            },
            new UnlockDef
            {
                Id = "start_life_up", Name = "起始：延时引信", Cost = 2,
                Desc = "开局背包自带 1 个延时引信（存在时间 +0.5s）",
            },
        };

        public static UnlockDef FindUnlock(string id)
        {
            foreach (var u in Catalog) if (u.Id == id) return u;
            return new UnlockDef { Id = id, Name = "?", Cost = 0, Desc = "" };
        }

        // ---------------- 经济 ----------------

        /// <summary>
        /// 按完整存活分钟和提升等级结算，通关额外奖励一枚。
        /// </summary>
        public int FinishRun(bool won, float bestHit, float seconds = 0, int level = 1)
        {
            int reward = System.Math.Max(0, (int)(seconds / 60)) + System.Math.Max(0, level - 1) + (won ? 1 : 0);
            Shard += reward;
            if (won) Clears++;
            if (bestHit > BestSingleHit) BestSingleHit = bestHit;
            return reward;
        }

        public bool CanAfford(string unlockId)
        {
            var u = FindUnlock(unlockId);
            return u.Cost > 0 && Shard >= u.Cost && !IsUnlocked(unlockId);
        }

        public bool IsUnlocked(string unlockId) => Unlocked.Contains(unlockId);

        /// <summary>
        /// 购买解锁。返回 null 表示成功，否则是拒绝码。
        /// </summary>
        public string Purchase(string unlockId)
        {
            var u = FindUnlock(unlockId);
            if (u.Cost <= 0) return "bad-id";
            if (IsUnlocked(unlockId)) return "already";
            if (Shard < u.Cost) return "poor";

            Shard -= u.Cost;
            Unlocked.Add(unlockId);

            // 起始配件类解锁后，开局会自动带上对应配件（见 StartingItems）
            return null;
        }

        /// <summary>
        /// 开局应自动放进背包的配件 id 列表（由已解锁的起始配件项决定）。
        /// </summary>
        public List<string> StartingItems()
        {
            var list = new List<string>();
            if (IsUnlocked("start_dmg_up")) list.Add("m_dmg_up");
            if (IsUnlocked("start_spread_down")) list.Add("m_spread_down");
            if (IsUnlocked("start_mag")) list.Add("m_mag");
            if (IsUnlocked("start_life_up")) list.Add("m_life_up");
            return list;
        }

        /// <summary>起手可选枪数（未解锁第三把时为 2）。</summary>
        public int StarterGunSlots => IsUnlocked("gun_third") ? 3 : 2;

        // ---------------- 图鉴 ----------------

        /// <summary>记录一次「见到该配件」。三选一与拾取都算。</summary>
        public void RecordSeen(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return;
            Codex.TryGetValue(itemId, out int n);
            Codex[itemId] = n + 1;
        }

        /// <summary>记录一次最高单发伤害（图鉴展示用）。</summary>
        public void RecordHit(float damage)
        {
            if (damage > BestSingleHit) BestSingleHit = damage;
        }

        // ---------------- 序列化 ----------------

        public string Serialize()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"{ShardKey}={Shard}");
            sb.AppendLine($"clears={Clears}");
            sb.AppendLine($"performer={Performer}");
            for (int i = 0; i < Training.Length; i++) sb.AppendLine($"training.{i}={Training[i]}");
            sb.AppendLine($"bestHit={BestSingleHit.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}");

            foreach (var u in Unlocked)
                sb.AppendLine($"{UnlockPrefix}{u}=1");

            foreach (var kv in Codex)
                sb.AppendLine($"{CodexPrefix}{kv.Key}={kv.Value}");

            return sb.ToString();
        }

        public static MetaProgress Deserialize(string text)
        {
            var m = new MetaProgress();
            if (string.IsNullOrEmpty(text)) return m;

            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string k = line.Substring(0, eq).Trim();
                string v = line.Substring(eq + 1).Trim();

                if (k == ShardKey)
                    int.TryParse(v, out m.Shard);
                else if (k == "performer" && int.TryParse(v, out int performer))
                    m.Performer = System.Math.Clamp(performer, 0, 5);
                else if (k.StartsWith("training.") && int.TryParse(k.Substring(9), out int track) &&
                    track >= 0 && track < m.Training.Length && int.TryParse(v, out int rank))
                    m.Training[track] = System.Math.Clamp(rank, 0, MaxTraining);
                else if (k == "clears")
                    int.TryParse(v, out m.Clears);
                else if (k == "bestHit")
                    float.TryParse(v, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out m.BestSingleHit);
                else if (k.StartsWith(UnlockPrefix))
                    m.Unlocked.Add(k.Substring(UnlockPrefix.Length));
                else if (k.StartsWith(CodexPrefix))
                {
                    int.TryParse(v, out int n);
                    m.Codex[k.Substring(CodexPrefix.Length)] = n;
                }
            }
            return m;
        }
    }
}
