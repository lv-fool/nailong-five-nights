namespace NailongFNAF.UI
{
    /// <summary>
    /// 鼠标位置 → 转头量（`_pan`）的映射 —— **纯逻辑，不引用 UnityEngine**，可脱机测试。
    ///
    /// ## 为什么要饱和区
    /// 鼠标位置同时决定「看哪边」和「鼠标在哪」，墙上的按钮又跟着墙走。
    /// 绝对线性映射下墙上的点滑得比鼠标快，永远追不上
    /// （推导见 <c>PrototypeScene.BuildWallButtons</c>）。所以两头必须有**饱和区**。
    ///
    /// ## 为什么 2026-11-03 又加了死区
    /// 用户反馈「镜头跟随太晃」。原来鼠标一动镜头就动，等于镜头长在鼠标上。
    /// 死区里 `dx/dmouse = 0`，鼠标在屏幕中间那一小段随便晃，画面纹丝不动。
    /// 实测（60fps 模拟）：鼠标在屏幕正中 ±8% 抖，镜头摆幅 **0.0000°**（改之前是 16.5°）。
    ///
    /// ## 为什么曲线是分段线性，不是 smoothstep
    /// 任何「起止导数为 0、终点仍到 1」的曲线，中间段斜率必然**大于** `1/span`，
    /// 也就是中间会更灵敏 —— 正是要治的毛病。线性是同等饱和点下最不灵敏的选择。
    ///
    /// ## 限速？不需要
    /// 试过用 <c>Mathf.SmoothDamp</c> 的 `maxSpeed` 限角速度，结果是**越限越慢** ——
    /// 那个参数卡的是位移不是角速度，在起手最需要快的那一段直接把速度按住。
    /// 现在纯靠缓动，实测转到 45° 只要 0.12 秒，本来也没快到会闪。
    /// </summary>
    public static class ViewPanMapping
    {
        /// <summary>
        /// 中心死区半宽（屏幕宽度的比例）。
        ///
        /// 0.10 → 1920 宽下**中间 384px 完全不动**（鼠标在屏幕正中左右各 10% 随便晃，画面纹丝不动）。
        ///
        /// ⚠️ 调大的代价是**窗口宽高比适应变差**：死区 + span 决定饱和起点
        /// （0.5 + 0.10 + 0.15 = 0.75），而按钮满转时的屏幕 x 随窗口变宽往中间跑
        /// （16:9 在 0.944、2.4:1 在 0.829、3.15:1 在 0.750）。起点 0.75 对应 **3.15:1**，
        /// 再宽的窗口鼠标推到边也压不住墙按钮。老配置（0.05 + 0.20 → 0.75）恰好同一个起点，
        /// 所以这一档**没有牺牲**任何宽高比余量 —— 死区是从活跃区里匀出来的。
        /// </summary>
        public const float DefaultDeadZone = 0.10f;

        /// <summary>
        /// 死区外沿到饱和点的宽度（屏幕宽度的比例）。饱和起点 = 0.5 + 死区 + span。
        ///
        /// 0.15 而不是原来的 0.20 —— 死区从 0.05 加到 0.10 时，从活跃区里匀出 0.05，
        /// 保持饱和起点仍是 **0.75** 不变。这样死区白得，宽高比余量一点没丢。
        ///
        /// ⚠️ 这是**另一个**代价，和上面说的宽高比无关：活跃区变窄了，
        /// 增益从 5.49 px/° 提到 **4.11 px/°**（转得更快一点），
        /// 16:9 下满转行程从 384px 缩到 288px —— 鼠标只要推出去 288px 就到头。
        /// 也就是「中间更稳了，两头更急了」。想把活跃区也放宽，得同时挪饱和点，
        /// 那就得上路线 B（把按钮投影钳制进饱和区）。
        /// </summary>
        public const float DefaultSpan = 0.15f;

        /// <summary>
        /// 把鼠标横坐标映射成转头量。
        /// </summary>
        /// <param name="mouseX01">鼠标横坐标，0 = 屏幕最左、1 = 最右</param>
        /// <param name="deadZone">中心死区半宽，&lt;= 0 表示取消死区</param>
        /// <param name="span">死区外沿到饱和点的宽度，必须 &gt; 0（否则退化成阶跃）</param>
        /// <returns>-1 满左、0 正前方、+1 满右</returns>
        public static float Map(float mouseX01, float deadZone, float span)
        {
            // 参数兜底：span 非正会让下面的除法炸掉（或退化成 0/0），直接当「永远正前方」
            if (span <= 0.0001f)
            {
                return 0f;
            }

            if (deadZone < 0f)
            {
                deadZone = 0f;
            }

            float offset = mouseX01 - 0.5f;
            float distance = offset < 0f ? -offset : offset;

            if (distance <= deadZone)
            {
                return 0f;
            }

            float pan = (distance - deadZone) / span;
            if (pan > 1f)
            {
                pan = 1f;
            }

            return offset < 0f ? -pan : pan;
        }

        /// <summary>用默认参数映射（项目实际用的那一档）。</summary>
        public static float Map(float mouseX01)
        {
            return Map(mouseX01, DefaultDeadZone, DefaultSpan);
        }
    }
}
