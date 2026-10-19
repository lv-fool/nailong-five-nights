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

            // 办公室已经改成 3D 全景 + 旋转相机（OfficeRoom），门也是 3D 世界物体
            // （贴在门洞里，不再是全屏叠层）—— 门板是单张正视贴图。
            // 带 alpha 的（门板、立绘、监控）必须不压缩导入，压缩会让透明边缘出色块。
            EnsurePanoImport();

            foreach (var name in AlphaSpriteNames())
            {
                EnsureSpriteImport(FindImage(name), true);
            }

            AssetDatabase.Refresh();

            var characters = LoadCharacters();

            prototype.SetArtwork(
                LoadSprite("door_face_left"),
                LoadSprite("door_face_right"),
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

            Debug.Log("[奶龙] 素材已接入：背景全景、门板 2 张、监控画面 11 张、"
                + "角色立绘 " + characters.Length + " 套、音频 10 条。按 Play 看效果。");
        }

        private static IEnumerable<string> AlphaSpriteNames()
        {
            yield return "door_face_left";
            yield return "door_face_right";

            yield return "mon_1a";
            yield return "mon_1b";
            yield return "mon_1c";
            yield return "mon_2a";
            yield return "mon_2b";
            yield return "mon_3";
            yield return "mon_4a";
            yield return "mon_4b";
            yield return "mon_5";
            yield return "mon_6";
            yield return "mon_7";

            foreach (var id in EnemyIds.All)
            {
                string code = id.Code();
                foreach (var pose in new[]
                         {
                             "idle", "stare", "peek", "startled", "angry",
                             "jumpscare", "blackout"
                         })
                {
                    yield return "char_" + code + "_" + pose;
                }
            }
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

                // 按房间的专属姿势：char_<代号>_mon_1a / _mon_1b / … 有就接，没有留空
                var roomPoses = new Sprite[PrototypeScene.RoomCodeCount];
                bool anyRoomPose = false;
                for (int cam = 0; cam < roomPoses.Length; cam++)
                {
                    string room = PrototypeScene.RoomCode(cam);
                    if (string.IsNullOrEmpty(room))
                    {
                        continue;
                    }

                    roomPoses[cam] = LoadSprite("char_" + code + "_" + room);
                    if (roomPoses[cam] != null)
                    {
                        anyRoomPose = true;
                    }
                }

                if (idle == null && stare == null && peek == null && startled == null
                    && angry == null && jumpscare == null && blackout == null
                    && !anyRoomPose)
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
                    Blackout = blackout,
                    RoomPoses = roomPoses
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
        /// <paramref name="needsAlpha"/> 决定要不要关压缩：
        /// 立绘、门片、监控画面带 alpha 且会压在暗背景上 —— **必须关压缩**，
        /// 否则透明边缘会压出色块，暗场里一眼就能看出来。
        /// 背景整段是不透明的大图（一转二十来张），反过来**必须压缩**，
        /// 不然光背景就吃掉一百多 MB 显存。两边都是踩过坑的结论，别对调。
        /// </summary>

        private static void EnsureSpriteImport(string path, bool needsAlpha)
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

            if (importer.alphaIsTransparency != needsAlpha)
            {
                importer.alphaIsTransparency = needsAlpha;
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

            var wanted = needsAlpha
                ? TextureImporterCompression.Uncompressed
                : TextureImporterCompression.CompressedHQ;

            if (importer.textureCompression != wanted)
            {
                importer.textureCompression = wanted;
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

        /// <summary>
        /// 全景条（3D 办公室用）的导入设置。它是 5120 宽的一条图，Unity 默认的
        /// <c>maxTextureSize</c> 会把它压回 2048 —— 那正好是视野里看得见的宽度，
        /// 一压就糊。所以这里必须显式放到 8192。
        ///
        /// 它不进场景的 SerializeField，是 <c>OfficeRoom</c> 用
        /// <c>Resources.Load</c> 在运行时自己抓的，所以只要导入设置对就行。
        /// </summary>
        private static void EnsurePanoImport()
        {
            const string Path = "Assets/Resources/office_pano.png";

            var importer = AssetImporter.GetAtPath(Path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning("[奶龙] 缺 " + Path +
                                 " —— 先跑 tools/render_pano.py 生成全景条。");
                return;
            }

            bool dirty = false;

            if (importer.textureType != TextureImporterType.Default)
            {
                importer.textureType = TextureImporterType.Default;
                dirty = true;
            }

            if (importer.maxTextureSize < 8192)
            {
                importer.maxTextureSize = 8192;
                dirty = true;
            }

            if (!importer.mipmapEnabled)
            {
                // 相机一转远近缩放就变，没 mip 会闪
                importer.mipmapEnabled = true;
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
