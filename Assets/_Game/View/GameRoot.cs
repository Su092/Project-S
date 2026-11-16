using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using S.Core;

namespace S.View
{
    /// <summary>
    /// 局内总控：场景构建、刷怪、经验掉落、威胁推进、UI 与暂停。
    /// 只做「编排」——时间轴、刷怪规格与 Boss 规则都在 Core（RunDirector / BossState），可 headless 断言。
    /// </summary>
    public sealed class GameRoot : MonoBehaviour
    {
        public static GameRoot Instance { get; private set; }

        [Header("引用")]
        public PlayerController Player;
        public PlayerWeapons Weapons;
        public Camera Cam;

        public SData Data { get; private set; }
        public RunState Run { get; private set; }

        public Transform PlayerTransform => Player != null ? Player.transform : null;
        public Vector3 PlayerAimPoint => Cam != null
            ? Cam.transform.position + Cam.transform.forward * 30f
            : (PlayerTransform != null ? PlayerTransform.position + Vector3.up * 1.6f : Vector3.zero);

        // ---- 玩家状态 ----
        public float Hp { get; private set; }
        public float MaxHp { get; private set; }
        float _iFrameLeft;

        // ---- 关卡 ----
        /// <summary>单张竞技场的地形构建器。</summary>
        public ArenaBuilder Zones { get; private set; }

        /// <summary>节奏总导演：阶段、刷怪规格、精英与 Boss 时间轴。</summary>
        public RunDirector Director { get; private set; }

        public BossView Boss { get; private set; }

        /// <summary>存活秒数（暂停不推进）。HUD 的倒计时与结算都用它。</summary>
        public float RunElapsed => Director != null ? Director.Elapsed : 0f;

        public int AliveEnemies { get; private set; }
        readonly List<Enemy> _enemies = new List<Enemy>();

        readonly List<ExperiencePickup> _experiencePickups = new List<ExperiencePickup>();
        public IReadOnlyList<ExperiencePickup> ExperiencePickups => _experiencePickups;

        float _spawnTimer;
        bool _bossFightActive;

        /// <summary>已刷出的精英数（自检与遥测用）。</summary>
        public int ElitesSpawned => Director != null ? Director.ElitesSpawned : 0;

        // ---- UI ----
        HudView _hud;
        InventoryView _inv;
        MenuView _menu;
        MusicDirector _music;
        RunTelemetry _telemetry;

        public bool InventoryOpen => _inv != null && _inv.Open;
        public bool Paused { get; private set; }

        /// <summary>主菜单（正常启动时打开；-scheck 等测试模式下不创建）。</summary>
        public MenuView Menu => _menu;

        /// <summary>局内遥测（正常模式自动开启；-scheck 下由自检自行决定）。</summary>
        public RunTelemetry Telemetry => _telemetry;

        /// <summary>
        /// 启动时是否弹主菜单。测试模式（-scheck / -smoke / -autoplay）会把它关掉，
        /// 否则一进游戏就停在 timeScale=0，后续断言全跑不起来（踩过）。
        /// </summary>
        public bool ShowMenuOnStart = true;

        /// <summary>由 Bootstrap 告知「本次是自检启动」，用于决定是否自动开启遥测。</summary>
        public bool SelfCheckRunning;

        /// <summary>三选一队列</summary>
        readonly Queue<List<Offer>> _pendingOffers = new Queue<List<Offer>>();

        /// <summary>
        /// 自检用：为 true 时三选一不弹窗（配件仍进背包）。
        /// 否则战斗段触发升级会把游戏暂停，后续断言全在 timeScale=0 下跑。
        /// </summary>
        public bool AutoAcceptOffers { get; set; }

        /// <summary>自检用：强制关掉背包/三选一弹层并恢复时间。</summary>
        public void CloseOverlayForTest()
        {
            _inv?.DismissForTest();
            Paused = false;
            Time.timeScale = 1f;
            if (Player != null) Player.InputLocked = false;
        }

        /// <summary>自检用：测试期间免伤（区域/Boss 逻辑的断言不该被玩家猝死打断）。</summary>
        public bool InvulnerableForTest { get; set; }

        public IReadOnlyList<Enemy> Enemies => _enemies;

        void Awake()
        {
            Instance = this;
            Data = SDataLoader.Get();
        }

        void Start()
        {
            if (Data == null) { enabled = false; return; }

            MaxHp = Data.PlayerHp;
            Hp = MaxHp;
            Run = new RunState(Data, (uint)System.Environment.TickCount);

            BuildWorld();

            Director = Data.Director ?? new RunDirector(null, null);

            Zones = gameObject.AddComponent<ArenaBuilder>();
            Zones.Build(Data.Arena);

            // 把玩家放到场地出生点
            if (Player != null)
                Player.transform.position = Zones.PlayerSpawn;

            _hud = gameObject.AddComponent<HudView>();
            _hud.Init(this);
            _inv = gameObject.AddComponent<InventoryView>();
            _inv.Init(this);

            _music = gameObject.AddComponent<MusicDirector>();
            _music.Init();

            _telemetry = gameObject.AddComponent<RunTelemetry>();
            _telemetry.Init(this);
            // 自检期间不自动开遥测：断言会频繁暂停/快进，采到的节奏数据没有意义。
            if (!SelfCheckRunning) _telemetry.Enable();

            // 主菜单：正常启动时先停在菜单，点「开始」才进 1 区。
            // 测试模式把 ShowMenuOnStart 关掉，直接开跑。
            if (ShowMenuOnStart)
            {
                SetPaused(true);
                _menu = gameObject.AddComponent<MenuView>();
                _menu.Init(MetaSave.Load(), StartRun);
                _menu.Open();
                // Init 内部会 Close() 一次，Open 里已同步光标；这里再兜一次底，
                // 确保「菜单开着 → 光标解锁」这个不变量在 Start 结束时一定成立。
                SyncCursorToState();
            }
            else
            {
                StartRun();
            }
        }

        bool _runStarted;
        void StartRun()
        {
            if (_runStarted) return;
            string id = _menu != null ? _menu.SelectedGunId : null;
            if (!Weapons.StartWith(string.IsNullOrEmpty(id) ? Data.GunOrder[0].Id : id)) return;
            _runStarted = true;
            SetPaused(false);
            _hud?.Toast("进入竞技场 · 撑住并摧毁铸造核心");
            NextSpawnTick(instant: true);
        }

        /// <summary>测试用：跳过菜单直接开局（自动驾驶 / 冒烟测试走这条）。</summary>
        public void StartRunForTest()
        {
            _menu?.Close();
            StartRun();
            _telemetry?.Enable();
        }

        void OnDestroy()
        {
            Time.timeScale = 1f;
            if (Instance == this) Instance = null;
        }

        // ---------------- 场景 ----------------

        void BuildWorld()
        {
            // 光照与环境（几何体由 ArenaBuilder 负责）
            if (FindLight() == null)
            {
                var lgo = new GameObject("Sun");
                var light = lgo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.15f;
                light.color = new Color(1f, 0.96f, 0.88f);
                lgo.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.28f, 0.31f, 0.38f);
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.12f, 0.14f, 0.18f);
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 40f;
            RenderSettings.fogEndDistance = 130f;
        }

        static Light FindLight()
        {
            var lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude);
            foreach (var l in lights) if (l.type == LightType.Directional) return l;
            return null;
        }

        // ---------------- 关卡流程（时间轴驱动，docs/gameplay-spec.md §2.1）----------------

        /// <summary>
        /// 每帧的节奏推进：计时 → 威胁同步 → 事件（阶段切换 / 精英 / Boss）→ 刷怪。
        ///
        /// 顺序不能颠倒：威胁必须先由计时同步，再让刷怪用 Run.HpScale；
        /// 否则本帧刷出的敌人会用上一帧的硬度，8 分钟处会看到一次「突然整体变硬」的跳变。
        /// </summary>
        void TickDirector(float dt)
        {
            if (Run == null || Director == null) return;

            var events = Director.Tick(dt);
            Run.SyncThreat(Director.Threat);

            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                switch (e.Kind)
                {
                    case RunDirector.EventKind.PhaseChanged:
                        NotifyPhase(Director.CurrentPhase);
                        break;
                    case RunDirector.EventKind.EliteDue:
                        SpawnElite(e.EliteIndex);
                        break;
                    case RunDirector.EventKind.BossDue:
                        SpawnBoss();
                        break;
                }
            }

            // Boss 出场后普通刷怪继续（降档），否则 Boss 战会变成空场单挑；
            // 但 Boss 死亡后就不再刷，避免通关结算被残余刷怪拖住。
            if (!_bossFightActive || (Boss != null && Boss.State != null && Boss.State.Alive))
                TickSpawn(dt);
        }

        void NotifyPhase(RunDirector.Phase ph)
        {
            if (ph == null) return;
            _hud?.Toast($"阶段 · {ph.Name}");
            _telemetry?.NotePhase(ph.Name, RunElapsed);
        }

        /// <summary>
        /// 按当前阶段的并发与间隔刷怪。玩家打量越大，场上越满，但永远不超过并发上限——
        /// 上限是「可读性」的保护：无上限会让画面变成一堵墙，玩家分不清威胁来自哪。
        /// </summary>
        void TickSpawn(float dt)
        {
            var phase = Director.CurrentPhase;
            if (phase == null || phase.Pool == null || phase.Pool.Length == 0) return;

            _spawnTimer -= dt;
            if (_spawnTimer > 0f) return;
            if (AliveEnemies >= phase.Concurrency) { _spawnTimer = 0.2f; return; }

            _spawnTimer = Mathf.Max(0.15f, phase.Interval);
            SpawnFromPool(phase.Pool, elite: false);
        }

        /// <summary>开局立刻刷一批：不让玩家空场站桩等第一只。</summary>
        void NextSpawnTick(bool instant)
        {
            _spawnTimer = instant ? 0f : _spawnTimer;
        }

        // ---------------- 更新 ----------------

        void Update()
        {
            if (Data == null || Run == null) return;

            if (HandlePauseInput()) return;
            if (Paused || InventoryOpen) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (_iFrameLeft > 0f) _iFrameLeft -= dt;

            TickDirector(dt);
            TickExperiencePickups(dt);
            if (Paused || InventoryOpen) return;

            // 音乐必须每帧驱动（audio-spec §5）：这一句此前全工程缺失，
            // 导致五个层的音量恒为 0 —— 写了音乐却永远听不到。
            _music?.UpdateMix(Run.Threat, _bossFightActive, InventoryOpen, dt);
        }

        // ---------------- Boss ----------------

        void SpawnBoss()
        {
            if (_bossFightActive || Boss != null) return;
            _bossFightActive = true;
            _hud?.Toast("铸造核心入场 · 破坏四根能量柱破防");

            // 柱子只在 Boss 出场时生成：单图没有「Boss 区」，
            // 柱子如果一直立在中央，普通阶段会被当成掩体，破坏后玩家会以为地图坏了。
            Zones?.SpawnPillars();

            var go = new GameObject("Boss");
            go.transform.position = ArenaBuilder.Center + Vector3.up * 0.1f;
            var boss = go.AddComponent<BossView>();
            boss.Init(Data.BossHp, Data.Boss);
            Boss = boss;

            _telemetry?.NoteBossSpawn(RunElapsed);
        }

        public void BossToast(string msg) => _hud?.Toast(msg);

        public void BossSummonAdds(int count)
        {
            // 召唤哪种敌人来自 Boss.tsv 的 summonEnemy（原先是写死的 "Crawler"）
            string id = Data != null && Data.Boss != null && !string.IsNullOrEmpty(Data.Boss.SummonEnemy)
                ? Data.Boss.SummonEnemy
                : "Crawler";
            for (int i = 0; i < count; i++) SpawnFromPool(new[] { id }, elite: false);
            var def = Data != null ? Data.Enemy(id) : null;
            _hud?.Toast($"召唤 {count} 只{(def != null ? def.Name : id)}");
        }
        public void BossShockwave(Vector3 center)
        {
            var vis = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            vis.name = "Shockwave";
            vis.transform.position = center + Vector3.up * 0.15f;
            vis.transform.localScale = new Vector3(18f, 0.1f, 18f);
            SAssets.RemoveColliders(vis);
            SMat.Apply(vis.GetComponent<Renderer>(), SMat.Lit(new Color(1f, 0.35f, 0.55f), 0.2f));
            Object.Destroy(vis, 0.4f);

            // 判定：未跳起则受伤
            if (Player != null && Player.IsAirborne)
            {
                _hud?.Toast("躲过冲击波");
                return;
            }
            DamagePlayer(Data.Boss.ShockwaveDamage * Run.DmgScale);
        }

        public void OnBossKilled()
        {
            _bossFightActive = false;
            // Boss 奖励：3 次三选一
            for (int i = 0; i < 3; i++)
            {
                var offers = Run.Roll(3, Weapons.OwnedGunIds);
                if (offers.Count > 0) _pendingOffers.Enqueue(offers);
            }
            _hud?.Toast("铸造核心已摧毁");
            Win();
        }

        /// <summary>打能量柱（由子弹命中 Pillar 时调用）。</summary>
        public void OnPillarHit(Pillar p, float damage)
        {
            if (p == null || Boss == null) return;
            bool broke = p.TakeDamage(damage);
            if (!broke) return;

            // 规则层：碎柱 → 关盾 + 破防
            Boss.State.DamagePillar(p.Index, Boss.State.PillarHp);
            _hud?.Toast($"能量柱 {p.Index + 1} 已破坏 · 破防 {Boss.State.PillarBreachTime:0}s");
        }

        /// <summary>
        /// 从敌人池里刷一只。位置由 ArenaBuilder 的环形刷怪点给出，
        /// 并强制「不刷在玩家脸上」——这是单图无限刷怪下最容易毁掉手感的一条。
        /// </summary>
        void SpawnFromPool(string[] pool, bool elite)
        {
            if (pool == null || pool.Length == 0) return;

            var def = PickEnemyDef(pool);
            if (def == null) return;

            Vector3 pos = Zones != null && Player != null
                ? Zones.PickSpawnPoint(Player.transform.position, 14f, null)
                : new Vector3(0f, 0.9f, ArenaBuilder.Center.z + 40f);

            SpawnAt(def, pos, elite);
        }

        /// <summary>
        /// 从池子里随机取一个**能在 Enemy.tsv 里查到**的定义。
        ///
        /// 原先这里是 `Enum.TryParse` 失败就静默退化成 Crawler ——
        /// 数据里拼错名字看起来完全正常，只是永远只出一种敌人。
        /// 现在拼错会被 SCfgValidator 在加载期拦下；这里再兜一层，
        /// 是为了「校验被绕过时也不崩」。
        /// </summary>
        EnemyDef PickEnemyDef(string[] pool)
        {
            for (int attempt = 0; attempt < pool.Length; attempt++)
            {
                string id = pool[Random.Range(0, pool.Length)];
                var def = Data.Enemy(id);
                if (def != null) return def;
            }

            // 池子里一个都查不到：退到表里的第一种，至少不空场
            if (Data.EnemyOrder.Count > 0) return Data.EnemyOrder[0];
            return null;
        }

        /// <summary>刷一只精英：继承本体行为，但更硬、更慢、更值钱，击杀回血。</summary>
        void SpawnElite(int index)
        {
            var phase = Director != null ? Director.CurrentPhase : null;
            var pool = phase != null && phase.Pool != null && phase.Pool.Length > 0
                ? phase.Pool
                : null;

            var def = PickEnemyDef(pool ?? new[] { Data.EnemyOrder.Count > 0 ? Data.EnemyOrder[0].Id : "" });
            if (def == null) return;

            Vector3 pos = Zones != null && Player != null
                ? Zones.PickSpawnPoint(Player.transform.position, 18f, null)
                : Vector3.forward * 30f;

            SpawnAt(def, pos, elite: true);
            _hud?.Toast($"精英 #{index + 1} 入场 · 击杀回复 {Director.Elite.HealOnKill:0} 生命");
            _telemetry?.NoteElite(RunElapsed, def.Id);
        }

        /// <summary>真正造出敌人的唯一入口（普通与精英共用）。数值全部来自 EnemyDef。</summary>
        Enemy SpawnAt(EnemyDef def, Vector3 pos, bool elite)
        {
            if (def == null) return null;

            var go = new GameObject(elite ? "Elite_" + def.Id : "Enemy_" + def.Id);
            go.transform.position = pos;

            var cc = go.AddComponent<CharacterController>();
            // 碰撞体跟着模型高度走（原先写死 1.7 / 0.42，矮敌人会有看不见的碰撞壳、
            // 高敌人则打不到上半身）。
            float h = Mathf.Max(0.3f, def.Height);
            cc.height = h;
            cc.radius = Mathf.Clamp(h * 0.25f, 0.15f, 0.7f);
            cc.center = new Vector3(0f, h * 0.5f, 0f);

            // 视觉：真实模型（缺件时 SAssets 自动回退为胶囊占位）
            var visual = SAssets.SpawnFit(def.Model, go.transform, Vector3.zero,
                def.Height, Color.white, 0.45f);
            visual.name = "Body";

            var e = go.AddComponent<Enemy>();
            var el = Director != null ? Director.Elite : new RunDirector.EliteDef();

            float hpMul = 1f, dmgMul = 1f, speedMul = 1f;
            int xpMul = 1;
            if (elite)
            {
                hpMul = el.HpMul; dmgMul = el.DmgMul; speedMul = el.SpeedMul; xpMul = el.XpMul;
            }

            // 缩放系数在 Enemy.Configure 里统一乘：威胁缩放与精英倍率
            e.Configure(def, hpMul, dmgMul, speedMul, xpMul, Run.HpScale, Run.DmgScale);

            e.IsElite = elite;
            e.HealOnKill = elite ? el.HealOnKill : 0f;
            if (elite) e.ApplyEliteScale(el.Scale);

            _enemies.Add(e);
            AliveEnemies++;
            _telemetry?.NoteSpawn(e);
            return e;
        }

        /// <summary>炸弹人自爆：范围伤害 + 视觉。</summary>
        public void SpawnBomberBlast(Vector3 at, float dmg)
        {
            CombatFx.Explosion(at, 4.5f, new Color(1f, 0.4f, 0.1f));

            SfxBank.Instance?.PlayExplode();

            if (Player == null) return;
            if (Vector3.Distance(Player.transform.position, at) < 4.5f) DamagePlayer(dmg);
        }

        // ---------------- 事件 ----------------

        public void OnEnemyKilled(Enemy e, int xp)
        {
            _enemies.Remove(e);
            AliveEnemies = Mathf.Max(0, AliveEnemies - 1);
            Run.Kills++;

            // 精英回血：单图无限刷怪下没有「清区补给」，这是中期唯一的续航来源。
            // 放在击杀结算最前面：玩家看到的是「打死它 → 血条立刻回来」，
            // 而不是等升级动画播完才回，反馈才对得上因果。
            if (e != null && e.HealOnKill > 0f)
            {
                Hp = Mathf.Min(MaxHp, Hp + e.HealOnKill);
                _hud?.Toast($"精英击杀 · 回复 {e.HealOnKill:0} 生命");
            }

            _telemetry?.NoteKill(e);

            // 击杀只产生掉落；经验与升级必须等玩家走近拾取后才结算。
            if (e != null) SpawnExperience(e.transform.position, xp);
        }

        /// <summary>一只敌人掉一个晶体，完整保留普通/精英的配置经验值。</summary>
        void SpawnExperience(Vector3 at, int amount)
        {
            if (amount <= 0) return;

            // 在死亡位置的正下方找地面，排除玩家/敌人胶囊，兼容高台与悬空敌人。
            var hits = Physics.RaycastAll(at + Vector3.up * 0.25f, Vector3.down, 100f,
                ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            foreach (var hit in hits)
            {
                if (hit.normal.y < 0.5f || hit.distance >= nearest) continue;
                if (hit.collider.GetComponentInParent<Enemy>() != null ||
                    hit.collider.GetComponentInParent<PlayerController>() != null) continue;
                nearest = hit.distance;
                at.y = hit.point.y;
            }

            var go = new GameObject("ExperiencePickup");
            go.transform.SetParent(transform, true);
            go.transform.position = at;
            var pickup = go.AddComponent<ExperiencePickup>();
            pickup.Init(this, amount);
            _experiencePickups.Add(pickup);
        }

        void TickExperiencePickups(float dt)
        {
            for (int i = _experiencePickups.Count - 1; i >= 0; i--)
            {
                var pickup = _experiencePickups[i];
                if (pickup == null) { _experiencePickups.RemoveAt(i); continue; }
                pickup.TickVisual(dt);
                TryCollectExperience(pickup);
                // 本次拾取触发三选一后立即停止，不能在暂停状态继续吃掉其他经验。
                if (Paused || InventoryOpen) break;
            }
        }

        /// <summary>唯一拾取入口：检查距离/暂停/归属，先移除再结算，避免同帧重复领取。</summary>
        public bool TryCollectExperience(ExperiencePickup pickup)
        {
            if (Run == null || PlayerTransform == null || Hp <= 0f || Paused || InventoryOpen ||
                Time.timeScale <= 0f || pickup == null || pickup.Owner != this ||
                !pickup.gameObject.activeInHierarchy) return false;
            if ((PlayerTransform.position - pickup.transform.position).sqrMagnitude >
                ExperiencePickup.PickupRadius * ExperiencePickup.PickupRadius) return false;
            if (!_experiencePickups.Remove(pickup)) return false;

            int amount = pickup.Amount;
            pickup.gameObject.SetActive(false);
            Destroy(pickup.gameObject);
            GrantExperience(amount);
            return true;
        }

        public bool TryPickOffer(Offer offer)
        {
            if (!offer.IsGun)
            {
                bool picked = Run.TryPick(offer.Id);
                if (!picked) HudToast("背包已满 · 按 Tab 整理配件后返回选择");
                return picked;
            }
            if (!Weapons.AcquireGun(offer.Id, Run, out string reason))
            { HudToast(reason); return false; }
            HudToast("获得枪械：" + Weapons.Active.Base.Name);
            return true;
        }

        void GrantExperience(int amount)
        {
            int ups = Run.AddXp(amount);
            for (int i = 0; i < ups; i++)
            {
                var offers = Run.Roll(3, Weapons.OwnedGunIds);
                if (offers.Count > 0)
                {
                    if (AutoAcceptOffers)
                    {
                        // 自检模式：直接进背包，不弹窗、不暂停
                        for (int k = 0; k < offers.Count && k < 1; k++)
                            TryPickOffer(offers[0]);
                    }
                    else
                    {
                        _pendingOffers.Enqueue(offers);
                    }
                }
            }
            if (ups > 0) SfxBank.Instance?.PlayLevelUp();
            if (!AutoAcceptOffers && _pendingOffers.Count > 0 && !InventoryOpen)
                _inv.ShowOffers(_pendingOffers.Dequeue());
        }

        public void DamagePlayer(float amount)
        {
            if (InvulnerableForTest) return;
            if (_iFrameLeft > 0f || amount <= 0f) return;
            _iFrameLeft = Data.PlayerIFrames;
            Hp -= amount;
            _hud?.FlashDamage();
            SfxBank.Instance?.PlayHurt();
            if (Hp <= 0f)
            {
                Hp = 0f;
                Lose();
            }
        }

        public void Win()
        {
            SetPaused(true);
            _music?.UpdateMix(Run.Threat, true, false, 0.2f);

            // 局外成长的唯一来源（gameplay-spec §8）：只有通关给 shard。
            var meta = MetaSave.Load();
            meta.FinishRun(true, 0f);
            MetaSave.Save(meta);

            _telemetry?.Finish(RunElapsed, true);
            _hud?.ShowEnd(true);
        }

        public void Lose()
        {
            SetPaused(true);
            var meta = MetaSave.Load();
            meta.FinishRun(false, 0f);
            MetaSave.Save(meta);

            _telemetry?.Finish(RunElapsed, false);
            _hud?.ShowEnd(false);
        }

        // ---------------- 弹丸回调 ----------------

        public void SpawnHitFx(Vector3 p, bool onEnemy = true)
        {
            CombatFx.Impact(p, onEnemy ? new Color(1f, 0.75f, 0.3f) : new Color(0.5f, 0.8f, 1f), Vector3.up);

            SfxBank.Instance?.PlayHit(onEnemy);
        }

        public void SpawnDamageNumber(Vector3 p, float amount)
        {
            _hud?.DamageNumber(p, amount);
        }

        public void HudToast(string msg) => _hud?.Toast(msg);

        public void OnProjectileHit(Projectile p, Vector3 point) { }

        /// <summary>光束/连锁的视觉线（只表现，不结算）。</summary>
        public void SpawnBeamFx(Vector3 from, Vector3 to, Color color)
        {
            CombatFx.Beam(from, to, color);
        }

        /// <summary>
        /// 触发弹的载荷：在命中点施放下一组投射物。
        /// 载荷走的是与主弹丸完全相同的生成路径，所以它自己也能带触发（递归由 Core 限制代数）。
        /// </summary>
        public void SpawnPayload(Vector3 at, Vector3 normal, System.Collections.Generic.List<ProjectileDesc> payload)
        {
            if (payload == null || payload.Count == 0) return;

            Vector3 up = normal.sqrMagnitude > 0.01f ? normal : Vector3.up;
            Vector3 dir = Vector3.Slerp(up, Vector3.up, 0.5f).normalized;

            for (int i = 0; i < payload.Count; i++)
            {
                var d = payload[i];
                if (d == null) continue;

                int n = Mathf.Clamp(d.Count, 1, Loadout.MaxCount);
                for (int k = 0; k < n; k++)
                {
                    float yaw = Random.Range(-0.5f, 0.5f) * d.Spread;
                    float pitch = Random.Range(-0.5f, 0.5f) * d.Spread;
                    Quaternion rot = Quaternion.LookRotation(dir) * Quaternion.Euler(pitch, 0f, yaw);

                    var go = new GameObject("Payload");
                    go.transform.position = at + up * 0.1f;
                    var p = go.AddComponent<Projectile>();
                    p.Init(d, rot * Vector3.forward);
                }
            }
        }

        /// <summary>爆炸：半径内线性衰减（gameplay-spec §5.2 的 0.45 边缘系数）。</summary>
        public void Explode(Vector3 at, ProjectileDesc desc)
        {
            float r = desc.ExplodeR;
            if (r <= 0f) return;

            CombatFx.Explosion(at, r,
                PlayerWeapons.ParseColor(desc.Source != null ? desc.Source.Color : "#ff9638"),
                desc.Kind == BulletKind.Hole);

            SfxBank.Instance?.PlayExplode();

            var hits = Physics.OverlapSphere(at, r, ~0, QueryTriggerInteraction.Ignore);
            var seen = new HashSet<Enemy>();
            foreach (var c in hits)
            {
                var e = c.GetComponentInParent<Enemy>();
                if (e == null || !e.Alive) continue;
                if (!seen.Add(e)) continue;
                float d = Vector3.Distance(at, e.transform.position);
                float t = Mathf.Clamp01(d / r);
                // 爆炸伤害独立于直接命中伤害：榴弹的 10 点直伤 + 38 点爆伤是两笔
                float dmg = desc.ExplodeDmg * Mathf.Lerp(1f, 0.45f, t);
                e.TakeDamage(desc, at, dmg, true);
            }
        }

        public void EnemyShot(Vector3 from, Vector3 to, float dmg)
        {
            var go = new GameObject("EnemyShot");
            go.transform.position = from;
            var eb = go.AddComponent<EnemyBullet>();
            eb.Init((to - from).normalized, dmg);
        }

        public Enemy FindNearestEnemy(Vector3 from, float maxDist)
        {
            Enemy best = null;
            float bestD = maxDist * maxDist;
            for (int i = _enemies.Count - 1; i >= 0; i--)
            {
                var e = _enemies[i];
                if (e == null || !e.Alive) { _enemies.RemoveAt(i); continue; }
                float d = (e.transform.position - from).sqrMagnitude;
                if (d < bestD) { bestD = d; best = e; }
            }
            return best;
        }

        /// <summary>连锁闪电用：排除已经打过的目标，避免在两个敌人之间来回跳。</summary>
        public Enemy FindNearestEnemyExcept(Vector3 from, float maxDist, HashSet<Enemy> exclude)
        {
            Enemy best = null;
            float bestD = maxDist * maxDist;
            for (int i = _enemies.Count - 1; i >= 0; i--)
            {
                var e = _enemies[i];
                if (e == null || !e.Alive) continue;
                if (exclude != null && exclude.Contains(e)) continue;
                float d = (e.transform.position - from).sqrMagnitude;
                if (d < bestD) { bestD = d; best = e; }
            }
            return best;
        }

        // ---------------- 输入/暂停 ----------------

        bool HandlePauseInput()
        {
            var kb = Keyboard.current;
            if (kb == null) return false;

            if (kb.tabKey.wasPressedThisFrame)
            {
                // 开背包会 SetPaused(true)：若仍要求 !Paused 才能 Toggle，
                // Tab 就只能开不能关（用户只能靠 Esc）。背包开着时 Tab = 关闭。
                if (InventoryOpen) _inv.Close();
                else if (!Paused) _inv.OpenBag();
                return true;
            }
            if (kb.escapeKey.wasPressedThisFrame)
            {
                if (InventoryOpen) { _inv.Close(); return true; }
                SetPaused(!Paused);
                return true;
            }
            return false;
        }

        public void ToggleInventory()
        {
            if (InventoryOpen) _inv.Close();
            else _inv.OpenBag();
        }

        public void SetPaused(bool v)
        {
            Paused = v;
            Time.timeScale = v ? 0f : 1f;
            SyncCursorToState();
            if (!v && _pendingOffers.Count > 0 && !InventoryOpen)
                _inv.ShowOffers(_pendingOffers.Dequeue());
        }

        /// <summary>
        /// 光标的**唯一**裁决点。
        ///
        /// 为什么必须集中：光标状态原先散落在 SetPaused / MenuView.Open / InventoryView.OpenBag /
        /// PlayerController.Start 四处各自写，谁最后执行谁生效。Start 之间的顺序由 Unity 决定，
        /// 于是「菜单开着却被锁光标」这种竞态必然出现 —— 表现就是打开游戏后鼠标动不了。
        ///
        /// 规则只有一条：**只要有 UI 面板在等玩家点击，就必须解锁光标**；
        /// 全部关掉之后才锁回去。任何新面板都应该被纳入这里的判断，而不是自己调 LockCursor。
        /// </summary>
        public void SyncCursorToState()
        {
            bool uiNeedsMouse = Paused
                || InventoryOpen
                || (_menu != null && _menu.IsOpen)
                || (_inv != null && _inv.IsOfferVisible);

            PlayerController.LockCursor(!uiNeedsMouse);
        }

        /// <summary>背包关闭后，若还有排队的三选一则继续弹。</summary>
        public void OnInventoryClosed()
        {
            if (_pendingOffers.Count > 0) _inv.ShowOffers(_pendingOffers.Dequeue());
            else SetPaused(false);
        }

        public void PopOfferIfAny()
        {
            if (_pendingOffers.Count > 0) _inv.ShowOffers(_pendingOffers.Dequeue());
        }

        public bool HasPendingOffers => _pendingOffers.Count > 0;
    }
}
