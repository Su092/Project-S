using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using S.Core;

namespace S.View
{
    /// <summary>
    /// 局内总控：场景构建、刷怪、经验掉落、威胁推进、UI 与暂停。
    /// 只做「编排」——区域推进与 Boss 规则都在 Core（ZoneProgress / BossState），可 headless 断言。
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
        public ZoneProgress Progress { get; private set; }
        public ZoneBuilder Zones { get; private set; }
        public BossView Boss { get; private set; }

        public int Zone => Progress != null ? Progress.Current : 1;
        public int Wave => Progress != null ? Progress.Wave : 0;
        public int ZoneCount => Progress != null ? Progress.ZoneCount : 0;
        public int WaveCount
        {
            get
            {
                var d = Progress?.CurrentDef;
                return d != null ? d.Waves : 0;
            }
        }

        public int AliveEnemies { get; private set; }
        readonly List<Enemy> _enemies = new List<Enemy>();

        float _spawnTimer;
        int _spawnRemaining;
        float _waveBreak;
        bool _waveInProgress;
        bool _bossFightActive;

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
            _inv?.Close();
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

            Progress = new ZoneProgress(Data.Zones);
            Zones = gameObject.AddComponent<ZoneBuilder>();
            Zones.Build(Data.Zones);

            // 把玩家放到 1 区入口
            if (Player != null)
                Player.transform.position = ZoneBuilder.EntryPoint(1);

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

        void StartRun()
        {
            SetPaused(false);
            BeginZone(1);
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
            // 光照与环境（几何体由 ZoneBuilder 负责）
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

        // ---------------- 关卡流程 ----------------

        public void BeginZone(int zone)
        {
            var code = Progress.TryEnter(zone);
            if (code != null) return;

            // 威胁有下限（跨区累积，不清零）
            if (Run.Threat < Progress.ThreatFloor) Run.AddThreat(Progress.ThreatFloor - Run.Threat);

            Zones.ApplyGateState(Progress);

            // 把玩家送到本区入口
            if (Player != null)
                Player.transform.position = ZoneBuilder.EntryPoint(zone);

            // Boss 区
            if (Progress.CurrentDef != null && Progress.CurrentDef.IsBoss)
            {
                SpawnBoss();
                return;
            }

            _hud?.Toast($"区域 {zone}/{ZoneCount} · {Progress.CurrentDef?.Name}");
            NextWave();
        }

        void NextWave()
        {
            if (!Progress.BeginWave())
            {
                // 波次已打满，等场上清空再结算本区
                _waveInProgress = false;
                return;
            }

            int wave = Progress.Wave;
            int defWaves = Progress.CurrentDef.Waves;

            // 每波敌人数量：随波次与区域递增
            _spawnRemaining = 3 + wave + (Progress.Current - 1);
            _spawnTimer = 0f;
            _waveInProgress = true;

            string elite = "";
            if (Run.EliteWaveUnlocked && wave == defWaves) elite = "  ·  精英";
            _hud?.Toast($"区域 {Progress.Current} · 波次 {wave}/{defWaves}{elite}");
        }

        void CompleteZone()
        {
            if (!Progress.TryClearCurrent()) return;

            Run.AddThreat(-25f);
            Hp = Mathf.Min(MaxHp, Hp + Data.PlayerHealPickup);

            Zones.ApplyGateState(Progress);

            _hud?.Toast($"区域 {Progress.Current} 已清空 · 补给 +{Data.PlayerHealPickup:0} · 门已开");
            _waveBreak = 3.0f;
        }

        void Update()
        {
            if (Data == null || Run == null) return;

            // 进区检测放在暂停判断之前：只要门开着且玩家位置越过了门就推进。
            // 这样即使暂停/背包态把后续逻辑短路，进区也不会被漏掉。
            if (Progress != null && Progress.CurrentCleared && !_bossFightActive)
                CheckZoneProgression();

            if (HandlePauseInput()) return;
            if (Paused || InventoryOpen) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Run.ThreatTick(dt);
            if (_iFrameLeft > 0f) _iFrameLeft -= dt;

            // 清区后：给玩家时间吃经验/改枪，然后自动推进（不强制）
            if (Progress.CurrentCleared && !_bossFightActive)
            {
                if (_waveBreak > 0f) _waveBreak -= dt;
                return;
            }

            if (_bossFightActive) return;   // Boss 由 BossView 自行推进

            // 刷怪
            if (_spawnRemaining > 0)
            {
                _spawnTimer -= dt;
                if (_spawnTimer <= 0f && AliveEnemies < 16)
                {
                    _spawnTimer = Mathf.Max(0.25f, Random.Range(0.45f, 0.95f) - Progress.Current * 0.04f);
                    SpawnEnemy();
                    _spawnRemaining--;
                }
                return;
            }

            // 本波刷完且清空 → 下一波 或 清区
            if (_waveInProgress && AliveEnemies == 0)
            {
                _waveInProgress = false;

                bool hasMore = Progress.Wave < Progress.CurrentDef.Waves;
                if (hasMore)
                {
                    NextWave();
                }
                else
                {
                    CompleteZone();
                }
            }
        }

        /// <summary>
        /// 清区后，玩家走进门洞（本区后墙处）就进入下一区。
        /// 不用按键、不用自动传送——「走过去」让空间推进可感知。
        /// </summary>
        void CheckZoneProgression()
        {
            if (Player == null) return;
            if (!Progress.IsDoorOpen(Progress.Current)) return;

            float gateZ = ZoneBuilder.CenterOf(Progress.Current).z + ZoneBuilder.ZoneLength * 0.5f;
            float dz = Player.transform.position.z - gateZ;

            // 已越过门（往 +Z 方向）
            if (dz > 0.6f)
            {
                int next = Progress.NextZone;
                if (next > 0)
                {
                    BeginZone(next);
                }
            }
        }

        /// <summary>自检用：进区判定的中间量，便于定位「为什么没进区」。</summary>
        public string ZoneProgressionDebug
        {
            get
            {
                if (Progress == null || Player == null) return "no progress/player";
                float gateZ = ZoneBuilder.CenterOf(Progress.Current).z + ZoneBuilder.ZoneLength * 0.5f;
                return $"cur={Progress.Current} cleared={Progress.CurrentCleared} doorOpen={Progress.IsDoorOpen(Progress.Current)} " +
                       $"playerZ={Player.transform.position.z:0.00} gateZ={gateZ:0.00} boss={_bossFightActive}";
            }
        }

        // ---------------- Boss ----------------

        void SpawnBoss()
        {
            _bossFightActive = true;
            _hud?.Toast("铸造核心 · 破坏四根能量柱破防");

            var go = new GameObject("Boss");
            go.transform.position = ZoneBuilder.CenterOf(Progress.Current) + Vector3.up * 0.1f;
            var boss = go.AddComponent<BossView>();
            boss.Init(Data.BossHp);
            Boss = boss;
        }

        public void BossToast(string msg) => _hud?.Toast(msg);

        public void BossSummonAdds(int count)
        {
            for (int i = 0; i < count; i++) SpawnEnemy();
            _hud?.Toast($"召唤 {count} 只爬行者");
        }

        public void BossShockwave(Vector3 center)
        {
            var vis = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            vis.name = "Shockwave";
            vis.transform.position = center + Vector3.up * 0.15f;
            vis.transform.localScale = new Vector3(18f, 0.1f, 18f);
            Object.Destroy(vis.GetComponent<Collider>());
            SMat.Apply(vis.GetComponent<Renderer>(), SMat.Lit(new Color(1f, 0.35f, 0.55f), 0.2f));
            Object.Destroy(vis, 0.4f);

            // 判定：未跳起则受伤
            if (Player != null && Player.IsAirborne)
            {
                _hud?.Toast("躲过冲击波");
                return;
            }
            DamagePlayer(22f * Run.DmgScale);
        }

        public void OnBossKilled()
        {
            _bossFightActive = false;
            // Boss 奖励：3 次三选一
            for (int i = 0; i < 3; i++)
            {
                var offers = Run.Roll(3);
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
            Boss.State.DamagePillar(p.Index, BossState.PillarHp);
            _hud?.Toast($"能量柱 {p.Index + 1} 已破坏 · 破防 {BossState.PillarBreachTime:0}s");
        }

        void SpawnEnemy()
        {
            var def = Progress.CurrentDef;
            if (def == null || def.Pool == null || def.Pool.Length == 0) return;

            string kindName = def.Pool[Random.Range(0, def.Pool.Length)];
            var kind = (Enemy.Kind)System.Enum.Parse(typeof(Enemy.Kind), kindName);

            // 从本区外缘刷入（不给「凭空出现」的观感）
            int slots = 6;
            Vector3 pos = Zones != null
                ? Zones.SpawnPoint(Progress.Current, Random.Range(0, slots), slots)
                : ZoneBuilder.CenterOf(Progress.Current) + Vector3.forward * 18f;

            // 别刷在玩家脸上
            if (Player != null)
            {
                var toPlayer = Player.transform.position - pos;
                toPlayer.y = 0f;
                if (toPlayer.magnitude < 9f)
                    pos += toPlayer.normalized * (9f - toPlayer.magnitude);
            }

            var go = new GameObject("Enemy_" + kindName);
            go.transform.position = pos;

            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.7f;
            cc.radius = 0.42f;
            cc.center = new Vector3(0f, 0.85f, 0f);

            // 视觉：真实模型（缺件时 SAssets 自动回退为胶囊占位）
            string modelName = KindModel(kind);
            float targetH = KindHeight(kind);
            var visual = SAssets.SpawnFit(modelName, go.transform, Vector3.zero,
                targetH, Color.white, 0.45f);
            visual.name = "Body";

            var e = go.AddComponent<Enemy>();
            float hp = KindHp(kind) * Run.HpScale;
            e.Configure(kind, hp, KindDmg(kind) * Run.DmgScale, KindSpeed(kind), KindXp(kind));
            _enemies.Add(e);
            AliveEnemies++;
        }

        static string KindModel(Enemy.Kind k) => k switch
        {
            Enemy.Kind.Spitter => "Modle_Enemy_Spitter",
            Enemy.Kind.Shield => "Modle_Enemy_Shield",
            Enemy.Kind.Drone => "Modle_Enemy_Drone",
            _ => "Modle_Enemy_Crawler",
        };

        static float KindHeight(Enemy.Kind k) => k switch
        {
            Enemy.Kind.Spitter => 1.74f,
            Enemy.Kind.Shield => 1.97f,
            Enemy.Kind.Drone => 0.38f,
            _ => 0.86f,
        };

        static Color KindColor(Enemy.Kind kind)
        {
            switch (kind)
            {
                case Enemy.Kind.Spitter: return new Color(0.62f, 0.85f, 0.25f);
                case Enemy.Kind.Shield: return new Color(0.30f, 0.52f, 0.95f);
                case Enemy.Kind.Drone: return new Color(0.92f, 0.62f, 0.22f);
                default: return new Color(0.88f, 0.28f, 0.30f);
            }
        }

        static float KindHp(Enemy.Kind k) => k switch
        {
            Enemy.Kind.Spitter => 45f,
            Enemy.Kind.Shield => 140f,
            Enemy.Kind.Drone => 70f,
            _ => 60f,
        };
        static float KindDmg(Enemy.Kind k) => k switch
        {
            Enemy.Kind.Spitter => 9f,
            Enemy.Kind.Shield => 18f,
            Enemy.Kind.Drone => 10f,
            _ => 12f,
        };
        /// <summary>
        /// 敌人移速（m/s）。数值为 `docs/gameplay-spec.md` §3 的设计值 × 1/3。
        ///
        /// 为什么整体降 1/3：原速下爬行者 6.0 m/s 已接近玩家走路 5.6 m/s，
        /// 玩家被追上后几乎无法用走位拉开，近战压力变成"只能正面硬换血"。
        /// 降到 1/3 后爬行者 2.0 m/s 明显慢于玩家，走位与后撤才有意义。
        ///
        /// 注意：**不要**把这里再乘一次系数。规格里的设计值仍记录在文档中，
        /// 这里就是实际生效值，避免"文档一个数、代码另一个数"。
        /// </summary>
        static float KindSpeed(Enemy.Kind k) => k switch
        {
            Enemy.Kind.Spitter => 3.5f / 3f,   // 3.5 → 1.167
            Enemy.Kind.Shield => 3.0f / 3f,    // 3.0 → 1.0
            Enemy.Kind.Drone => 7.0f / 3f,     // 7.0 → 2.333
            _ => 6.0f / 3f,                    // 6.0 → 2.0
        };
        static int KindXp(Enemy.Kind k) => k switch
        {
            Enemy.Kind.Spitter => 10,
            Enemy.Kind.Shield => 16,
            Enemy.Kind.Drone => 14,
            _ => 8,
        };

        // ---------------- 事件 ----------------

        public void OnEnemyKilled(Enemy e, int xp)
        {
            _enemies.Remove(e);
            AliveEnemies = Mathf.Max(0, AliveEnemies - 1);
            Run.Kills++;
            Run.AddThreat(0.4f);

            int ups = Run.AddXp(xp);
            for (int i = 0; i < ups; i++)
            {
                var offers = Run.Roll(3);
                if (offers.Count > 0)
                {
                    if (AutoAcceptOffers)
                    {
                        // 自检模式：直接进背包，不弹窗、不暂停
                        for (int k = 0; k < offers.Count && k < 1; k++)
                            Run.TryPick(offers[0].Id);
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
            _hud?.ShowEnd(true);
        }

        public void Lose()
        {
            SetPaused(true);
            _hud?.ShowEnd(false);
        }

        // ---------------- 弹丸回调 ----------------

        public void SpawnHitFx(Vector3 p, bool onEnemy = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "HitFx";
            go.transform.position = p;
            go.transform.localScale = Vector3.one * 0.25f;
            Object.Destroy(go.GetComponent<Collider>());
            // 用 sharedMaterial：材质已全局缓存，写 .material 会为每颗弹复制一份实例（泄漏）
            SMat.Apply(go.GetComponent<Renderer>(), SMat.Lit(new Color(1f, 0.9f, 0.4f)));
            Object.Destroy(go, 0.08f);

            SfxBank.Instance?.PlayHit(onEnemy);
        }

        public void SpawnDamageNumber(Vector3 p, float amount)
        {
            _hud?.DamageNumber(p, amount);
        }

        public void OnProjectileHit(Projectile p, Vector3 point) { }

        /// <summary>爆炸：半径内线性衰减（gameplay-spec §5.2 的 0.45 边缘系数）。</summary>
        public void Explode(Vector3 at, ProjectileDesc desc)
        {
            float r = desc.SplashRadius;
            if (r <= 0f) return;

            var vis = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            vis.name = "Blast";
            vis.transform.position = at;
            vis.transform.localScale = Vector3.one * (r * 2f);
            Object.Destroy(vis.GetComponent<Collider>());
            SMat.Apply(vis.GetComponent<Renderer>(), SMat.Lit(new Color(1f, 0.55f, 0.18f, 1f), 0.05f));
            Object.Destroy(vis, 0.18f);

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
                float dmg = desc.Damage * Mathf.Lerp(1f, 0.45f, t);
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

        // ---------------- 输入/暂停 ----------------

        bool HandlePauseInput()
        {
            var kb = Keyboard.current;
            if (kb == null) return false;

            if (kb.tabKey.wasPressedThisFrame)
            {
                if (!Paused) ToggleInventory();
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
