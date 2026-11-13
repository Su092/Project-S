using UnityEngine;

namespace S.View
{
    /// <summary>
    /// 区域之间的门。清空当前区前是实心屏障（挡玩家也挡子弹），清空后升起消失。
    /// 规格见 docs/gameplay-spec.md §2「区域完成」。
    /// </summary>
    public sealed class ZoneGate : MonoBehaviour
    {
        public int FromZone;

        const float GateWidth = 9f;
        const float GateHeight = 5.4f;

        GameObject _barrier;
        Renderer _barrierR;
        bool _open;
        float _anim;   // 0 = 完全关闭，1 = 完全打开

        public bool IsOpen => _open;

        public void Build()
        {
            _barrier = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _barrier.name = "Barrier";
            _barrier.transform.SetParent(transform, false);
            _barrier.transform.localPosition = new Vector3(0f, GateHeight * 0.5f, 0f);
            _barrier.transform.localScale = new Vector3(GateWidth, GateHeight, 0.5f);

            // 屏障用半透明能量感：亮蓝偏白，关闭时明显不可穿过
            _barrierR = _barrier.GetComponent<Renderer>();
            SMat.Apply(_barrierR, SMat.Lit(new Color(0.24f, 0.62f, 0.95f), 0.6f));

            SetOpen(false, instant: true);
        }

        public void SetOpen(bool open, bool instant = false)
        {
            if (_open == open && !instant) return;
            _open = open;
            if (instant) _anim = open ? 1f : 0f;

            if (_barrier != null)
            {
                // 关闭时参与碰撞（挡人挡子弹）；打开时移除碰撞
                var col = _barrier.GetComponent<Collider>();
                if (col != null) col.enabled = !open;
            }
        }

        void Update()
        {
            float target = _open ? 1f : 0f;
            if (Mathf.Approximately(_anim, target)) return;

            _anim = Mathf.MoveTowards(_anim, target, Time.deltaTime * 2.2f);

            if (_barrier != null)
            {
                // 开启时整体升入门楣并淡出
                float h = Mathf.Lerp(GateHeight * 0.5f, GateHeight * 1.5f, _anim);
                var p = _barrier.transform.localPosition;
                _barrier.transform.localPosition = new Vector3(p.x, h, p.z);

                if (_barrierR != null)
                {
                    var c = _open
                        ? new Color(0.24f, 0.62f, 0.95f, 1f - _anim)
                        : new Color(0.24f, 0.62f, 0.95f, _anim);
                    // 用 MaterialPropertyBlock：材质是全局共享缓存，直接改它会污染其他物件
                    var mpb = new MaterialPropertyBlock();
                    _barrierR.GetPropertyBlock(mpb);
                    mpb.SetColor("_BaseColor", c);
                    _barrierR.SetPropertyBlock(mpb);
                }
            }
        }
    }
}
