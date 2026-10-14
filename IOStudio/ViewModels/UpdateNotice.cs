using System;
using HiMind.Distribution;
using IOStudio.Models;

namespace IOStudio.ViewModels
{
    /// <summary>
    /// 启动时的更新提醒状态 —— 「有没有待提醒的更新, 以及怎么让它停下来」。
    ///
    /// <para><b>为什么要单独一个类</b>: 这段逻辑 (横幅文案、忽略持久化、强制更新不给忽略)
    /// 与窗口/DI 都无关, 抽出来才能直接测 —— 而它恰好是"更新提醒别烦人"的全部规则所在。</para>
    ///
    /// <para><b>行为约定</b>:</para>
    /// <list type="bullet">
    ///   <item><b>稍后</b>: 本次会话不再显示, <b>不</b>落盘 (下次启动还会提);</item>
    ///   <item><b>忽略此版本</b>: 把版本号落盘, 该版本永久不再提 —— 但<b>更新的版本仍会提</b>;</item>
    ///   <item><b>强制更新</b>: 不提供忽略 —— 忽略等于让用户停在有问题的版本上。</item>
    /// </list>
    /// </summary>
    public sealed class UpdateNotice
    {
        private readonly Func<string> _currentVersion;

        /// <param name="currentVersion">
        /// 当前版本号的取值器 —— 用委托而不是字符串, 因为这个类在字段初始化时就要构造,
        /// 而版本号来自那时还没解析出来的更新服务。写死成初值会让横幅里"当前"一栏永远为空。
        /// </param>
        public UpdateNotice(Func<string> currentVersion) =>
            _currentVersion = currentVersion ?? (() => "");

        private SoftwareUpdateManifest? _pending;

        /// <summary>是否有待提醒的更新 (横幅是否可见)。</summary>
        public bool IsVisible => _pending is not null;

        /// <summary>横幅文案 (版本 + 体积)。</summary>
        public string Text
        {
            get
            {
                if (_pending is not { } info)
                    return "";
                string size =
                    info.Size > 0
                        ? $"，更新包 {UpdateDownloadViewModel.FormatBytes(info.Size)}"
                        : "";
                return $"发现新版本 {info.Version}（当前 {_currentVersion()}）{size}";
            }
        }

        /// <summary>是否提供「忽略此版本」(强制更新时不给)。</summary>
        public bool CanSkip => _pending is { Mandatory: false };

        /// <summary>
        /// 判断一条更新该不该提醒。已被忽略的版本不再提 —— 除非是强制更新
        /// (忽略的是版本, 不是更新这件事; 但强制更新不接受被忽略)。
        /// </summary>
        public static bool ShouldNotify(SoftwareUpdateManifest info, string? skippedVersion) =>
            info.Mandatory
            || string.IsNullOrEmpty(skippedVersion)
            || !string.Equals(skippedVersion, info.Version, StringComparison.OrdinalIgnoreCase);

        /// <summary>挂上待提醒的更新 (调用方应先过 <see cref="ShouldNotify"/>)。</summary>
        public void Set(SoftwareUpdateManifest info) => _pending = info;

        /// <summary>「稍后」: 收起横幅, 不落盘。</summary>
        public void Dismiss() => _pending = null;

        /// <summary>
        /// 「忽略此版本」: 落盘后收起。强制更新时是空操作。
        /// </summary>
        /// <returns>被忽略的版本号; 未忽略 (强制更新/无待提醒) 返回 null。</returns>
        public string? Skip()
        {
            if (_pending is not { Mandatory: false } info)
                return null;

            try
            {
                IOStudioSettings.Instance.SkippedUpdateVersion = info.Version;
                IOStudioSettings.Instance.Save();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateNotice] 记住忽略版本失败: {ex.Message}");
                // 落盘失败也收起横幅 —— 本次会话至少不再烦; 下次启动会再提, 属可接受降级
            }

            _pending = null;
            return info.Version;
        }

        /// <summary>「查看更新」: 取走待提醒的更新 (取走后横幅收起)。</summary>
        public SoftwareUpdateManifest? Take()
        {
            var info = _pending;
            _pending = null;
            return info;
        }
    }
}
