using UnityEngine;
using S.Core;

namespace S.View
{
    /// <summary>
    /// 敌人。AI 极简（趋向玩家 + 接触/远程攻击），重点是「能吃住各种配件效果」：
    /// 护甲、燃烧、减速、穿透都在这层体现。
    /// </summary>
    public sealed class Enemy : MonoBehaviour
    {
        public enum Kind { Crawler, Brute, Bomber, Turret }

        public Kind Type = Kind.Crawler;
        public float MaxHp = 60f;
        public float Hp = 60f;
        public float ContactDamage = 12f;
        public float Speed = 2f;   // 默认值；实际由 GameRoot.KindSpeed 在 Configure 时写入
        public int Xp = 8;
        public bool IsFlying;

        /// <summary>是否为精英（体积更大、色调更亮、击杀回血）。</summary>
        public bool IsElite;

        /// <summary>击杀时回复玩家多少生命（0 = 不回）。精英是单图下唯一的续航来源。</summary>
        public float HealOnKill;

        public Vector3 AimPoint => transform.position + Vector3.up * 0.8f;

        CharacterController _cc;
        float _attackCd;
        float _slowLeft;
        float _slowPct;
        int _burnStacks;
        float _burnLeft;
        float _burnDps;
        float _burnTick;
        bool _shieldFacingPlayer;

        public bool Alive => Hp > 0f;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
        }

        public void Configure(Enemy.Kind kind, float hp, float dmg, float speed, int xp)
        {
            Type = kind;
            MaxHp = hp; Hp = hp;
            ContactDamage = dmg; Speed = speed; Xp = xp;
            IsFlying = false;
        }

        /// <summary>
        /// 精英放大：放大**视觉子物体**与**碰撞体**，但不改 transform.localScale。
        ///
        /// 为什么不动 localScale：CharacterController 的 radius/height 是本地空间尺寸，
        /// 会随 transform 缩放自动放大。若两者同时乘 scale，实际体积会变成 scale²
        /// （1.35² = 1.82 倍），精英看起来像巨怪而且近战范围远超视觉。
        /// 只放大 Body 子物体与碰撞体，世界尺寸才是可控的 1.35 倍。
        /// </summary>
        public void ApplyEliteScale(float scale)
        {
            if (scale <= 0f || Mathf.Approximately(scale, 1f)) return;

            // 视觉：SpawnFit 已经把模型缩到目标高度，这里再乘一次即可
            var body = transform.Find("Body");
            if (body != null) body.localScale = body.localScale * scale;

            // 碰撞体：显式按世界尺寸放大（因为 transform 没变）
            var cc = GetComponent<CharacterController>();
            if (cc != null)
            {
                cc.height *= scale;
                cc.radius *= scale;
                cc.center = new Vector3(0f, cc.height * 0.5f, 0f);
            }

            // 精英必须一眼可辨：加一圈自发光轮廓
            var glow = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            glow.name = "EliteGlow";
            glow.transform.SetParent(transform, false);
            glow.transform.localPosition = new Vector3(0f, cc != null ? cc.center.y : 0.95f, 0f);
            glow.transform.localScale = new Vector3(1.35f, 1.1f, 1.35f) * scale;
            Destroy(glow.GetComponent<Collider>());
            SMat.Apply(glow.GetComponent<Renderer>(), SMat.Unlit(new Color(1f, 0.82f, 0.32f, 0.20f)));
        }

        void Update()
        {
            if (!Alive) return;
            float dt = Time.deltaTime;

            // 燃烧
            if (_burnLeft > 0f)
            {
                _burnLeft -= dt;
                _burnTick += dt;
                if (_burnTick >= 0.25f)
                {
                    _burnTick -= 0.25f;
                    ApplyDamage(_burnDps * 0.25f * _burnStacks);
                    if (!Alive) return;
                }
                if (_burnLeft <= 0f) { _burnStacks = 0; _burnDps = 0f; }
            }

            // 减速
            float speedMul = 1f;
            if (_slowLeft > 0f) { _slowLeft -= dt; speedMul = 1f - _slowPct; }

            var root = GameRoot.Instance;
            if (root == null || root.PlayerTransform == null) return;

            Vector3 toPlayer = root.PlayerTransform.position - transform.position;
            float dist = toPlayer.magnitude;
            Vector3 dir = dist > 0.01f ? toPlayer / dist : Vector3.forward;

            switch (Type)
            {
                case Kind.Crawler:
                    Move(dir * Speed * speedMul * dt);
                    TryTouch(ContactDamage);
                    break;

                case Kind.Brute:
                    Move(dir * Speed * speedMul * dt);
                    TryTouch(ContactDamage);
                    break;

                case Kind.Bomber:
                    // 自爆型：贴近后引爆（撤离时间极短，玩家必须优先点掉）
                    if (dist > 2.6f) Move(dir * Speed * speedMul * dt);
                    else
                    {
                        _attackCd -= dt;
                        if (_attackCd <= 0f)
                        {
                            _attackCd = 1.2f;
                            GameRoot.Instance?.SpawnBomberBlast(transform.position, ContactDamage);
                            ApplyDamage(MaxHp);
                        }
                    }
                    break;

                case Kind.Turret:
                    // 炮塔：原地不动，远程点射
                    _attackCd -= dt;
                    if (dist < 30f && _attackCd <= 0f)
                    {
                        _attackCd = 1.6f;
                        root.EnemyShot(AimPoint, root.PlayerAimPoint, ContactDamage);
                    }
                    break;
            }

            if (transform.position.y > -30f)
            {
                // 面向玩家
                Vector3 flat = dir; flat.y = 0f;
                if (flat.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.Slerp(transform.rotation,
                        Quaternion.LookRotation(flat), 12f * dt);
            }
        }

        void Move(Vector3 delta)
        {
            if (_cc != null && _cc.enabled) _cc.Move(delta);
            else transform.position += delta;
        }

        void TryTouch(float dmg)
        {
            if (GameRoot.Instance == null) return;
            if (Vector3.Distance(transform.position, GameRoot.Instance.PlayerTransform.position) < 1.4f)
                GameRoot.Instance.DamagePlayer(dmg);
        }

        /// <summary>命中判定入口。desc 携带所有配件结算结果。</summary>
        public void TakeDamage(ProjectileDesc desc, Vector3 point, float amount, bool splash)
        {
            if (!Alive) return;

            float final = amount;

            // 盾卫正面减伤（穿甲弹无视）
            if (Type == Kind.Brute && !splash)
            {
                Vector3 toSrc = (point - transform.position).normalized;
                float facing = Vector3.Dot(transform.forward, -toSrc);
                if (facing > 0.3f)
                {
                    bool ignore = desc != null && desc.Source != null && desc.Source.Pierce > 0;
                    final *= ignore ? 0.75f : 0.45f;
                }
            }

            ApplyDamage(final);

            // 燃烧
            if (desc != null && desc.Burn > 0f)
            {
                _burnDps = Mathf.Max(_burnDps, desc.Burn);
                _burnLeft = Mathf.Max(_burnLeft, 3f);
                _burnStacks = Mathf.Min(_burnStacks + 1, 3);
            }

            // 冰霜
            if (desc != null && desc.Slow > 0f) ApplySlow(desc.Slow, 2f);

            GameRoot.Instance?.SpawnHitFx(point);
        }

        /// <summary>施加减速。力场每秒会重复施加，所以这里必须「取更强、刷新时间」而不是累乘。</summary>
        public void ApplySlow(float pct, float time)
        {
            _slowPct = Mathf.Max(_slowPct, Mathf.Clamp01(pct));
            _slowLeft = Mathf.Max(_slowLeft, time);
        }

        /// <summary>被奇点吸引：直接改位移（绕过 AI 决策，但保留 CharacterController 的碰撞）。</summary>
        public void PullToward(Vector3 delta)
        {
            Move(delta);
        }

        public void ApplyDamage(float amount)
        {
            if (!Alive) return;
            Hp -= amount;
            GameRoot.Instance?.SpawnDamageNumber(transform.position + Vector3.up * 1.2f, amount);
            if (Hp <= 0f)
            {
                Hp = 0f;
                GameRoot.Instance?.OnEnemyKilled(this, Xp);
                Destroy(gameObject);
            }
        }
    }
}
