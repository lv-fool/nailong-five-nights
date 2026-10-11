using NailongFNAF.Audio;
using NailongFNAF.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NailongFNAF.EditorTools
{
    /// <summary>
    /// 一键把 Assets/Art 与 Assets/Audio 里的素材接到场景组件上。
    ///
    /// 素材换新之后重跑一次这个菜单即可 —— 不用手工往 Inspector 里拖。
    /// 素材缺失时对应字段留空，运行时自动退回色块占位，不会报错。
    /// </summary>
    public static class AssetBinder
    {
        private const string BackgroundDir = "Assets/Art/Backgrounds/";
        private const string UiDir = "Assets/Art/UI/";

        [MenuItem("奶龙/5. 接入素材", false, 14)]
        public static void Bind()
        {
            var scene = EditorSceneManager.GetActiveScene();

            var prototype = Object.FindObjectOfType<PrototypeScene>();
            if (prototype == null)
            {
                EditorUtility.DisplayDialog("还没有原型",
                    "当前场景里没有 PrototypeScene。先跑一次「奶龙 → 4. 搭建可玩原型」。",
                    "知道了");
                return;
            }

            // 图片要按 Sprite 导入，UI 才能直接用
            string[] images =
            {
                "bg_office_front", "bg_office_left_01", "bg_office_right_01",
                "ui_door_left_open", "ui_door_left_closed",
                "ui_door_right_open", "ui_door_right_closed"
            };

            foreach (var name in images)
            {
                EnsureSpriteImport(FindImage(name));
            }

            AssetDatabase.Refresh();

            prototype.SetArtwork(
                LoadSprite("bg_office_front"),
                LoadSprite("bg_office_left_01"),
                LoadSprite("bg_office_right_01"),
                LoadSprite("ui_door_left_open"),
                LoadSprite("ui_door_left_closed"),
                LoadSprite("ui_door_right_open"),
                LoadSprite("ui_door_right_closed"));

            var audio = Object.FindObjectOfType<AudioManager>();
            if (audio == null)
            {
                var go = new GameObject("AudioManager");
                audio = go.AddComponent<AudioManager>();
                Undo.RegisterCreatedObjectUndo(go, "创建 AudioManager");
            }

            audio.SetClips(
                LoadClip("Assets/Audio/Ambient/amb_fan_loop.wav"),
                LoadClip("Assets/Audio/Ambient/amb_electric_hum.wav"),
                LoadClip("Assets/Audio/SFX/sfx_door_close.ogg"),
                LoadClip("Assets/Audio/SFX/sfx_door_open.ogg"),
                LoadClip("Assets/Audio/SFX/sfx_light_on.ogg"),
                LoadClip("Assets/Audio/SFX/sfx_light_off.ogg"),
                LoadClip("Assets/Audio/SFX/sfx_camera_switch.ogg"),
                LoadClip("Assets/Audio/SFX/sfx_footstep.ogg"),
                LoadClip("Assets/Audio/SFX/sfx_power_out.ogg"));

            EditorUtility.SetDirty(prototype);
            EditorUtility.SetDirty(audio);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[奶龙] 素材已接入：背景 3 张、门片 4 张、音频 9 条。按 Play 看效果。");
        }

        private static string FindImage(string baseName)
        {
            string[] dirs = { BackgroundDir, UiDir };
            string[] exts = { ".png", ".jpg", ".jpeg" };

            foreach (var dir in dirs)
            {
                foreach (var ext in exts)
                {
                    string path = dir + baseName + ext;
                    if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
                    {
                        return path;
                    }
                }
            }

            return null;
        }

        private static void EnsureSpriteImport(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null || importer.textureType == TextureImporterType.Sprite)
            {
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.SaveAndReimport();
        }

        private static Sprite LoadSprite(string baseName)
        {
            string path = FindImage(baseName);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static AudioClip LoadClip(string path)
        {
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }
    }
}
