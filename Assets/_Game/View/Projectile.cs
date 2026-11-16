using UnityEngine;
using S.Core;

namespace S.View
{
    /// <summary>
    /// 单个投射物的表现与命中处理（对齐 MODCORE 原型的 7 种弹道形态）。
    ///
    /// 本类只执行「飞、撞、命中、爆炸、触发」。所有数值来自 Core.ProjectileDesc，
    /// 不参与任何结算规则 —— 规则改动一律发生在 Core，否则 dotnet 断言就测不到。
    ///
    /// 七种形态各自的行为：
    ///   Proj    直线飞行，撞什么算什么（最常用）
    ///   Beam    瞬时命中（不飞行），用射线一次结算
    ///   Mine    抛出去落地吸附，敌人靠近才炸
    ///   Field   抛出去落地展开力场，持续伤害 + 减速
    ///   Chain   锁定准星附近敌人，在敌人之间弹跳
    ///   Saw     飞出后返回玩家，路径上无限穿透
    ///   Hole    缓慢飞行并持续吸引/伤害周围敌人，结束时坍缩爆炸
    /// </summary>
    public sealed class Projectile : MonoBehaviour
    {
        ProjectileDesc _d;
        Vector3 _dir;
        float _life;
        int _pierceLeft;
        int _bounceLeft;
        Vector3 _vel;

        // 陷阱/力场状态
        bool _planted;
        float _tickTimer;
        float _fieldLeft;
        ProjectileVisual _visual;
        readonly System.Collections.Generic.HashSet<Enemy> _touched =
            new System.Collections.Generic.HashSet<Enemy>();

        // 回旋锯
        bool _returning;

        // 触发弹
        bool _triggered;

        public void Init(ProjectileDesc desc, Vector3 dir)
        {
            _d = desc;
            _dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
            _life = desc.Lifetime;
            _pierceLeft = desc.Pierce;
            _bounceLeft = desc.Bounce;
            _vel = _dir * desc.Speed;

            BuildVisual();

            // 光束与连锁不是「飞行物」：生成即结算
            if (desc.Kind == BulletKind.Beam) { FireBeam(); return; }
            if (desc.Kind == BulletKind.Chain) { FireChain(); return; }
        }

        void BuildVisual()
        {
            transform.rotation = Quaternion.LookRotation(_dir);
            _visual = gameObject.AddComponent<ProjectileVisual>();
            _visual.Init(_d);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            switch (_d.Kind)
            {
                case BulletKind.Mine: TickMine(dt); return;
                case BulletKind.Field: TickField(dt); return;
                case BulletKind.Hole: TickHole(dt); return;
                case BulletKind.Saw: TickSaw(dt); return;
            }

            TickFlight(dt);
        }

        // ---------------- 普通飞行 ----------------

        void TickFlight(float dt)
        {
            _life -= dt;
            if (_life <= 0f) { Expire(); return; }

            if (_d.Homing > 0f && GameRoot.Instance != null)
            {
                var target = GameRoot.Instance.FindNearestEnemy(transform.position, 40f);
                if (target != null)
                {
                    Vector3 want = (target.AimPoint - transform.position).normalized;
                    _vel = Vector3.RotateTowards(_vel.normalized, want,
                        _d.Homing * Mathf.Deg2Rad * 60f * dt, 0f) * _vel.magnitude;
                }
            }

            // 榴弹类受重力（数据里 gravity > 0 才是抛射）
            if (_d.Gravity > 0f) _vel += Vector3.down * _d.Gravity * dt;

            float step = _vel.magnitude * dt;
            if (step <= 0.0001f) return;

            // Collide 而非 Ignore：Boss 与敌人用 Trigger 做受击判定（踩过）
            if (Physics.SphereCast(transform.position, Mathf.Max(0.025f, _d.Size * 0.5f),
                    _vel.normalized, out var hit, step, ~0, QueryTriggerInteraction.Collide))
            {
                HandleHit(hit);
                return;
            }

            transform.position += _vel.normalized * step;
            if (_vel.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(_vel.normalized);
        }

        void HandleHit(RaycastHit hit)
        {
            var boss = hit.collider.GetComponentInParent<BossView>();
            if (boss != null)
            {
                boss.OnHit(_d, hit.point, _d.Damage);
                TriggerPayload(hit.point, hit.normal);
                if (_d.ExplodeR > 0f) Explode(hit.point);
                Destroy(gameObject);
                return;
            }

            var pillar = hit.collider.GetComponentInParent<Pillar>();
            if (pillar != null && pillar.Alive)
            {
                GameRoot.Instance?.OnPillarHit(pillar, _d.Damage);
                GameRoot.Instance?.SpawnHitFx(hit.point);
                if (_pierceLeft > 0) { _pierceLeft--; transform.position += _vel.normalized * 0.4f; return; }
                if (_d.ExplodeR > 0f) Explode(hit.point);
                Destroy(gameObject);
                return;
            }

            var e = hit.collider.GetComponentInParent<Enemy>();
            if (e != null && e.Alive)
            {
                e.TakeDamage(_d, hit.point, _d.Damage, false);
                TriggerPayload(hit.point, hit.normal);

                if (_pierceLeft > 0)
                {
                    _pierceLeft--;
                    transform.position += _vel.normalized * 0.35f;
                    if (_d.ExplodeR > 0f) Explode(hit.point);
                    return;
                }

                if (_d.ExplodeR > 0f) Explode(hit.point);
                Destroy(gameObject);
                return;
            }

            // 地形命中：按弹种取色，反弹也留下接触火花。
            CombatFx.Impact(hit.point, PlayerWeapons.ParseColor(_d.Source != null ? _d.Source.Color : "#ffd25a"), hit.normal);
            TriggerPayload(hit.point, hit.normal);

            if (_bounceLeft > 0)
            {
                _bounceLeft--;
                _vel = Vector3.Reflect(_vel, hit.normal);
                transform.position = hit.point + hit.normal * 0.15f;
                return;
            }

            if (_d.ExplodeR > 0f) Explode(hit.point);
            Destroy(gameObject);
        }

        void Expire()
        {
            if (_d.Trigger == TriggerKind.Expire) TriggerPayload(transform.position, Vector3.up);
            if (_d.ExplodeR > 0f) Explode(transform.position);
            Destroy(gameObject);
        }

        // ---------------- 光束 ----------------

        /// <summary>
        /// 光束：一次射线结算，穿透与弹跳都按「沿射线继续」处理。
        /// 用 RaycastAll + 排序而不是逐段 SphereCast：光束本来就没有体积，
        /// 分段检测会让同一个敌人被打多次。
        /// </summary>
        void FireBeam()
        {
            float range = _d.Range;
            var hits = Physics.RaycastAll(transform.position, _dir, range, ~0, QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            int allowed = 1 + Mathf.Max(0, _pierceLeft);
            int done = 0;
            Vector3 end = transform.position + _dir * range;

            foreach (var h in hits)
            {
                if (done >= allowed) break;

                var e = h.collider.GetComponentInParent<Enemy>();
                if (e != null && e.Alive)
                {
                    e.TakeDamage(_d, h.point, _d.Damage, false);
                    GameRoot.Instance?.SpawnHitFx(h.point);
                    done++;
                    continue;
                }

                var boss = h.collider.GetComponentInParent<BossView>();
                if (boss != null)
                {
                    boss.OnHit(_d, h.point, _d.Damage);
                    done++;
                    continue;
                }

                var pillar = h.collider.GetComponentInParent<Pillar>();
                if (pillar != null && pillar.Alive)
                {
                    GameRoot.Instance?.OnPillarHit(pillar, _d.Damage);
                    done++;
                    continue;
                }

                // 撞到地形就截断，并留下接触火花。
                CombatFx.Impact(h.point, PlayerWeapons.ParseColor(_d.Source != null ? _d.Source.Color : "#ff4fd8"), h.normal);
                end = h.point;
                break;
            }

            GameRoot.Instance?.SpawnBeamFx(transform.position, end,
                PlayerWeapons.ParseColor(_d.Source != null ? _d.Source.Color : "#ff4fd8"));
            TriggerPayload(end, -_dir);
            Destroy(gameObject);
        }

        // ---------------- 连锁闪电 ----------------

        /// <summary>
        /// 连锁：从准星附近最近敌人起，在敌人之间弹跳 chainCount 次。
        /// 每次跳有明显更短的射程上限，否则会变成「全屏清场」，与「需要瞄准」的定位冲突。
        /// </summary>
        void FireChain()
        {
            var root = GameRoot.Instance;
            if (root == null) { Destroy(gameObject); return; }

            Vector3 from = transform.position;
            var first = root.FindNearestEnemy(from, _d.Range);
            if (first == null)
            {
                CombatFx.Beam(from, from + _dir * 6f, PlayerWeapons.ParseColor("#b7a6ff"), true);
                Destroy(gameObject);
                return;
            }

            var hitSet = new System.Collections.Generic.HashSet<Enemy>();
            var cur = first;
            int jumps = Mathf.Max(1, _d.ChainCount);

            for (int i = 0; i <= jumps && cur != null; i++)
            {
                cur.TakeDamage(_d, cur.AimPoint, _d.Damage, false);
                hitSet.Add(cur);

                CombatFx.Beam(from, cur.AimPoint,
                    PlayerWeapons.ParseColor(_d.Source != null ? _d.Source.Color : "#b7a6ff"), true);

                // 下一跳：离当前敌人最近、且没打过的
                from = cur.transform.position;
                cur = root.FindNearestEnemyExcept(from, _d.Range * 0.55f, hitSet);
            }

            TriggerPayload(first.AimPoint, Vector3.up);
            Destroy(gameObject);
        }

        // ---------------- 地雷 ----------------

        void TickMine(float dt)
        {
            if (!_planted)
            {
                _life -= dt;
                if (_life <= 0f) { Explode(transform.position); Destroy(gameObject); return; }
                if (_d.Gravity > 0f) _vel += Vector3.down * _d.Gravity * dt;

                float step = _vel.magnitude * dt;
                if (Physics.Raycast(transform.position, _vel.normalized, out var hit,
                        step + 0.2f, ~0, QueryTriggerInteraction.Ignore))
                {
                    transform.position = hit.point + hit.normal * 0.05f;
                    _planted = true;
                    _visual?.Deploy(hit.normal, _d.Radius);
                    _life = _d.Duration > 0f ? _d.Duration : 25f;
                    _vel = Vector3.zero;
                    return;
                }
                transform.position += _vel.normalized * step;
                return;
            }

            // 已布设：等敌人靠近
            _life -= dt;
            if (_life <= 0f) { Explode(transform.position); Destroy(gameObject); return; }

            var e = GameRoot.Instance != null
                ? GameRoot.Instance.FindNearestEnemy(transform.position, 2.2f)
                : null;
            if (e != null)
            {
                Explode(transform.position);
                Destroy(gameObject);
            }
        }

        // ---------------- 力场 ----------------

        void TickField(float dt)
        {
            if (!_planted)
            {
                _life -= dt;
                if (_life <= 0f) { Destroy(gameObject); return; }
                if (_d.Gravity > 0f) _vel += Vector3.down * _d.Gravity * dt;

                float step = _vel.magnitude * dt;
                if (Physics.Raycast(transform.position, _vel.normalized, out var hit,
                        step + 0.2f, ~0, QueryTriggerInteraction.Ignore))
                {
                    transform.position = hit.point + hit.normal * 0.05f;
                    _planted = true;
                    _visual?.Deploy(hit.normal, _d.Radius);
                    _fieldLeft = _d.Duration > 0f ? _d.Duration : 5f;
                    _vel = Vector3.zero;

                    return;
                }
                transform.position += _vel.normalized * step;
                return;
            }

            _fieldLeft -= dt;
            if (_fieldLeft <= 0f) { Destroy(gameObject); return; }

            _tickTimer -= dt;
            if (_tickTimer > 0f) return;
            _tickTimer = 0.35f;

            var list = GameRoot.Instance != null ? GameRoot.Instance.Enemies : null;
            if (list == null) return;

            for (int i = list.Count - 1; i >= 0; i--)
            {
                var e = list[i];
                if (e == null || !e.Alive) continue;
                if (Vector3.Distance(e.transform.position, transform.position) > _d.Radius) continue;
                e.TakeDamage(_d, e.AimPoint, _d.Damage * 0.35f, true);
                if (_d.Slow > 0f) e.ApplySlow(_d.Slow, 0.6f);
            }
        }

        // ---------------- 奇点 ----------------

        void TickHole(float dt)
        {
            _life -= dt;
            transform.position += _vel.normalized * _vel.magnitude * dt;

            var list = GameRoot.Instance != null ? GameRoot.Instance.Enemies : null;
            if (list != null)
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var e = list[i];
                    if (e == null || !e.Alive) continue;
                    float dist = Vector3.Distance(e.transform.position, transform.position);
                    if (dist > _d.Radius) continue;

                    // 吸引：把敌人往中心拉
                    Vector3 pull = (transform.position - e.transform.position).normalized;
                    e.PullToward(pull * 1.6f * dt);

                    _tickTimer -= dt;
                    if (_tickTimer <= 0f)
                    {
                        _tickTimer = 0.3f;
                        e.ApplyDamage(_d.Damage * 0.3f);
                    }
                }
            }

            if (_life <= 0f)
            {
                if (_d.ExplodeR > 0f) Explode(transform.position);
                Destroy(gameObject);
            }
        }

        // ---------------- 回旋锯 ----------------

        void TickSaw(float dt)
        {
            _life -= dt;
            if (_life <= 0f) { Destroy(gameObject); return; }

            var player = GameRoot.Instance != null ? GameRoot.Instance.PlayerTransform : null;

            // 飞出 40% 存活时间后折返
            if (!_returning && _life < _d.Lifetime * 0.6f) _returning = true;

            if (_returning && player != null)
            {
                Vector3 want = (player.position + Vector3.up * 1.2f - transform.position).normalized;
                _vel = Vector3.RotateTowards(_vel.normalized, want, 12f * dt, 0f) * _vel.magnitude;
            }

            if (_vel.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(_vel.normalized);

            float step = _vel.magnitude * dt;
            if (Physics.SphereCast(transform.position, Mathf.Max(0.1f, _d.Size * 0.5f),
                    _vel.normalized, out var hit, step, ~0, QueryTriggerInteraction.Collide))
            {
                var e = hit.collider.GetComponentInParent<Enemy>();
                if (e != null && e.Alive && _touched.Add(e))
                {
                    e.TakeDamage(_d, hit.point, _d.Damage, false);
                    transform.position += _vel.normalized * 0.4f;
                    return;
                }

                var boss = hit.collider.GetComponentInParent<BossView>();
                if (boss != null)
                {
                    boss.OnHit(_d, hit.point, _d.Damage);
                    transform.position += _vel.normalized * 0.4f;
                    return;
                }

                if (_bounceLeft > 0)
                {
                    _bounceLeft--;
                    _vel = Vector3.Reflect(_vel, hit.normal);
                    return;
                }
            }

            transform.position += _vel.normalized * step;
        }

        // ---------------- 爆炸与触发 ----------------

        void Explode(Vector3 at)
        {
            GameRoot.Instance?.Explode(at, _d);
        }

        /// <summary>
        /// 触发载荷：命中 / 定时 / 消散三种条件都会走到这里。
        /// 只触发一次 —— 穿透弹连续命中多个敌人时，载荷不应重复施放（会指数爆炸）。
        /// </summary>
        void TriggerPayload(Vector3 at, Vector3 normal)
        {
            if (_d.Trigger == TriggerKind.None) return;
            if (_d.Payload == null || _d.Payload.Count == 0) return;
            if (_triggered) return;
            _triggered = true;

            CombatFx.Impact(at, PlayerWeapons.ParseColor(_d.Source != null ? _d.Source.Color : "#d4b8ff"), normal);
            GameRoot.Instance?.SpawnPayload(at, normal, _d.Payload);
        }
    }
}
