using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace S.View
{
    /// <summary>Visual-only performance. GunRuntime remains the sole owner of shots and reload timing.</summary>
    public sealed class MusicWeaponVisual : MonoBehaviour
    {
        public string AssetId, GunId;
        public Animator Animator;
        public AnimationClip[] Clips;
        public string[] States;
        public Transform Output;
        public Vector3 ViewPosition, ViewEuler;
        public string CurrentState { get; private set; }
        public float StateTime { get; private set; }
        PlayerWeapons owner;
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        AnimationClipPlayable[] players;
        int current = -1;
        float lastShot = -100, shotWindow, stateAge;
        bool restartShot, pendingShot;
        public int PresentedShots { get; private set; }
        Vector3 initialPosition;
        public void Bind(PlayerWeapons weapons)
        {
            owner = weapons;
            transform.localPosition = ViewPosition;
            transform.localRotation = Quaternion.Euler(ViewEuler);
            initialPosition = ViewPosition;
            EnsureGraph();
            Pose("Equip", 0, 0, true);
        }
        void EnsureGraph()
        {
            if (graph.IsValid()) return;
            graph = PlayableGraph.Create("MusicWeapon_" + AssetId);
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, Clips.Length);
            players = new AnimationClipPlayable[Clips.Length];
            for (int i = 0; i < Clips.Length; i++)
            {
                players[i] = AnimationClipPlayable.Create(graph, Clips[i]);
                players[i].SetApplyFootIK(false); players[i].SetApplyPlayableIK(false); players[i].SetSpeed(0);
                graph.Connect(players[i], 0, mixer, i);
            }
            Animator.applyRootMotion = false; Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            AnimationPlayableOutput.Create(graph, "Weapon", Animator).SetSourcePlayable(mixer);
            graph.Play();
        }
        int Index(string state) => System.Array.IndexOf(States, state);
        public bool HasState(string state) => Index(state) >= 0;
        public void Shot(float interval)
        {
            pendingShot = true;
            if (!HasState("FireLoop")) restartShot = true;
            lastShot = Time.time;
            shotWindow = HasState("FireLoop") ? Mathf.Clamp(interval * 1.15f, .18f, .9f) : Mathf.Max(interval, Clips[Index("Fire")].length);
        }
        public void PreviewPose(string state, float normalized)
        {
            EnsureGraph(); int i = Index(state); if (i < 0) return;
            Pose(state, Mathf.Clamp01(normalized) * Clips[i].length, 0, true);
        }
        void Pose(string state, float time, float dt, bool immediate = false)
        {
            int index = Index(state); if (index < 0) index = Index("Idle");
            if (current != index) { current = index; stateAge = 0; }
            CurrentState = States[index]; StateTime = time;
            players[index].SetTime(time);
            float sum = 0;
            for (int i = 0; i < players.Length; i++)
            {
                float weight = immediate ? (i == index ? 1 : 0) : Mathf.MoveTowards(mixer.GetInputWeight(i), i == index ? 1 : 0, dt / .055f);
                mixer.SetInputWeight(i, weight); sum += weight;
            }
            if (sum < .001f) mixer.SetInputWeight(index, 1);
            else for (int i = 0; i < players.Length; i++) mixer.SetInputWeight(i, mixer.GetInputWeight(i) / sum);
            graph.Evaluate(0);
        }
        void LateUpdate()
        {
            if (!owner || Time.deltaTime <= 0 || owner.Active == null) return;
            EnsureGraph();
            float dt = Time.deltaTime;
            var gun = owner.Active;
            string state;
            float time;
            if (gun.Reloading)
            {
                state = "Reload"; time = gun.MagBarFill * Clips[Index(state)].length;
                restartShot = false; pendingShot = false;
            }
            else if (owner.IsSwitching || (CurrentState == "Equip" && stateAge < .15f))
            {
                state = "Equip"; time = owner.IsSwitching ? (1 - owner.SwitchNormalized) * .15f : stateAge;
            }
            else if (pendingShot || Time.time - lastShot < shotWindow)
            {
                state = HasState("FireLoop") ? "FireLoop" : "Fire";
                if (pendingShot) { PresentedShots++; pendingShot = false; lastShot = Time.time; }
                if (restartShot) { stateAge = 0; restartShot = false; }
                time = CurrentState == state ? stateAge : 0;
                float length = Clips[Index(state)].length;
                time = state == "FireLoop" ? time % length : Mathf.Min(time, length);
            }
            else if ((CurrentState == "Fire" || CurrentState == "FireLoop") || (CurrentState == "FireRecover" && stateAge < .15f))
            {
                state = "FireRecover"; time = CurrentState == state ? stateAge : 0;
            }
            else
            {
                state = HasState("AimIdle") && owner.Player && owner.Player.Cam && owner.Player.Cam.fieldOfView < 40f ? "AimIdle" : "Idle";
                time = CurrentState == state ? stateAge % Clips[Index(state)].length : 0;
            }
            Pose(state, time, dt);
            stateAge += dt;
            // Keep the same metric asset and screen footprint while the gameplay camera zooms.
            var camera = owner.Player ? owner.Player.Cam : null;
            if (camera)
            {
                float ratio = Mathf.Tan(78 * Mathf.Deg2Rad * .5f) / Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f);
                transform.localPosition = initialPosition + Vector3.forward * Mathf.Max(0, (ratio - 1) * .85f);
            }
        }
        void OnDestroy() { if (graph.IsValid()) graph.Destroy(); }
    }
}
