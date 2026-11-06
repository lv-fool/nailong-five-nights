using NailongFNAF.Audio;
using NailongFNAF.Core;
using NailongFNAF.Gameplay;

namespace NailongFNAF.UI
{
    /// <summary>
    /// 开关门 / 灯的**统一入口** —— 带音效，供 AI 代打和调试快捷键用。
    ///
    /// ## 为什么需要它
    /// 原来的音效写在 <c>PrototypeScene.OnDoorLeft</c> / <c>OnLightLeft</c> 这些**按钮回调**里：
    ///
    ///     玩家点按钮 → OnDoorLeft() → GameManager.ToggleLeftDoor() + _audio.PlayDoor()
    ///     AI 代打   →                  GameManager.ToggleLeftDoor()            ← 没声音
    ///
    /// AI 直接调 <see cref="GameManager"/>，绕过了回调，所以**开关门/灯全都没有声音**。
    /// （开灯电音例外 —— 那个是每帧在 <c>UpdateVisuals</c> 里按「任意一侧亮着」汇总算的，
    /// 所以 AI 开灯有电音，只是没有「按下去」那一声。）
    ///
    /// 现在把「改状态 + 放音效」收成一处，谁调都有声音：
    ///   · 按钮回调 → 走这里
    ///   · AI 代打  → 走这里
    ///   · 调试快捷键 → 走这里
    ///
    /// ⚠️ **不要**在调用方再补一次 <c>PlayDoor</c> —— 会响两遍。
    /// </summary>
    public static class DoorLightActions
    {
        /// <summary>
        /// 切换一侧的门。返回 true 表示状态真的变了（被突破 / 不能操作时为 false，不响）。
        /// </summary>
        public static bool ToggleDoor(GameManager gm, Side side, AudioManager audio)
        {
            if (gm == null)
            {
                return false;
            }

            bool before = side == Side.Left ? gm.LeftDoorClosed : gm.RightDoorClosed;

            if (side == Side.Left)
            {
                gm.ToggleLeftDoor();
            }
            else
            {
                gm.ToggleRightDoor();
            }

            bool after = side == Side.Left ? gm.LeftDoorClosed : gm.RightDoorClosed;

            if (after == before)
            {
                return false;
            }

            if (audio != null)
            {
                // 该侧已被突破 → 退回默认机械音（见 AudioManager.PlayDoor）
                audio.PlayDoor(after, gm.IsSideDisabled(side));
            }

            return true;
        }

        /// <summary>
        /// 切换一侧的灯。返回 true 表示状态真的变了。
        ///
        /// ⚠️ 开灯**电音**（循环那一层）不在这里 —— 它由
        /// <c>PrototypeScene.UpdateVisuals</c> 每帧按「任意一侧亮着」汇总，
        /// 这里只负责「按下去」那一声。
        /// </summary>
        public static bool ToggleLight(GameManager gm, Side side, AudioManager audio)
        {
            if (gm == null)
            {
                return false;
            }

            bool before = side == Side.Left ? gm.LeftLightOn : gm.RightLightOn;

            if (side == Side.Left)
            {
                gm.ToggleLeftLight();
            }
            else
            {
                gm.ToggleRightLight();
            }

            bool after = side == Side.Left ? gm.LeftLightOn : gm.RightLightOn;

            if (after == before)
            {
                return false;
            }

            if (audio != null)
            {
                audio.PlayLight(after, gm.IsSideDisabled(side));
            }

            return true;
        }
    }
}
