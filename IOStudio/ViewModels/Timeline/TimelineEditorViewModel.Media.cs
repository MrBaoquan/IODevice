using System;

namespace IOStudio.ViewModels.Timeline
{
    /// <summary>
    /// 媒体时钟接入 —— 播放期间由媒体 (影片/音频) 驱动动作时间。
    ///
    /// <para><b>为什么需要</b>: 否则动作由内部 Stopwatch 驱动、画面由 LibVLC 自由跑, 而 LibVLC
    /// 速率不是精确 1x, 5 分钟以上必然漂移且无人察觉。由播放器当主时钟可以让动作与画面
    /// 在结构上不可能失步 (而不是靠定期纠偏)。</para>
    ///
    /// <para><b>与"媒体绑定"无关</b>: 影片路径**不写入 .motion**。编排内容是媒体无关的
    /// (同一个 .motion 可能用于影片, 也可能用于固定循环演出或测试序列); 影片与动作的
    /// 对应关系属于演出/生产层。这里只是"当前正在预览哪部片子"的运行期状态。</para>
    ///
    /// <para>VM 不直接依赖 LibVLC: 时钟通过 View 注入的回调获取, 与既有的
    /// VideoDurationDetected → VideoDurationMs 是同一模式。</para>
    /// </summary>
    public partial class TimelineEditorViewModel
    {
        /// <summary>由 View 注入: 返回媒体当前时间 (ms); null = 无媒体时钟 (未加载或未播放)。</summary>
        public Func<double?>? MediaClockProvider { get; set; }

        /// <summary>
        /// 播放期间把媒体时钟推给引擎 —— 有影片时由影片当主时钟。
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
    }
}
