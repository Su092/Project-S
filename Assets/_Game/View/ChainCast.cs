using System.Collections.Generic;
using UnityEngine;
using S.Core;

namespace S.View
{
    /// <summary>
    /// 一次施法的执行器：把 `CastResult.Steps`（链式触发计划）按时间轴逐项执行。
    ///
    /// 为什么要有这个东西（规格见 docs/loadout-spec.md §1.2）：
    ///   以前 `TryFire` 一次结算完就把全部弹丸铺在枪口，视觉上是「一枪打出一团」，
    ///   链里排了几件、谁先谁后完全看不出。现在扣扳机只拿到一份**计划**，
    ///   由本类按「链顺序 + 相邻间隔 = 枪的击发间隔」逐项触发：
    ///     子弹件 → 生成投射物；衍生件 → 作用到当时存活、以及之后生成的投射物上。
    ///
    /// 数值纪律（最容易写错的地方）：
    ///   规格 §3 的结算域里，链内**所有子弹在第一步就都在 ctx 里**了。所以
    ///   `[校准套件, 制式弹]` 的校准必须作用到后面才出现的子弹，否则总伤害会低于
    ///   `ResolveCast` 的期望值，HUD 显示的数字就成了假话。
    ///
    ///   因此执行分两路，合起来等价于全量结算：
    ///     1) 子弹生成时，先补上「在它之前触发、按作用域确实作用于它」的配件；
    ///     2) 之后触发的配件，再实时改当时在场的实例。
    ///   两路都不会重复施加同一件（第 1 路只取下标 < 当前步的配件）。
    ///
    /// 其他纪律：
    ///   · 必须能被打断：换枪 / 换弹时整体丢弃，不能留下半条链继续喷弹。
    ///   · 分裂只在配置了 `SplitCount > 1` 的配件触发时发生，且父体退役、子体从父体位置散开。
    /// </summary>
    public sealed class ChainCast
    {
        readonly CastResult _cast;
        readonly GunDef _gun;

        int _next;                 // 下一个要执行的 step 下标
        float _elapsed;

        /// <summary>本次施法已触发过的配件（按触发顺序），供后生成的子弹补算。</summary>
        readonly List<ChainStep> _firedMods = new List<ChainStep>();
        readonly List<int> _firedModIndex = new List<int>();   // 对应的 Steps 下标

        /// <summary>当前在场的投射物实例（按生成顺序）。</summary>
        readonly List<Live> _alive = new List<Live>();

        public sealed class Live
        {
            public ProjectileDesc Desc;
            public Projectile Visual;
            public bool Destroyed;
        }

        /// <summary>每次「某一步触发」回调，供 HUD 做链条点亮。</summary>
        public event System.Action<ChainStep> OnStepFired;

        public ChainCast(CastResult cast, GunDef gun)
        {
            _cast = cast;
            _gun = gun;
        }

        /// <summary>本次施法对应的格位（HUD 链条要按格位取链项列表）。可为空。</summary>
        public SlotState Slot => _cast?.Slot;

        /// <summary>执行计划（HUD 用它把「第几步」映射回链上第几项）。</summary>
        public System.Collections.Generic.IReadOnlyList<ChainStep> Steps => _cast.Steps;

        /// <summary>整条链是否执行完毕（所有步都已触发）。</summary>
        public bool Finished => _next >= _cast.Steps.Count;

        /// <summary>场上仍由本次施法产生的存活弹丸数（跑完后据此释放执行器）。</summary>
        public int RemainingProjectiles
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _alive.Count; i++)
                    if (!_alive[i].Destroyed && _alive[i].Visual != null) n++;
                return n;
            }
        }

        /// <summary>下一个待触发的项（HUD 高亮用），没有则返回 null。</summary>
        public ChainStep Pending => _next < _cast.Steps.Count ? _cast.Steps[_next] : null;

        /// <summary>已触发的项数 / 总项数（HUD 进度用）。</summary>
        public int FiredCount => _next;
        public int StepCount => _cast.Steps.Count;

        /// <summary>整条链跑完所需时间（秒）。</summary>
        public float Duration => _cast.Duration;

        /// <summary>整次施法的期望伤害（与全量结算同口径）。</summary>
        public float ExpectedDamage => _cast.ExpectedDamage;

        /// <summary>
        /// 推进时间轴。`spawn` 由调用方提供（真正创建场景弹丸），
        /// `remove` 用于把被替换掉的旧弹丸销毁（分裂时父体退役）。
        ///
        /// 之所以把「造弹丸」做成回调：本类不依赖 UnityEngine 场景对象的构造方式，
        /// 也便于自动驾驶与自检走同一条路径。
        /// </summary>
        public void Tick(float dt, System.Func<ProjectileDesc, Projectile> spawn,
            System.Action<Projectile> remove)
        {
            _elapsed += dt;

            // 按时间轴把到期的项一次处理完（dt 可能大于间隔，不能一帧只处理一项）
            while (_next < _cast.Steps.Count && _cast.Steps[_next].Delay <= _elapsed + 0.0001f)
            {
                var step = _cast.Steps[_next];
                int idx = _next;
                _next++;
                Execute(step, idx, spawn, remove);
                OnStepFired?.Invoke(step);
            }

            // 清掉已销毁的实例，避免后续衍生件对空指针施加效果
            for (int i = _alive.Count - 1; i >= 0; i--)
                if (_alive[i].Destroyed || _alive[i].Visual == null) _alive.RemoveAt(i);
        }

        void Execute(ChainStep step, int stepIndex, System.Func<ProjectileDesc, Projectile> spawn,
            System.Action<Projectile> remove)
        {
            if (step.Kind == ChainStepKind.Bullet)
            {
                var template = step.Bullet;
                if (template == null) return;

                // 补算「在它之前触发、且按作用域作用于它」的全部配件，**按链顺序**逐件施加。
                // 判据与 Loadout.SelectTargets 同源：全场 = 全部；后继 = 子弹序号 >= 触发时已出现数。
                //
                // 为什么要补全而不是只算第一件：规格 §3 的 ctx 里链内所有子弹一开始就都在，
                // 所以 `[校准, 增压, 弹]` 这颗粒子要吃两件；只算一件会少一段伤害。
                var descs = new List<ProjectileDesc> { template.Clone() };
                for (int k = 0; k < _firedMods.Count; k++)
                {
                    if (_firedModIndex[k] >= stepIndex) continue;
                    var mod = _firedMods[k].Mod;
                    if (mod == null) continue;

                    var appliedDescs = new List<ProjectileDesc>(descs.Count);
                    for (int d = 0; d < descs.Count; d++)
                    {
                        var cur = descs[d];
                        if (!ScopeHits(mod, _firedMods[k].BulletsSeen, cur.SourceIndex)) { appliedDescs.Add(cur); continue; }
                        appliedDescs.AddRange(Loadout.ApplyModRuntime(cur, mod, _gun));
                    }
                    descs = appliedDescs;
                    if (descs.Count == 0) return;
                }

                for (int d = 0; d < descs.Count; d++)
                {
                    var vis = spawn != null ? spawn(descs[d]) : null;
                    _alive.Add(new Live { Desc = descs[d], Visual = vis });
                }
                return;
            }

            // 衍生件：作用到当时在场的实例
            if (step.Mod == null) return;

            _firedMods.Add(step);
            _firedModIndex.Add(stepIndex);

            var next = new List<Live>(_alive.Count);
            for (int i = 0; i < _alive.Count; i++)
            {
                var parent = _alive[i];
                if (parent.Destroyed || parent.Desc == null) { continue; }
                if (!ScopeHits(step.Mod, step.BulletsSeen, parent.Desc.SourceIndex)) { next.Add(parent); continue; }

                var applied = Loadout.ApplyModRuntime(parent.Desc, step.Mod, _gun);
                if (applied.Count == 1)
                {
                    // 普通修正：就地改描述符，场景弹丸不重建（避免子弹瞬移重置）
                    parent.Desc = applied[0];
                    parent.Visual?.ApplyDesc(applied[0]);
                    next.Add(parent);
                    continue;
                }

                // 分裂：父体退役，换成 n 个新弹丸，从父体当前位置与朝向散开
                parent.Destroyed = true;
                Vector3 pos = Vector3.zero;
                Quaternion rot = Quaternion.identity;
                if (parent.Visual != null)
                {
                    pos = parent.Visual.transform.position;
                    rot = parent.Visual.transform.rotation;
                }
                remove?.Invoke(parent.Visual);

                for (int k = 0; k < applied.Count; k++)
                {
                    var child = applied[k].Clone();
                    var cv = spawn != null ? spawn(child) : null;
                    if (cv != null)
                    {
                        // 保留父体位置，朝向略作散开，视觉上像"空中炸开"
                        cv.transform.position = pos;
                        cv.transform.rotation = rot;
                    }
                    next.Add(new Live { Desc = child, Visual = cv });
                }
            }
            _alive.Clear();
            _alive.AddRange(next);
        }

        /// <summary>作用域判定，与 `Loadout.SelectTargets` 保持同一判据。</summary>
        static bool ScopeHits(ModifierDef mod, int bulletsSeen, int sourceIndex)
        {
            if (mod == null) return false;
            if (mod.Scope == Scope.Whole) return true;
            if (mod.Scope == Scope.After) return sourceIndex >= bulletsSeen;
            return false;   // Gun 作用域不在时间轴上触发
        }

        /// <summary>丢弃尚未触发的部分（换枪 / 换弹 / 死亡时调用）。</summary>
        public void Cancel() => _next = _cast.Steps.Count;
    }
}
