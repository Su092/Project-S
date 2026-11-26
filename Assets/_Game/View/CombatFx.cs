using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace S.View
{
    /// <summary>仅表现：无碰撞、无伤害；共享材质、限制同时存活的瞬态特效数量。</summary>
    public sealed class CombatFx : MonoBehaviour
    {
        static readonly HashSet<CombatFx> Active = new HashSet<CombatFx>();
        static uint _seed = 701;
        float _age, _duration;
        LineRenderer _ring;
        float _radius;
        readonly List<LineRenderer> _lines = new List<LineRenderer>();
        readonly List<float> _widths = new List<float>();
        public static int ActiveCount => Active.Count;
        public const int MaxActive = 80;

        static CombatFx Create(string name, Vector3 at, float duration)
        {
            if (Active.Count >= MaxActive) return null;
            var go = new GameObject(name);
            go.transform.position = at;
            var fx = go.AddComponent<CombatFx>();
            fx._duration = duration;
            Active.Add(fx);
            return fx;
        }

        public static LineRenderer Line(Transform parent, string name, Color color, float width, bool world = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = world;
            line.widthMultiplier = width;
            line.numCapVertices = 3;
            line.numCornerVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = SMat.Combat(false);
            line.startColor = line.endColor = color;
            return line;
        }

        public static LineRenderer Ring(Transform parent, Color color, float radius, float width)
        {
            var line = Line(parent, "EnergyRing", color, width);
            line.loop = true;
            line.positionCount = 64;
            for (int i = 0; i < 64; i++)
            {
                float a = i * Mathf.PI * 2f / 64;
                line.SetPosition(i, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius);
            }
            return line;
        }

        public static ParticleSystem Particles(Transform parent, string name, Color color,
            int count, float speed, float size, float life, bool smoke = false, bool loop = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.useAutoRandomSeed = false; ps.randomSeed = ++_seed;
            var main = ps.main;
            main.playOnAwake = false; main.loop = loop; main.duration = Mathf.Max(life, 0.1f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.3f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
            main.startColor = color; main.maxParticles = loop ? 48 : Mathf.Max(8, count);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = smoke ? -0.08f : 0.15f;
            var emission = ps.emission;
            emission.rateOverTime = loop ? count : 0;
            if (!loop) emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var shape = ps.shape;
            shape.shapeType = loop ? ParticleSystemShapeType.Cone : ParticleSystemShapeType.Sphere;
            shape.radius = loop ? size * 0.15f : size * 0.1f;
            shape.angle = 12f;
            var fade = ps.colorOverLifetime; fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(smoke ? Color.white : new Color(0.65f, 0.45f, 0.3f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            var scale = ps.sizeOverLifetime; scale.enabled = true;
            scale.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, smoke ? 0.3f : 1f, 1f, smoke ? 1.6f : 0f));
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = SMat.Combat(true, smoke);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (name.Contains("Sparks"))
            {
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = 0.08f;
                renderer.lengthScale = 2f;
            }
            ps.Play();
            return ps;
        }

        public static void Muzzle(Vector3 at, Vector3 direction, Color color)
        {
            var fx = Create("MuzzleFlash", at, 0.075f);
            if (fx == null) return;
            Particles(fx.transform, "MuzzleCore", Color.Lerp(color, Color.white, 0.6f) * 1.8f,
                2, 0.1f, 0.16f, 0.07f);
            var line = Line(fx.transform, "MuzzleStreak", color * 1.8f, 0.065f, true);
            line.positionCount = 2; line.SetPosition(0, at); line.SetPosition(1, at + direction * 0.25f);
            fx._lines.Add(line); fx._widths.Add(0.065f);
        }

        public static void Impact(Vector3 at, Color color, Vector3 normal)
        {
            var fx = Create("ImpactFx", at, 0.42f);
            if (fx == null) return;
            Particles(fx.transform, "ImpactFlash", Color.Lerp(color, Color.white, 0.65f) * 2f, 2, 0.3f, 0.25f, 0.09f);
            Particles(fx.transform, "ImpactSparks", color * 1.6f, 10, 4.5f, 0.065f, 0.38f);
            fx._ring = Ring(fx.transform, color, 1f, 0.025f);
            fx._ring.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal.sqrMagnitude > 0 ? normal : Vector3.up);
            fx._radius = 0.28f;
        }

        public static void Explosion(Vector3 at, float radius, Color color, bool singularity = false)
        {
            var fx = Create(singularity ? "SingularityCollapse" : "ExplosionFx", at, 1.25f);
            if (fx == null) return;
            radius = Mathf.Max(0.1f, radius);
            var hot = singularity ? new Color(0.65f, 0.35f, 1f) : new Color(1f, 0.38f, 0.055f);
            Particles(fx.transform, "Flash", Color.Lerp(color, Color.white, 0.8f) * 2.4f, 3, radius, radius * 0.8f, 0.14f);
            Particles(fx.transform, "Fire", hot * 1.8f, 20, radius * 2.5f, radius * 0.65f, 0.5f);
            Particles(fx.transform, "Sparks", Color.Lerp(hot, Color.white, 0.4f) * 2f, 28, radius * 4f, 0.08f, 0.8f);
            Particles(fx.transform, "Smoke", new Color(0.32f, 0.34f, 0.38f, 0.42f), 12, radius * 0.6f, radius, 1.2f, true);
            fx._ring = Ring(fx.transform, Color.Lerp(hot, color, 0.4f) * 1.4f, 1f, 0.09f);
            fx._radius = radius;
            fx._ring.transform.localPosition = Vector3.up * 0.04f;
        }

        public static void Beam(Vector3 from, Vector3 to, Color color, bool lightning = false)
        {
            var fx = Create(lightning ? "LightningFx" : "LaserFx", from, lightning ? 0.22f : 0.15f);
            if (fx == null) return;
            int count = lightning ? 13 : 17;
            var points = new Vector3[count];
            var direction = (to - from).normalized;
            var right = Vector3.Cross(direction, Vector3.up).normalized;
            if (right.sqrMagnitude < 0.1f) right = Vector3.right;
            var up = Vector3.Cross(direction, right);
            var random = new System.Random((int)++_seed);
            float jitter = Mathf.Min(0.45f, Vector3.Distance(from, to) * 0.035f);
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1);
                points[i] = Vector3.Lerp(from, to, t);
                if (!lightning) points[i] += up * (Mathf.Sin(t * Mathf.PI * 4) * Mathf.Sin(t * Mathf.PI) * jitter * .26f);
                else if (i > 0 && i < count - 1)
                    points[i] += (right * ((float)random.NextDouble() - 0.5f) + up * ((float)random.NextDouble() - 0.5f)) * jitter;
            }
            if (!lightning)
            {
                var stringLine = Line(fx.transform, "HarpString", Color.Lerp(color, Color.white, .25f), .018f, true);
                stringLine.positionCount = count;
                for (int i = 0; i < count; i++)
                {
                    float t = i / (float)(count - 1);
                    stringLine.SetPosition(i, points[i] + right * (.16f * Mathf.Sin(Mathf.PI*t)));
                }
                fx._lines.Add(stringLine); fx._widths.Add(.018f);
            }
            for (int layer = 0; layer < 2; layer++)
            {
                float width = layer == 0 ? 0.13f : 0.035f;
                var line = Line(fx.transform, layer == 0 ? "Aura" : "Core",
                    layer == 0 ? color * 1.4f : Color.Lerp(color, Color.white, 0.8f) * 2f, width, true);
                line.positionCount = count; line.SetPositions(points);
                fx._lines.Add(line); fx._widths.Add(width);
            }
        }

        // 诊断可以固定取某个动画时刻；实际运行由 Update 推进。
        public void PreviewAt(float age)
        {
            _age = age;
            float t = Mathf.Clamp01(age / Mathf.Max(0.01f, _duration));
            if (_ring != null)
            {
                float expand = 1f - Mathf.Pow(1f - Mathf.Clamp01(t * 3f), 3f);
                _ring.transform.localScale = Vector3.one * Mathf.Max(0.01f, _radius * expand);
                _ring.widthMultiplier = Mathf.Lerp(0.09f, 0f, Mathf.Clamp01(t * 2f));
                var c = _ring.startColor; c.a = 1f - Mathf.Clamp01(t * 2.5f);
                _ring.startColor = _ring.endColor = c;
            }
            for (int i = 0; i < _lines.Count; i++)
                _lines[i].widthMultiplier = _widths[i] * (1f - t);
        }
        void Update()
        {
            PreviewAt(_age + Time.deltaTime);
            if (_age >= _duration) Destroy(gameObject);
        }
        void OnDestroy() { Active.Remove(this); }
    }
}
