using System.Collections.ObjectModel;
using System.Linq;
using IOStudio.Models.Motion;
using ReactiveUI;

namespace IOStudio.ViewModels.Timeline
{
    /// <summary>
    /// 片段 ViewModel — 包装 MotionClip 模型
    /// </summary>
    public class ClipViewModel : ViewModelBase
    {
        public MotionClip Clip { get; }

        public ClipViewModel(MotionClip clip)
        {
            Clip = clip;

            foreach (var kf in clip.Keyframes)
            {
                Keyframes.Add(new KeyframeViewModel(kf));
            }
        }

        public string Id => Clip.Id;

        public double StartMs
        {
            get => Clip.StartMs;
            set
            {
                Clip.StartMs = value;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(DurationMs));
            }
        }

        public double EndMs
        {
            get => Clip.EndMs;
            set
            {
                Clip.EndMs = value;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(DurationMs));
            }
        }

        public double DurationMs => Clip.DurationMs;

        public ObservableCollection<KeyframeViewModel> Keyframes { get; } = new();

        /// <summary>
        /// 在指定时间添加关键帧
        /// </summary>
        public KeyframeViewModel AddKeyframe(
            double timeMs,
            float value,
            string interpolation = "linear"
        )
        {
            var kf = new MotionKeyframe
            {
                TimeMs = timeMs,
                Value = value,
                Interpolation = interpolation,
            };

            // 按时间排序插入
            int insertIdx = 0;
            for (int i = 0; i < Clip.Keyframes.Count; i++)
            {
                if (Clip.Keyframes[i].TimeMs > timeMs)
                    break;
                insertIdx = i + 1;
            }

            Clip.Keyframes.Insert(insertIdx, kf);
            var vm = new KeyframeViewModel(kf);
            Keyframes.Insert(insertIdx, vm);
            return vm;
        }

        /// <summary>
        /// 移除关键帧
        /// </summary>
        public void RemoveKeyframe(KeyframeViewModel kfVm)
        {
            Clip.Keyframes.Remove(kfVm.Keyframe);
            Keyframes.Remove(kfVm);
        }

        /// <summary>
        /// 按时间排序关键帧 (拖拽关键帧后重新排序)
        /// 返回指定关键帧的新索引位置 (-1 表示未找到)
        /// </summary>
        public int SortKeyframesByTime(KeyframeViewModel targetKf)
        {
            var sorted = Keyframes.OrderBy(k => k.TimeMs).ToList();
            bool changed = false;
            for (int i = 0; i < sorted.Count; i++)
            {
                if (sorted[i] != Keyframes[i])
                {
                    changed = true;
                    break;
                }
            }
            if (!changed)
                return Keyframes.IndexOf(targetKf);

            // 同步 Model 层
            Clip.Keyframes.Clear();
            foreach (var kfVm in sorted)
                Clip.Keyframes.Add(kfVm.Keyframe);

            // 同步 ViewModel 层
            Keyframes.Clear();
            foreach (var kfVm in sorted)
                Keyframes.Add(kfVm);

            return Keyframes.IndexOf(targetKf);
        }
    }
}
