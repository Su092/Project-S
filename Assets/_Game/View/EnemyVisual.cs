using System;
using UnityEngine;

namespace S.View
{
    /// <summary>Authored character presentation. Never drives actor movement or damage.</summary>
    public sealed class EnemyVisual : MonoBehaviour
    {
        public string AssetId;
        public Vector3 Dimensions;
        public Vector3 MovementCenter;
        public float MovementHeight, MovementRadius;
        public Animator Animator;
        public Transform Aim, Muzzle;
        public string IdleState, MoveState, AttackState, HitState, DeathState;
        public float MoveCycleMetres = .38f;
        public float AttackContactNormalized = .52f;
        public Collider[] HitColliders;
        public AnimationClip[] Clips;
        public string[] ClipNames;
        public PoseBinding[] ShieldPose;
        [Serializable] public class PoseBinding
        {
            public Transform Bone;
            public Vector3 Closed, Open;
        }
        string _state;
        float _remaining, _emission = -1;
        int _priority;
        bool _dead, _shieldOpen;
        Vector3 _lastPosition;
        Renderer[] _renderers;
        MaterialPropertyBlock _block;
        public string CurrentState => _state;
        public bool IsDying => _dead;
        public Vector3 AimPoint => Aim ? Aim.position : transform.position + Vector3.up * Dimensions.y * .5f;

        void Awake()
        {
            _lastPosition = transform.position;
            _renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            _block = new MaterialPropertyBlock();
            if (Animator) { Animator.applyRootMotion = false; Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; }
        }
        public float ClipLength(string state)
        {
            for (int i = 0; i < ClipNames.Length; i++)
                if (ClipNames[i] == state && Clips[i]) return Clips[i].length;
            return 0f;
        }
        void Play(string state, float speed = 1f, bool force = false)
        {
            if (!Animator || string.IsNullOrEmpty(state) || !Animator.HasState(0, Animator.StringToHash(state))) return;
            if (_state != state || force)
            {
                if (state == DeathState) Animator.Play(state, 0, 0f);
                else Animator.CrossFadeInFixedTime(state, .055f, 0, 0f);
                _state = state;
            }
            Animator.speed = speed;
        }
        public void Action(string state, float seconds = 0f, int priority = 1)
        {
            if (AssetId == MusicEnemyAssets.Ballerina || AssetId == MusicEnemyAssets.Conductor)
                state = MusicEnemyAssets.LegacyBossAction(AssetId, state);
            float length = ClipLength(state);
            if (_dead || length <= 0f || (_remaining > 0f && _priority > priority)) return;
            _remaining = seconds > 0f ? seconds : length;
            _priority = priority;
            Play(state, length / _remaining, true);
        }
        public void LoopAction(string state,float seconds,int priority=1)
        {
            Action(state,seconds,priority);
            if(_state==state && Animator)Animator.speed=1;
        }
        public void Attack(float windup)
        {
            // Delivered contact pose is around 52% of the clip. Gameplay keeps its existing windup.
            Action(AttackState, Mathf.Max(.1f, windup / Mathf.Max(.05f, AttackContactNormalized)), 3);
        }
        public void Hit()
        {
            if (_remaining <= 0f) Action(HitState, .24f, 1);
        }
        public float Die()
        {
            if (_dead) return 0f;
            Action(DeathState, 0f, 10);
            _dead = true;
            foreach (var c in HitColliders) if (c) c.enabled = false;
            SetEmission(0f);
            return Mathf.Max(.25f, ClipLength(DeathState));
        }
        public void BossState(bool shield, bool breached, int phase)
        {
            _shieldOpen = breached || (!shield && phase >= 2);
            SetEmission(breached ? 2.1f : phase >= 3 ? 1.6f : .85f);
        }
        public void SetEmission(float value)
        {
            if (Mathf.Approximately(_emission, value)) return;
            _emission = value;
            if (_renderers == null) return;
            foreach (var r in _renderers)
            {
                r.GetPropertyBlock(_block);
                _block.SetColor("_EmissionColor", Color.white * value);
                r.SetPropertyBlock(_block);
            }
        }
        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) { _lastPosition = transform.position; return; }
            if (_dead) return;
            _remaining = Mathf.Max(0f, _remaining - dt);
            Vector3 delta = transform.position - _lastPosition; delta.y = 0f;
            _lastPosition = transform.position;
            if (_remaining > 0f) return;
            float speed = delta.magnitude / dt;
            if (speed > .06f && !string.IsNullOrEmpty(MoveState))
                Play(MoveState, Mathf.Clamp(speed / Mathf.Max(.05f, MoveCycleMetres), .2f, 8f));
            else Play(IdleState);
        }
        void LateUpdate()
        {
            if (_dead || Time.deltaTime <= 0f || ShieldPose == null) return;
            // Keep the visible door state consistent between one-shot skill animations.
            foreach (var p in ShieldPose)
                if (p.Bone) p.Bone.localPosition = _shieldOpen ? p.Open : p.Closed;
        }
    }

    public static class EnemyAssets
    {
        public const string Boss = "Boss_CrucibleSovereign";
        public static string Name(string kind, bool elite)
        {
            switch (kind)
            {
                case "Crawler": return elite ? "SlagHound_Elite_MoldBreaker" : "SlagHound";
                case "Brute": return elite ? "FurnaceWarden_Elite_TwinGate" : "FurnaceWarden";
                case "Bomber": return elite ? "PressureRunner_Elite_TriplePressure" : "PressureRunner";
                case "Turret": return elite ? "RingSentry_Elite_Crown" : "RingSentry";
                default: throw new ArgumentException("Unknown enemy kind: " + kind);
            }
        }
        public static EnemyVisual Spawn(string asset, Transform parent)
        {
            var prefab = SRes.Get<GameObject>(asset);
            if (!prefab) throw new InvalidOperationException("Missing enemy prefab: " + asset);
            var body = UnityEngine.Object.Instantiate(prefab, parent, false);
            body.name = "Body";
            var visual = body.GetComponent<EnemyVisual>();
            if (!visual) throw new InvalidOperationException("Missing EnemyVisual: " + asset);
            return visual;
        }
    }
}
