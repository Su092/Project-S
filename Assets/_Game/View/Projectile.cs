using UnityEngine;
using S.Core;

namespace S.View
{
    /// <summary>
    /// 单个弹丸的表现与命中处理。所有数值来自 Core.ProjectileDesc，
    /// 本类不参与结算规则，只执行「飞、撞、命中、爆炸」。
    /// </summary>
    public sealed class Projectile : MonoBehaviour
    {
        ProjectileDesc _d;
        GunDef _gun;
        Vector3 _dir;
        Vector3 _side;          // 施法瞬间的朝向（分裂体散开用）
        float _life;
        float _speed;
        Vector3 _vel;
        int _pierceLeft;
        int _bounceLeft;

        static readonly Collider[] s_hits = new Collider[8];

        public void Init(ProjectileDesc desc, GunDef gun, Vector3 dir)
        {
            _d = desc;
            _gun = gun;
            _dir = dir.normalized;
            _side = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
            _life = desc.Lifetime;
            _speed = gun.MuzzleSpeed * desc.SpeedMul;
            _pierceLeft = desc.Pierce;
            _bounceLeft = desc.Bounce;

            if (gun.Mode == ProjectileMode.Ballistic)
                _vel = _dir * _speed;
            else
                _vel = _dir * _speed;

            // 简易外观：按来源配色
            var vis = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            vis.name = "Vis";
            vis.transform.SetParent(transform, false);
            vis.transform.localScale = Vector3.one * (desc.SplashRadius > 0.5f ? 0.34f : 0.16f);
            Object.Destroy(vis.GetComponent<Collider>());
            var r = vis.GetComponent<Renderer>();
            var mat = SMat.Lit(desc.SplashRadius > 0.5f
                ? new Color(1f, 0.45f, 0.15f)
                : new Color(0.55f, 0.85f, 1f), 0.2f);
            SMat.Apply(r, mat);
        }

        /// <summary>
        /// 链式触发时，衍生件可能在飞行途中改这颗弹丸（加速、追踪、分裂前的属性）。
        /// 这里只同步影响飞行与表现的部分，命中伤害始终读 `_d.Damage`（外部已就地更新）。
        /// </summary>
        public void ApplyDesc(ProjectileDesc desc)
        {
            if (desc == null) return;
            _d = desc;

            float want = _gun.MuzzleSpeed * desc.SpeedMul;
            if (_vel.sqrMagnitude > 0.0001f)
                _vel = _vel.normalized * want;
            _speed = want;

            _pierceLeft = desc.Pierce;
            _bounceLeft = desc.Bounce;

            var vis = transform.childCount > 0 ? transform.GetChild(0) : null;
            if (vis != null)
            {
                float s = desc.SplashRadius > 0.5f ? 0.34f : 0.16f;
                vis.localScale = Vector3.one * s;
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _life -= dt;
            if (_life <= 0f) { ExplodeOrDie(); return; }

            // 追踪
            if (_d.HomingRate > 0f && GameRoot.Instance != null)
            {
                var target = GameRoot.Instance.FindNearestEnemy(transform.position, 40f);
                if (target != null)
                {
                    Vector3 want = (target.AimPoint - transform.position).normalized;
                    _vel = Vector3.RotateTowards(_vel.normalized, want,
                        _d.HomingRate * Mathf.Deg2Rad * dt, 0f) * _vel.magnitude;
                }
            }

            // 重力（榴弹）
            if (_gun.Mode == ProjectileMode.Ballistic)
                _vel += Vector3.down * _gun.Gravity * dt;

            float step = _vel.magnitude * dt;
            Vector3 delta = _vel.normalized * step;

            // Collide 而非 Ignore：Boss 与敌人用 Trigger 做受击判定，
            // 设为 Ignore 会让它们完全打不到（踩过）。
            if (Physics.SphereCast(transform.position, 0.12f, _vel.normalized, out var hit, step, ~0, QueryTriggerInteraction.Collide))
            {
                HandleHit(hit, delta);
                return;
            }

            transform.position += delta;
            transform.rotation = Quaternion.LookRotation(_vel.normalized);
        }

        void HandleHit(RaycastHit hit, Vector3 delta)
        {
            // Boss 本体（Trigger 球）
            var bossHit = hit.collider.GetComponentInParent<BossView>();
            if (bossHit != null)
            {
                bossHit.OnHit(_d, hit.point, _d.Damage);
                GameRoot.Instance?.OnProjectileHit(this, hit.point);
                if (_d.SplashRadius > 0f) Explode(hit.point);
                Destroy(gameObject);
                return;
            }

            // 能量柱
            var pillar = hit.collider.GetComponentInParent<Pillar>();
            if (pillar != null && pillar.Alive)
            {
                GameRoot.Instance?.OnPillarHit(pillar, _d.Damage);
                GameRoot.Instance?.SpawnHitFx(hit.point);
                if (_pierceLeft > 0)
                {
                    _pierceLeft--;
                    transform.position += _vel.normalized * 0.4f;
                    return;
                }
                if (_d.SplashRadius > 0f) Explode(hit.point);
                Destroy(gameObject);
                return;
            }

            bool isEnemy = hit.collider.GetComponentInParent<Enemy>() != null;

            if (isEnemy)
            {
                var e = hit.collider.GetComponentInParent<Enemy>();
                e.TakeDamage(_d, hit.point, _d.Damage, false);
                GameRoot.Instance?.OnProjectileHit(this, hit.point);

                if (_pierceLeft > 0)
                {
                    _pierceLeft--;
                    transform.position += _vel.normalized * 0.3f; // 推过敌人，避免重复命中
                    if (_d.SplashRadius > 0f) Explode(hit.point);
                    return;
                }

                if (_d.SplashRadius > 0f) Explode(hit.point);
                Destroy(gameObject);
                return;
            }

            // 地形
            if (_bounceLeft > 0)
            {
                _bounceLeft--;
                _vel = Vector3.Reflect(_vel, hit.normal);
                transform.position = hit.point + hit.normal * 0.15f;
                return;
            }

            if (_d.SplashRadius > 0f) Explode(hit.point);
            Destroy(gameObject);
        }

        void ExplodeOrDie()
        {
            if (_d.SplashRadius > 0f) Explode(transform.position);
            Destroy(gameObject);
        }

        void Explode(Vector3 at)
        {
            GameRoot.Instance?.Explode(at, _d);
        }
    }
}
