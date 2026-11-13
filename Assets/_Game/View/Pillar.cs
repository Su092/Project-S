using UnityEngine;
using S.Core;

namespace S.View
{
    /// <summary>
    /// Boss 场的能量柱。被打碎后立刻让 Boss 关盾并进入破防窗口（逻辑在 Core.BossState）。
    /// 本类只管「吃伤害、变色、播放倒塌」。
    /// </summary>
    public sealed class Pillar : MonoBehaviour
    {
        public int Index;
        public float Hp;
        public float MaxHp;

        public bool Alive => Hp > 0f;

        Renderer _r;
        Vector3 _baseScale;
        bool _broken;

        void Awake()
        {
            _r = GetComponent<Renderer>();
            _baseScale = transform.localScale;
        }

        /// <summary>受到伤害。返回 true 表示这一击打碎了柱子。</summary>
        public bool TakeDamage(float amount)
        {
            if (_broken || amount <= 0f) return false;

            Hp -= amount;
            if (Hp > 0f)
            {
                // 血量越低越暗，给「快碎了」的可读反馈。
                // 必须用 MaterialPropertyBlock：材质已改为全局共享缓存，
                // 直接写 sharedMaterial 会把所有用同一材质的物件一起染色（踩过）。
                float t = Mathf.Clamp01(Hp / Mathf.Max(1f, MaxHp));
                Tint(Color.Lerp(new Color(0.20f, 0.10f, 0.28f), new Color(0.62f, 0.30f, 0.82f), t));
                return false;
            }

            Hp = 0f;
            Break();
            return true;
        }

        void Tint(Color c)
        {
            if (_r == null) return;
            var mpb = new MaterialPropertyBlock();
            _r.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", c);
            _r.SetPropertyBlock(mpb);
        }

        void Break()
        {
            _broken = true;
            var col = GetComponent<Collider>();
            if (col != null) col.enabled = false;

            // 倒塌：压扁 + 变暗，然后移除
            transform.localScale = new Vector3(_baseScale.x * 1.4f, _baseScale.y * 0.12f, _baseScale.z * 1.4f);
            Tint(new Color(0.12f, 0.10f, 0.14f));

            Destroy(gameObject, 1.2f);
        }
    }
}
