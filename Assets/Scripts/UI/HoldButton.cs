using UnityEngine;
using UnityEngine.EventSystems;

namespace NailongFNAF.UI
{
    /// <summary>
    /// 「按住才生效」的按钮 —— 手电筒用。
    ///
    /// 和 <see cref="UnityEngine.UI.Button"/> 不一样：那个是点一下触发一次，
    /// 这个是按下、抬起各回调一次。指针滑出按钮、或按钮被禁用，都当作松手 ——
    /// 免得手电筒粘在「亮着」的状态上。
    ///
    /// 谁在用它：<see cref="PrototypeScene"/>（手电筒按钮）。
    /// </summary>
    public class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        /// <summary>按下 / 抬起时回调。true = 按住。</summary>
        public System.Action<bool> HoldChanged;

        /// <summary>现在是不是按着。</summary>
        public bool IsHeld { get; private set; }

        public void OnPointerDown(PointerEventData eventData)
        {
            Set(true);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            Set(false);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            // 鼠标滑出去了就当松手 —— 手电筒不该跟着鼠标粘住
            Set(false);
        }

        /// <summary>强制松手（断电、退出时用）。</summary>
        public void Release()
        {
            Set(false);
        }

        private void OnDisable()
        {
            Set(false);
        }

        private void Set(bool held)
        {
            if (IsHeld == held)
            {
                return;
            }

            IsHeld = held;

            if (HoldChanged != null)
            {
                HoldChanged(held);
            }
        }
    }
}
