using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using IOStudio.Models.Motion;
using IOStudio.ViewModels.Timeline;

namespace IOStudio.Views.Timeline
{
    /// <summary>
    /// 待机循环编辑器 — 独立工具窗口。
    /// 复用关键帧编辑体验 (单周期曲线, 双击添加/拖拽/Delete), 非字段表格。
    /// 直接操作 TrackViewModel.IdleLoop, Undo 复用 TimelineEditorViewModel 既有方法。
    /// </summary>
    public partial class IdleEditorWindow : Window, INotifyPropertyChanged
    {
        private TrackViewModel? _track;
        private TimelineEditorViewModel? _parent;
        private DispatcherTimer? _previewTimer;
        private bool _isPreviewing;
        public bool IsPreviewing
        {
            get => _isPreviewing;
            private set
            {
                _isPreviewing = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PreviewToggleText));
            }
        }

        public string PreviewToggleText => _isPreviewing ? "⏸ 停止预览" : "▶ 预览动画";

        private IdleLoop? _idleLoop;
        public IdleLoop? IdleLoop => _idleLoop;

        private double _periodSeconds = 1.6;
        public double PeriodSeconds
        {
            get => _periodSeconds;
            set
            {
                _periodSeconds = value;
                SyncParamsToModel();
                OnPropertyChanged();
            }
        }

        private double _blendMs = 300;
        public double BlendMs
        {
            get => _blendMs;
            set
            {
                _blendMs = value;
                SyncParamsToModel();
                OnPropertyChanged();
            }
        }

        private double _phaseOffsetMs;
        public double PhaseOffsetMs
        {
            get => _phaseOffsetMs;
            set
            {
                _phaseOffsetMs = value;
                SyncParamsToModel();
                OnPropertyChanged();
            }
        }

        private int _phaseModeIndex;
        public int PhaseModeIndex
        {
            get => _phaseModeIndex;
            set
            {
                _phaseModeIndex = value;
                SyncParamsToModel();
                OnPropertyChanged();
            }
        }

        public ObservableCollection<string> PhaseModeOptions { get; } =
            new() { "∞ 连续 (跨空窗连贯)", "▶ 重置 (每个空窗从头)" };

        private string _trackName = "";
        public string TrackName
        {
            get => _trackName;
            set
            {
                _trackName = value;
                OnPropertyChanged();
            }
        }

        private string _valueType = "float";
        public string ValueType
        {
            get => _valueType;
            set
            {
                _valueType = value;
                OnPropertyChanged();
            }
        }

        private string _statusText = "";
        public string StatusText
        {
            get => _statusText;
            set
            {
                _statusText = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public IdleEditorWindow()
        {
            InitializeComponent();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        }

        /// <summary>打开时装载目标轨道 (由窗口工厂调用)。</summary>
        public void LoadTrack(TrackViewModel track, TimelineEditorViewModel parent)
        {
            _track = track;
            _parent = parent;
            TrackName = track.Label ?? track.DeviceName;
            ValueType = track.ValueType ?? "float";

            // 复用轨道已有 IdleLoop; 未配置则给默认呼吸 (与轨道头菜单一致)
            if (track.IdleLoop == null)
            {
                track.IdleLoop = new IdleLoop
                {
                    Enabled = true,
                    PeriodMs = 1600.0,
                    BlendMs = 300.0,
                    PhaseMode = "continuous",
                    Keyframes =
                    {
                        new MotionKeyframe
                        {
                            TimeMs = 0,
                            Value = 0.5f,
                            Interpolation = "bezier"
                        },
                        new MotionKeyframe
                        {
                            TimeMs = 800,
                            Value = 0.6f,
                            Interpolation = "bezier"
                        },
                        new MotionKeyframe
                        {
                            TimeMs = 1600,
                            Value = 0.5f,
                            Interpolation = "bezier"
                        },
                    },
                };
            }
            _idleLoop = track.IdleLoop;
            _periodSeconds = Math.Round(_idleLoop.PeriodMs / 1000.0, 2);
            _blendMs = _idleLoop.BlendMs;
            _phaseOffsetMs = _idleLoop.PhaseOffsetMs;
            _phaseModeIndex = _idleLoop.PhaseMode == "restart" ? 1 : 0;
            StatusText = _idleLoop.Enabled ? "" : "(已停用 — 请在轨道头右键菜单启用)";

            DataContext = this;
            OnPropertyChanged(nameof(PeriodSeconds));
            OnPropertyChanged(nameof(BlendMs));
            OnPropertyChanged(nameof(PhaseOffsetMs));
            OnPropertyChanged(nameof(PhaseModeIndex));
            OnPropertyChanged(nameof(IdleLoop));

            var curve = IdleCurve;
            if (curve != null)
            {
                curve.IdleLoop = _idleLoop;
                curve.ValueType = ValueType;
                curve.SyncLoopClosure(); // 装载即保证首尾闭合
                // 先退订再订阅: ShowOrActivate 复用窗口, 避免多次 LoadTrack 叠加 handler
                curve.AddKeyframeRequested -= OnCurveAddKeyframe;
                curve.AddKeyframeRequested += OnCurveAddKeyframe;
                curve.KeyframeEditCommitted -= OnCurveKeyframeCommitted;
                curve.KeyframeEditCommitted += OnCurveKeyframeCommitted;
                curve.DeleteKeyframeRequested -= OnCurveDeleteKeyframe;
                curve.DeleteKeyframeRequested += OnCurveDeleteKeyframe;
            }
        }

        // ── 参数同步 (NumericUpDown/ComboBox 变更 → 写回模型) ──

        private void SyncParamsToModel()
        {
            if (_idleLoop == null || _track == null)
                return;
            bool enabled = _idleLoop.Enabled;
            _idleLoop.PeriodMs = Math.Max(100, _periodSeconds * 1000.0);
            _idleLoop.BlendMs = Math.Max(0, _blendMs);
            _idleLoop.PhaseOffsetMs = Math.Max(0, _phaseOffsetMs);
            _idleLoop.PhaseMode = _phaseModeIndex == 1 ? "restart" : "continuous";
            _idleLoop.Enabled = enabled;
            _parent?.NotifyTrackDataChanged(_track);
            if (IdleCurve != null)
                IdleCurve.InvalidateVisual();
        }

        // ── 曲线编辑器事件 (复用 VM 既有 Undo 方法) ──

        private void OnCurveAddKeyframe(double phaseMs, float value)
        {
            if (_parent == null || _track == null)
                return;
            _parent.AddIdleKeyframeAtTime(_track, phaseMs, value); // 带 Undo
            IdleCurve.SyncLoopClosure(); // 首尾同步 (新增帧落在相位 0/period 时保证闭合)
            _parent.NotifyTrackDataChanged(_track);
            IdleCurve.InvalidateVisual();
        }

        private void OnCurveKeyframeCommitted(
            MotionKeyframe kf,
            double t0,
            float v0,
            double t1,
            float v1
        )
        {
            if (_parent == null || _track == null)
                return;
            _parent.CurveEditorVm.CommitIdleKeyframeEdit(_track, kf, t0, v0, t1, v1); // 带 Undo
            IdleCurve.SyncLoopClosure(); // 首尾同步 (拖到相位 0/period 边界时保证闭合)
            _parent.NotifyTrackDataChanged(_track);
            IdleCurve.InvalidateVisual();
        }

        private void OnCurveDeleteKeyframe(MotionKeyframe kf)
        {
            if (_parent == null || _track == null || _idleLoop?.Keyframes == null)
                return;
            double phase = kf.TimeMs;
            float val = kf.Value;
            _parent.CurveEditorVm.OnIdleKeyframeDeleteRequested(_track, phase, val); // 带 Undo
            _parent.NotifyTrackDataChanged(_track);
            IdleCurve.InvalidateVisual();
        }

        // ── 顶部按钮 ──

        // ── 标题栏原生行为: 拖拽移动 / 最小化 / 最大化 / 关闭 ──

        private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            // 左键按住标题栏空白处即可拖动窗口
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        }

        private void OnMinimizeClick(object? sender, RoutedEventArgs e) =>
            WindowState = WindowState.Minimized;

        private void OnMaximizeClick(object? sender, RoutedEventArgs e) =>
            WindowState =
                WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

        private void OnTemplateClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tpl && _track != null && _parent != null)
            {
                _parent.ApplyIdleTemplateToTrack(_track, tpl);
                _idleLoop = _track.IdleLoop;
                _periodSeconds = Math.Round(_idleLoop!.PeriodMs / 1000.0, 2);
                OnPropertyChanged(nameof(PeriodSeconds));
                OnPropertyChanged(nameof(IdleLoop));
                if (IdleCurve != null)
                    IdleCurve.IdleLoop = _idleLoop;
                IdleCurve.InvalidateVisual();
                _parent.NotifyTrackDataChanged(_track);
            }
        }

        private void OnEnableClick(object? sender, RoutedEventArgs e)
        {
            if (_track?.IdleLoop == null || _parent == null)
                return;
            bool enable = sender is Button b && b.Tag is string s && s == "enable";
            _track.IdleLoop.Enabled = enable;
            StatusText = enable ? "" : "(已停用)";
            _parent.NotifyTrackDataChanged(_track);
            IdleCurve.InvalidateVisual();
        }

        private void OnPreviewClick(object? sender, RoutedEventArgs e)
        {
            if (_previewTimer != null)
            {
                _previewTimer.Stop();
                _previewTimer = null;
                IdleCurve.PreviewPhase = -1;
                IsPreviewing = false;
                return;
            }
            double period = _idleLoop?.PeriodMs > 1.0 ? _idleLoop.PeriodMs : 1000.0;
            double start = Environment.TickCount64;
            _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _previewTimer.Tick += (_, _) =>
            {
                double elapsed = Environment.TickCount64 - start;
                IdleCurve.PreviewPhase = elapsed % period; // 0 → period 循环
            };
            _previewTimer.Start();
            IsPreviewing = true;
        }

        protected override void OnClosed(EventArgs e)
        {
            _previewTimer?.Stop();
            _previewTimer = null;
            IsPreviewing = false;
            base.OnClosed(e);
        }
    }
}
