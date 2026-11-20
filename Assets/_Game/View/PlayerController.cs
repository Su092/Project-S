using UnityEngine;
using UnityEngine.InputSystem;

namespace S.View
{
    /// <summary>
    /// 第一人称玩家控制器：WASD 移动、鼠标视角、冲刺、跳跃、重力。
    /// 输入走新 Input System 直接读设备（不依赖 .inputactions 资源，避免资产引用断链）。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        [Header("引用")]
        public Transform CameraPivot;
        public Camera Cam;

        [Header("参数（由 Bootstrap 写入，源自 sdata.json）")]
        public float WalkSpeed = 5.6f;
        public float SprintSpeed = 9.2f;
        public float SprintTime = 0.55f;
        public float SprintCooldown = 1.4f;
        public float JumpHeight = 1.15f;
        public float Gravity = 22f;
        public float MouseSensitivity = 0.12f;

        public bool InputLocked { get; set; }

        // ---- 自动驾驶（AutoPilot）用的输入通道 ----
        // 走与真人相同的移动/视角管线，只是数据来源换成 AI 的决策，
        // 这样测出来的节奏才代表真实玩家能打出的节奏。
        public bool AiControlled { get; set; }
        public Vector3 AiMove;
        public Vector2 AiLookDelta;
        public bool AiWantsSprint { get; set; }
        public bool AiWantsJump { get; set; }

        /// <summary>是否在空中（Boss 冲击波判定用：跳起可躲）。</summary>
        public bool IsAirborne => _cc != null && !_cc.isGrounded && _vy > 0.5f;

        CharacterController _cc;
        float _pitch;
        float _vy;
        float _sprintLeft;
        float _sprintCd;

        // 供 HUD/Audio 读取
        public bool IsSprinting => _sprintLeft > 0f;
        public float SprintNormalized => Mathf.Clamp01(_sprintLeft / Mathf.Max(0.01f, SprintTime));
        public float SprintCooldownNormalized => 1f - Mathf.Clamp01(_sprintCd / Mathf.Max(0.01f, SprintCooldown));

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            WorldPhysics.ConfigureActor(gameObject);
            if (Cam == null) Cam = GetComponentInChildren<Camera>();
            if (CameraPivot == null) CameraPivot = Cam != null ? Cam.transform.parent : transform;
        }

        void Start()
        {
            // 注意：这里**不能**无条件 LockCursor(true)。
            // GameRoot 可能已经因为「显示主菜单」把光标解锁了（SetPaused(true) → LockCursor(false)），
            // 而两个 Start 的执行顺序不由我们决定。若这里后执行，光标会被锁回屏幕中心，
            // 表现就是「打开游戏后鼠标动不了、点不到开始按钮」（踩过）。
            //
            // 正确做法：只在「确实该由玩家操控」时锁。输入被锁或游戏暂停都说明
            // 现在轮不到 PlayerController 决定光标，交给 GameRoot/UI 处理。
            if (ShouldOwnCursor()) LockCursor(true);
        }

        /// <summary>光标是否该由本组件锁住（即处于可操作的运行态）。</summary>
        bool ShouldOwnCursor()
        {
            if (InputLocked) return false;
            var root = GameRoot.Instance;
            if (root != null && (root.Paused || root.InventoryOpen || root.HudPointerHeld)) return false;
            return true;
        }

        public static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (_sprintCd > 0f) _sprintCd -= dt;
            if (_sprintLeft > 0f) _sprintLeft -= dt;

            if (InputLocked) return;

            // AI 接管：用自动机的决策替代键鼠输入，移动/重力共用同一套代码。
            if (AiControlled) { UpdateAi(dt); return; }

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null || Cam == null) return;

            // ---- 视角 ----
            Vector2 look = GameRoot.Instance != null && GameRoot.Instance.HudPointerHeld
                ? Vector2.zero : mouse.delta.ReadValue() * MouseSensitivity;
            transform.Rotate(0f, look.x, 0f, Space.Self);
            _pitch = Mathf.Clamp(_pitch - look.y, -88f, 88f);
            CameraPivot.localEulerAngles = new Vector3(_pitch, 0f, 0f);

            // ---- 移动 ----
            float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
            float z = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);

            Vector3 dir = transform.right * x + transform.forward * z;
            if (dir.sqrMagnitude > 1f) dir.Normalize();

            MovePlayer(dir, kb.leftShiftKey.isPressed && z > 0.1f,
                kb.spaceKey.wasPressedThisFrame, dt);
        }

        /// <summary>由 Bootstrap 用 Player.tsv 的数值初始化。</summary>
        public void Configure(S.Core.SData d)
        {
            var p = d != null ? d.Player : null;
            if (p == null) return;
            WalkSpeed = p.Walk;
            Gravity = p.Gravity;
            JumpHeight = p.JumpHeight;
            if (p.SprintSpeed > 0f) SprintSpeed = p.SprintSpeed;
            if (p.SprintTime > 0f) SprintTime = p.SprintTime;
            if (p.SprintCooldown > 0f) SprintCooldown = p.SprintCooldown;
        }

        /// <summary>
        /// 自动驾驶的移动分支。与真人分支的差别只在「输入从哪来」：
        /// 视角增量取 AiLookDelta、位移取 AiMove（世界方向，已由 AI 算好）。
        /// 重力/跳跃/冲刺的规则保持完全一致，否则测出的节奏没有参考价值。
        /// </summary>
        void UpdateAi(float dt)
        {
            transform.Rotate(0f, AiLookDelta.x, 0f, Space.Self);
            _pitch = Mathf.Clamp(_pitch - AiLookDelta.y, -88f, 88f);
            CameraPivot.localEulerAngles = new Vector3(_pitch, 0f, 0f);
            AiLookDelta = Vector2.zero;

            var move = AiMove;
            move.y = 0f;
            if (move.sqrMagnitude > 1f) move.Normalize();
            MovePlayer(move, AiWantsSprint, AiWantsJump, dt);
            AiWantsJump = false;
        }

        // isGrounded describes the preceding Move. Do not overwrite it with a
        // horizontal/zero-distance Move before checking jump: standing still
        // can lose the Below flag even though the previous gravity step landed.
        void MovePlayer(Vector3 direction, bool wantSprint, bool wantJump, float dt)
        {
            bool grounded = _cc.isGrounded;
            if (grounded && _vy < 0f) _vy = -2f;
            if (wantSprint && _sprintCd <= 0f && _sprintLeft <= 0f && grounded)
            {
                _sprintLeft = SprintTime;
                _sprintCd = SprintCooldown + SprintTime;
            }
            if (grounded && wantJump) _vy = Mathf.Sqrt(2f * Gravity * JumpHeight);

            _vy -= Gravity * dt;
            float speed = _sprintLeft > 0f ? SprintSpeed : WalkSpeed;
            var flags = _cc.Move((direction * speed + Vector3.up * _vy) * dt);
            if ((flags & CollisionFlags.Below) != 0 && _vy < 0f) _vy = -2f;
            if ((flags & CollisionFlags.Above) != 0 && _vy > 0f) _vy = 0f;
        }
    }
}
