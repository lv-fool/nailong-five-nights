using UnityEngine;

namespace NailongFNAF.UI
{
    /// <summary>
    /// 「显示比例」的存档。真正摆安全区 / 黑边的是 <see cref="PrototypeScene"/>：
    /// 它把相机 rect 和 UGUI 安全区一起收进去，纯算比例的规则在
    /// <see cref="LetterboxPolicy"/>（可单测）。
    /// </summary>
    public static class DisplayAspectController
    {
        /// <summary>显示比例存档键（int，对应 <see cref="DisplayAspect"/>）。</summary>
        public const string Key = "nailong.displayAspect";

        /// <summary>当前存档里的档位。</summary>
        public static DisplayAspect Current
        {
            get
            {
                return DisplayAspectPolicy.FromInt(
                    PlayerPrefs.GetInt(Key, (int)DisplayAspect.Auto));
            }
        }

        /// <summary>设置菜单：保存档位（布局由调用方接着刷新）。</summary>
        public static void Apply(DisplayAspect aspect)
        {
            PlayerPrefs.SetInt(Key, (int)aspect);
            PlayerPrefs.Save();
        }
    }
}