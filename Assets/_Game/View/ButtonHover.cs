using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace S.View
{
    /// <summary>
    /// 按钮悬停/按下的视觉反馈。
    ///
    /// 为什么需要：Button 自带的 Transition 只改 tint，而本项目按钮用的是**贴图 + 9-slice**，
    /// 纯 tint 在深色底上几乎看不出变化 —— 玩家会觉得「这按钮是不是不能点」。
    /// 这里直接替换 sprite（normal → hover），反馈明确。
    ///
    /// 用法：挂在按钮对象上，指定 Target（要换图的 Image）。
    /// 素材缺失时自动退化为只改颜色，不会报错。
    /// </summary>
    [RequireComponent(typeof(Button))]
    public sealed class ButtonHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler
    {
        public Image Target;
        public Color Normal = Color.white;
        public Color Hover = new Color(0.88f, 0.96f, 1f);
        public Color Pressed = new Color(0.72f, 0.82f, 0.92f);

        Button _btn;
        bool _over;

        void Awake()
        {
            _btn = GetComponent<Button>();
            if (Target == null) Target = GetComponent<Image>();
        }

        void OnDisable()
        {
            _over = false;
            Apply(Normal);
        }

        public void OnPointerEnter(PointerEventData e)
        {
            _over = true;
            Apply(Hover);
        }

        public void OnPointerExit(PointerEventData e)
        {
            _over = false;
            Apply(Normal);
        }

        public void OnPointerDown(PointerEventData e) => Apply(Pressed);

        public void OnPointerUp(PointerEventData e) => Apply(_over ? Hover : Normal);

        void Apply(Color c)
        {
            if (Target == null) return;
            // 不可交互时不要给出「可点」的错觉
            if (_btn != null && !_btn.interactable) return;
            Target.color = c;
        }
    }
}
