using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NailongFNAF.Core;
using NailongFNAF.Gameplay;
using NailongFNAF.UI;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;

namespace NailongFNAF.Diagnostics
{
    /// <summary>
    /// Play 模式冒烟探针：只在编辑器里存在，且只有命令行带 -playSmoke 时才自动装到场景里。
    /// 作用是把「进 Play → 截标题页 → 点永夜模式 → 模拟操作 → 截 HUD → 打印状态 → 退出」
    /// 这一整套动作做完，用来做无人值守的画面验证。
    /// </summary>
    public sealed class PlaySmokeProbe : MonoBehaviour
    {
        private const string FlagArg = "-playSmoke";

        private readonly List<string> _errors = new List<string>();
        private string _outDir;
        private float _t0;
        private int _step;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string[] args = Environment.GetCommandLineArgs();
            bool wanted = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], FlagArg, StringComparison.OrdinalIgnoreCase))
                {
                    wanted = true;
                    break;
                }
            }

            if (!wanted)
            {
                return;
            }

            GameObject go = new GameObject("PlaySmokeProbe");
            go.AddComponent<PlaySmokeProbe>();
            Debug.Log("SMOKE: 探针已装载");
        }

        private void Awake()
        {
            _outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".workbuddy-ai", "screenshots", "story"));
            Directory.CreateDirectory(_outDir);
            Application.logMessageReceived += OnLog;
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
        }

        private void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error)
            {
                _errors.Add(type + ": " + message);
            }
        }

        private void Update()
        {
            if (_t0 <= 0f)
            {
                _t0 = Time.realtimeSinceStartup;
            }

            float elapsed = Time.realtimeSinceStartup - _t0;

            if (_step == 0 && elapsed > 1.5f)
            {
                _step = 1;
                Snap("01_title");
                DumpScene("标题页");
            }
            else if (_step == 1 && elapsed > 2.5f)
            {
                _step = 2;
                object scene = Instance("NailongFNAF.UI.PrototypeScene");
                Debug.Log("SMOKE: OnEndlessClicked = " + Invoke(scene, "OnEndlessClicked", null));
            }
            else if (_step == 2 && elapsed > 4.0f)
            {
                _step = 3;
                Snap("02_endless");
                DriveActions();
            }
            else if (_step == 3 && elapsed > 6.5f)
            {
                _step = 4;
                Snap("03_endless_after_actions");
                ReportEndless();
            }
            else if (_step == 4 && elapsed > 7.4f)
            {
                _step = 5;
                object scene = Instance("NailongFNAF.UI.PrototypeScene");
                Debug.Log("SMOKE: 请求暂停 = " + Invoke(scene, "OpenPause", null));
                Snap("06_pause");
                ReportPause();
            }
            else if (_step == 5 && elapsed > 8.6f)
            {
                _step = 6;
                object scene = Instance("NailongFNAF.UI.PrototypeScene");
                Invoke(scene, "OnPauseSave", null);
                ReportSave();
                Invoke(scene, "ResumeFromPause", null);
                Debug.Log("SMOKE: 恢复后 timeScale = " + Time.timeScale +
                          " AudioListener.pause = " + AudioListener.pause);
            }
            else if (_step == 6 && elapsed > 9.4f)
            {
                _step = 7;
                object scene = Instance("NailongFNAF.UI.PrototypeScene");
                object gm = Instance("NailongFNAF.Core.GameManager");

                // 刚存的档应该能被「继续永夜」读回来：再点一次按钮，看时间有没有接上
                Invoke(scene, "OnEndlessClicked", null);
                object clock = gm != null ? Get(gm, "Clock") : null;
                object power = gm != null ? Get(gm, "Power") : null;
                Debug.Log("SMOKE: 续档后 已撑秒 = " + (clock != null ? Get(clock, "ElapsedSeconds") : null) +
                          " 电量 = " + (power != null ? Get(power, "Level") : null));
            }
            else if (_step == 7 && elapsed > 10.2f)
            {
                _step = 8;
                object scene = Instance("NailongFNAF.UI.PrototypeScene");
                Debug.Log("SMOKE: 直接走失败结算（永夜分支）= " +
                          Invoke(scene, "ShowLoseEnd", null));
            }
            else if (_step == 8 && elapsed > 11.6f)
            {
                _step = 9;
                Snap("05_endless_end");
                ReportEndPanel();
            }
            else if (_step == 9 && elapsed > 12.6f)
            {
                _step = 10;
                object scene = Instance("NailongFNAF.UI.PrototypeScene");
                Invoke(scene, "OnStartClicked", null);
                Invoke(scene, "OnNightClicked", new object[] { 0 });
                Debug.Log("SMOKE: 已切到普通夜晚 1");
            }
            else if (_step == 10 && elapsed > 14.0f)
            {
                _step = 11;
                Snap("04_normal_night");
                ReportNormal();
            }
            else if (_step == 11 && elapsed > 15.0f)
            {
                _step = 12;

                // 先看「故事模式」的选夜界面（6 个夜晚 + 返回，两行排布最容易压边）
                object scene = Instance("NailongFNAF.UI.PrototypeScene");
                Invoke(scene, "OpenTitle", null);
                Invoke(scene, "OnStartClicked", null);
                Snap("10_levels");
            }
            else if (_step == 12 && elapsed > 16.2f)
            {
                _step = 13;

                // 自定义夜默认锁着，探针先替玩家把锁打开，再进界面截图
                PlayerPrefs.SetInt("nailong.customUnlocked", 1);
                PlayerPrefs.Save();

                object scene = Instance("NailongFNAF.UI.PrototypeScene");
                Invoke(scene, "OpenTitle", null);
                Invoke(scene, "OnCustomClicked", null);
                Snap("07_custom");
                ReportCustom("自定义夜界面（进入时）");
            }
            else if (_step == 13 && elapsed > 17.4f)
            {
                _step = 14;
                object scene = Instance("NailongFNAF.UI.PrototypeScene");
                object config = Get(scene, "_customConfig");
                if (config != null)
                {
                    Invoke(config, "SetAll", new object[] { 20 });
                }

                Invoke(scene, "RefreshCustomLabels", null);
                Snap("08_custom20");
                ReportCustom("自定义夜界面（全 20）");
            }
            else if (_step == 14 && elapsed > 18.6f)
            {
                _step = 15;
                object scene = Instance("NailongFNAF.UI.PrototypeScene");
                Invoke(scene, "OnAchievementsClicked", null);
                Snap("09_achievements");
                ReportAchievements();
            }
            else if (_step == 15 && elapsed > 19.8f)
            {
                _step = 16;
                object scene = Instance("NailongFNAF.UI.PrototypeScene");
                Invoke(scene, "OnCustomStartClicked", null);

                object gm = Instance("NailongFNAF.Core.GameManager");
                object director = Instance("NailongFNAF.Gameplay.EnemyDirector");
                Debug.Log("SMOKE: 自定义夜开局 CustomActive = " +
                          (director != null ? Get(director, "CustomActive") : null) +
                          " EndlessMode = " + (gm != null ? Get(gm, "EndlessMode") : null) +
                          " 菜单开着 = " + Get(scene, "_menuOpen"));
            }
            else if (_step == 16 && elapsed > 21.5f)
            {
                _step = 17;

                // 彩蛋：两列 19 条，彩蛋未解锁应该是 ???
                object scene = Instance("NailongFNAF.UI.PrototypeScene");
                Invoke(scene, "OpenTitle", null);
                Invoke(scene, "OnAchievementsClicked", null);
                Snap("11_achv_locked");
                ReportAchievements();
            }
            else if (_step == 17 && elapsed > 22.7f)
            {
                _step = 18;

                // 解锁一条彩蛋，看名字会不会从 ??? 变回真名
                System.Type store = FindType("NailongFNAF.Gameplay.AchievementStore");
                store.GetMethod("Unlock").Invoke(null, new object[] { "door_keeper" });

                object scene = Instance("NailongFNAF.UI.PrototypeScene");
                Invoke(scene, "RefreshAchievementsView", null);
                Invoke(scene, "ShowAchievementFragment", new object[] { 8 });
                Snap("12_achv_unlocked");
                var achievementScroll = UnityEngine.Object.FindObjectOfType<UnityEngine.UI.ScrollRect>();
                if (achievementScroll != null)
                {
                    Canvas.ForceUpdateCanvases();
                    achievementScroll.verticalNormalizedPosition = 0f;
                    Canvas.ForceUpdateCanvases();
                    Debug.Log("SMOKE: 成就滚动到底 = " + achievementScroll.verticalNormalizedPosition
                        + " 内容高度 = " + achievementScroll.content.rect.height
                        + " 窗口高度 = " + achievementScroll.viewport.rect.height);
                }
                ReportAchievements();
            }
            else if (_step == 18 && elapsed > 24.0f)
            {
                _step = 19;

                // \u6b7b\u5728 5:50 \u4e4b\u540e\uff1a\u9a8c\u201c\u5dee\u4e00\u5206\u949f\u201d\u7684\u5f69\u86cb\u548c\u65c1\u767d
                object gm = Instance("NailongFNAF.Core.GameManager");
                object stats = Get(gm, "Stats");
                stats.GetType().GetMethod("MarkEnded", new[] { typeof(float), typeof(float) })
                    .Invoke(stats, new object[] { 85f * 5.9f, 85f });

                object scene = Instance("NailongFNAF.UI.PrototypeScene");
                Invoke(scene, "ShowLoseEnd", null);
                Snap("13_lose_egg");

                object body = Get(scene, "_endBodyText");
                Debug.Log("SMOKE: \u5931\u8d25\u7ed3\u7b97\u6587\u6848 = [" +
                          (body != null ? Get(body, "text") : null) + "]");
            }
            else if (_step == 19 && elapsed > 26.0f)
            {
                _step = 20;

                // 第 1 夜：奶包只引导。引导第一句要 18 秒才到，探针直接把她推快一点。
                object scene = Instance("NailongFNAF.UI.PrototypeScene");
                Invoke(scene, "OpenTitle", null);
                Invoke(scene, "OnNightClicked", new object[] { 0 });
                ForceNaibaoOffline(20f);
                Debug.Log("SMOKE: 第 1 夜已开局");
            }
            else if (_step == 20 && elapsed > 27.5f)
            {
                _step = 21;
                Snap("14_naibao_guide");
                ReportNaibao("引导");
            }
            else if (_step == 21 && elapsed > 28.5f)
            {
                _step = 22;

                // 第 2 夜：改手欠。直接把她的计时推到第一次出手之后。
                object scene = Instance("NailongFNAF.UI.PrototypeScene");
                Invoke(scene, "OpenTitle", null);
                Invoke(scene, "OnNightClicked", new object[] { 1 });
                ForceNaibaoOffline(31f);
                Debug.Log("SMOKE: 第 2 夜已开局");
            }
            else if (_step == 22 && elapsed > 30.2f)
            {
                _step = 23;
                Snap("15_naibao_mischief");
                ReportNaibao("手欠");
            }
            else if (_step == 23 && elapsed > 30.4f)
            {
                _step = 24;

                // 因她而死：先让她按住监控，再被抓住 —— 她该道歉
                object gm = Instance("NailongFNAF.Core.GameManager");
                var cue = new NaibaoCue
                {
                    Action = NaibaoAction.MonitorGrab,
                    Pose = NaibaoPose.Peek,
                    Line = "这个机位我替你看看……",
                    Seconds = 2.6f,
                };

                Invoke(gm, "ApplyNaibaoCue", new object[] { cue });
                Invoke(gm, "MarkCaught", null);

                if (gm != null)
                {
                    Debug.Log("SMOKE: 因奶包而死 道歉 = [" + Get(gm, "NaibaoApologyLine") + "]");
                }
            }
            else if (_step == 24 && elapsed > 31.8f)
            {
                _step = 25;
                Snap("16_naibao_apology");
                ReportNaibao("道歉");
            }
            else if (_step == 25 && elapsed > 33.0f)
            {
                _step = 26;

                // 桌上那部手机：重新开第 1 夜，并把手机摆到正前方 ——
                // 冒烟里没法移动鼠标，只能靠改位置让「转到它」这件事必然发生。
                SetPhonePosition(new Vector3(0f, 1.30f, 0f));

                object scene = Instance("NailongFNAF.UI.PrototypeScene");
                Invoke(scene, "OpenTitle", null);
                Invoke(scene, "OnNightClicked", new object[] { 0 });

                Debug.Log("SMOKE: 第 1 夜重开，手机摆到正前方");
            }
            else if (_step == 26 && elapsed > 34.0f)
            {
                _step = 27;
                SetPhonePosition(new Vector3(0.24f, 1.02f, 0.00f));
            }
            else if (_step == 27 && elapsed > 34.8f)
            {
                _step = 28;
                SnapStraight("18_naibao_phone");
                ReportPhone();
            }
            else if (_step == 28 && elapsed > 35.6f)
            {
                _step = 29;

                object gm2 = Instance("NailongFNAF.Core.GameManager");
                // 第二个参数是 custom（反射调用不认默认值，必须显式给）
                object started2 = Invoke(gm2, "StartNaibaoCall", new object[] { 0, false });
                Debug.Log("SMOKE: 接通 = " + started2);
            }
            else if (_step == 29 && elapsed > 36.8f)
            {
                _step = 30;
                SnapStraight("17_naibao_call");
            }
            else if (_step == 30 && elapsed > 38.0f)
            {
                _step = 31;

                object gm = Instance("NailongFNAF.Core.GameManager");
                object call = gm != null ? Get(gm, "NaibaoCall") : null;
                object current = call != null ? Get(call, "Current") : null;

                Debug.Log("SMOKE: 通话面板 当前句 = [" +
                          (current != null ? Get(current, "Text") : null) + "] 序号 = " +
                          (call != null ? Get(call, "Index") : null) +
                          " 用电档 = " + (gm != null ? Invoke(gm, "CountUsageLevel", null) : null) +
                          " 手机隐藏 = " + PhoneHidden());

                ReportVoice();

                // 贝利亚之夜：切进模式，开局白送的「拉钩」三张卡应该立刻弹出来
                object gmBelial = Instance("NailongFNAF.Core.GameManager");
                Invoke(gmBelial, "SetBelial", new object[] { true });
                Invoke(gmBelial, "Restart", null);
                Debug.Log("SMOKE: 贝利亚之夜已开局");
            }
            else if (_step == 31 && elapsed > 40.0f)
            {
                _step = 32;
                SnapStraight("19_belial_support");
                ReportBelial("白送那次");
            }
            else if (_step == 32 && elapsed > 41.2f)
            {
                _step = 33;

                object gmBelial2 = Instance("NailongFNAF.Core.GameManager");
                object night = Get(gmBelial2, "Belial");
                object picked = Call(night, "Choose", new object[] { 0 });
                Call(gmBelial2, "ApplySupport", new object[] { picked });
                Canvas.ForceUpdateCanvases();
                Debug.Log("SMOKE: 选完「拉钩」后还等选择 = " + Get(night, "AwaitingChoice") +
                          " · 已拿 = " + Get(night, "Picks") +
                          " · timeScale = " + Time.timeScale);
                SnapStraight("20_belial_after_pick");
            }
            else if (_step == 33 && elapsed > 42.6f)
            {
                _step = 34;

                // 白送之后补的那一次正常三选一：再挑一张，局面应该解除暂停
                object gmBelial3 = Instance("NailongFNAF.Core.GameManager");
                object night2 = Get(gmBelial3, "Belial");
                object picked2 = Call(night2, "Choose", new object[] { 0 });
                Call(gmBelial3, "ApplySupport", new object[] { picked2 });
                Canvas.ForceUpdateCanvases();
                Debug.Log("SMOKE: 第二次挑完 · 还等选择 = " + Get(night2, "AwaitingChoice") +
                          " · 已拿 = " + Get(night2, "Picks") +
                          " · timeScale = " + Time.timeScale);
                SnapStraight("21_belial_resumed");
            }
            else if (_step == 34 && elapsed > 43.8f)
            {
                _step = 35;
                ReportBelial("两张都挑完之后");

                // 开始看贝利亚本人：先让模式进入第二阶段，再把 encounter 摆到左门外
                object gm = Instance("NailongFNAF.Core.GameManager");
                gm.GetType().GetMethod("SetBelial").Invoke(gm, new object[] { true });
                gm.GetType().GetMethod("Restart").Invoke(gm, null);

                // 前面几步可能已经被怪抓了，先把死亡状态和面板清掉再摆贝利亚
                gm.GetType().GetProperty("HasBeenCaught").GetSetMethod(true)
                    .Invoke(gm, new object[] { false });

                object scene = FindScene();
                Invoke(scene, "ClearJumpscare", null);
                object endView = Get(scene, "_endView");

                if (endView is GameObject endGo)
                {
                    endGo.SetActive(false);
                }

                // 画面冻住再摆贝利亚：不然怪会在截图前又把玩家吃了
                UnityEngine.Time.timeScale = 0f;
                var menuFlags = BindingFlags.NonPublic | BindingFlags.Instance;
                var shownField = scene.GetType().GetField("_endShown", menuFlags);

                if (shownField != null)
                {
                    shownField.SetValue(scene, false);
                }

                object night = Get(gm, "Belial");
                night.GetType().GetMethod("AdvanceHours").Invoke(night, new object[] { 6 });

                // 把开局那张白送的牌收起来，别挡着贝利亚
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                night.GetType().GetField("_offer", flags).SetValue(night, null);

                ForceBelial((GameManager)gm, false, 0);
            }
            else if (_step == 35 && elapsed > 45.2f)
            {
                _step = 36;
                SnapPan("22_belial_dark", -1f);
                object gm = Instance("NailongFNAF.Core.GameManager");
                object night = Get(gm, "Belial");
                object h = night.GetType().GetProperty("Hours").GetValue(night);
                object a = night.GetType().GetProperty("BelialActive").GetValue(night);
                Debug.Log("SMOKE: 贝利亚外观 小时 = " + h + " 已上场 = " + a +
                    " 在门口 = " + Get(gm, "BelialEncounter") + " 提示 = [" +
                    (Get(FindScene(), "_belialHint") != null ? Get(Get(FindScene(), "_belialHint"), "text") : "") + "]");
            }
            else if (_step == 36 && elapsed > 46.2f)
            {
                _step = 37;
                object gm = Instance("NailongFNAF.Core.GameManager");
                ForceBelial((GameManager)gm, true, 0);
            }
            else if (_step == 37 && elapsed > 47.4f)
            {
                _step = 38;
                SnapPan("23_belial_white", -1f);
            }
            else if (_step == 38 && elapsed > 48.6f)
            {
                // 截图是异步落盘的，多等一帧再退出，不然最后一张会丢
                _step = 39;
                Finish();
            }
        }

        /// <summary>找到运行中的 PrototypeScene。</summary>
        private static UnityEngine.Object FindScene()
        {
            return UnityEngine.Object.FindObjectOfType<PrototypeScene>();
        }

        /// <summary>把贝利亚之夜的当前状态打出来（小时 / 抽到第几张 / 桌上摆着哪些卡）。</summary>
        private void ReportBelial(string title)
        {
            object gm = Instance("NailongFNAF.Core.GameManager");
            object night = gm != null ? Get(gm, "Belial") : null;

            if (night == null)
            {
                Debug.Log("SMOKE: 贝利亚[" + title + "] 账本为空");
                return;
            }

            var titles = new List<string>();
            object offer = Get(night, "Offer");

            if (offer is Array cards)
            {
                foreach (object card in cards)
                {
                    titles.Add(card != null ? Get(card, "Title") as string : "?");
                }
            }

            Debug.Log("SMOKE: 贝利亚[" + title + "] 小时 = " + Get(night, "Hours") +
                      " 阶段 = " + Get(night, "Phase") +
                      " 已拿 = " + Get(night, "Picks") +
                      " 等选择 = " + Get(night, "AwaitingChoice") +
                      " 白送 = " + Get(night, "IsForcedOffer") +
                      " 桌上 = [" + string.Join(", ", titles.ToArray()) + "]" +
                      " 当前电量 = " + (gm != null ? Get(gm, "Power") == null ? "?" : Get(Get(gm, "Power"), "Level") : "?") +
                      " timeScale = " + Time.timeScale);
        }

        /// <summary>把手机各个 renderer 的世界包围盒打出来 —— 用来判断模型的长轴 / 朝向。</summary>
        private static void DumpPhoneBounds()
        {
            Transform[] all = Resources.FindObjectsOfTypeAll<Transform>();
            int phones = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name == "NaibaoPhone")
                {
                    phones++;
                    Debug.Log("SMOKE: 手机实例[" + phones + "] " + all[i].position.ToString("F3") +
                              " 缩放 " + all[i].lossyScale.ToString("F3") +
                              " 激活 " + all[i].gameObject.activeInHierarchy);
                }
            }

            GameObject go = GameObject.Find("NaibaoPhone");
            if (go == null)
            {
                Debug.Log("SMOKE: 找不到 NaibaoPhone 物体");
                return;
            }

            Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
            Debug.Log("SMOKE: 手机 renderer 数 = " + renderers.Length);

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                Bounds b = r.bounds;
                Camera cam = Camera.main;
                if (cam != null)
                {
                    Vector3 min = new Vector3(99999f, 99999f, 0f);
                    Vector3 max = new Vector3(-99999f, -99999f, 0f);
                    for (int c = 0; c < 8; c++)
                    {
                        Vector3 corner = b.center + new Vector3(
                            (c & 1) == 0 ? -b.extents.x : b.extents.x,
                            (c & 2) == 0 ? -b.extents.y : b.extents.y,
                            (c & 4) == 0 ? -b.extents.z : b.extents.z);
                        Vector3 sp = cam.WorldToScreenPoint(corner);
                        min = Vector3.Min(min, sp);
                        max = Vector3.Max(max, sp);
                    }

                    Debug.Log("SMOKE: 手机 屏幕矩形 = " + min.ToString("F0") + " .. " +
                              max.ToString("F0") + "（画布 " + Screen.width + "x" + Screen.height +
                              " fov " + cam.fieldOfView.ToString("F1") + "）");
                }

                Debug.Log("SMOKE: 手机 renderer[" + i + "] " + r.name +
                          " 可见 = " + r.isVisible +
                          " 材质 = " + (r.sharedMaterial != null ? r.sharedMaterial.shader.name : "null") +
                          " 中心 = " + r.bounds.center.ToString("F3") +
                          " 尺寸 = " + r.bounds.size.ToString("F3"));
            }
        }

        /// <summary>把手机挪到某个世界坐标（并同步已经生成的那一个）。</summary>
        private static void SetPhonePosition(Vector3 position)
        {
            NailongFNAF.UI.NaibaoPhoneProp.WorldPosition = position;

            object scene = Instance("NailongFNAF.UI.PrototypeScene");
            object prop = scene != null ? Get(scene, "_naibaoPhone") : null;
            object tr = prop != null ? Get(prop, "transform") : null;
            if (tr != null)
            {
                Invoke(tr, "set_position", new object[] { position });
            }
        }

        /// <summary>
        /// 截图前先把视角掰回正前方 —— 冒烟里鼠标不在手里，视角会停在最左 / 最右，
        /// 那样手机根本不在画面里。SetPan 是立即生效的，同一帧截就能拍到正面。
        /// </summary>
        private void SnapStraight(string name)
        {
            object scene = Instance("NailongFNAF.UI.PrototypeScene");
            object room = scene != null ? Get(scene, "_room") : null;
            if (room != null)
            {
                Invoke(room, "SetPan", new object[] { 0f });
            }

            Snap(name);
        }

        /// <summary>把视角摆到指定 pan 再截图（-1 最左，+1 最右）。</summary>
        private void SnapPan(string name, float pan)
        {
            object scene = Instance("NailongFNAF.UI.PrototypeScene");
            object room = scene != null ? Get(scene, "_room") : null;
            if (room != null)
            {
                Invoke(room, "SetPan", new object[] { pan });
            }

            Snap(name);
        }

        /// <summary>把手机转到指定欧拉角（冒烟里用来挑朝向）。</summary>
        private static void SetPhoneEuler(Vector3 euler)
        {
            object scene = Instance("NailongFNAF.UI.PrototypeScene");
            object prop = scene != null ? Get(scene, "_naibaoPhone") : null;
            object tr = prop != null ? Get(prop, "transform") : null;
            if (tr == null)
            {
                Debug.Log("SMOKE: 手机还没生成，没法转 " + euler);
                return;
            }

            Invoke(tr, "set_rotation", new object[] { Quaternion.Euler(euler) });
        }

        /// <summary>
        /// 配音这一组：能加载几条、抽几句看时长。
        /// 冒烟里听不到声音，所以只能证明「文件在、Resources 找得到、时长不是 0」。
        /// </summary>
        private static void ReportVoice()
        {
            string[] ids =
            {
                "guide_1", "usage_1", "monitor_1", "lightoff_1", "apology_1",
                "call_guide_first_1", "call_early_1", "call_late_3"
            };

            int loaded = 0;
            System.Text.StringBuilder lengths = new System.Text.StringBuilder();

            for (int i = 0; i < ids.Length; i++)
            {
                AudioClip clip = NailongFNAF.Audio.VoiceBank.Get(ids[i]);
                if (clip == null)
                {
                    lengths.Append(ids[i]).Append("=缺 ");
                    continue;
                }

                loaded++;
                lengths.Append(ids[i]).Append("=").Append(clip.length.ToString("F2")).Append("s ");
            }

            Debug.Log("SMOKE: 配音 抽查 " + ids.Length + " 条 / 加载到 " + loaded +
                      " 条 / 缓存 " + NailongFNAF.Audio.VoiceBank.CachedCount + " 条 · " + lengths);

            object scene = Instance("NailongFNAF.UI.PrototypeScene");
            object src = scene != null ? Get(scene, "_voiceSource") : null;
            object clipNow = src != null ? Get(src, "clip") : null;
            Debug.Log("SMOKE: 当前播放的配音 = " + (clipNow != null ? Get(clipNow, "name") : "无") +
                      " · 是否在播 = " + (src != null ? Get(src, "isPlaying") : "无"));

            // 主动播一句：证明 AudioSource 真的建出来了、clip 真的挂上去并按下了播放
            object played = scene != null ? Invoke(scene, "PlayVoice", new object[] { "guide_1" }) : null;
            Debug.Log("SMOKE: 主动播放 guide_1 返回 = " + played +
                      " · isPlaying = " + (src != null ? Get(src, "isPlaying") : "无") +
                      " · 音量 = " + (src != null ? Get(src, "volume") : "无"));
        }

        /// <summary>手机这一组：在不在、亮没亮、提示字出来没有。</summary>
        private static void ReportPhone()
        {
            DumpPhoneBounds();

            object scene = Instance("NailongFNAF.UI.PrototypeScene");
            object prop = scene != null ? Get(scene, "_naibaoPhone") : null;
            object propGo = prop != null ? Get(prop, "gameObject") : null;
            object hint = scene != null ? Get(scene, "_naibaoPhoneHint") : null;
            object hintGo = hint != null ? Get(hint, "gameObject") : null;

            object tr2 = prop != null ? Get(prop, "transform") : null;
            Debug.Log("SMOKE: 手机位置 = " +
                      (tr2 != null ? Get(tr2, "position") : null) +
                      " 缩放 = " + (tr2 != null ? Get(tr2, "lossyScale") : null) +
                      " 相机 = " + (Camera.main != null ? Camera.main.transform.position.ToString() : "无") +
                      " 朝向 = " + (Camera.main != null ? Camera.main.transform.forward.ToString() : "无"));

            Debug.Log("SMOKE: 手机 生成 = " + (prop != null) +
                      " 在场 = " + (propGo != null && (bool)Get(propGo, "activeSelf")) +
                      " 亮起 = " + (scene != null ? Get(scene, "_naibaoPhoneHighlighted") : null) +
                      " 提示 = [" + (hint != null ? Get(hint, "text") : null) + "]" +
                      " 提示可见 = " + (hintGo != null && (bool)Get(hintGo, "activeSelf")));
        }

        /// <summary>把奶包的计时直接推 <paramref name="seconds"/> 秒，再把到点的出场应用上。</summary>
        /// <summary>通话面板打开时，桌上那部手机应当是藏起来的。</summary>
        private static bool PhoneHidden()
        {
            object scene = Instance("NailongFNAF.UI.PrototypeScene");
            object button = scene != null ? Get(scene, "_naibaoPhone") : null;
            object go = button != null ? Get(button, "gameObject") : null;
            return go != null && !(bool)Get(go, "activeSelf");
        }

        private void ForceNaibaoOffline(float seconds)
        {
            object gm = Instance("NailongFNAF.Core.GameManager");
            object naibao = gm != null ? Get(gm, "Naibao") : null;
            if (naibao == null)
            {
                Debug.LogError("SMOKE: 这一夜没有奶包");
                return;
            }

            Invoke(naibao, "Tick", new object[] { seconds });
            Invoke(gm, "TickNaibao", new object[] { 0.016f });
        }

        private void ReportNaibao(string title)
        {
            object gm = Instance("NailongFNAF.Core.GameManager");
            object naibao = gm != null ? Get(gm, "Naibao") : null;
            object scene = Instance("NailongFNAF.UI.PrototypeScene");
            object line = Get(scene, "_naibaoLine");
            object group = Get(scene, "_naibaoGroup");
            object cue = gm != null ? Get(gm, "LastNaibaoCue") : null;

            string cueText = cue != null ? Get(cue, "Line") as string : null;
            string pose = cue != null ? Get(cue, "Pose")?.ToString() : null;

            Debug.Log("SMOKE: 奶包(" + title + ") 在场 = " + (naibao != null) +
                      " 引导 = " + (naibao != null ? Get(naibao, "IsGuide") : null) +
                      " 捣乱次数 = " + (naibao != null ? Get(naibao, "MischiefCount") : null) +
                      " 姿势 = " + pose +
                      " 台词 = [" + cueText + "]" +
                      " 界面台词 = [" + (line != null ? Get(line, "text") : null) + "]" +
                      " 组透明 = " + (group != null ? Get(group, "alpha") : null));
        }

        private void DriveActions()
        {
            object gm = Instance("NailongFNAF.Core.GameManager");
            if (gm == null)
            {
                Debug.LogError("SMOKE: 找不到 GameManager");
                return;
            }

            for (int i = 0; i < 3; i++)
            {
                Invoke(gm, "ToggleLeftDoor", null);
                Invoke(gm, "ToggleRightDoor", null);
                Invoke(gm, "SetCamera", new object[] { (i % 4) + 1 });
                Invoke(gm, "SetFlashlight", new object[] { true });
                Invoke(gm, "SetFlashlight", new object[] { false });
            }

            // 永夜模式的补给是「固定周期结算」：把周期临时压到 1 秒，
            // 让随后的真实 Update 路径自己结算（Tick → Power.Grant → LastGain），
            // 这样验到的是玩家真会看到的节拍，而不是直接调 Grant。
            object recharge = Get(gm, "Recharge");
            if (recharge != null)
            {
                SetBackingField(recharge, "<Interval>k__BackingField", 1f);
                Debug.Log("SMOKE: 结算周期临时压到 1 秒，动静 " + Get(recharge, "Noise"));
            }

            Debug.Log("SMOKE: 已模拟 3 轮门/监控/手电筒操作");
        }

        private void ReportCustom(string title)
        {
            object scene = Instance("NailongFNAF.UI.PrototypeScene");
            object config = Get(scene, "_customConfig");
            string line = "SMOKE: " + title + " 数值 = ";
            if (config != null)
            {
                System.Reflection.MethodInfo getLevel = config.GetType().GetMethod("GetLevel");
                System.Type idType = getLevel.GetParameters()[0].ParameterType;
                foreach (string code in new[] { "niulai", "daishu", "lulu", "nailong" })
                {
                    object id = System.Enum.Parse(idType, code, true);
                    line += code + ":" + getLevel.Invoke(config, new[] { id }) + " ";
                }
            }

            Debug.Log(line);
        }

        private void ReportAchievements()
        {
            object scene = Instance("NailongFNAF.UI.PrototypeScene");
            object count = Get(scene, "_achvCountText");
            object firstTitle = null;
            object titles = Get(scene, "_achvLineTexts");
            if (titles is System.Array && ((System.Array)titles).Length > 0)
            {
                firstTitle = ((System.Array)titles).GetValue(0);
            }

            object eggTitle = null;
            if (titles is System.Array && ((System.Array)titles).Length > 9)
            {
                eggTitle = ((System.Array)titles).GetValue(9);
            }

            Debug.Log("SMOKE: 成就计数 = [" + (count != null ? Get(count, "text") : null) +
                      "] 第一条 = [" + (firstTitle != null ? Get(firstTitle, "text") : null) +
                      "] 第 10 条 = [" + (eggTitle != null ? Get(eggTitle, "text") : null) + "]");
        }

        private void ReportPause()
        {
            object scene = Instance("NailongFNAF.UI.PrototypeScene");
            object pauseView = Get(scene, "_pauseView");
            object info = Get(scene, "_pauseInfoText");
            Debug.Log("SMOKE: 暂停 timeScale = " + Time.timeScale +
                      " AudioListener.pause = " + AudioListener.pause +
                      " 暂停面板 = " + (pauseView != null ? Get(pauseView, "activeSelf") : null) +
                      " 面板文案 = [" + (info != null ? Get(info, "text") : null) + "]");
        }

        private void ReportSave()
        {
            string raw = PlayerPrefs.GetString("nailong.eternalSave", string.Empty);
            Debug.Log("SMOKE: 存档长度 = " + raw.Length + " 内容 = [" + raw + "]");

            object gm = Instance("NailongFNAF.Core.GameManager");
            object clock = gm != null ? Get(gm, "Clock") : null;
            object power = gm != null ? Get(gm, "Power") : null;
            Debug.Log("SMOKE: 存档时 已撑秒 = " + (clock != null ? Get(clock, "ElapsedSeconds") : null) +
                      " 电量 = " + (power != null ? Get(power, "Level") : null));
        }

        private void ReportEndless()
        {
            object gm = Instance("NailongFNAF.Core.GameManager");
            if (gm == null)
            {
                Debug.LogError("SMOKE: 找不到 GameManager");
                return;
            }

            object clock = Get(gm, "Clock");
            object power = Get(gm, "Power");
            object recharge = Get(gm, "Recharge");

            Debug.Log("SMOKE: EndlessMode = " + Get(gm, "EndlessMode") +
                      " LastGain = " + Get(gm, "LastGain"));
            if (clock != null)
            {
                Debug.Log("SMOKE: Clock 点=" + Get(clock, "CurrentHour") +
                          " 夜内点=" + Get(clock, "HourOfNight") +
                          " 夜数=" + Get(clock, "NightNumber") +
                          " 天亮=" + Get(clock, "IsNightComplete"));
            }
            if (power != null)
            {
                Debug.Log("SMOKE: 电量 = " + Get(power, "Level"));
            }
            if (recharge != null)
            {
                Debug.Log("SMOKE: 记点 = " + Get(recharge, "Credits") +
                          " 结算次数 = " + Get(recharge, "Settlements") +
                          " 累计补电 = " + Get(recharge, "TotalGranted") +
                          " 记账次数 = " + Get(recharge, "ActionsRegistered") +
                          " 动静 = " + Get(recharge, "Noise"));
            }

            object scene = Instance("NailongFNAF.UI.PrototypeScene");
            object endlessText = Get(scene, "_endlessText");
            if (endlessText == null)
            {
                Debug.LogError("SMOKE: 找不到 HUD 永夜行 _endlessText");
                return;
            }

            object textGo = Get(endlessText, "gameObject");
            Debug.Log("SMOKE: HUD 永夜行 = [" + Get(endlessText, "text") + "] active=" +
                      (textGo != null ? Get(textGo, "activeSelf") : null));
        }

        private void ReportNormal()
        {
            object gm = Instance("NailongFNAF.Core.GameManager");
            object clock = Get(gm, "Clock");
            object scene = Instance("NailongFNAF.UI.PrototypeScene");
            object endlessText = Get(scene, "_endlessText");
            object textGo = Get(endlessText, "gameObject");

            Debug.Log("SMOKE: 普通夜 EndlessMode = " + Get(gm, "EndlessMode") +
                      " 夜内点 = " + (clock != null ? Get(clock, "CurrentHour") : null) +
                      " 结束 = " + (clock != null ? Get(clock, "IsNightComplete") : null) +
                      " HUD 永夜行 active = " + (textGo != null ? Get(textGo, "activeSelf") : null));
        }

        private void ReportEndPanel()
        {
            object scene = Instance("NailongFNAF.UI.PrototypeScene");
            Debug.Log("SMOKE: 结算标题 = [" + Get(scene, "_endTitleText") + "]");
            Debug.Log("SMOKE: 结算标题文案 = [" + Get(Get(scene, "_endTitleText"), "text") + "]");
            Debug.Log("SMOKE: 结算正文 = [" + Get(Get(scene, "_endBodyText"), "text") + "]");
            Debug.Log("SMOKE: 结算主按钮 = [" + Get(Get(scene, "_endPrimaryLabel"), "text") + "]");
        }

        /// <summary>冒烟里没法真等到第 6 小时，直接把贝利亚摆到门口看一眼外观。</summary>
        private static void ForceBelial(GameManager gm, bool white, int side)
        {
            object enc = Get(gm, "BelialEncounter");

            if (enc == null)
            {
                return;
            }

            var t = enc.GetType();
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            t.GetField("_atDoor", flags).SetValue(enc, true);
            t.GetField("_retreating", flags).SetValue(enc, false);
            t.GetField("_kill", flags).SetValue(enc, false);
            t.GetField("_flashes", flags).SetValue(enc, 3);
            t.GetField("_side", flags).SetValue(enc, side == 0 ? BelialSide.Left : BelialSide.Right);
            t.GetField("_whiteLeft", flags).SetValue(enc, white ? 0.85f : 0f);
        }

        private void Finish()
        {            for (int i = 0; i < _errors.Count && i < 12; i++)
            {
                Debug.LogError("SMOKE-ERROR " + i + ": " + _errors[i]);
            }

            Debug.Log("SMOKE: 完成，错误数 = " + _errors.Count);
#if UNITY_EDITOR
            EditorApplication.Exit(_errors.Count == 0 ? 0 : 2);
#endif
        }

        private void Snap(string name)
        {
            string path = Path.Combine(_outDir, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log("SMOKE: 截图 -> " + path);
        }

        private void DumpScene(string label)
        {
            object scene = Instance("NailongFNAF.UI.PrototypeScene");
            object gm = Instance("NailongFNAF.Core.GameManager");
            Debug.Log("SMOKE: " + label + " PrototypeScene=" + (scene != null) +
                      " GameManager=" + (gm != null) +
                      " 相机数=" + Camera.allCamerasCount +
                      " 分辨率=" + Screen.width + "x" + Screen.height);
        }

        private static Type FindType(string fullName)
        {
            Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < asms.Length; i++)
            {
                Type t = asms[i].GetType(fullName, false);
                if (t != null)
                {
                    return t;
                }
            }

            return null;
        }

        private static object Instance(string fullName)
        {
            Type t = FindType(fullName);
            if (t == null)
            {
                return null;
            }

            PropertyInfo pi = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            if (pi != null)
            {
                object viaProperty = pi.GetValue(null);
                if (viaProperty != null)
                {
                    return viaProperty;
                }
            }

            return UnityEngine.Object.FindObjectOfType(t);
        }

        private static object Get(object target, string name)
        {
            if (target == null)
            {
                return null;
            }

            Type t = target.GetType();
            PropertyInfo pi = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (pi != null)
            {
                return pi.GetValue(target);
            }

            FieldInfo fi = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return fi != null ? fi.GetValue(target) : null;
        }

        private static bool Invoke(object target, string name, object[] args)
        {
            if (target == null)
            {
                return false;
            }

            MethodInfo mi = target.GetType().GetMethod(
                name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (mi == null)
            {
                Debug.LogError("SMOKE: 找不到方法 " + name);
                return false;
            }

            mi.Invoke(target, args);
            return true;
        }

        /// <summary>和 <see cref="Invoke"/> 一样，但把返回值拿出来（选卡这类要拿对象的用它）。</summary>
        private static object Call(object target, string name, object[] args)
        {
            if (target == null)
            {
                return null;
            }

            MethodInfo mi = target.GetType().GetMethod(
                name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (mi == null)
            {
                Debug.LogError("SMOKE: 找不到方法 " + name);
                return null;
            }

            return mi.Invoke(target, args);
        }

        /// <summary>改自动属性的幕后字段（探针专用，比如把结算周期临时压短）。</summary>
        private static void SetBackingField(object target, string fieldName, object value)
        {
            if (target == null)
            {
                return;
            }

            FieldInfo fi = target.GetType().GetField(
                fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (fi != null)
            {
                fi.SetValue(target, value);
            }
        }

    }
}
#endif
