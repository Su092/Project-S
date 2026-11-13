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
        public enum Kind { Crawler, Spitter, Shield, Drone }

        public Kind Type = Kind.Crawler;
        public float MaxHp = 60f;
        public float Hp = 60f;
        public float ContactDamage = 12f;
        public float Speed = 2f;   // 默认值；实际由 GameRoot.KindSpeed 在 Configure 时写入
        public int Xp = 8;
        public bool IsFlying;

        public Vector3 AimPoint => transform.position + Vector3.up * (IsFlying ? 0.1f : 0.8f);

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
            IsFlying = kind == Kind.Drone;
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

            // 只在玩家所在区域追击：玩家换区后，旧敌人不再追（避免跨区大追杀）
            if (root.Zones != null && root.Progress != null)
            {
                float zoneZ = ZoneBuilder.CenterOf(root.Progress.Current).z;
                float myZ = transform.position.z;
                if (Mathf.Abs(myZ - zoneZ) > ZoneBuilder.ZoneLength * 0.62f)
                {
                    // 玩家已离开本区：慢速游荡，不追
                    Move(transform.forward * Speed * 0.25f * speedMul * dt);
                    return;
                }
            }

            Vector3 toPlayer = root.PlayerTransform.position - transform.position;
            float dist = toPlayer.magnitude;
            Vector3 dir = dist > 0.01f ? toPlayer / dist : Vector3.forward;

            switch (Type)
            {
                case Kind.Crawler:
                    Move(dir * Speed * speedMul * dt);
                    TryTouch(ContactDamage);
                    break;

                case Kind.Spitter:
                    if (dist > 13f) Move(dir * Speed * speedMul * dt);
                    else if (dist < 10f) Move(-dir * Speed * speedMul * dt);
                    _attackCd -= dt;
                    if (dist < 20f && _attackCd <= 0f)
                    {
                        _attackCd = 1.9f;
                        root.EnemyShot(AimPoint, root.PlayerAimPoint, ContactDamage);
                    }
                    break;

                case Kind.Shield:
                    Move(dir * Speed * speedMul * dt);
                    TryTouch(ContactDamage);
                    break;

                case Kind.Drone:
                    // 飞到玩家侧后方
                    Vector3 hover = root.PlayerTransform.position + Quaternion.Euler(0, 40f, 0) * (Vector3.back * 9f) + Vector3.up * 4.2f;
                    Vector3 toHover = hover - transform.position;
                    Move(toHover.normalized * Speed * speedMul * dt);
                    _attackCd -= dt;
                    if (_attackCd <= 0f && dist < 26f)
                    {
                        _attackCd = 1.4f;
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

            // 盾卫正面减伤（空蚀弹无视一半）
            if (Type == Kind.Shield && !splash)
            {
                Vector3 toSrc = (point - transform.position).normalized;
                float facing = Vector3.Dot(transform.forward, -toSrc);
                if (facing > 0.3f)
                {
                    bool ignore = desc != null && desc.Source != null && desc.Source.IgnoreArmor;
                    final *= ignore ? 0.7f : 0.4f;   // 正面 −60%，空蚀弹只减 30%
                }
            }
            if (desc != null && desc.Source != null && desc.Source.IgnoreArmor) final *= 1f;

            ApplyDamage(final);

            // 燃素
            if (desc != null && desc.Source != null && desc.Source.BurnDps > 0f)
            {
                _burnDps = Mathf.Max(_burnDps, desc.Source.BurnDps);
                _burnLeft = Mathf.Max(_burnLeft, desc.Source.BurnTime);
                _burnStacks = Mathf.Min(_burnStacks + 1,
                    Mathf.Max(1, desc.Source.BurnMaxStacks));
            }

            // 冻结
            if (desc != null && desc.Source != null && desc.Source.SlowPct > 0f)
            {
                _slowPct = Mathf.Max(_slowPct, desc.Source.SlowPct);
                _slowLeft = Mathf.Max(_slowLeft, desc.Source.SlowTime);
            }

            GameRoot.Instance?.SpawnHitFx(point);
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
