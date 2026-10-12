namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 断电演出的三个阶段 —— 照着 FNAF 1 原版的断电流程分。
    /// 见 <see cref="BlackoutSequence"/>。
    /// </summary>
    public enum BlackoutPhase
    {
        /// <summary>刚断电：门弹开、灯灭、监控掉线，一片漆黑，什么也看不见。</summary>
        Silence = 0,

        /// <summary>它现身了。音乐盒响起，脸跟着节拍一亮一亮。</summary>
        Music = 1,

        /// <summary>音乐停了。画面彻底黑掉，只剩一个勉强能辨认的轮廓。</summary>
        Dark = 2,

        /// <summary>已经动手。</summary>
        Done = 3
    }
}
