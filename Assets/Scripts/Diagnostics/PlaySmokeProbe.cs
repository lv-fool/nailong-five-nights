using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
            _outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".setup", "play"));
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
                Snap("12_achv_unlocked");
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
            else if (_step == 23 && elapsed > 31.6f)
            {
                _step = 24;
                Finish();
            }
        }

        /// <summary>把奶包的计时直接推 <paramref name="seconds"/> 秒，再把到点的出场应用上。</summary>
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
