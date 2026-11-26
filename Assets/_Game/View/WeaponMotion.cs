using UnityEngine;
using S.Core;

namespace S.View
{
    /// <summary>用真实换弹进度驱动弹匣和枪机；模型后座不改变准星与弹道。</summary>
    public sealed class WeaponMotion : MonoBehaviour
    {
        PlayerWeapons _owner;
        RigidMeshRig.Rig _rig;
        Vector3 _restPosition;
        Quaternion _restRotation;
        float _kick, _velocity;
        bool _bound, _music;
        public void Bind(PlayerWeapons owner, GameObject model)
        {
            _owner = owner;
            if (!_bound) { _restPosition = transform.localPosition; _restRotation = transform.localRotation; _bound = true; }
            transform.localPosition = _restPosition; transform.localRotation = _restRotation;
            _music = model.GetComponent<MusicWeaponVisual>() != null || (owner?.Active?.Base.Model?.StartsWith("Orchestra/") ?? false);
            _rig = _music ? null : RigidMeshRig.Build(model, true);
            _kick = _velocity = 0f;
        }
        public void Fire(GunDef gun)
        {
            float power = gun == null ? 1f : gun.Id == "shotgun" || gun.Id == "launcher" ? 1.8f : gun.Id == "sniper" ? 1.6f : gun.Id == "smg" || gun.Id == "beam" ? 0.45f : 1f;
            _velocity += 18f * power;
            _kick = Mathf.Min(2f, _kick + 0.15f * power);
        }
        public void Sample(float reload, float recoil)
        {
            if (_music)
            {
                // Imported bones own instrument mechanisms; retain only whole-instrument impulse.
                transform.localPosition = _restPosition + Vector3.back * (.018f * recoil);
                transform.localRotation = _restRotation * Quaternion.Euler(-2.5f * recoil, 0, 0);
                return;
            }
            float tilt = reload > 0f && reload < 1f ? Mathf.Sin(Mathf.PI * reload) : 0f;
            transform.localPosition = _restPosition + new Vector3(-0.035f * tilt, -0.12f * tilt, -0.06f * recoil);
            transform.localRotation = _restRotation * Quaternion.Euler(-7f * recoil + 12f * tilt, -18f * tilt, -35f * tilt);
            float detach = reload < 0.18f ? 0f : reload < 0.42f ? Mathf.InverseLerp(0.18f, 0.42f, reload)
                : reload < 0.62f ? 1f : 1f - Mathf.InverseLerp(0.62f, 0.82f, reload);
            _rig?.Pose(1, new Vector3(0f, -0.18f * detach, 0f), new Vector3(0f, 0f, -12f * detach));
            float bolt = reload > 0.82f && reload < 1f ? Mathf.Sin(Mathf.PI * (reload - 0.82f) / 0.18f) : 0f;
            _rig?.Pose(2, Vector3.back * (0.025f * recoil + 0.04f * bolt), Vector3.zero);
        }
        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || _owner == null) return;
            // 分步积分，低帧率时也不发散。
            int steps = Mathf.Clamp(Mathf.CeilToInt(dt / 0.008f), 1, 16);
            float h = Mathf.Min(dt, 0.1f) / steps;
            for (int i = 0; i < steps; i++)
            {
                _velocity += (-180f * _kick - 22f * _velocity) * h;
                _kick = Mathf.Clamp(_kick + _velocity * h, -0.1f, 2.5f);
            }
            var gun = _owner.Active;
            Sample(gun != null && gun.Reloading ? gun.MagBarFill : 0f, _kick);
        }
    }
}
