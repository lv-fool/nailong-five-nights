using UnityEngine;
using UnityEngine.EventSystems;

namespace NailongFNAF.UI
{
    /// <summary>
    /// 「按一下走一格、按住连发」的按钮 —— 自定义夜的 ± 用。
    ///
    /// 为什么不用 <see cref="HoldButton"/>：那个是「按住才生效」（手电筒），
    /// 只在按下/抬起各回调一次，中途不重复。而调活动度需要能在 0 ↔ 20 之间
    /// 快速移动 —— 一格一格点要按 20 次，太累。
    ///
    /// 手感照常见的数字输入框（和浏览器 input[type=number] 一致）：
    ///   · 按下立刻走**一格**（单击永远精确，不会因为按住太久而多跳）
    ///   · 停 <see cref="DelaySeconds"/> 后开始连发
    ///   · 连发间隔先快后更快 —— 先 <see cref="SlowRepeatSeconds"/>，
    ///     过 <see cref="AccelerateAfter"/> 秒后改用 <see cref="FastRepeatSeconds"/>
    ///   · 指针滑出、按钮被禁用、松开 —— 一律停
    ///
    /// 用 unscaled 时间：菜单里 timeScale 可能是 0（暂停），用 scaled 会不动。
    /// </summary>
    public class RepeatButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        /// <summary>按下时先等多久才开始连发（秒）。</summary>
        public const float DelaySeconds = 0.35f;

        /// <summary>连发初期的间隔（秒）。</summary>
        public const float SlowRepeatSeconds = 0.11f;

        /// <summary>按住多久之后加速（秒）。</summary>
        public const float AccelerateAfter = 1.4f;

        /// <summary>加速后的间隔（秒）。</summary>
        public const float FastRepeatSeconds = 0.04f;

        /// <summary>点一下 / 连发 tick 时回调。</summary>
        public System.Action OnRepeat;

        /// <summary>现在是不是按着。</summary>
        public bool IsHeld { get; private set; }

        private float _heldFor;
        private float _nextAt;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (IsHeld)
            {
                return;
            }

            IsHeld = true;
            _heldFor = 0f;

            // 按下立刻走一格 —— 单击必须精确，不能等连发延迟
            Fire();

            _nextAt = DelaySeconds;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            Stop();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            // 鼠标滑出去了就当松手，免得它一直加下去停不下来
            Stop();
        }

        /// <summary>强制停止（退出菜单、切界面时用）。</summary>
        public void Stop()
        {
            IsHeld = false;
            _heldFor = 0f;
            _nextAt = 0f;
        }

        private void OnDisable()
        {
            Stop();
        }

        private void Update()
        {
            if (!IsHeld)
            {
                return;
            }

            _heldFor += Time.unscaledDeltaTime;

            if (_heldFor < _nextAt)
            {
                return;
            }

            Fire();

            float interval = _heldFor >= AccelerateAfter
                ? FastRepeatSeconds
                : SlowRepeatSeconds;

            // 按「那一刻应该到了」来累加，避免掉帧时节奏被拖慢一截
            _nextAt = _heldFor + interval;
        }

        private void Fire()
        {
            if (OnRepeat != null)
            {
                OnRepeat();
            }
        }
    }
}
