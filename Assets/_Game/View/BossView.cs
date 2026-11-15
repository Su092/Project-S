using System.Collections.Generic;
using UnityEngine;
using S.Core;

namespace S.View
{
    /// <summary>
    /// 铸造核心 Boss 的表现与伤害接入。
    /// 规则（阶段/护盾/破防）全在 Core.BossState，本类只做「读状态 → 改外观 → 转发事件」。
    /// 规格见 docs/gameplay-spec.md §3.1。
    /// </summary>
    public sealed class BossView : MonoBehaviour
    {
        public BossState State { get; private set; }

        Renderer _body;
        Renderer _shieldR;
        GameObject _shieldGo;
        float _moveTimer;
        Vector3 _target;
        float _shockCd;
        float _volleyCd;
        float _spikeCd;

        /// <summary>Boss 累计发射的弹幕数量（自检用：不能靠场景对象净增量，
        /// 因为弹幕会到期销毁，净增量可能为负）。</summary>
        public int BossFanVolleyCount { get; private set; }

        /// <summary>Boss 累计触发的地刺预警次数。</summary>
        public int BossSpikeCount { get; private set; }

        public Vector3 AimPoint => transform.position + Vector3.up * 1.6f;

        public void Init(float hp)
        {
            State = new BossState(hp);

            // 真实模型（缺件时回退到球体占位）
            var visual = SAssets.SpawnFit("Modle_Boss_ForgeCore", transform, Vector3.zero,
                3.23f, new Color(0.70f, 0.36f, 0.88f), 0.45f);
            visual.name = "Body";

            // 兜底：模型缺失时用球体，并保留 _body 引用以便破防变色
            _body = visual.GetComponentInChildren<Renderer>();
            if (_body == null)
            {
                var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                body.name = "BodyFallback";
                body.transform.SetParent(transform, false);
                body.transform.localPosition = new Vector3(0f, 2.2f, 0f);
                body.transform.localScale = new Vector3(3.6f, 2.6f, 3.6f);
                Destroy(body.GetComponent<Collider>());
                _body = body.GetComponent<Renderer>();
                SMat.Apply(_body, SMat.Lit(new Color(0.70f, 0.36f, 0.88f), 0.45f));
            }

            // 护盾球（P2 起显示）
            _shieldGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _shieldGo.name = "Shield";
            _shieldGo.transform.SetParent(transform, false);
            _shieldGo.transform.localPosition = new Vector3(0f, 2.2f, 0f);
            _shieldGo.transform.localScale = Vector3.one * 6.6f;
            Destroy(_shieldGo.GetComponent<Collider>());
            _shieldR = _shieldGo.GetComponent<Renderer>();
            SMat.Apply(_shieldR, SMat.Unlit(new Color(0.35f, 0.70f, 1f, 0.16f)));
            _shieldGo.SetActive(false);

            // 受击判定：用 Trigger 球，不挡玩家移动
            var col = gameObject.AddComponent<SphereCollider>();
            col.radius = 2.6f;
            col.center = new Vector3(0f, 2.2f, 0f);
            col.isTrigger = true;

            var hitbox = gameObject.AddComponent<BossHitbox>();
            hitbox.Owner = this;

            _target = transform.position;
            PickNewTarget();
        }

        void Update()
        {
            if (State == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // 规则推进
            var events = State.Tick(dt);
            for (int i = 0; i < events.Count; i++) Handle(events[i]);

            // 护盾显示与破防着色
            if (_shieldGo != null) _shieldGo.SetActive(State.ShieldUp);

            // 破防/狂暴时整体偏亮偏红：要作用到全部渲染器，不能只改第一个，
            // 否则模型只有一块变色，看起来像贴图错误（踩过）。
            var tint = State.IsBreached
                ? new Color(1f, 0.88f, 0.45f)
                : State.Phase == BossPhase.P3
                    ? new Color(1f, 0.55f, 0.62f)
                    : Color.white;
            ApplyTint(tint);

            // 缓慢移动（P3 加速）
            float speed = 2.5f * (State.Phase == BossPhase.P3 ? 1.6f : 1f);
            _moveTimer -= dt;
            if (_moveTimer <= 0f) PickNewTarget();
            transform.position = Vector3.MoveTowards(transform.position, _target, speed * dt);

            // 面向玩家
            var root = GameRoot.Instance;
            if (root != null && root.PlayerTransform != null)
            {
                var flat = root.PlayerTransform.position - transform.position;
                flat.y = 0f;
                if (flat.sqrMagnitude > 0.1f)
                    transform.rotation = Quaternion.Slerp(transform.rotation,
                        Quaternion.LookRotation(flat), 3f * dt);
            }

            // 接触伤害
            if (root != null && root.PlayerTransform != null)
            {
                if (Vector3.Distance(transform.position, root.PlayerTransform.position) < 3.2f)
                    root.DamagePlayer(25f * (root.Run != null ? root.Run.DmgScale : 1f));
            }

            // 冲击波视觉计时
            if (_shockCd > 0f) _shockCd -= dt;

            // ---- 技能调度（gameplay-spec §3.1）----
            if (root != null && root.PlayerTransform != null) TickSkills(dt, root);
        }

        /// <summary>
        /// 技能调度。三处细节必须守住：
        /// 1. **弹幕/地刺的计时器要独立**，不能和移动/护盾的节拍共用，否则改一个参数会连带改节奏。
        /// 2. **弹幕的「朝玩家」要在发射瞬间取一次方向**，逐帧追踪会让 5 发散开成一条线。
        /// 3. **地刺必须先预警再出伤**（SpikeTelegraph 自己等满 1.2s），
        ///    否则这个技能就变成不可躲的随机伤害（踩过）。
        /// </summary>
        void TickSkills(float dt, GameRoot root)
        {
            var phase = State.Phase;

            // P1 起就有扇形弹幕；P3 改为追踪 3 连
            _volleyCd -= dt;
            if (_volleyCd <= 0f)
            {
                if (phase == BossPhase.P3)
                {
                    _volleyCd = 2.4f;
                    FireTrackVolley(root);
                }
                else
                {
                    _volleyCd = 3.0f;
                    BossFanVolley(transform.position + Vector3.up * 2.0f,
                        root.PlayerAimPoint, 5);
                }
            }

            // 地刺：P2 起才出现，周期比弹幕长
            if (phase == BossPhase.P2 || phase == BossPhase.P3)
            {
                _spikeCd -= dt;
                if (_spikeCd <= 0f)
                {
                    _spikeCd = 4.5f;
                    SpawnSpikeTelegraph(root);
                }
            }
        }

        /// <summary>
        /// 扇形弹幕：以「朝向玩家」为中心均匀散开发慢弹。
        /// 计数累加在发射处而不是靠数场景对象 —— 弹幕会到期销毁，净增量可能为负。
        /// </summary>
        public void BossFanVolley(Vector3 origin, Vector3 aimPoint, int count)
        {
            BossFanVolleyCount += count;

            Vector3 baseDir = aimPoint - origin;
            baseDir.y = 0f;
            if (baseDir.sqrMagnitude < 0.01f) baseDir = transform.forward;
            baseDir.Normalize();

            const float totalArc = 70f;   // 扇形总角度
            float step = count > 1 ? totalArc / (count - 1) : 0f;

            for (int i = 0; i < count; i++)
            {
                float ang = -totalArc * 0.5f + step * i;
                Vector3 dir = Quaternion.Euler(0f, ang, 0f) * baseDir;

                var go = new GameObject("BossShot");
                go.name = "BossShot";
                go.transform.position = origin;

                var eb = go.AddComponent<EnemyBullet>();
                eb.Init(dir, 14f * (GameRoot.Instance != null && GameRoot.Instance.Run != null
                    ? GameRoot.Instance.Run.DmgScale : 1f));
            }

            SfxBank.Instance?.PlayShoot(0.6f, 1.2f, count, false);
        }

        /// <summary>P3 狂暴的追踪 3 连：三发带轻微时间差，各自在发射时重新取玩家方向。</summary>
        void FireTrackVolley(GameRoot root)
        {
            BossFanVolleyCount += 3;
            const float spread = 12f;

            for (int i = 0; i < 3; i++)
            {
                Vector3 origin = transform.position + Vector3.up * 2.0f;
                Vector3 dir = root.PlayerAimPoint - origin;
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
                dir = Quaternion.Euler(0f, (i - 1) * spread, 0f) * dir.normalized;

                var go = new GameObject("BossTrackShot");
                go.transform.position = origin;
                var eb = go.AddComponent<EnemyBullet>();
                eb.Init(dir, 16f * (root.Run != null ? root.Run.DmgScale : 1f));
            }

            SfxBank.Instance?.PlayShoot(0.5f, 1.2f, 3, false);
        }

        /// <summary>P2 地刺：在玩家脚下放一个预警盘，SpikeTelegraph 自己等满 1.2s 再刺出。</summary>
        void SpawnSpikeTelegraph(GameRoot root)
        {
            BossSpikeCount++;

            Vector3 at = root.PlayerTransform.position;
            at.y = 0.02f;

            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "SpikeTelegraph";
            go.transform.position = at;
            go.transform.localScale = new Vector3(2.1f, 0.02f, 2.1f);
            Destroy(go.GetComponent<Collider>());

            var st = go.AddComponent<SpikeTelegraph>();
            st.Seconds = 1.2f;
            st.Damage = 18f * (root.Run != null ? root.Run.DmgScale : 1f);
        }

        void PickNewTarget()
        {
            float ang = Random.Range(0f, Mathf.PI * 2f);
            float rad = Random.Range(3f, 8f);
            _target = new Vector3(Mathf.Cos(ang) * rad, transform.position.y, Mathf.Sin(ang) * rad);
            _moveTimer = Random.Range(2.5f, 4.5f);
        }

        Renderer[] _renderers;
        Color[] _baseColors;
        int _lastTintState = -1;

        /// <summary>
        /// 整体染色：用 MaterialPropertyBlock，避免每帧 new Material 或破坏共享材质。
        /// 只在状态变化时写入，正常帧无开销。
        /// </summary>
        void ApplyTint(Color tint)
        {
            if (_renderers == null || _renderers.Length == 0)
                _renderers = GetComponentsInChildren<Renderer>();
            if (_renderers == null || _renderers.Length == 0) return;

            if (_baseColors == null || _baseColors.Length != _renderers.Length)
            {
                _baseColors = new Color[_renderers.Length];
                for (int i = 0; i < _renderers.Length; i++) _baseColors[i] = Color.white;
            }

            int state = tint == Color.white ? 0
                : tint.g > 0.8f ? 1
                : 2;
            if (state == _lastTintState) return;
            _lastTintState = state;

            var mpb = new MaterialPropertyBlock();
            for (int i = 0; i < _renderers.Length; i++)
            {
                var r = _renderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", _baseColors[i] * tint);
                r.SetPropertyBlock(mpb);
            }
        }

        void Handle(BossEvent e)
        {
            var root = GameRoot.Instance;
            switch (e.Kind)
            {
                case BossEventKind.PhaseChange:
                    SfxBank.Instance?.PlayBossPhase();
                    root?.BossToast($"铸造核心 · 阶段 {(int)e.Value}");
                    break;
                case BossEventKind.ShieldOn:
                    SfxBank.Instance?.PlayShield();
                    root?.BossToast("护盾开启 · 绕后或打碎能量柱");
                    break;
                case BossEventKind.ShieldOff:
                    break;
                case BossEventKind.SummonAdds:
                    root?.BossSummonAdds(e.Value);
                    break;
                case BossEventKind.Shockwave:
                    if (_shockCd <= 0f)
                    {
                        _shockCd = 1.5f;
                        root?.BossShockwave(transform.position);
                    }
                    break;
            }
        }

        /// <summary>被子弹命中。返回实际伤害（0 表示被护盾挡下）。</summary>
        public float OnHit(ProjectileDesc desc, Vector3 point, float amount)
        {
            if (State == null || !State.Alive) return 0f;

            bool ignoreArmor = desc != null && desc.Source != null && desc.Source.Pierce > 0;

            // 护盾：正面挡下（破防期无效）
            if (State.ShieldUp && !State.IsBreached)
            {
                var toSrc = point - transform.position;
                toSrc.y = 0f;
                bool front = Vector3.Dot(transform.forward, -toSrc.normalized) > 0f;
                if (front && !ignoreArmor)
                {
                    GameRoot.Instance?.SpawnHitFx(point);
                    return 0f;
                }
            }

            float dealt = State.TakeDamage(amount, false);
            GameRoot.Instance?.SpawnDamageNumber(point, dealt);

            if (!State.Alive)
                GameRoot.Instance?.OnBossKilled();
            return dealt;
        }
    }

    /// <summary>Boss 受击判定转发。</summary>
    public sealed class BossHitbox : MonoBehaviour
    {
        public BossView Owner;
    }
}
