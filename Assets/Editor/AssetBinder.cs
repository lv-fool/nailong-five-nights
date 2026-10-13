using System.Collections.Generic;
using NailongFNAF.Audio;
using NailongFNAF.Gameplay;
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
        private const string MonitoringDir = "Assets/Art/Monitoring/";
        private const string CharacterDir = "Assets/Art/Characters/";

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
                "ui_door_right_open", "ui_door_right_closed",
                "mon_1a", "mon_1b", "mon_1c",
                "mon_2a", "mon_2b", "mon_3",
                "mon_4a", "mon_4b", "mon_5", "mon_6", "mon_7",
                "char_nailong_idle", "char_nailong_stare", "char_nailong_jumpscare",
                "char_nailong_blackout",
                "char_niulai_idle", "char_niulai_stare", "char_niulai_jumpscare",
                "char_daishu_idle", "char_daishu_stare", "char_daishu_jumpscare",
                "char_lulu_idle", "char_lulu_stare", "char_lulu_peek",
                "char_lulu_startled", "char_lulu_angry",
                "char_lulu_jumpscare"
            };

            foreach (var name in images)
            {
                EnsureSpriteImport(FindImage(name));
            }

            AssetDatabase.Refresh();

            var characters = LoadCharacters();

            prototype.SetArtwork(
                LoadSprite("bg_office_front"),
                LoadSprite("bg_office_left_01"),
                LoadSprite("bg_office_right_01"),
                LoadSprite("ui_door_left_open"),
                LoadSprite("ui_door_left_closed"),
                LoadSprite("ui_door_right_open"),
                LoadSprite("ui_door_right_closed"),
                LoadMonitorFrames(),
                characters);

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
                LoadClip("Assets/Audio/SFX/sfx_power_out.ogg"),
                LoadClip("Assets/Audio/SFX/sfx_power_out_music.wav"));

            EditorUtility.SetDirty(prototype);
            EditorUtility.SetDirty(audio);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[奶龙] 素材已接入：背景 3 张、门片 4 张、监控画面 11 张、"
                + "角色立绘 " + characters.Length + " 套、音频 10 条。按 Play 看效果。");
        }

        /// <summary>
        /// 按机位顺序读监控画面 —— 文件名照官方 CAM 编号，
        /// **顺序必须与 PrototypeScene.CameraNames 一致**。
        /// 缺哪张哪张留空，运行时退回色块。
        /// </summary>
        private static Sprite[] LoadMonitorFrames()
        {
            string[] names =
            {
                "mon_1a", "mon_1b", "mon_1c",
                "mon_2a", "mon_2b", "mon_3",
                "mon_4a", "mon_4b",
                "mon_5", "mon_6", "mon_7"
            };

            var frames = new Sprite[names.Length];

            for (int i = 0; i < names.Length; i++)
            {
                frames[i] = LoadSprite(names[i]);
            }

            return frames;
        }

        /// <summary>
        /// 按「代号 × 状态」读角色立绘。四个角色里哪几个有素材就收哪几个 ——
        /// 缺的运行时自动退回色块，不会报错。
        /// </summary>
        private static PrototypeScene.CharacterArt[] LoadCharacters()
        {
            var list = new List<PrototypeScene.CharacterArt>();

            foreach (var id in EnemyIds.All)
            {
                string code = id.Code();

                var idle = LoadSprite("char_" + code + "_idle");
                var stare = LoadSprite("char_" + code + "_stare");
                var peek = LoadSprite("char_" + code + "_peek");
                var startled = LoadSprite("char_" + code + "_startled");
                var angry = LoadSprite("char_" + code + "_angry");
                var jumpscare = LoadSprite("char_" + code + "_jumpscare");
                var blackout = LoadSprite("char_" + code + "_blackout");

                if (idle == null && stare == null && peek == null && startled == null
                    && angry == null && jumpscare == null && blackout == null)
                {
                    continue;
                }

                list.Add(new PrototypeScene.CharacterArt
                {
                    Id = id,
                    Idle = idle,
                    Stare = stare,
                    Peek = peek,
                    Startled = startled,
                    Angry = angry,
                    Jumpscare = jumpscare,
                    Blackout = blackout
                });
            }

            return list.ToArray();
        }

        private static string FindImage(string baseName)
        {
            string[] dirs = { BackgroundDir, UiDir, MonitoringDir, CharacterDir };
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

        /// <summary>
        /// 把图片调成 UI 能直接用的 Sprite。
        ///
        /// 立绘与门片都带 alpha，且会压在暗背景上 —— 所以关掉压缩与 mipmap，
        /// 否则透明边缘会压出色块，暗场里一眼就能看出来。
        /// </summary>
        private static void EnsureSpriteImport(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            bool dirty = false;

            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                dirty = true;
            }

            if (importer.spriteImportMode != SpriteImportMode.Single)
            {
                importer.spriteImportMode = SpriteImportMode.Single;
                dirty = true;
            }

            if (!importer.alphaIsTransparency)
            {
                importer.alphaIsTransparency = true;
                dirty = true;
            }

            if (importer.mipmapEnabled)
            {
                importer.mipmapEnabled = false;
                dirty = true;
            }

            if (importer.maxTextureSize < 2048)
            {
                importer.maxTextureSize = 2048;
                dirty = true;
            }

            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                dirty = true;
            }

            if (importer.filterMode != FilterMode.Bilinear)
            {
                importer.filterMode = FilterMode.Bilinear;
                dirty = true;
            }

            if (!dirty)
            {
                return;
            }

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
