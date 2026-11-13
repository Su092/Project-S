using UnityEngine;
using S.Core;

namespace S.View
{
    /// <summary>
    /// 自动驾驶：让游戏自己跑一局，用来采集节奏数据。
    ///
    /// 为什么需要它：威胁值节奏、敌人 TTK、等级曲线这三项只能靠「玩」才能测，
    /// 而人工试玩的时间成本很高。这个 AI 用与真人相同的输入管线（PlayerController.UpdateAi）
    /// 驱动移动与开火，走完完整流程，产出真实的遥测数据。
    ///
    /// 它**不是**敌人 AI，也**不是**给玩家的辅助——只服务于数值校准。
    /// 行为刻意做得「中等水平」：会走位、会瞄准、会换弹，但不会极限操作。
    /// 这样得到的 DPS/TTK 比「神枪手」更接近普通玩家。
    /// </summary>
    public sealed class AutoPilot : MonoBehaviour
    {
        GameRoot _root;
        PlayerController _pc;
        PlayerWeapons _weapons;

        float _repathTimer;
        Vector3 _strafeDir;
        float _strafeTimer;
        float _offerTimer;
        float _diagTimer;

        /// <summary>是否已完成一局（自检/采样用）。</summary>
        public bool Finished { get; private set; }
        public bool Won { get; private set; }
        public float Elapsed { get; private set; }

        /// <summary>硬性时长上限：防止 AI 卡住时无限跑。</summary>
        public float MaxSeconds = 900f;

        public void Init(GameRoot root)
        {
            _root = root;
            _pc = root.Player;
            _weapons = root.Weapons;

            if (_pc != null) _pc.AiControlled = true;
        }

        void Update()
        {
            if (_root == null || _pc == null) return;

            // 暂停时（背包/三选一/结算）不推进逻辑，但仍要处理弹窗
            if (_root.InventoryOpen)
            {
                HandleOffers();
                return;
            }
            if (_root.Paused)
            {
                // 结算后结束
                if (Finished) return;
                if (_root.Hp <= 0f || IsBossDead())
                {
                    Finished = true;
                    Won = IsBossDead();
                    _pc.AiControlled = false;
                }
                return;
            }

            float dt = Time.deltaTime;
            Elapsed += dt;

            // 超时保护
            if (Elapsed > MaxSeconds)
            {
                Finished = true;
                Won = false;
                _pc.AiControlled = false;
                _root.Lose();
                return;
            }

            // 结算判定
            if (_root.Hp <= 0f)
            {
                Finished = true;
                Won = false;
                _pc.AiControlled = false;
                return;
            }

            // 每 0.5 秒记一次状态（诊断 AI 是否真的在动、在打）
            _diagTimer -= dt;
            if (_diagTimer <= 0f)
            {
                _diagTimer = 0.5f;
                var nearest = _root.FindNearestEnemy(_pc.transform.position, 60f);
                string enemyInfo = "none";
                if (nearest != null)
                {
                    float nd = Vector3.Distance(_pc.transform.position, nearest.transform.position);
                    enemyInfo = $"{nearest.Type} hp={nearest.Hp:0.#}/{nearest.MaxHp:0.#} d={nd:0.0}m " +
                                $"at({nearest.transform.position.x:0.0},{nearest.transform.position.y:0.0},{nearest.transform.position.z:0.0})";
                }

                var g = _weapons != null ? _weapons.Active : null;
                string gunInfo = g != null
                    ? $"{g.Ammo}/{g.Resolved.MagSize} cd={g.FireCooldown:0.00} rl={g.ReloadLeft:0.0}"
                    : "none";

                // 列出全部敌人（诊断「有敌人但找不到」的僵持）
                var sb = new System.Text.StringBuilder();
                foreach (var e in _root.Enemies)
                {
                    if (e == null) { sb.Append("null;"); continue; }
                    var p = e.transform.position;
                    sb.Append($"{e.Type}/hp{e.Hp:0}/x{p.x:0}/y{p.y:0}/z{p.z:0}; ");
                }

                Debug.Log($"[AUTOPILOT] t={Elapsed:0.0}s hp={_root.Hp:0} " +
                          $"enemies={_root.Enemies.Count} [{enemyInfo}] " +
                          $"gun({gunInfo}) threat={_root.Run.Threat:0} kills={_root.Run.Kills} " +
                          $"pos=({_pc.transform.position.x:0.0},{_pc.transform.position.z:0.0}) " +
                          $"LIST=[{sb}]");

                // 直接验证查找结果（区分「找不到」还是「找到但不打」）
                {
                    var direct = _root.FindNearestEnemy(_pc.transform.position, 100f);
                    if (direct == null && _root.Enemies.Count > 0)
                    {
                        var e0 = _root.Enemies[0];
                        Debug.LogError("[AUTOPILOT] 查找失败但列表非空：" +
                            (e0 == null ? "首个引用为 null"
                                : $"首个 {e0.Type} alive={e0.Alive} hp={e0.Hp:0.#} " +
                                  $"dist={Vector3.Distance(_pc.transform.position, e0.transform.position):0.0}m"));
                    }
                }
            }
            if (IsBossDead())
            {
                Finished = true;
                Won = true;
                _pc.AiControlled = false;
                return;
            }

            Drive(dt);
        }

        bool IsBossDead()
        {
            var b = _root.Boss;
            return b != null && b.State != null && !b.State.Alive;
        }

        void Drive(float dt)
        {
            var target = _root.FindNearestEnemy(_pc.transform.position, 45f);
            var boss = _root.Boss;

            // Boss 战优先瞄 Boss
            Vector3 aimPoint;
            Vector3 toTarget;
            float dist;

            if (boss != null && boss.State != null && boss.State.Alive)
            {
                aimPoint = boss.AimPoint;
                toTarget = aimPoint - _pc.Cam.transform.position;
                dist = toTarget.magnitude;
            }
            else if (target != null)
            {
                aimPoint = target.AimPoint;
                toTarget = aimPoint - _pc.Cam.transform.position;
                dist = toTarget.magnitude;
            }
            else
            {
                // 没目标：如果本区已清空，就走向门洞进下一区；否则朝区域中心待命
                if (_root.Progress != null && _root.Progress.CurrentCleared &&
                    _root.Progress.IsDoorOpen(_root.Progress.Current))
                {
                    // 门在本区后墙处（+Z 方向）
                    float gateZ = ZoneBuilder.CenterOf(_root.Progress.Current).z
                                + ZoneBuilder.ZoneLength * 0.5f + 3f;
                    var doorPos = new Vector3(0f, _pc.transform.position.y, gateZ);
                    Vector3 toDoor = doorPos - _pc.transform.position;
                    toDoor.y = 0f;

                    if (toDoor.magnitude > 1f)
                    {
                        // 朝门走，同时把视角也转向门（不然会撞墙）
                        Vector3 flat = toDoor.normalized;
                        _pc.AiMove = flat;

                        float yaw = Mathf.DeltaAngle(
                            _pc.transform.eulerAngles.y,
                            Quaternion.LookRotation(flat).eulerAngles.y);
                        _pc.AiLookDelta = new Vector2(Mathf.Clamp(yaw, -25f, 25f), 0f);
                        _pc.AiWantsSprint = toDoor.magnitude > 6f;
                    }
                    else
                    {
                        _pc.AiMove = Vector3.zero;
                    }
                    return;
                }

                var center = ZoneBuilder.CenterOf(_root.Zone);
                Vector3 toCenter = center - _pc.transform.position;
                toCenter.y = 0f;
                if (toCenter.magnitude > 6f)
                    _pc.AiMove = toCenter.normalized;
                else
                    _pc.AiMove = Vector3.zero;
                return;
            }

            // ---- 瞄准：把相机朝向目标（转成 yaw/pitch 增量）----
            float yawDelta = Mathf.DeltaAngle(
                _pc.transform.eulerAngles.y,
                Quaternion.LookRotation(new Vector3(toTarget.x, 0f, toTarget.z)).eulerAngles.y);
            // 每帧最多转 25 度，模拟人类转头速度
            yawDelta = Mathf.Clamp(yawDelta, -25f, 25f);

            float pitchTarget = -Mathf.Asin(Mathf.Clamp(toTarget.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
            float pitchNow = _pc.Cam.transform.eulerAngles.x;
            if (pitchNow > 180f) pitchNow -= 360f;
            float pitchDelta = Mathf.Clamp(pitchTarget - pitchNow, -20f, 20f);

            _pc.AiLookDelta = new Vector2(yawDelta, -pitchDelta * 0.5f);

            // ---- 走位：保持 8–16m，太近后退，太远接近，其余侧移 ----
            Vector3 flatToTarget = new Vector3(toTarget.x, 0f, toTarget.z).normalized;
            Vector3 desired;

            if (dist < 8f)
                desired = -flatToTarget;                       // 后撤
            else if (dist > 18f)
                desired = flatToTarget;                        // 接近
            else
            {
                // 侧移躲避（每 1.2 秒换方向）
                _strafeTimer -= dt;
                if (_strafeTimer <= 0f)
                {
                    _strafeTimer = 1.2f;
                    _strafeDir = Vector3.Cross(Vector3.up, flatToTarget)
                                 * (Random.value < 0.5f ? 1f : -1f);
                }
                desired = _strafeDir * 0.9f;
            }

            // 朝向瞄准方向走时，移动也大致沿着视线（人类习惯）
            Vector3 move = _pc.transform.forward * Mathf.Max(0f, Vector3.Dot(desired, _pc.transform.forward))
                         + _pc.transform.right * Vector3.Dot(desired, _pc.transform.right);
            move.y = 0f;
            if (move.sqrMagnitude < 0.01f) move = desired;
            _pc.AiMove = move.normalized;

            // 远距离冲刺接近
            _pc.AiWantsSprint = dist > 22f;

            // ---- 开火：对准了就射（不要求完美瞄准，容忍 12 度）----
            bool aimed = Mathf.Abs(yawDelta) < 12f && Mathf.Abs(pitchDelta) < 14f;
            if (aimed && dist < 40f)
            {
                FireIfPossible();
            }

            // 弹匣空则换弹由 TryFire 内部自动处理
        }

        void FireIfPossible()
        {
            var gun = _weapons != null ? _weapons.Active : null;
            if (gun == null) return;

            // 直接驱动（与玩家点左键走同一条 TryFire 路径）
            var cast = gun.TryFire(out _);
            if (cast != null) _weapons.SpawnCastForAI(cast);
        }

        /// <summary>
        /// 自动处理三选一与背包：总是选第一个 offer，攒到一定数量就随便装。
        /// 目的不是「最优构筑」，而是让数值链路跑起来以便测节奏。
        /// </summary>
        void HandleOffers()
        {
            _offerTimer -= Time.unscaledDeltaTime;
            if (_offerTimer > 0f) return;
            _offerTimer = 0.15f;

            var inv = FindInventory();
            if (inv == null) return;

            inv.AutoResolveForAI();
        }

        InventoryView FindInventory()
        {
            return _root.GetComponent<InventoryView>();
        }
    }
}
