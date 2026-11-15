using System.Collections.Generic;
using UnityEngine;
using S.Core;

namespace S.View
{
    /// <summary>
    /// 单张竞技场的构建器：一个 120×120 的连续场地，四周实心墙、中央 Boss 台、
    /// 固定种子的掩体与四座高台。**没有门、没有分区、没有加载切换。**
    ///
    /// 为什么从「6 区串走廊」改成一张图（docs/gameplay-spec.md §2）：
    /// 分区制的代价是每段都要重新暖场，真正高压只占全程 1/3；
    /// 单图把暖场压缩成一次，玩家从第 1 分钟起就在同一套掩体里做走位决策，
    /// 构筑差异才能被连续放大。
    ///
    /// 尺寸全部来自 sdata.json 的 arena 段（SData.Arena）—— 场地大小属于规则，
    /// 改大小要连带改刷怪点密度，所以不写死在这里。
    /// </summary>
    public sealed class ArenaBuilder : MonoBehaviour
    {
        ArenaDef _def = new ArenaDef();

        public ArenaDef Def => _def;

        /// <summary>场地半宽（X 轴）。</summary>
        public float HalfWidth => _def.Width * 0.5f;

        /// <summary>场地半深（Z 轴）。</summary>
        public float HalfDepth => _def.Depth * 0.5f;

        /// <summary>玩家出生点。</summary>
        public Vector3 PlayerSpawn =>
            new Vector3(0f, 1.2f, _def.PlayerSpawnZ);

        /// <summary>场地中心（Boss 站位）。</summary>
        public static Vector3 Center => Vector3.zero;

        readonly List<Pillar> _pillars = new List<Pillar>();

        /// <summary>Boss 场的能量柱（只有 Boss 出场后才有实体）。</summary>
        public IReadOnlyList<Pillar> Pillars => _pillars;

        public void Build(ArenaDef def)
        {
            if (def != null) _def = def;

            BuildFloor();
            BuildWalls();
            BuildBossPad();
            BuildCover();
            BuildHighGround();
        }

        // ---------------- 地面与墙 ----------------

        void BuildFloor()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "ArenaFloor";
            floor.transform.SetParent(transform, false);
            // Plane 原始尺寸 10×10 m
            floor.transform.localScale = new Vector3(_def.Width / 10f, 1f, _def.Depth / 10f);

            var m = SAssets.Textured("tex_floor_metal", new Color(0.90f, 0.92f, 0.96f), 0.14f,
                tiling: _def.Width / 6f);
            SMat.Apply(floor.GetComponent<Renderer>(), m);

            // 地面导引线：四条从中场射向四边，给玩家一个「场地比想象中大」的参照
            var lineMat = SMat.Lit(new Color(0.91f, 0.64f, 0.24f), 0.1f);
            for (int i = 0; i < 4; i++)
            {
                var strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                strip.name = "GuideLine";
                strip.transform.SetParent(transform, false);
                float ang = i * 90f;
                strip.transform.localRotation = Quaternion.Euler(0f, ang, 0f);
                strip.transform.localPosition = Quaternion.Euler(0f, ang, 0f) *
                    new Vector3(0f, 0.02f, _def.Depth * 0.33f);
                strip.transform.localScale = new Vector3(0.6f, 0.04f, _def.Depth * 0.30f);
                Object.Destroy(strip.GetComponent<Collider>());
                SMat.Apply(strip.GetComponent<Renderer>(), lineMat);
            }
        }

        void BuildWalls()
        {
            var mat = SMat.Lit(new Color(0.17f, 0.19f, 0.23f), 0.15f);

            // 四面实心墙。墙必须**挡子弹**（不做门，所以不存在「开门后打通视线」的需求）
            for (int s = -1; s <= 1; s += 2)
            {
                var wx = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wx.name = "WallX";
                wx.transform.SetParent(transform, false);
                wx.transform.localPosition = new Vector3(s * HalfWidth, _def.WallHeight * 0.5f, 0f);
                wx.transform.localScale = new Vector3(1.6f, _def.WallHeight, _def.Depth);
                SMat.Apply(wx.GetComponent<Renderer>(), mat);

                var wz = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wz.name = "WallZ";
                wz.transform.SetParent(transform, false);
                wz.transform.localPosition = new Vector3(0f, _def.WallHeight * 0.5f, s * HalfDepth);
                wz.transform.localScale = new Vector3(_def.Width, _def.WallHeight, 1.6f);
                SMat.Apply(wz.GetComponent<Renderer>(), mat);
            }
        }

        void BuildBossPad()
        {
            var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pad.name = "BossPad";
            pad.transform.SetParent(transform, false);
            pad.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            pad.transform.localScale = new Vector3(_def.BossPadRadius * 2f, 0.05f, _def.BossPadRadius * 2f);
            Object.Destroy(pad.GetComponent<Collider>());
            SMat.Apply(pad.GetComponent<Renderer>(), SMat.Lit(new Color(0.34f, 0.20f, 0.30f), 0.3f));

            // 环：破防提示与 Boss 站位的地标，也让玩家一眼看出中央是「舞台」
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "BossPadRing";
            ring.transform.SetParent(transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            ring.transform.localScale = new Vector3(_def.BossPadRadius * 2f + 0.8f, 0.06f, _def.BossPadRadius * 2f + 0.8f);
            Object.Destroy(ring.GetComponent<Collider>());
            SMat.Apply(ring.GetComponent<Renderer>(), SMat.Lit(new Color(0.72f, 0.36f, 0.88f), 0.5f));
        }

        // ---------------- 掩体与高台 ----------------

        void BuildCover()
        {
            // 固定种子：同一局的掩体每次一致，便于反复验收与玩家记忆。
            // 避开中央 Boss 台与外墙内侧，否则会挡住 Boss 出场或把刷怪点封死。
            var rng = new System.Random(20261115);
            float clearCenter = _def.BossPadRadius + 8f;
            float margin = 9f;

            int placed = 0, guard = 0;
            while (placed < _def.CoverCount && guard++ < _def.CoverCount * 40)
            {
                float x = RandomRange(rng, -HalfWidth + margin, HalfWidth - margin);
                float z = RandomRange(rng, -HalfDepth + margin, HalfDepth - margin);

                // 中央留空（Boss 台 + 它的出场空间）
                if (Mathf.Sqrt(x * x + z * z) < clearCenter) continue;
                // 出生点附近留空，避免玩家一开局被卡住
                if (Mathf.Abs(x) < 6f && Mathf.Abs(z - _def.PlayerSpawnZ) < 8f) continue;

                int kind = placed % 3;
                if (kind == 0) SpawnCrate(rng, x, z);
                else if (kind == 1) SpawnBarrel(rng, x, z);
                else SpawnCoverWall(rng, x, z);

                placed++;
            }
        }

        static float RandomRange(System.Random rng, float min, float max) =>
            min + (float)rng.NextDouble() * (max - min);

        void SpawnCrate(System.Random rng, float x, float z)
        {
            var crate = new GameObject("Crate");
            crate.transform.SetParent(transform, false);
            crate.transform.localPosition = new Vector3(x, 0f, z);
            crate.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);

            var vis = SAssets.SpawnFit("Modle_Prop_Crate", crate.transform, Vector3.zero,
                0.92f, Color.white, 0.4f);
            vis.name = "Model";

            var box = crate.AddComponent<BoxCollider>();
            box.size = new Vector3(1.16f, 0.92f, 1.16f);
            box.center = new Vector3(0f, 0.46f, 0f);
        }

        void SpawnBarrel(System.Random rng, float x, float z)
        {
            var barrel = new GameObject("Barrel");
            barrel.transform.SetParent(transform, false);
            barrel.transform.localPosition = new Vector3(x, 0f, z);

            var vis = SAssets.SpawnFit("Modle_Prop_Barrel", barrel.transform, Vector3.zero,
                0.94f, Color.white, 0.4f);
            vis.name = "Model";

            var cap = barrel.AddComponent<CapsuleCollider>();
            cap.radius = 0.33f;
            cap.height = 0.94f;
            cap.center = new Vector3(0f, 0.47f, 0f);
        }

        void SpawnCoverWall(System.Random rng, float x, float z)
        {
            var wall = new GameObject("CoverWall");
            wall.transform.SetParent(transform, false);
            wall.transform.localPosition = new Vector3(x, 0f, z);
            wall.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);

            float h = RandomRange(rng, 1.4f, 2.6f);
            float w = RandomRange(rng, 5f, 8f);

            var vis = SAssets.SpawnFit("Modle_Prop_Wall", wall.transform, Vector3.zero,
                h, Color.white, 0.35f, "tex_wall_panel");
            vis.name = "Model";
            // 墙板模型是 8m 宽，按 w 再横缩
            if (SAssets.Model("Modle_Prop_Wall") != null)
                vis.transform.localScale = new Vector3(
                    vis.transform.localScale.x * (w / 8f), vis.transform.localScale.y, vis.transform.localScale.z);

            var box = wall.AddComponent<BoxCollider>();
            box.size = new Vector3(w, h, 0.5f);
            box.center = new Vector3(0f, h * 0.5f, 0f);
        }

        void BuildHighGround()
        {
            // 四座高台放在四边中点：给玩家四条等长的绕行路线。
            // 环形场地会让路线退化成同一个大圈，走位就失去选择（设计取舍见 gameplay-spec §2）。
            if (_def.HighGroundCount <= 0) return;

            var mat = SAssets.Textured("tex_floor_concrete", new Color(0.9f, 0.9f, 0.92f), 0.2f);
            for (int i = 0; i < _def.HighGroundCount; i++)
            {
                float ang = i * 360f / _def.HighGroundCount;
                var dir = new Vector3(Mathf.Sin(ang * Mathf.Deg2Rad), 0f, Mathf.Cos(ang * Mathf.Deg2Rad));
                float rad = HalfDepth * 0.68f;
                var basePos = dir * rad;

                const float h = 2.4f;
                var ledge = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ledge.name = "HighGround" + i;
                ledge.transform.SetParent(transform, false);
                ledge.transform.localPosition = basePos + Vector3.up * (h * 0.5f);
                ledge.transform.localRotation = Quaternion.Euler(0f, ang, 0f);
                ledge.transform.localScale = new Vector3(12f, h, 9f);
                SMat.Apply(ledge.GetComponent<Renderer>(), mat);

                // 台阶：让玩家能上去（没台阶的高台等于障碍物，玩家只会绕着走）
                for (int s = 0; s < 3; s++)
                {
                    float sh = h * (s + 1) / 4f;
                    var step = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    step.name = "Step";
                    step.transform.SetParent(transform, false);
                    // 台阶沿 -dir 方向逐级抬升，朝向场地内侧
                    Vector3 outDir = -dir;
                    step.transform.localPosition = basePos + outDir * (5.2f - s * 1.5f)
                        + Vector3.up * (sh * 0.5f);
                    step.transform.localRotation = Quaternion.Euler(0f, ang, 0f);
                    step.transform.localScale = new Vector3(4f, sh, 1.5f);
                    SMat.Apply(step.GetComponent<Renderer>(), mat);
                }
            }
        }

        // ---------------- 刷怪点 ----------------

        /// <summary>
        /// 第 slot 个刷怪点。沿外缘环形分布，全部落在场地内侧 6 m，
        /// 这样敌人不会从墙里挤出来，也不会出现在玩家脚下。
        /// </summary>
        public Vector3 SpawnPoint(int slot, int totalSlots)
        {
            int n = Mathf.Max(1, totalSlots > 0 ? totalSlots : _def.SpawnPoints);
            float ang = 360f / n * slot;
            float rad = Mathf.Min(HalfWidth, HalfDepth) - 6f;
            // y = 0 就是地面：敌人的 CharacterController 自己带 center(0, h/2, 0)，
            // 这里若再抬 0.9m，敌人会被放到半空再靠重力落下 —— 实测表现为
            // 「敌人从天上掉下来」，在截图里非常明显（踩过）。
            return new Vector3(
                Mathf.Sin(ang * Mathf.Deg2Rad) * rad,
                0.02f,
                Mathf.Cos(ang * Mathf.Deg2Rad) * rad);
        }

        /// <summary>
        /// 挑一个「离玩家足够远」的刷怪点，避免凭空贴脸。
        /// 返回的 y 已经抬到地面以上 0.9 m（敌人胶囊中心）。
        /// </summary>
        public Vector3 PickSpawnPoint(Vector3 playerPos, float minDist, System.Random rng)
        {
            int n = Mathf.Max(1, _def.SpawnPoints);
            int start = rng != null ? rng.Next(n) : Random.Range(0, n);
            Vector3 best = SpawnPoint(start, n);
            float bestD = -1f;

            for (int i = 0; i < n; i++)
            {
                var p = SpawnPoint((start + i) % n, n);
                float d = (p - playerPos).sqrMagnitude;
                if (d > minDist * minDist) return p;
                if (d > bestD) { bestD = d; best = p; }
            }
            return best;   // 全都不够远时给最远的那个，不返回零向量
        }

        // ---------------- Boss 场 ----------------

        /// <summary>
        /// 生成 Boss 场的四根能量柱。只在 Boss 出场时调用 —— 单图没有「Boss 区」，
        /// 柱子如果一直立在中央，普通战斗阶段会被当成掩体，破坏后玩家会以为地图坏了。
        /// </summary>
        public void SpawnPillars()
        {
            if (_pillars.Count > 0) return;

            for (int i = 0; i < BossState.PillarCount; i++)
            {
                float ang = 45f + i * 90f;
                float rad = _def.BossPadRadius * 0.72f;
                var pos = new Vector3(
                    Mathf.Cos(ang * Mathf.Deg2Rad) * rad,
                    2.6f,
                    Mathf.Sin(ang * Mathf.Deg2Rad) * rad);

                var pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pillar.name = "Pillar" + i;
                pillar.transform.SetParent(transform, false);
                pillar.transform.localPosition = pos;
                pillar.transform.localScale = new Vector3(1.7f, 2.6f, 1.7f);

                // 真实模型（缺件时保留圆柱占位）。模型原点在地面，高 4.96m。
                var vis = SAssets.SpawnFit("Modle_Prop_Pillar", pillar.transform, new Vector3(0f, -2.6f, 0f),
                    4.96f, Color.white, 0.5f);
                vis.name = "Model";
                var pr = pillar.GetComponent<Renderer>();
                if (pr != null && SAssets.Model("Modle_Prop_Pillar") != null) pr.enabled = false;
                else SMat.Apply(pr, SMat.Lit(new Color(0.62f, 0.30f, 0.82f), 0.5f));

                var pc = pillar.AddComponent<Pillar>();
                pc.Index = i;
                pc.Hp = BossState.PillarHp;
                pc.MaxHp = BossState.PillarHp;
                _pillars.Add(pc);
            }
        }
    }
}
