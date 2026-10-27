using System;

namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 玩家是因为奶包才死的吗？
    ///
    /// 她本人不杀人（见 <see cref="NaibaoDirector"/>），所以「因为她死」永远是一个**归因判断**，
    /// 不是因果链上的事实。这里的口径是时间窗：她刚动过某样东西，玩家在几秒内就死了，
    /// 就算她的锅 —— 玩家心里也是这么算的。窗口开得比她那次动作长一点，
    /// 因为「她按住监控 → 你看不见 → 敌人摸到门口 → 跳杀」本来就有一段延迟。
    ///
    /// 只在她确实动过设备时才算；纯嘲讽不背锅（她只是嘴欠，没碰任何东西）。
    /// </summary>
    public sealed class NaibaoApology
    {
        /// <summary>抢监控之后多久内死掉算她头上。</summary>
        public const float MonitorWindow = 5f;

        /// <summary>关灯之后多久内死掉算她头上。</summary>
        public const float LightWindow = 6f;

        /// <summary>偷加耗电之后多久内断电算她头上（断电到跳杀本身还有十几秒）。</summary>
        public const float UsageWindow = 26f;

        /// <summary>最近一次「会背锅的」动作。</summary>
        public NaibaoFault Fault { get; private set; }

        /// <summary>距那次动作过了多少秒。</summary>
        public float Age { get; private set; }

        /// <summary>她最近一次动作还在这件事的窗口里 —— 现在死了就是她的锅。</summary>
        public bool Culpable
        {
            get { return Fault != NaibaoFault.None && Age <= WindowOf(Fault); }
        }

        /// <summary>记一笔：她又动设备了。<see cref="NaibaoAction.Line"/>（纯嘴欠）不算。</summary>
        public void Note(NaibaoAction action)
        {
            NaibaoFault fault = FaultOf(action);
            if (fault == NaibaoFault.None)
            {
                return;
            }

            Fault = fault;
            Age = 0f;
        }

        /// <summary>推进计时；窗口过了就当她没动过。</summary>
        public void Tick(float dt)
        {
            if (Fault == NaibaoFault.None)
            {
                return;
            }

            Age += dt;
            if (Age > WindowOf(Fault))
            {
                Fault = NaibaoFault.None;
            }
        }

        /// <summary>这一局重来，账本也清空。</summary>
        public void Clear()
        {
            Fault = NaibaoFault.None;
            Age = 0f;
        }

        /// <summary>
        /// 她的道歉。照豆包那种口气：先认下来，再解释，不推给别人 ——
        /// 但也别写得太长，死亡画面上没人读大段文字。
        /// </summary>
        public static string LineFor(NaibaoFault fault)
        {
            // 台词存在 Assets/Resources/naibao_lines.json（「奶龙 / 12. 编辑奶包台词」），
            // 那三条按「抢监控 / 关灯 / 偷加耗电」的**固定顺序**排，所以这里用下标取。
            // 文件缺失时 NaibaoDirector.Lines 会给一份纯默认的实例，不会返回 null。
            switch (fault)
            {
                case NaibaoFault.MonitorGrab:
                    return NaibaoDirector.Lines.LineAt(NaibaoLines.KeyApology, 0);

                case NaibaoFault.LightOff:
                    return NaibaoDirector.Lines.LineAt(NaibaoLines.KeyApology, 1);

                case NaibaoFault.UsageSpike:
                    return NaibaoDirector.Lines.LineAt(NaibaoLines.KeyApology, 2);

                default:
                    return null;
            }
        }

        /// <summary>现在该说的那句道歉；不该背锅时返回 null（她就安静地待着）。</summary>
        public string CulpableLine()
        {
            return Culpable ? LineFor(Fault) : null;
        }

        /// <summary>道歉那句的配音 id（Assets/Resources/Voice/apology_*.mp3）；不该背锅时空串。</summary>
        public string CulpableLineId()
        {
            return Culpable ? IdFor(Fault) : string.Empty;
        }

        /// <summary>故障 → 配音 id。三条台词按「抢监控 / 关灯 / 偷加耗电」固定顺序编号。</summary>
        public static string IdFor(NaibaoFault fault)
        {
            switch (fault)
            {
                case NaibaoFault.MonitorGrab:
                    return "apology_1";

                case NaibaoFault.LightOff:
                    return "apology_2";

                case NaibaoFault.UsageSpike:
                    return "apology_3";

                default:
                    return string.Empty;
            }
        }

        /// <summary>这个故障的判定窗口有多长。</summary>
        public static float WindowOf(NaibaoFault fault)
        {
            switch (fault)
            {
                case NaibaoFault.MonitorGrab:
                    return MonitorWindow;
                case NaibaoFault.LightOff:
                    return LightWindow;
                case NaibaoFault.UsageSpike:
                    return UsageWindow;
                default:
                    return 0f;
            }
        }

        /// <summary>动作 → 故障。纯说话与「什么都不做」不产生归因。</summary>
        public static NaibaoFault FaultOf(NaibaoAction action)
        {
            switch (action)
            {
                case NaibaoAction.MonitorGrab:
                    return NaibaoFault.MonitorGrab;
                case NaibaoAction.LightOff:
                    return NaibaoFault.LightOff;
                case NaibaoAction.UsageSpike:
                    return NaibaoFault.UsageSpike;
                default:
                    return NaibaoFault.None;
            }
        }
    }

    /// <summary>奶包背的这口锅是哪一种。</summary>
    public enum NaibaoFault
    {
        None = 0,
        MonitorGrab = 1,
        LightOff = 2,
        UsageSpike = 3,
    }
}
