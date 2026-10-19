using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace NailongFNAF.Diagnostics
{
#if UNITY_EDITOR
    /// <summary>
    /// Play 模式冒烟探针：只在编辑器里存在，且只有命令行带 -playSmoke 时才自动装到场景里。
    /// 作用是把「进 Play → 截标题页 → 点无尽模式 → 模拟操作 → 截 HUD → 打印状态 → 退出」
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
            else if (_step == 4 && elapsed > 7.0f)
            {
                _step = 5;
                object scene = Instance("NailongFNAF.UI.PrototypeScene");
                Debug.Log("SMOKE: 直接走失败结算（无尽分支）= " +
                          Invoke(scene, "ShowLoseEnd", null));
            }
            else if (_step == 5 && elapsed > 8.5f)
            {
                _step = 6;
                Snap("05_endless_end");
                ReportEndPanel();
            }
            else if (_step == 6 && elapsed > 9.5f)
            {
                _step = 7;
                object scene = Instance("NailongFNAF.UI.PrototypeScene");
                Invoke(scene, "OnStartClicked", null);
                Invoke(scene, "OnNightClicked", new object[] { 0 });
                Debug.Log("SMOKE: 已切到普通夜晚 1");
            }
            else if (_step == 7 && elapsed > 11.0f)
            {
                _step = 8;
                Snap("04_normal_night");
                ReportNormal();
            }
            else if (_step == 8 && elapsed > 13.0f)
            {
                _step = 9;
                Finish();
            }
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

            Debug.Log("SMOKE: 已模拟 3 轮门/监控/手电筒操作");
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
                          " 记账次数 = " + Get(recharge, "ActionsRegistered"));
            }

            object scene = Instance("NailongFNAF.UI.PrototypeScene");
            object endlessText = Get(scene, "_endlessText");
            if (endlessText == null)
            {
                Debug.LogError("SMOKE: 找不到 HUD 无尽行 _endlessText");
                return;
            }

            object textGo = Get(endlessText, "gameObject");
            Debug.Log("SMOKE: HUD 无尽行 = [" + Get(endlessText, "text") + "] active=" +
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
                      " HUD 无尽行 active = " + (textGo != null ? Get(textGo, "activeSelf") : null));
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
    }
}
#endif
