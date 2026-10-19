using UnityEngine;

namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 成就解锁状态：PlayerPrefs 每个成就一个键（<see cref="KeyPrefix"/> + id，1 = 已解锁）。
    ///
    /// 纯数据类，不依赖场景。菜单与游戏内提示都读它；
    /// 触发点（通关 / 断电生还 / 逼退噜噜 …）只管调 <see cref="Unlock"/>，
    /// 返回 true 表示「这次是新解锁」，调用方据此弹提示。
    /// </summary>
    public static class AchievementStore
    {
        /// <summary>存档键前缀。</summary>
        public const string KeyPrefix = "nailong.achv.";

        /// <summary>某个成就是否已解锁。未知 id 一律 false。</summary>
        public static bool IsUnlocked(string id)
        {
            if (AchievementCatalog.Find(id) == null)
            {
                return false;
            }

            return PlayerPrefs.GetInt(KeyPrefix + id, 0) == 1;
        }

        /// <summary>
        /// 解锁一个成就。返回 true 表示**这次是新解锁**（之前没拿过），
        /// 重复解锁返回 false —— 调用方只在 true 时弹提示，免得刷屏。
        /// 未知 id 不解锁，返回 false。
        /// </summary>
        public static bool Unlock(string id)
        {
            if (AchievementCatalog.Find(id) == null || IsUnlocked(id))
            {
                return false;
            }

            PlayerPrefs.SetInt(KeyPrefix + id, 1);
            PlayerPrefs.Save();
            return true;
        }

        /// <summary>已解锁数量 / 总数。</summary>
        public static int UnlockedCount()
        {
            int count = 0;
            foreach (AchievementCatalog.Def def in AchievementCatalog.All)
            {
                if (IsUnlocked(def.Id))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>清空所有成就（调试用，正常流程不调用）。</summary>
        public static void ResetAll()
        {
            foreach (AchievementCatalog.Def def in AchievementCatalog.All)
            {
                PlayerPrefs.DeleteKey(KeyPrefix + def.Id);
            }

            PlayerPrefs.Save();
        }
    }
}
