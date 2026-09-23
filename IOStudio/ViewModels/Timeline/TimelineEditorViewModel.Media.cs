using System;
using System.IO;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;
using ReactiveUI;

namespace IOStudio.ViewModels.Timeline
{
    /// <summary>
    /// 影片媒体绑定 — 让"影片 + 动作"成为一个可复现的项目整体, 并让动作由媒体时钟驱动。
    ///
    /// <para>两个问题的解法都在这里:</para>
    /// <list type="number">
    /// <item><b>可复现</b>: 视频路径随 .motion 落盘 (优先相对路径), 重开项目自动恢复,
    ///   找不到时只提示"媒体缺失", 不阻断动作编辑。</item>
    /// <item><b>不失步</b>: 播放期间把媒体当前时间推给引擎当主时钟。否则动作(内部计时)与
    ///   画面(LibVLC)各自自由跑, LibVLC 速率不是精确 1x, 5 分钟以上必然漂移。</item>
    /// </list>
    ///
    /// <para>VM 不直接依赖 LibVLC: 时钟与加载都通过 View 注入的回调/请求属性完成,
    /// 与既有的 VideoDurationDetected → VideoDurationMs 模式一致。</para>
    /// </summary>
    public partial class TimelineEditorViewModel
    {
        // ---- 与 View 的契约 ----

        /// <summary>由 View 注入: 返回媒体当前时间 (ms); null = 无媒体时钟 (未加载或未播放)。</summary>
        public Func<double?>? MediaClockProvider { get; set; }

        private string _mediaLoadRequestedPath = "";

        /// <summary>View 监听此属性: 非空即请求加载该媒体文件; 处理后由 View 置回空串。</summary>
        public string MediaLoadRequestedPath
        {
            get => _mediaLoadRequestedPath;
            set => this.RaiseAndSetIfChanged(ref _mediaLoadRequestedPath, value);
        }

        private string _mediaStatusText = "";

        /// <summary>媒体绑定状态提示 (如"媒体缺失: xxx")。空串 = 正常。</summary>
        public string MediaStatusText
        {
            get => _mediaStatusText;
            set => this.RaiseAndSetIfChanged(ref _mediaStatusText, value);
        }

        // ---- 时钟 ----

        /// <summary>
        /// 播放期间把媒体时钟推给引擎 —— 影片编排必须由媒体当主时钟。
        /// 媒体不可用时推 null, 引擎回退到内部计时 (纯动作预览)。
        /// </summary>
        private void PushMediaClock()
        {
            double? mediaMs = null;
            try
            {
                mediaMs = MediaClockProvider?.Invoke();
            }
            catch
            {
                mediaMs = null;
            }
            _engine.SetExternalClockMs(mediaMs);
        }

        // ---- 落盘 ----

        /// <summary>把当前媒体绑定同步到 Timeline 模型 (与 marker 同步同一时机, 写盘前调用)。</summary>
        private void SyncMediaToTimeline()
        {
            if (Timeline == null)
                return;

            string path = VideoFilePath;
            if (string.IsNullOrEmpty(path))
            {
                Timeline.Media = null;
                return;
            }

            Timeline.Media = new MotionMedia
            {
                VideoPath = MotionMediaResolver.ToPortablePath(path, CurrentFilePath),
                DurationMs = VideoDurationMs,
            };
        }

        /// <summary>加载 .motion 后按记录恢复影片绑定 (缺失时只提示, 不阻断)。</summary>
        private void RestoreMediaBinding(MotionTimeline timeline, string motionFilePath)
        {
            string? recorded = timeline.Media?.VideoPath;
            if (string.IsNullOrEmpty(recorded))
            {
                MediaStatusText = "";
                return;
            }

            string? resolved = MotionMediaResolver.Resolve(recorded, motionFilePath);
            if (resolved == null)
            {
                MediaStatusText = $"媒体缺失: {recorded}";
                PushStatus($"媒体缺失, 请重新选择影片: {recorded}");
                return;
            }

            MediaStatusText = "";
            VideoFilePath = resolved;
            // 请求 View 加载 (View 处理后会把该属性置回空串, 允许重复加载同一文件)
            MediaLoadRequestedPath = resolved;
        }
    }
}
