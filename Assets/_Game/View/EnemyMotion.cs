using UnityEngine;

namespace S.View
{
    /// <summary>移动步态与攻击动作只驱动可见网格；根节点碰撞、移动仍由 Enemy 管理。</summary>
    public sealed class EnemyMotion : MonoBehaviour
    {
        Enemy _owner;
        RigidMeshRig.Rig _rig;
        Vector3 _last;
        float _phase, _attack;
        bool _crawler, _drone;
        public int JointCount => _rig != null ? _rig.Joints.Count : 0;
        public void Bind(Enemy owner, GameObject body)
        {
            _owner = owner; _last = transform.position;
            _crawler = owner.Def != null && (owner.Def.Model ?? "").Contains("Crawler");
            _drone = owner.Def != null && (owner.Def.Model ?? "").Contains("Drone");
            _rig = RigidMeshRig.Build(body, false, _crawler, _drone);
        }
        public void Attack() { _attack = 0.45f; }
        public void Sample(float phase, float stride, float attack)
        {
            if (_rig == null) return;
            _rig.Root.localPosition = Vector3.up * (_drone ? 0.04f * Mathf.Sin(phase) : Mathf.Abs(Mathf.Sin(phase)) * 0.018f * stride);
            _rig.Root.localRotation = Quaternion.Euler(-12f * attack, 0f, Mathf.Sin(phase) * 2f * stride);
            foreach (var entry in _rig.Joints)
            {
                int group = entry.Key;
                if (group == 0) continue;
                float sign = group == 1 || group == 4 ? 1f : -1f;
                if (_drone) _rig.Pose(group, Vector3.zero, new Vector3(0f, phase * Mathf.Rad2Deg * 8f, 0f));
                else _rig.Pose(group, Vector3.zero, new Vector3(Mathf.Sin(phase) * 24f * stride * sign - attack * (group >= 3 ? 42f : 8f), 0f, 0f));
            }
        }
        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || _owner == null || !_owner.Alive) return;
            float speed = Vector3.Distance(transform.position, _last) / dt;
            _last = transform.position;
            float stride = Mathf.Clamp01(speed / Mathf.Max(0.2f, _owner.Speed));
            _phase += dt * (_drone ? 10f : 2f + speed * 5f);
            _attack = Mathf.Max(0f, _attack - dt);
            float attack = Mathf.Sin((1f - _attack / 0.45f) * Mathf.PI) * (_attack > 0f ? 1f : 0f);
            Sample(_phase, stride, attack);
        }
    }
}
