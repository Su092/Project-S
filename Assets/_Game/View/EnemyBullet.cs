using UnityEngine;

namespace S.View
{
    /// <summary>敌人发射的抛射物。简单的直线移动 + 命中玩家。</summary>
    public sealed class EnemyBullet : MonoBehaviour
    {
        Vector3 _dir;
        float _dmg;
        float _life = 4f;
        const float Speed = 18f;

        public void Init(Vector3 dir, float dmg)
        {
            _dir = dir.normalized;
            _dmg = dmg;

            var vis = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            vis.transform.SetParent(transform, false);
            vis.transform.localScale = Vector3.one * 0.28f;
            Destroy(vis.GetComponent<Collider>());
            var m = SMat.Lit(new Color(0.75f, 1f, 0.35f));
            SMat.Apply(vis.GetComponent<Renderer>(), m);
            Destroy(gameObject, 5f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _life -= dt;
            if (_life <= 0f) { Destroy(gameObject); return; }

            float step = Speed * dt;
            if (Physics.SphereCast(transform.position, 0.18f, _dir, out var hit, step, ~0, QueryTriggerInteraction.Ignore))
            {
                var root = GameRoot.Instance;
                if (root != null && root.PlayerTransform != null)
                {
                    if (Vector3.Distance(hit.point, root.PlayerTransform.position + Vector3.up) < 2.2f)
                        root.DamagePlayer(_dmg);
                }
                Destroy(gameObject);
                return;
            }
            transform.position += _dir * step;
        }
    }
}
