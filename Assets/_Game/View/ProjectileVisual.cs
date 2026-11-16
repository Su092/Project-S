using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using S.Core;

namespace S.View
{
    /// <summary>弹种模型、拖尾与待机动画。只读取结算描述，不参与命中判定。</summary>
    public sealed class ProjectileVisual : MonoBehaviour
    {
        static readonly Dictionary<string, Mesh> Meshes = new Dictionary<string, Mesh>();
        readonly List<Transform> _rings = new List<Transform>();
        Transform _model;
        ProjectileDesc _desc;
        Color _color;
        float _age;
        TrailRenderer _trail;
        bool _deployed;
        LineRenderer _fieldBoundary;
        public string StyleId { get; private set; }

        public void Init(ProjectileDesc desc)
        {
            _desc = desc;
            StyleId = desc.Source != null ? desc.Source.Id : desc.ModuleId ?? "std";
            _color = PlayerWeapons.ParseColor(desc.Source != null ? desc.Source.Color : "#ffd25a");
            _model = new GameObject("BulletModel_" + StyleId).transform;
            _model.SetParent(transform, false);
            float size = Mathf.Max(0.05f, desc.Size);
            _model.localScale = Vector3.one * size;
            if (desc.Kind == BulletKind.Beam || desc.Kind == BulletKind.Chain) return;
            var metal = new Color(0.12f, 0.16f, 0.21f);
            switch (StyleId)
            {
                case "pellet":
                    Part(PrimitiveType.Sphere, "Shot", Vector3.zero, Vector3.one * 0.85f, _color, false);
                    break;
                case "grenade":
                    Body("Canister", metal, 1.1f);
                    Band(0.7f, 0.32f); Band(0.7f, -0.32f);
                    Part(PrimitiveType.Cube, "Fuse", new Vector3(0f, 0f, 0.6f), new Vector3(0.25f, 0.25f, 0.2f), _color, true);
                    break;
                case "missile":
                    Body("Rocket", new Color(0.58f, 0.63f, 0.66f), 2.5f);
                    for (int i = 0; i < 4; i++)
                    {
                        float a = i * Mathf.PI * 0.5f;
                        var fin = Part(PrimitiveType.Cube, "Fin", new Vector3(Mathf.Cos(a) * 0.35f, Mathf.Sin(a) * 0.35f, -0.7f),
                            new Vector3(0.3f, 0.07f, 0.6f), metal, false);
                        fin.transform.localRotation = Quaternion.Euler(0f, 0f, i * 90f);
                    }
                    Part(PrimitiveType.Sphere, "Exhaust", new Vector3(0f, 0f, -1.1f), new Vector3(0.3f, 0.3f, 0.8f), _color, true);
                    break;
                case "mine":
                case "shock":
                    Part(PrimitiveType.Cylinder, "ArmoredDisc", Vector3.zero, new Vector3(1.8f, 0.2f, 1.8f), metal, false);
                    Part(PrimitiveType.Sphere, "Sensor", Vector3.up * 0.3f, Vector3.one * 0.6f, _color, true);
                    var ring = CombatFx.Ring(_model, _color * 2f, 0.72f, 0.06f);
                    ring.transform.localPosition = Vector3.up * 0.2f; _rings.Add(ring.transform);
                    for (int i = 0; i < 4; i++)
                    {
                        float a = i * Mathf.PI * 0.5f;
                        Part(PrimitiveType.Cube, "Contact", new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.65f,
                            new Vector3(0.23f, 0.4f, 0.23f), _color, false);
                    }
                    break;
                case "saw":
                    var saw = new GameObject("ToothedBlade", typeof(MeshFilter), typeof(MeshRenderer));
                    saw.transform.SetParent(_model, false);
                    saw.GetComponent<MeshFilter>().sharedMesh = SawMesh();
                    SMat.Apply(saw.GetComponent<Renderer>(), SMat.Lit(new Color(0.55f, 0.65f, 0.72f), 0.75f));
                    Part(PrimitiveType.Cylinder, "Hub", Vector3.zero, new Vector3(0.45f, 0.13f, 0.45f), metal, false);
                    var edge = CombatFx.Ring(_model, new Color(0.3f, 0.85f, 1f) * 2f, 0.47f, 0.035f);
                    edge.transform.localPosition = Vector3.up * 0.07f;
                    break;
                case "hole":
                    Part(PrimitiveType.Sphere, "DarkCore", Vector3.zero, Vector3.one * 1.5f, new Color(0.006f, 0.003f, 0.018f), false);
                    for (int i = 0; i < 3; i++)
                    {
                        var orbit = CombatFx.Ring(_model, _color * (1.5f + i * 0.4f), 1f + i * 0.22f, 0.06f);
                        orbit.transform.localRotation = Quaternion.Euler(25f + i * 40f, 0f, i * 35f);
                        _rings.Add(orbit.transform);
                    }
                    break;
                case "flame":
                    var fire = CombatFx.Particles(transform, "FlamePlume", new Color(1.8f, 0.4f, 0.04f), 32, 0.4f, size * 2f, 0.25f, false, true);
                    fire.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    break;
                case "bouncer":
                    Part(PrimitiveType.Sphere, "ElasticCore", Vector3.zero, Vector3.one * 0.85f, metal, false);
                    Band(0.95f, 0f);
                    var arc = CombatFx.Ring(_model, _color * 2f, 0.5f, 0.08f);
                    arc.transform.localRotation = Quaternion.Euler(0f, 0f, 90f); _rings.Add(arc.transform);
                    break;
                case "trigger":
                case "timer":
                case "expire":
                    Body("PayloadCapsule", metal, 1.4f);
                    int bands = StyleId == "trigger" ? 1 : StyleId == "timer" ? 2 : 3;
                    for (int i = 0; i < bands; i++) Band(0.95f, (i - (bands - 1) * 0.5f) * 0.35f);
                    Part(PrimitiveType.Sphere, "PayloadCore", new Vector3(0f, 0f, 0.55f), Vector3.one * 0.45f, _color, true);
                    break;
                default:
                    Body("BallisticDart", StyleId == "ap" ? new Color(0.25f, 0.55f, 0.65f) : new Color(0.65f, 0.4f, 0.12f),
                        StyleId == "ap" ? 3.4f : StyleId == "light" ? 2.6f : 1.8f);
                    Band(0.8f, -0.3f);
                    break;
            }
            if (desc.Kind != BulletKind.Mine && desc.Kind != BulletKind.Field && desc.Kind != BulletKind.Hole)
            {
                var tracer = new GameObject("Tracer");
                tracer.transform.SetParent(transform, false);
                _trail = tracer.AddComponent<TrailRenderer>();
                _trail.time = StyleId == "ap" ? 0.12f : StyleId == "pellet" ? 0.045f : 0.09f;
                _trail.minVertexDistance = 0.04f;
                _trail.widthCurve = AnimationCurve.Linear(0f, size * 0.6f, 1f, 0f);
                _trail.sharedMaterial = SMat.Combat(false);
                var gradient = new Gradient();
                gradient.SetKeys(new[] { new GradientColorKey(Color.Lerp(_color, Color.white, 0.4f) * 1.8f, 0f), new GradientColorKey(_color, 1f) },
                    new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) });
                _trail.colorGradient = gradient;
                _trail.shadowCastingMode = ShadowCastingMode.Off; _trail.receiveShadows = false;
            }
        }

        public void Deploy(Vector3 normal, float radius)
        {
            if (_deployed) return;
            _deployed = true;
            transform.rotation = Quaternion.FromToRotation(Vector3.up, normal);
            if (_desc.Kind == BulletKind.Field)
            {
                var boundary = CombatFx.Ring(transform, _color * 1.6f, radius, 0.055f);
                _fieldBoundary = boundary;
                boundary.transform.localPosition = Vector3.up * 0.025f;
                for (int i = -2; i <= 2; i++)
                {
                    float offset = radius * i / 3f;
                    float reach = Mathf.Sqrt(radius * radius - offset * offset);
                    for (int axis = 0; axis < 2; axis++)
                    {
                        var line = CombatFx.Line(transform, "ElectricGrid", new Color(_color.r, _color.g, _color.b, 0.28f), 0.025f);
                        line.positionCount = 2;
                        line.SetPosition(0, axis == 0 ? new Vector3(-reach, 0.03f, offset) : new Vector3(offset, 0.03f, -reach));
                        line.SetPosition(1, axis == 0 ? new Vector3(reach, 0.03f, offset) : new Vector3(offset, 0.03f, reach));
                    }
                }
            }
        }

        public void Animate(float dt)
        {
            _age += dt;
            if (_model == null) return;
            if (_fieldBoundary != null) _fieldBoundary.widthMultiplier = 0.045f + 0.02f * Mathf.Sin(_age * 7f);
            if (StyleId == "saw") _model.Rotate(0f, 900f * dt, 0f);
            for (int i = 0; i < _rings.Count; i++)
                _rings[i].Rotate(0f, (60f + 35f * i) * dt, StyleId == "hole" ? dt * 20f : 0f, Space.Self);
            if (_deployed || StyleId == "timer")
            {
                foreach (var ring in _rings)
                    ring.localScale = Vector3.one * (1f + 0.08f * Mathf.Sin(_age * 9f));
            }
        }
        void Update() { Animate(Time.deltaTime); }

        void OnDestroy()
        {
            if (!Application.isPlaying || _trail == null || !gameObject.scene.isLoaded) return;
            // 保留最后一小段拖尾，飞行物命中时自然淡出。
            _trail.emitting = false;
            _trail.transform.SetParent(null, true);
            Destroy(_trail.gameObject, _trail.time);
        }

        void Body(string name, Color color, float length)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(_model, false); go.transform.localScale = new Vector3(0.8f, 0.8f, length);
            go.GetComponent<MeshFilter>().sharedMesh = DartMesh();
            SMat.Apply(go.GetComponent<Renderer>(), SMat.Lit(color, 0.65f));
        }
        void Band(float diameter, float z)
        {
            var ring = CombatFx.Ring(_model, _color * 2f, diameter * 0.5f, 0.08f);
            ring.transform.localPosition = new Vector3(0f, 0f, z);
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _rings.Add(ring.transform);
        }
        GameObject Part(PrimitiveType type, string name, Vector3 pos, Vector3 scale, Color color, bool glow)
        {
            var go = GameObject.CreatePrimitive(type); SAssets.RemoveColliders(go);
            go.name = name; go.transform.SetParent(_model, false);
            go.transform.localPosition = pos; go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            SMat.Apply(r, glow ? SMat.Unlit(color * 1.8f) : SMat.Lit(color, 0.6f));
            return go;
        }
        static Mesh DartMesh()
        {
            if (Meshes.TryGetValue("dart", out var cached) && cached != null) return cached;
            const int sides = 12;
            var radii = new[] { 0f, 0.42f, 0.5f, 0.38f, 0f };
            var z = new[] { -0.5f, -0.5f, 0.1f, 0.3f, 0.5f };
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            for (int row = 0; row < radii.Length; row++)
                for (int i = 0; i < sides; i++)
                {
                    float a = i * Mathf.PI * 2f / sides;
                    vertices.Add(new Vector3(Mathf.Cos(a) * radii[row], Mathf.Sin(a) * radii[row], z[row]));
                    if (row == 0) continue;
                    int a0 = (row - 1) * sides + i, b = (row - 1) * sides + (i + 1) % sides;
                    int c = row * sides + i, d = row * sides + (i + 1) % sides;
                    triangles.AddRange(new[] { a0, b, c, b, d, c });
                }
            var mesh = new Mesh { name = "SharedBallisticDart" };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            Meshes["dart"] = mesh; return mesh;
        }
        static Mesh SawMesh()
        {
            if (Meshes.TryGetValue("saw", out var cached) && cached != null) return cached;
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            const int n = 48;
            vertices.Add(Vector3.up * 0.06f); vertices.Add(Vector3.down * 0.06f);
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n, r = i % 3 == 0 ? 1f : 0.7f;
                vertices.Add(new Vector3(Mathf.Cos(a) * r, 0.06f, Mathf.Sin(a) * r));
                vertices.Add(new Vector3(Mathf.Cos(a) * r, -0.06f, Mathf.Sin(a) * r));
            }
            for (int i = 0; i < n; i++)
            {
                int a = 2 + i * 2, b = 2 + (i + 1) % n * 2;
                triangles.AddRange(new[] { 0, b, a, 1, a + 1, b + 1, a, b, a + 1, b, b + 1, a + 1 });
            }
            var mesh = new Mesh { name = "SharedToothedSaw" };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            Meshes["saw"] = mesh; return mesh;
        }
    }
}
