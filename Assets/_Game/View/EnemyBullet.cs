using UnityEngine;

namespace S.View
{
    /// <summary>敌人发射的抛射物。简单的直线移动 + 命中玩家。</summary>
    public sealed class EnemyBullet : MonoBehaviour
    {
        Vector3 _dir;
        float _dmg;
        float _life = 4f;

        /// <summary>飞行速度（米/秒）。由 Boss.tsv 的 volleySpeed / trackVolleySpeed 注入。</summary>
        public float SpeedOf = 18f;

        public void Init(Vector3 dir, float dmg, float speed = 18f)
        {
            _dir = dir.normalized;
            _dmg = dmg;
            SpeedOf = speed > 0f ? speed : 18f;

            transform.rotation = Quaternion.LookRotation(_dir.sqrMagnitude > 0.001f ? _dir : Vector3.forward);
            var visual = gameObject.AddComponent<ProjectileVisual>();
            visual.Init(new S.Core.ProjectileDesc
            {
                Size = 0.28f,
                Source = new S.Core.BulletDef { Id = "enemy", Color = "#ff5544" }
            });
            Destroy(gameObject, 5f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _life -= dt;
            if (_life <= 0f) { Destroy(gameObject); return; }

            float step = SpeedOf * dt;
            if (Physics.SphereCast(transform.position, 0.14f, _dir, out var hit, step, WorldPhysics.ShotMask, QueryTriggerInteraction.Ignore))
            {
                var root = GameRoot.Instance;
                if (root != null && root.PlayerTransform != null)
                {
                    if (Vector3.Distance(hit.point, root.PlayerTransform.position + Vector3.up) < 2.2f)
                        root.DamagePlayer(_dmg);
                }
                CombatFx.Impact(hit.point, new Color(1f, 0.25f, 0.15f), hit.normal);
                Destroy(gameObject);
                return;
            }
            transform.position += _dir * step;
        }
    }
}
