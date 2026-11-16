using System.Collections.Generic;
using UnityEngine;

namespace S.View
{
    /// <summary>按现有网格的连通部件生成刚性关节，保持原三角面、UV、材质和导入轴转换。</summary>
    public static class RigidMeshRig
    {
        sealed class Part { public Mesh Mesh; public Material[] Materials; public Vector3 Pivot; public int Group; }
        static readonly Dictionary<string, List<Part>> Cache = new Dictionary<string, List<Part>>();
        public sealed class Rig
        {
            public Transform Root;
            public readonly Dictionary<int, Transform> Joints = new Dictionary<int, Transform>();
            public readonly Dictionary<int, Vector3> Rest = new Dictionary<int, Vector3>();
            public void Pose(int group, Vector3 offset, Vector3 euler)
            {
                if (!Joints.TryGetValue(group, out var joint)) return;
                joint.localPosition = Rest[group] + offset;
                joint.localRotation = Quaternion.Euler(euler);
            }
        }
        public static Rig Build(GameObject visual, bool weapon, bool crawler = false, bool drone = false)
        {
            var rig = new Rig();
            var root = new GameObject(weapon ? "WeaponRig" : "EnemyRig");
            root.transform.SetParent(visual.transform.parent, false);
            rig.Root = root.transform;
            foreach (var filter in visual.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = filter.sharedMesh;
                var renderer = filter.GetComponent<MeshRenderer>();
                if (mesh == null || renderer == null || !mesh.isReadable) continue;
                // 仅串联局部变换，避免世界坐标/旋转误差使每个敌人重复生成缓存网格。
                var matrix = Matrix4x4.identity;
                for (var t = filter.transform; t != visual.transform.parent; t = t.parent)
                    matrix = Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale) * matrix;
                string key = mesh.GetEntityId() + "|" + weapon + "|" + crawler + "|" + drone + "|" + matrix.ToString("F4");
                if (!Cache.TryGetValue(key, out var parts))
                {
                    parts = Split(mesh, renderer.sharedMaterials, matrix, weapon, crawler, drone);
                    Cache[key] = parts;
                }
                foreach (var part in parts)
                {
                    if (!rig.Joints.TryGetValue(part.Group, out var joint))
                    {
                        joint = new GameObject("Joint" + part.Group).transform;
                        joint.SetParent(root.transform, false); joint.localPosition = part.Pivot;
                        rig.Joints[part.Group] = joint; rig.Rest[part.Group] = part.Pivot;
                    }
                    var go = new GameObject("MeshPart", typeof(MeshFilter), typeof(MeshRenderer));
                    go.transform.SetParent(joint, false);
                    go.transform.localPosition = part.Pivot - rig.Rest[part.Group];
                    go.GetComponent<MeshFilter>().sharedMesh = part.Mesh;
                    go.GetComponent<MeshRenderer>().sharedMaterials = part.Materials;
                }
                renderer.enabled = false;
            }
            // 缺资源时仍让现有占位体整体运动。
            if (rig.Joints.Count == 0) visual.transform.SetParent(root.transform, true);
            return rig;
        }

        static List<Part> Split(Mesh mesh, Material[] materials, Matrix4x4 matrix, bool weapon, bool crawler, bool drone)
        {
            var source = mesh.vertices;
            var vertices = new Vector3[source.Length];
            var parent = new int[source.Length];
            int Find(int v) { while (parent[v] != v) { parent[v] = parent[parent[v]]; v = parent[v]; } return v; }
            void Union(int a, int b) { parent[Find(b)] = Find(a); }
            var weld = new Dictionary<Vector3Int, int>();
            var bounds = new Bounds(matrix.MultiplyPoint3x4(source[0]), Vector3.zero);
            for (int i = 0; i < source.Length; i++)
            {
                parent[i] = i; vertices[i] = matrix.MultiplyPoint3x4(source[i]); bounds.Encapsulate(vertices[i]);
                var q = new Vector3Int(Mathf.RoundToInt(source[i].x * 100000f), Mathf.RoundToInt(source[i].y * 100000f), Mathf.RoundToInt(source[i].z * 100000f));
                if (weld.TryGetValue(q, out int other)) Union(i, other); else weld[q] = i;
            }
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var triangles = mesh.GetTriangles(sub);
                for (int i = 0; i < triangles.Length; i += 3)
                { Union(triangles[i], triangles[i + 1]); Union(triangles[i], triangles[i + 2]); }
            }
            var islands = new Dictionary<int, Bounds>();
            for (int i = 0; i < vertices.Length; i++)
            {
                int id = Find(i);
                if (!islands.TryGetValue(id, out var b)) b = new Bounds(vertices[i], Vector3.zero);
                b.Encapsulate(vertices[i]); islands[id] = b;
            }
            var groups = new Dictionary<int, int>();
            var groupBounds = new Dictionary<int, Bounds>();
            foreach (var entry in islands)
            {
                var b = entry.Value;
                var c = b.center;
                float y = (c.y - bounds.min.y) / Mathf.Max(0.001f, bounds.size.y);
                float z = (c.z - bounds.min.z) / Mathf.Max(0.001f, bounds.size.z);
                float x = (c.x - bounds.center.x) / Mathf.Max(0.001f, bounds.extents.x);
                int group = 0;
                if (weapon)
                {
                    bool drum = mesh.name.Contains("Grenade");
                    if (y < 0.55f && z > (drum ? 0.25f : 0.38f) && z < 0.73f) group = 1; // 弹匣/弹鼓
                    else if (drum ? z > 0.65f : y > 0.82f) group = 2; // 榴弹枪活动炮管 / 上机匣
                }
                else if (drone)
                {
                    if (y > 0.48f && (Mathf.Abs(x) > 0.25f || Mathf.Abs(z - 0.5f) > 0.2f))
                        group = 1 + (x > 0 ? 1 : 0) + (z > 0.5f ? 2 : 0);
                }
                else if (y < (crawler ? 0.62f : 0.48f))
                    group = 1 + (x > 0 ? 1 : 0) + (crawler && z > 0.5f ? 2 : 0);
                else if (!crawler && Mathf.Abs(x) > 0.62f && y < 0.85f)
                    group = x < 0 ? 3 : 4;
                groups[entry.Key] = group;
                if (!groupBounds.TryGetValue(group, out var gb)) gb = b; else gb.Encapsulate(b);
                groupBounds[group] = gb;
            }
            var result = new List<Part>();
            foreach (var entry in groupBounds)
            {
                int group = entry.Key;
                var pivot = entry.Value.center;
                if (!weapon && !drone && group != 0) pivot.y = entry.Value.max.y;
                var vv = new List<Vector3>(); var nn = new List<Vector3>(); var uv = new List<Vector2>();
                var remap = new Dictionary<int, int>();
                var normals = mesh.normals; var uvs = mesh.uv;
                var normalMatrix = matrix.inverse.transpose;
                int Copy(int i)
                {
                    if (remap.TryGetValue(i, out int index)) return index;
                    index = vv.Count; remap[i] = index; vv.Add(vertices[i] - pivot);
                    nn.Add(normals.Length == source.Length ? normalMatrix.MultiplyVector(normals[i]).normalized : Vector3.up);
                    uv.Add(uvs.Length == source.Length ? uvs[i] : Vector2.zero);
                    return index;
                }
                var tris = new List<int[]>(); var mats = new List<Material>();
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var input = mesh.GetTriangles(sub); var output = new List<int>();
                    for (int i = 0; i < input.Length; i += 3)
                        if (groups[Find(input[i])] == group)
                        { output.Add(Copy(input[i])); output.Add(Copy(input[i + 1])); output.Add(Copy(input[i + 2])); }
                    if (output.Count == 0) continue;
                    tris.Add(output.ToArray()); mats.Add(materials[Mathf.Min(sub, materials.Length - 1)]);
                }
                var split = new Mesh { name = mesh.name + "_Motion" + group };
                split.SetVertices(vv); split.SetNormals(nn); split.SetUVs(0, uv); split.subMeshCount = tris.Count;
                for (int i = 0; i < tris.Count; i++) split.SetTriangles(tris[i], i);
                split.RecalculateBounds();
                result.Add(new Part { Mesh = split, Materials = mats.ToArray(), Pivot = pivot, Group = group });
            }
            return result;
        }
    }
}
