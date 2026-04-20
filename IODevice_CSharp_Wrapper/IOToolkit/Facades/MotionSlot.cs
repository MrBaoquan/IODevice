using IOToolkit.Core;
using System;

namespace IOToolkit
{
    /// <summary>
    /// 动作播放 Slot 实例 — 封装单个 Slot 的播控与状态查询。
    /// 通过 MotionPlayer.LoadSlot() 或 MotionPlayer.GetSlot() 获取实例。
    /// 使用方式类似 IODevice：slot.Play()、slot.Pause()、slot.State 等。
    /// </summary>
    public class MotionSlot
    {
        internal MotionSlot(string slotId, int priority, MixPolicy policy)
        {
            SlotId = slotId;
            Priority = priority;
            Policy = policy;
        }

        // ── 基本信息 ─────────────────────────────────

        public string SlotId { get; }

        public int Priority { get; }

        public MixPolicy Policy { get; }

        // ── 播控操作 ─────────────────────────────────

        public int Play() => IONativeWrapper.MotionPlaySlot(SlotId);

        public int PlayFrom(float timeMs) => IONativeWrapper.MotionPlaySlotFrom(SlotId, timeMs);

        public int Pause() => IONativeWrapper.MotionPauseSlot(SlotId);

        public int Resume() => IONativeWrapper.MotionResumeSlot(SlotId);

        public int Stop() => IONativeWrapper.MotionStopSlot(SlotId);

        public int Seek(float timeMs) => IONativeWrapper.MotionSeekSlot(SlotId, timeMs);

        public void Unload()
        {
            IONativeWrapper.MotionUnloadSlot(SlotId);
            MotionPlayer.RemoveSlot(SlotId);
        }

        /// <summary>
        /// 纯求值指定时间的所有通道值（不触发设备派发）。
        /// 用于编辑器 UI 实时值显示。
        /// </summary>
        public float[] EvaluateAt(float timeMs, int maxChannels = 32)
        {
            float[] values = new float[maxChannels];
            int count = IONativeWrapper.MotionEvaluateSlotAt(SlotId, timeMs, values, maxChannels);
            if (count < 0)
                return new float[0];
            if (count < maxChannels)
            {
                float[] trimmed = new float[count];
                Array.Copy(values, trimmed, count);
                return trimmed;
            }
            return values;
        }

        // ── 属性 ─────────────────────────────────────

        public float Speed
        {
            get => _speed;
            set
            {
                _speed = value;
                IONativeWrapper.MotionSetSlotSpeed(SlotId, value);
            }
        }

        public bool Loop
        {
            get => _loop;
            set
            {
                _loop = value;
                IONativeWrapper.MotionSetSlotLoop(SlotId, value);
            }
        }

        /// <summary>
        /// 时钟模式。Internal: 内部 delta 累加驱动; External: 由外部时间源驱动。
        /// 设置为 External 后，每帧通过 ExternalTime 设置绝对时间即可实现零漂移同步。
        /// </summary>
        public ClockMode ClockMode
        {
            get => _clockMode;
            set
            {
                _clockMode = value;
                IONativeWrapper.MotionSetSlotClockMode(SlotId, (int)value);
            }
        }

        /// <summary>
        /// 推送外部时钟时间 (ms)。仅当 ClockMode == External 时生效。
        /// 每帧调用一次，Tick 时 Slot 的 currentTimeMs 将直接使用此值。
        /// </summary>
        public void SetExternalTime(float timeMs)
        {
            IONativeWrapper.MotionSetSlotExternalTime(SlotId, timeMs);
        }

        // ── 状态查询 ─────────────────────────────────

        public MotionState State => (MotionState)IONativeWrapper.MotionGetSlotState(SlotId);

        public float CurrentTime => IONativeWrapper.MotionGetSlotCurrentTime(SlotId);

        public float Duration => IONativeWrapper.MotionGetSlotDuration(SlotId);

        // ── 内部状态 ─────────────────────────────────

        private float _speed = 1.0f;
        private bool _loop;
        private ClockMode _clockMode = ClockMode.Internal;
    }
}
