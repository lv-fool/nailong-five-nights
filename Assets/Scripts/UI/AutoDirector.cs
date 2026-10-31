using System.Collections;
using UnityEngine;
using NailongFNAF.Core;
using NailongFNAF.Gameplay;

namespace NailongFNAF.UI
{
    /// <summary>
    /// 宣传片自动演出 —— 按下开始后，60 秒内自动走完全部镜头。
    ///
    /// **为什么要在游戏内部走**：从外部逐条发 MCP 指令驱动时，每次调用都会
    /// 让 Unity 编辑器失焦，而 ffmpeg 的 gdigrab 抓的是桌面 —— 窗口一失焦
    /// 就录不到画面（试过，录出来 0 字节）。
    /// 所以把编排整个放进协程，外部只需要「开录」和「等着」两件事。
    ///
    /// 用法：挂在任意 GameObject 上，调 Begin()。仅调试/录制用。
    /// </summary>
    public class AutoDirector : MonoBehaviour
    {
        private PrototypeScene _scene;
        private GameManager _gm;
        private bool _running;

        /// <summary>开始演出。已在跑就忽略。</summary>
        public void Begin()
        {
            if (_running)
            {
                return;
            }

            StartCoroutine(Play());
        }

        private IEnumerator Play()
        {
            _running = true;

            _scene = FindObjectOfType<PrototypeScene>();
            _gm = GameManager.Instance;

            if (_scene == null || _gm == null)
            {
                Debug.LogWarning("[AutoDirector] 找不到 PrototypeScene / GameManager");
                _running = false;
                yield break;
            }

            var f = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var t = typeof(PrototypeScene);

            // 清干净，从标题页开始
            t.GetMethod("ClearJumpscare", f).Invoke(_scene, null);
            _gm.SetBelial(false);
            _gm.SetEndless(false);
            _gm.Restart();
            t.GetMethod("OpenTitle", f).Invoke(_scene, null);

            var debugGroup = t.GetField("_debugGroup", f).GetValue(_scene) as CanvasGroup;
            if (debugGroup != null)
            {
                debugGroup.alpha = 0f;
                debugGroup.blocksRaycasts = false;
            }

            // ── 0~7s：标题页 ──
            yield return new WaitForSeconds(7f);

            // ── 7s：选夜页 ──
            ClickMenuButton("故事模式");
            yield return new WaitForSeconds(3.2f);

            // ── 10.5s：第 1 夜 ──
            ClickMenuButton("第 1 夜");
            yield return new WaitForSeconds(5.2f);

            // ── 16s：正前方（挂断奶包，免得挡画面）──
            var endCall = _gm.GetType().GetMethod("EndNaibaoCall");
            if (endCall != null)
            {
                endCall.Invoke(_gm, null);
            }

            _gm.SetView(ViewSide.Front);
            yield return new WaitForSeconds(3.8f);

            // ── 20s：左转 ──
            _gm.SetView(ViewSide.Left);
            yield return new WaitForSeconds(2.8f);

            // ── 23s：右转 ──
            _gm.SetView(ViewSide.Right);
            yield return new WaitForSeconds(2.8f);

            // ── 26s：开监控 ──
            _gm.SetView(ViewSide.Front);
            SetMonitorIntent(true);
            yield return new WaitForSeconds(2.8f);

            // ── 逐个机位 ──
            int[] cams = { 0, 3, 6, 10, 9 };
            foreach (int cam in cams)
            {
                _gm.SetCamera(cam);
                yield return new WaitForSeconds(2.4f);
            }

            // ── 关监控 ──
            SetMonitorIntent(false);
            _gm.SetView(ViewSide.Front);
            yield return new WaitForSeconds(2.2f);

            // ── 停电 ──
            var powerProp = _gm.GetType().GetProperty("Power");
            if (powerProp != null)
            {
                var power = powerProp.GetValue(_gm);
                var setLevel = power.GetType().GetMethod("SetLevel");
                if (setLevel != null)
                {
                    setLevel.Invoke(power, new object[] { 0f });
                }
            }

            yield return new WaitForSeconds(6.5f);

            // ── 跳杀 ──
            t.GetMethod("DebugTriggerJumpscare", f)
                .Invoke(_scene, new object[] { EnemyId.Nailong });

            yield return new WaitForSeconds(4.8f);

            // ── 回标题收尾 ──
            t.GetMethod("ClearJumpscare", f).Invoke(_scene, null);
            t.GetMethod("OpenTitle", f).Invoke(_scene, null);

            _running = false;
            Debug.Log("[AutoDirector] 演出结束");
        }

        private void ClickMenuButton(string label)
        {
            var f = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var menuRoot = typeof(PrototypeScene).GetField("_menuRoot", f)
                .GetValue(_scene) as GameObject;

            if (menuRoot == null)
            {
                return;
            }

            foreach (var button in menuRoot.GetComponentsInChildren<UnityEngine.UI.Button>(true))
            {
                var text = button.GetComponentInChildren<UnityEngine.UI.Text>();
                if (text == null)
                {
                    continue;
                }

                if (text.text == label || text.text.Contains(label))
                {
                    button.onClick.Invoke();
                    return;
                }
            }
        }

        private void SetMonitorIntent(bool up)
        {
            var f = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var field = _gm.GetType().GetField("_monitorPlayerIntent", f);
            if (field != null)
            {
                field.SetValue(_gm, up);
            }
        }
    }
}
