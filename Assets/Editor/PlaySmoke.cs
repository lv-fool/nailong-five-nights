using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NailongFNAF.EditorTools
{
    /// <summary>
    /// 命令行入口：打开 Main 场景并进入 Play 模式。
    /// 真正的验证动作由 PlaySmokeProbe（Assets/Scripts/Diagnostics）在游戏侧完成，
    /// 它只在命令行带 -playSmoke 时自动装载。
    /// 用法：Unity.exe -projectPath "..." -playSmoke -executeMethod NailongFNAF.EditorTools.PlaySmoke.Run -logFile "..."
    /// </summary>
    public static class PlaySmoke
    {
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            Debug.Log("SMOKE: 场景已打开，请求进入 Play 模式");
            EditorApplication.EnterPlaymode();
        }
    }
}
