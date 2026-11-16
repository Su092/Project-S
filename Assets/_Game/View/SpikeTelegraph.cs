using UnityEngine;

namespace S.View
{
    /// <summary>
    /// 地刺预警 → 刺出。规格 gameplay-spec §3.1 P2：
    /// 地面预警 1.2s 后成排刺出（18 伤）。
    ///
    /// 为什么预警必须真的等满时间：这是玩家唯一的躲避窗口。
    /// 如果预警和伤害同时发生，这个技能就变成不可躲的随机伤害。
    /// </summary>
    public sealed class SpikeTelegraph : MonoBehaviour
    {
        public float Seconds = 1.2f;
        public float Damage = 18f;

        float _left;
        Renderer _r;
        bool _done;
        MaterialPropertyBlock _mpb;

        void Awake()
        {
            _r = GetComponent<Renderer>();
            _left = Seconds;
            _mpb = new MaterialPropertyBlock();

            if (_r != null)
            {
                var baseMat = SMat.Unlit(new Color(1f, 0.25f, 0.20f, 0.6f));
                if (baseMat != null) _r.sharedMaterial = baseMat;
            }
        }

        void Update()
        {
            if (_done) return;
            float dt = Time.deltaTime;
            _left -= dt;

            // 预警期内闪烁加速，给「快刺了」的可读反馈。
            // 用 MaterialPropertyBlock 而非每次 SMat.Unlit(新颜色)：
            // 后者会按颜色缓存，每帧一个新颜色 = 材质无限增长（踩过）。
            if (_r != null && _mpb != null)
            {
                float t = Mathf.Clamp01(_left / Mathf.Max(0.01f, Seconds));
                float blink = Mathf.Abs(Mathf.Sin(Time.time * Mathf.Lerp(24f, 8f, t)));
                var c = new Color(1f, 0.25f, 0.20f, Mathf.Lerp(0.15f, 0.80f, blink * t));

                _r.GetPropertyBlock(_mpb);
                _mpb.SetColor("_BaseColor", c);
                _r.SetPropertyBlock(_mpb);
            }

            if (_left > 0f) return;

            _done = true;
            Erupt();
        }

        void Erupt()
        {
            var root = GameRoot.Instance;
            Vector3 at = transform.position;

            // 刺：一排细长方块从地面升起
            for (int i = 0; i < 7; i++)
            {
                float ang = i * 51.4f;
                float rad = (i % 2 == 0) ? 0.7f : 1.35f;
                Vector3 p = at + new Vector3(
                    Mathf.Cos(ang * Mathf.Deg2Rad) * rad, 0f,
                    Mathf.Sin(ang * Mathf.Deg2Rad) * rad);

                var spike = GameObject.CreatePrimitive(PrimitiveType.Cube);
                spike.name = "Spike";
                spike.transform.position = p + Vector3.up * 0.9f;
                spike.transform.localScale = new Vector3(0.28f, 1.8f, 0.28f);
                spike.transform.rotation = Quaternion.Euler(0f, ang, 0f);
                SAssets.RemoveColliders(spike);
                SMat.Apply(spike.GetComponent<Renderer>(), SMat.Lit(new Color(0.55f, 0.15f, 0.20f), 0.6f));
                Destroy(spike, 0.55f);
            }

            SfxBank.Instance?.PlayExplode();

            // 伤害判定：玩家在范围内即受伤
            if (root != null && root.PlayerTransform != null)
            {
                float d = Vector3.Distance(
                    new Vector3(at.x, 0f, at.z),
                    new Vector3(root.PlayerTransform.position.x, 0f, root.PlayerTransform.position.z));
                if (d <= 2.1f) root.DamagePlayer(Damage);
            }

            Destroy(gameObject, 0.05f);
        }
    }
}
