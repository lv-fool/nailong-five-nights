using NailongFNAF.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NailongFNAF.EditorTools
{
    /// <summary>
    /// 一键把当前场景配成可玩原型：往场景里放一个挂着 PrototypeScene 的空物体，
    /// 剩下的 UI 全部由脚本在运行时生成。
    ///
    /// 素材还是占位色块，先跑通玩法；真图放进 Assets/Art 后再替换。
    /// </summary>
    public static class PrototypeSetup
    {
        [MenuItem("奶龙/4. 搭建可玩原型", false, 13)]
        public static void BuildPrototype()
        {
            var scene = EditorSceneManager.GetActiveScene();

            if (Object.FindObjectOfType<PrototypeScene>() != null)
            {
                EditorUtility.DisplayDialog(
                    "原型已存在",
                    "当前场景里已经有一个 PrototypeScene 了，直接按 Play 就能跑。",
                    "知道了");
                return;
            }

            var go = new GameObject("Prototype");
            go.AddComponent<PrototypeScene>();

            Undo.RegisterCreatedObjectUndo(go, "搭建可玩原型");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[奶龙] 原型已就绪：按 Play 运行。鼠标移到屏幕左右边缘转头，底部按钮开关门 / 灯 / 监控，R 键重开一夜。");
        }
    }
}
