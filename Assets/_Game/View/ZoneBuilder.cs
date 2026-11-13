using System.Collections.Generic;
using UnityEngine;
using S.Core;

namespace S.View
{
    /// <summary>
    /// 箱庭构建器：6 个区域沿 +Z 依次排开，用「门」连接。
    /// 每区是一个独立的战斗场地（有掩体与地面），门在清区后打开。
    ///
    /// 为什么不是无缝大世界：初版优先保证「区域推进真的有成本」这一机制成立。
    /// 手工连通布局能让威胁值与补给点的节奏立刻可玩；无缝大场地留到有美术资源后。
    /// 规格见 docs/gameplay-spec.md §2。
    /// </summary>
    public sealed class ZoneBuilder : MonoBehaviour
    {
        public const float ZoneLength = 44f;      // 单区沿 Z 的长度
        public const float ZoneWidth = 34f;       // 单区宽度
        public const float WallHeight = 9f;

        readonly List<Transform> _roots = new List<Transform>();
        readonly List<GameObject> _gates = new List<GameObject>();
        readonly List<ZoneGate> _gateScripts = new List<ZoneGate>();

        public int ZoneCount => _roots.Count;

        /// <summary>第 index 区（1 起）的中心点。</summary>
        public static Vector3 CenterOf(int index) => new Vector3(0f, 0f, (index - 1) * ZoneLength);

        /// <summary>第 index 区的刷怪环半径与高度（区域外缘）。</summary>
        public static float SpawnRadius => ZoneWidth * 0.42f;

        /// <summary>构建全部区域。zones 来自 sdata.json。</summary>
        public void Build(IList<ZoneDef> zones)
        {
            for (int i = 0; i < zones.Count; i++)
            {
                int idx = i + 1;
                var root = new GameObject($"Zone{idx}_{zones[i].Name}").transform;
                root.SetParent(transform, false);
                root.position = CenterOf(idx);
                _roots.Add(root);

                BuildFloor(root, idx, zones[i].IsBoss);
                BuildWalls(root, idx, zones[i].IsBoss, zones.Count);
                if (zones[i].IsBoss) BuildBossArena(root, idx);
                else BuildCover(root, idx);
            }

            BuildGates(zones.Count);
        }

        // ---------------- 地面与墙 ----------------

        void BuildFloor(Transform root, int idx, bool boss)
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.SetParent(root, false);
            floor.transform.localScale = new Vector3(ZoneWidth / 10f, 1f, ZoneLength / 10f);

            var tint = boss
                ? new Color(0.80f, 0.62f, 0.76f)
                : Color.Lerp(new Color(0.86f, 0.89f, 0.95f), new Color(0.98f, 0.90f, 0.82f), idx / 6f);
            // 平铺次数按区域尺寸算，避免拉伸糊；材质本身按 (贴图,色调,平铺) 缓存，不共享污染
            var m = SAssets.Textured("tex_floor_metal", tint, 0.14f, tiling: ZoneWidth / 6f);
            SMat.Apply(floor.GetComponent<Renderer>(), m);

            // 区域标识条：地面上一条琥珀带
            var strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            strip.name = "ZoneStrip";
            strip.transform.SetParent(root, false);
            strip.transform.localPosition = new Vector3(0f, 0.02f, -ZoneLength * 0.5f + 3f);
            strip.transform.localScale = new Vector3(ZoneWidth * 0.55f, 0.04f, 0.5f);
            Object.Destroy(strip.GetComponent<Collider>());
            var em = SMat.Lit(boss ? new Color(0.72f, 0.36f, 0.88f) : new Color(0.91f, 0.64f, 0.24f), 0.1f);
            SMat.Apply(strip.GetComponent<Renderer>(), em);
        }

        void BuildWalls(Transform root, int idx, bool boss, int totalZones)
        {
            var mat = SMat.Lit(new Color(0.17f, 0.19f, 0.23f), 0.15f);

            // 左右墙
            for (int s = -1; s <= 1; s += 2)
            {
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = "Wall";
                wall.transform.SetParent(root, false);
                wall.transform.localPosition = new Vector3(s * ZoneWidth * 0.5f, WallHeight * 0.5f, 0f);
                wall.transform.localScale = new Vector3(1.4f, WallHeight, ZoneLength);
                SMat.Apply(wall.GetComponent<Renderer>(), mat);
            }

            // 前墙（入口侧，除 1 区）
            if (idx > 1) MakeEndWall(root, -ZoneLength * 0.5f, mat, gateWidth: 9f);
            // 后墙（出口侧，除末区）
            if (idx < totalZones) MakeEndWall(root, ZoneLength * 0.5f, mat, gateWidth: 9f);
            else
            {
                var back = GameObject.CreatePrimitive(PrimitiveType.Cube);
                back.name = "WallBack";
                back.transform.SetParent(root, false);
                back.transform.localPosition = new Vector3(0f, WallHeight * 0.5f, ZoneLength * 0.5f);
                back.transform.localScale = new Vector3(ZoneWidth, WallHeight, 1.4f);
                SMat.Apply(back.GetComponent<Renderer>(), mat);
            }
        }

        /// <summary>带门洞的端墙：中间留 gateWidth 宽的开口。</summary>
        void MakeEndWall(Transform root, float z, Material mat, float gateWidth)
        {
            float side = (ZoneWidth - gateWidth) * 0.5f;
            for (int s = -1; s <= 1; s += 2)
            {
                var seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
                seg.name = "WallEnd";
                seg.transform.SetParent(root, false);
                seg.transform.localPosition = new Vector3(s * (gateWidth * 0.5f + side * 0.5f),
                    WallHeight * 0.5f, z);
                seg.transform.localScale = new Vector3(side, WallHeight, 1.4f);
                SMat.Apply(seg.GetComponent<Renderer>(), mat);
            }

            // 门楣
            var lintel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lintel.name = "Lintel";
            lintel.transform.SetParent(root, false);
            lintel.transform.localPosition = new Vector3(0f, 5.4f, z);
            lintel.transform.localScale = new Vector3(gateWidth, WallHeight - 5.4f, 1.4f);
            SMat.Apply(lintel.GetComponent<Renderer>(), mat);
        }

        void BuildCover(Transform root, int idx)
        {
            // 固定种子：同一区的掩体每次一致，便于反复验收与玩家记忆
            var rng = new System.Random(1000 + idx * 37);
            int count = 6 + idx;   // 越深越复杂

            for (int i = 0; i < count; i++)
            {
                float x = -ZoneWidth * 0.36f + (float)rng.NextDouble() * ZoneWidth * 0.72f;
                float z = -ZoneLength * 0.32f + (float)rng.NextDouble() * ZoneLength * 0.64f;

                bool asCrate = (i % 3 == 0);
                if (asCrate)
                {
                    // 货箱：真实模型，保留 Collider 作为掩体
                    var crate = new GameObject("Crate");
                    crate.transform.SetParent(root, false);
                    crate.transform.localPosition = new Vector3(x, 0f, z);
                    crate.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);

                    var vis = SAssets.SpawnFit("Modle_Prop_Crate", crate.transform, Vector3.zero,
                        0.92f, Color.white, 0.4f);
                    vis.name = "Model";

                    var box = crate.AddComponent<BoxCollider>();
                    box.size = new Vector3(1.16f, 0.92f, 1.16f);
                    box.center = new Vector3(0f, 0.46f, 0f);
                    continue;
                }

                // 其余用墙板模块或桶（按序号轮换，保证场景有变化）
                bool asBarrel = (i % 3 == 1);
                if (asBarrel)
                {
                    var barrel = new GameObject("Barrel");
                    barrel.transform.SetParent(root, false);
                    barrel.transform.localPosition = new Vector3(x, 0f, z);

                    var vis = SAssets.SpawnFit("Modle_Prop_Barrel", barrel.transform, Vector3.zero,
                        0.94f, Color.white, 0.4f);
                    vis.name = "Model";

                    var cap = barrel.AddComponent<CapsuleCollider>();
                    cap.radius = 0.33f;
                    cap.height = 0.94f;
                    cap.center = new Vector3(0f, 0.47f, 0f);
                    continue;
                }

                // 墙板模块：当作矮挡体
                {
                    var wall = new GameObject("CoverWall");
                    wall.transform.SetParent(root, false);
                    wall.transform.localPosition = new Vector3(x, 0f, z);
                    wall.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);

                    float h = 1.4f + (float)rng.NextDouble() * 1.2f;
                    float w = 5f + (float)rng.NextDouble() * 3f;

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
            }

            // 高台：制造垂直差，让跳跃与无人机有意义
            if (idx >= 3)
            {
                var ledge = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ledge.name = "Ledge";
                ledge.transform.SetParent(root, false);
                float lx = (idx % 2 == 0) ? -ZoneWidth * 0.34f : ZoneWidth * 0.34f;
                ledge.transform.localPosition = new Vector3(lx, 1.6f, ZoneLength * 0.22f);
                ledge.transform.localScale = new Vector3(7f, 3.2f, 9f);
                SMat.Apply(ledge.GetComponent<Renderer>(),
                    SAssets.Textured("tex_floor_concrete", new Color(0.9f, 0.9f, 0.92f), 0.2f));
            }
        }

        void BuildBossArena(Transform root, int idx)
        {
            // 四根能量柱（可破坏），均匀分布在环形四角
            for (int i = 0; i < BossState.PillarCount; i++)
            {
                float ang = 45f + i * 90f;
                float rad = ZoneWidth * 0.34f;
                var pos = new Vector3(
                    Mathf.Cos(ang * Mathf.Deg2Rad) * rad,
                    2.6f,
                    Mathf.Sin(ang * Mathf.Deg2Rad) * rad);

                var pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pillar.name = "Pillar" + i;
                pillar.transform.SetParent(root, false);
                pillar.transform.localPosition = pos;
                pillar.transform.localScale = new Vector3(1.7f, 2.6f, 1.7f);

                // 真实模型（缺件时保留圆柱占位）。模型原点在地面，高 4.96m。
                var vis = SAssets.SpawnFit("Modle_Prop_Pillar", pillar.transform, new Vector3(0f, -2.6f, 0f),
                    4.96f, Color.white, 0.5f);
                vis.name = "Model";
                // 占位体在模型就位后隐藏（保留 Collider 用于子弹命中）
                var pr = pillar.GetComponent<Renderer>();
                if (pr != null && SAssets.Model("Modle_Prop_Pillar") != null) pr.enabled = false;
                else SMat.Apply(pr, SMat.Lit(new Color(0.62f, 0.30f, 0.82f), 0.5f));

                var pc = pillar.AddComponent<Pillar>();
                pc.Index = i;
                pc.Hp = BossState.PillarHp;
                pc.MaxHp = BossState.PillarHp;
                _pillars.Add(pc);
            }

            // 中央平台（Boss 站位）
            var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pad.name = "BossPad";
            pad.transform.SetParent(root, false);
            pad.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            pad.transform.localScale = new Vector3(11f, 0.06f, 11f);
            Object.Destroy(pad.GetComponent<Collider>());
            SMat.Apply(pad.GetComponent<Renderer>(),
                SMat.Lit(new Color(0.34f, 0.20f, 0.30f), 0.3f));
        }

        readonly List<Pillar> _pillars = new List<Pillar>();
        public IReadOnlyList<Pillar> Pillars => _pillars;

        // ---------------- 门 ----------------

        void BuildGates(int totalZones)
        {
            for (int i = 1; i < totalZones; i++)
            {
                // 门位于第 i 区后墙处
                var pos = new Vector3(0f, 0f, CenterOf(i).z + ZoneLength * 0.5f);
                var go = new GameObject("Gate" + i);
                go.transform.SetParent(transform, false);
                go.transform.position = pos;

                var gate = go.AddComponent<ZoneGate>();
                gate.FromZone = i;
                gate.Build();
                _gates.Add(go);
                _gateScripts.Add(gate);
            }
        }

        /// <summary>根据推进状态刷新所有门的开关。</summary>
        public void ApplyGateState(ZoneProgress progress)
        {
            for (int i = 0; i < _gateScripts.Count; i++)
            {
                int from = _gateScripts[i].FromZone;
                _gateScripts[i].SetOpen(progress.IsDoorOpen(from));
            }
        }

        /// <summary>第 index 区的刷怪点：沿区域外缘环形分布，从玩家背后与两侧刷入。</summary>
        public Vector3 SpawnPoint(int index, int slot, int totalSlots)
        {
            var c = CenterOf(index);
            float ang = 360f / Mathf.Max(1, totalSlots) * slot + (index * 23f);
            float rad = SpawnRadius;
            var p = c + new Vector3(
                Mathf.Cos(ang * Mathf.Deg2Rad) * rad,
                0.9f,
                Mathf.Sin(ang * Mathf.Deg2Rad) * rad * 0.7f);
            return p;
        }

        /// <summary>第 index 区的玩家进入点（区域后部）。</summary>
        public static Vector3 EntryPoint(int index) => CenterOf(index) + new Vector3(0f, 1.2f, -ZoneLength * 0.38f);
    }
}
