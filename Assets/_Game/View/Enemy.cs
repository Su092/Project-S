using UnityEngine;
using S.Core;

namespace S.View
{
    /// <summary>
    /// 敌人。AI 极简（趋向玩家 + 接触/远程攻击），重点是「能吃住各种配件效果」：
    /// 护甲、燃烧、减速、穿透都在这层体现。
    ///
    /// **本轮改造**：`Enemy.Kind` 枚举换成 `EnemyDef`（来自 Cfg/Enemy.tsv）。
    /// 旧版的 Type 枚举同时承担了三件事 —— 模型/数值查表、行为分支、UI 与遥测的分类名。
    /// 新增一种敌人要改 7 处（GameRoot 的 6 个函数 + 这里的 switch），
    /// 漏一处就静默退化成爬行者。现在数值全由 def 提供，
    /// 这里只剩「行为策略」这一件事，且策略只有 3 种。
    /// </summary>
    public sealed class Enemy : MonoBehaviour
    {
        /// <summary>行为策略（Cfg/Enemy.tsv 的 `ai` 列）。</summary>
        public EnemyAi Ai = EnemyAi.Chaser;

        /// <summary>数据定义。HUD/遥测取名字与 id 都从这里读。</summary>
        public EnemyDef Def;

        public float MaxHp = 60f;
        public float Hp = 60f;
        public float ContactDamage = 12f;
        public float Speed = 2f;
        public int Xp = 8;

        /// <summary>是否为精英（体积更大、色调更亮、击杀回血）。</summary>
        public bool IsElite;
        public bool ExternalControl, CombatAuxiliary;
        public MusicEliteBrain MusicBrain { get; private set; }

        /// <summary>击杀时回复玩家多少生命（0 = 不回）。精英是单图下唯一的续航来源。</summary>
        public float HealOnKill;

        public Vector3 AimPoint => Visual ? Visual.AimPoint : transform.position + Vector3.up * 0.8f;
        public EnemyVisual Visual { get; private set; }
        public void BindVisual(EnemyVisual visual)
        {
            Visual = visual;
            if(visual.AssetId.StartsWith("S"))
            {
                _shieldFacing=0;
                if(IsElite){MusicBrain=gameObject.AddComponent<MusicEliteBrain>();MusicBrain.Init(this);}
                else if(!ExternalControl)
                {
                    Ai=visual.AssetId.StartsWith("S11")?EnemyAi.Turret:EnemyAi.Chaser;
                    if(visual.AssetId.StartsWith("S03")){Speed=3.5f;_contactRange=1.4f;}
                    if(visual.AssetId.StartsWith("S11"))Speed=2.2f;
                }
            }
            gameObject.layer = LayerMask.NameToLayer(WorldPhysics.MovementLayer);
            Physics.IgnoreLayerCollision(gameObject.layer, LayerMask.NameToLayer(WorldPhysics.ShotLayer), true);
        }

        CharacterController _cc;
        float _attackCd;
        float _attackWindup = -1f;
        EnemyMotion _motion;
        TheaterPathFollower _path;
        float _slowLeft;
        float _slowPct;
        int _burnStacks;
        float _burnLeft;
        float _burnDps;
        float _burnTick;

        // ---- 从 def 缓存下来，避免每帧查字典 ----
        float _contactRange;
        float _attackRange;
        float _attackCooldown;
        float _shieldFacing;
        float _shieldMul;
        float _shieldPierceMul;

        public bool Alive => Hp > 0f;

        /// <summary>分类名（遥测/诊断用）。旧代码里是 `Type.ToString()`，现在是配置里的 id。</summary>
        public string KindId => Def != null && !string.IsNullOrEmpty(Def.Id) ? Def.Id : "enemy";

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            WorldPhysics.ConfigureActor(gameObject);
        }

        /// <summary>
        /// 按数据定义配置。**所有倍率在这里一次乘完**，别在 Update 里再乘
        /// （旧版漏乘一处会让精英的伤害变成「普通 × 威胁 × 精英」的意外组合）。
        /// </summary>
        public void Configure(EnemyDef def, float hpMul, float dmgMul, float speedMul, int xpMul,
            float hpScale = 1f, float dmgScale = 1f)
        {
            Def = def;
            if (def == null) return;

            Ai = def.Ai;
            MaxHp = def.Hp * hpMul * hpScale;
            Hp = MaxHp;
            ContactDamage = def.Damage * dmgMul * dmgScale;
            Speed = def.Speed * speedMul;
            Xp = def.Xp * xpMul;

            _contactRange = def.ContactRange;
            _attackRange = def.AttackRange;
            _attackCooldown = def.AttackCooldown;
            _shieldFacing = def.ShieldFacing;
            _shieldMul = def.ShieldMul;
            _shieldPierceMul = def.ShieldPierceMul;
        }

        /// <summary>用于测试/兼容：只给 id 与数值，不接数据表。</summary>
        public void Configure(string id, float hp, float dmg, float speed, int xp)
        {
            Def = new EnemyDef { Id = id, Name = id };
            Ai = EnemyAi.Chaser;
            MaxHp = hp; Hp = hp;
            ContactDamage = dmg; Speed = speed; Xp = xp;
            _contactRange = 1.4f;
            _attackRange = 0f;
            _attackCooldown = 0f;
            _shieldFacing = 0f;
            _shieldMul = 1f;
            _shieldPierceMul = 1f;
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
            SAssets.RemoveColliders(glow);
            SMat.Apply(glow.GetComponent<Renderer>(), SMat.Unlit(new Color(1f, 0.82f, 0.32f, 0.20f)));
        }

        void Start()
        {
            if (GameRoot.Instance?.Zones?.Map && GameRoot.Instance.Zones.Map.IsTheater)
                _path = gameObject.AddComponent<TheaterPathFollower>();
            if (Visual) return;
            var body = transform.Find("Body");
            if (body != null)
            {
                _motion = gameObject.AddComponent<EnemyMotion>();
                _motion.Bind(this, body.gameObject);
            }
        }

        void Update()
        {
            if (!Alive) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            _attackCd = Mathf.Max(0f, _attackCd - dt);

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

            if(ExternalControl)return;
            if(MusicBrain){MusicBrain.Tick(dt,speedMul);return;}
            Vector3 toPlayer = root.PlayerTransform.position - transform.position;
            float dist = toPlayer.magnitude;
            Vector3 dir = dist > 0.01f ? toPlayer / dist : Vector3.forward;

            if (_attackWindup >= 0f)
            {
                _attackWindup -= dt;
                if (_attackWindup <= 0f)
                {
                    _attackWindup = -1f;
                    float range = Ai == EnemyAi.Chaser ? _contactRange : _attackRange;
                    if (dist <= range && (!_path || _path.ClearSight(AimPoint, root.PlayerTransform.position + Vector3.up)))
                    {
                        if (Ai == EnemyAi.Chaser) root.DamagePlayer(ContactDamage);
                        else if (Ai == EnemyAi.Turret) root.EnemyShot(Visual && Visual.Muzzle ? Visual.Muzzle.position : AimPoint, root.PlayerAimPoint, ContactDamage);
                        else { root.SpawnBomberBlast(transform.position, ContactDamage); ApplyDamage(MaxHp); return; }
                    }
                }
            }
            Vector3 movementStart=transform.position;
            Vector3 travel = _path ? _path.Direction(root.PlayerTransform.position) : dir;
            if (_path && _cc && _cc.enabled) _cc.Move(Vector3.down * (8f * dt));
            switch (Ai)
            {
                case EnemyAi.Chaser:
                    if (_attackWindup < 0f) Move(travel * Speed * speedMul * dt);
                    BeginAttack(dist, _contactRange);
                    break;
                case EnemyAi.Bomber:
                    if (dist > _attackRange && _attackWindup < 0f) Move(travel * Speed * speedMul * dt);
                    BeginAttack(dist, _attackRange);
                    break;
                case EnemyAi.Turret:
                    if(Visual && Visual.AssetId.StartsWith("S11") && (dist>10 || (_path && !_path.ClearSight(AimPoint,root.PlayerAimPoint))))Move(travel*Speed*speedMul*dt);
                    BeginAttack(dist, _attackRange);
                    break;
            }

            // Movement has priority over looking at the target, including detours and wall sliding.
            if(!ActorFacing.AlongMovement(transform,transform.position-movementStart)&&_attackWindup>=0f)
            {
                Vector3 flat=dir;flat.y=0;
                if(flat.sqrMagnitude>.001f)transform.rotation=Quaternion.LookRotation(flat);
            }
        }

        public CollisionFlags MoveDelta(Vector3 delta)
        {
            Vector3 before=transform.position;CollisionFlags flags=CollisionFlags.None;
            if(_cc&&_cc.enabled)flags=_cc.Move(delta);else transform.position+=delta;
            ActorFacing.AlongMovement(transform,transform.position-before);return flags;
        }
        public void GroundStep(float dt){if(_cc&&_cc.enabled)_cc.Move(Vector3.down*8*dt);}
        public void TravelTo(Vector3 target,float dt,float multiplier=1)
        {
            var dir=_path?_path.Direction(target):MusicCombatWorld.Flat(target-transform.position).normalized;
            if(dir.sqrMagnitude>.001f)MoveDelta(dir*Speed*multiplier*dt);
        }
        void Move(Vector3 delta)=>MoveDelta(delta);

        void BeginAttack(float distance, float range)
        {
            if (range <= 0f || distance > range || _attackCd > 0f || _attackWindup >= 0f) return;
            if (_path && !_path.ClearSight(AimPoint, GameRoot.Instance.PlayerTransform.position + Vector3.up)) return;
            _attackWindup = Ai == EnemyAi.Bomber ? 0.4f : 0.22f;
            _attackCd = _attackCooldown > 0f ? _attackCooldown : 0.85f;
            _motion?.Attack();
            if (Visual) Visual.Attack(_attackWindup);
        }

        /// <summary>命中判定入口。desc 携带所有配件结算结果。</summary>
        public void TakeDamage(ProjectileDesc desc, Vector3 point, float amount, bool splash)
        {
            if (!Alive) return;

            float final = amount;

            // 盾面减伤（穿甲弹减轻减免）。阈值与两种系数都来自 Enemy.tsv，
            // 原先 `facing > 0.3f` / `0.45f` / `0.75f` 是写死在这里的。
            if (_shieldFacing > 0f && !splash)
            {
                Vector3 toSrc = (point - transform.position).normalized;
                float facing = Vector3.Dot(transform.forward, -toSrc);
                if (facing > _shieldFacing)
                {
                    bool ignore = desc != null && desc.Source != null && desc.Source.Pierce > 0;
                    final *= ignore ? _shieldPierceMul : _shieldMul;
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
            if(CombatAuxiliary)return; // Balcony mounts stay fixed under attraction effects.
            Move(delta);
        }

        public void ApplyDamage(float amount)
        {
            if (!Alive) return;
            Hp -= amount;
            if (Hp > 0f && Visual) Visual.Hit();
            GameRoot.Instance?.SpawnDamageNumber(transform.position + Vector3.up * 1.2f, amount);
            if (Hp <= 0f)
            {
                Hp = 0f;
                MusicBrain?.Cancel();
                GetComponent<MusicNoteGun>()?.BreakApart();
                GameRoot.Instance?.OnEnemyKilled(this, Xp);
                if (_cc) _cc.enabled = false;
                if (Visual) Destroy(gameObject, Visual.Die() + (Visual.AssetId.Length > 2 && Visual.AssetId[0] == 'S' && char.IsDigit(Visual.AssetId[1]) ? 0f : .35f));
                else Destroy(gameObject);
            }
        }
    }
}
