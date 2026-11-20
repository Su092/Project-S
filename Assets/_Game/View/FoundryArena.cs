using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
namespace S.View
{
    /// <summary>Authored map bindings. Geometry and placement live in the prefab.</summary>
    public sealed class FoundryArena : MonoBehaviour
    {
        public const string AssetName = "FoundryArena";
        public const string AssetPath = "Assets/_Game/Res/Prefab/Maps/FoundryArena.prefab";
        public const string ActiveAssetName = "GrandTheater";
        public const string ActiveAssetPath = "Assets/_Game/Res/Prefab/Maps/GrandTheater.prefab";
        public bool IsTheater;
        readonly HashSet<MusicSpotlight> _focusLights = new();
        Light[] _stageLights;
        float[] _stageIntensities;
        float _focusDimming, _normalReflection;
        public float FocusDimming => _focusDimming;
        static readonly int StageDimId = Shader.PropertyToID("_MusicStageDim");
        static readonly Color NormalAmbient = new Color(.47f,.52f,.66f);
        static readonly Color FocusAmbient = new Color(.12f,.14f,.20f);
        public void SetSpotlightFocus(MusicSpotlight source, bool active)
        {
            if (!IsTheater || !source) return;
            if (active) _focusLights.Add(source); else _focusLights.Remove(source);
        }
        void Update()
        {
            if (!IsTheater || _stageLights == null) return;
            _focusLights.RemoveWhere(s => !s || !s.isActiveAndEnabled);
            float next = Mathf.MoveTowards(_focusDimming, _focusLights.Count > 0 ? 1 : 0, Time.deltaTime * 2.5f);
            if (Mathf.Approximately(next, _focusDimming)) return;
            _focusDimming = next;
            ApplyStageLighting();
        }
        void ApplyStageLighting()
        {
            RenderSettings.ambientLight = Color.Lerp(NormalAmbient, FocusAmbient, _focusDimming);
            RenderSettings.reflectionIntensity = Mathf.Lerp(_normalReflection, .25f, _focusDimming);
            Shader.SetGlobalFloat(StageDimId, _focusDimming);
            for (int i = 0; i < _stageLights.Length; i++)
                if (_stageLights[i]) _stageLights[i].intensity = _stageIntensities[i] * Mathf.Lerp(1, .1f, _focusDimming);
        }
        public float Width = 100, Depth = 100;
        public NavMeshData Navigation;
        NavMeshDataInstance _navigation;
        void OnEnable() { if (Navigation) _navigation = NavMesh.AddNavMeshData(Navigation, transform.position, transform.rotation); }
        void OnDisable()
        {
            if (_navigation.valid) _navigation.Remove();
            _focusLights.Clear(); _focusDimming = 0;
            if (IsTheater && _stageLights != null) ApplyStageLighting();
        }
        public Transform PlayerSpawn;
        public Transform BossSpawn;
        public Transform[] EnemySpawns;
        public Pillar[] EnergyPillars;
        public int LayoutInstanceCount;
        public string LayoutSha256;
        public void ValidateBindings()
        {
            if (!PlayerSpawn || !BossSpawn || EnemySpawns == null || EnemySpawns.Length != 16 ||
                EnergyPillars == null || EnergyPillars.Length != (IsTheater ? 0 : 4))
                throw new InvalidOperationException("FoundryArena marker bindings are incomplete.");
            foreach (var p in EnemySpawns) if (!p) throw new InvalidOperationException("Missing enemy spawn.");
            foreach (var p in EnergyPillars) if (!p) throw new InvalidOperationException("Missing energy pillar.");
        }
        public void BeginRun()
        {
            ValidateBindings();
            foreach (var pillar in EnergyPillars) pillar.gameObject.SetActive(false);
        }
        public void ApplyEnvironment(Camera camera = null)
        {
            if (IsTheater)
            {
                if (camera) { camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.025f,.035f,.06f); }
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                // Cache authored lighting before any temporary focus cue, never compound dimming.
                if (_stageLights == null)
                {
                    _normalReflection = RenderSettings.reflectionIntensity;
                    _stageLights = GetComponentsInChildren<Light>(true);
                    _stageIntensities = new float[_stageLights.Length];
                    for (int i = 0; i < _stageLights.Length; i++) _stageIntensities[i] = _stageLights[i].intensity;
                }
                ApplyStageLighting();
                RenderSettings.fog = false;
                return;
            }
            if (camera)
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.065f, .09f, .12f);
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.32f, .39f, .47f);
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(.12f, .16f, .2f);
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 75f;
            RenderSettings.fogEndDistance = 190f;
        }
    }
}
