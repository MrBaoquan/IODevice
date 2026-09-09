using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;
using ReactiveUI;

namespace IOStudio.ViewModels.Timeline
{
    /// <summary>
    /// 效果预设库面板 ViewModel — 管理预设浏览、搜索、应用、自定义保存。
    /// </summary>
    public class PresetLibraryPanelViewModel : ViewModelBase
    {
        private readonly EffectPresetLibrary _library;

        private string _searchText = "";
        private string _selectedCategory = "";
        private EffectPreset? _selectedPreset;
        private float _applyIntensity = 1.0f;
        private double _applySpeed = 1.0;
        private double _applyDurationMs;
        private bool _isPanelVisible;
        private bool _showParameterDialog;
        private string _operationStatus = "";
        private string? _targetTrackId;
        private string _targetTrackName = "";
        private bool _targetTrackLocked;

        public PresetLibraryPanelViewModel(EffectPresetLibrary? library = null)
        {
            _library = library ?? new EffectPresetLibrary();
            _library.Initialize();

            // 刷新分类列表
            RefreshCategories();
            RefreshPresets();

            // 命令
            ApplyPresetCommand = ReactiveCommand.Create(() => OnApplyPreset());
            ConfirmApplyCommand = ReactiveCommand.Create(() => OnConfirmApply());
            CancelApplyCommand = ReactiveCommand.Create(() =>
            {
                ShowParameterDialog = false;
            });
            SaveAsPresetCommand = ReactiveCommand.Create(() => OnSaveAsPreset());
            EditPresetCommand = ReactiveCommand.Create(() => OnEditPreset());
            TogglePanelCommand = ReactiveCommand.Create(() =>
            {
                IsPanelVisible = !IsPanelVisible;
            });
            ToggleFavoriteCommand = ReactiveCommand.Create<EffectPreset, Unit>(preset =>
            {
                if (preset == null)
                    return Unit.Default;
                bool fav = _library.ToggleFavorite(preset.Id);
                preset.IsFavorite = fav;
                RefreshPresets();
                OperationStatus = fav ? $"已收藏“{preset.Name}”" : $"已取消收藏“{preset.Name}”";
                return Unit.Default;
            });
        }

        // ── 属性 ──

        /// <summary>预设库（内部引用）</summary>
        public EffectPresetLibrary Library => _library;

        /// <summary>面板是否可见</summary>
        public bool IsPanelVisible
        {
            get => _isPanelVisible;
            set => this.RaiseAndSetIfChanged(ref _isPanelVisible, value);
        }

        /// <summary>搜索文本</summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                this.RaiseAndSetIfChanged(ref _searchText, value);
                RefreshPresets();
            }
        }

        /// <summary>选中的分类（空字符串 = 全部）</summary>
        public string SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedCategory, value);
                RefreshPresets();
            }
        }

        /// <summary>分类列表</summary>
        public ObservableCollection<string> Categories { get; } = new();

        /// <summary>过滤后的预设列表</summary>
        public ObservableCollection<EffectPreset> FilteredPresets { get; } = new();

        /// <summary>当前筛选结果数量（用于面板标题，避免用户需要数卡片）。</summary>
        public string FilteredPresetCountDisplay => $"{FilteredPresets.Count} 个预设";

        public bool HasFilteredPresets => FilteredPresets.Count > 0;

        public bool HasNoFilteredPresets => !HasFilteredPresets;

        /// <summary>选中的预设</summary>
        public EffectPreset? SelectedPreset
        {
            get => _selectedPreset;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedPreset, value);
                this.RaisePropertyChanged(nameof(HasSelectedPreset));
                this.RaisePropertyChanged(nameof(SelectedPresetInfo));
                this.RaisePropertyChanged(nameof(RequiresTargetTrack));
                this.RaisePropertyChanged(nameof(CanConfirmApply));
                this.RaisePropertyChanged(nameof(TargetTrackDisplay));
            }
        }

        /// <summary>是否显示参数对话框</summary>
        public bool ShowParameterDialog
        {
            get => _showParameterDialog;
            set => this.RaiseAndSetIfChanged(ref _showParameterDialog, value);
        }

        /// <summary>应用强度 (0.2~1.2)</summary>
        public float ApplyIntensity
        {
            get => _applyIntensity;
            set => this.RaiseAndSetIfChanged(ref _applyIntensity, value);
        }

        /// <summary>应用速度倍率 (0.5x~2x)</summary>
        public double ApplySpeed
        {
            get => _applySpeed;
            set => this.RaiseAndSetIfChanged(ref _applySpeed, value);
        }

        /// <summary>应用目标时长 (毫秒, 0=使用预设基准)</summary>
        public double ApplyDurationMs
        {
            get => _applyDurationMs;
            set => this.RaiseAndSetIfChanged(ref _applyDurationMs, value);
        }

        /// <summary>是否已选中预设</summary>
        public bool HasSelectedPreset => SelectedPreset != null;

        public bool RequiresTargetTrack => SelectedPreset?.Keyframes is { Count: > 0 };

        public bool CanConfirmApply =>
            HasSelectedPreset
            && (!RequiresTargetTrack || HasTargetTrack)
            && (!HasTargetTrack || !TargetTrackLocked);

        public bool HasTargetTrack => !string.IsNullOrWhiteSpace(TargetTrackId);

        public string? TargetTrackId
        {
            get => _targetTrackId;
            private set
            {
                this.RaiseAndSetIfChanged(ref _targetTrackId, value);
                this.RaisePropertyChanged(nameof(HasTargetTrack));
                this.RaisePropertyChanged(nameof(CanConfirmApply));
                this.RaisePropertyChanged(nameof(TargetTrackDisplay));
            }
        }

        /// <summary>当前目标轨道是否锁定；锁定时预设写入操作应在面板中直接禁用。</summary>
        public bool TargetTrackLocked
        {
            get => _targetTrackLocked;
            private set
            {
                this.RaiseAndSetIfChanged(ref _targetTrackLocked, value);
                this.RaisePropertyChanged(nameof(CanConfirmApply));
                this.RaisePropertyChanged(nameof(TargetTrackDisplay));
            }
        }

        public string TargetTrackName
        {
            get => _targetTrackName;
            private set
            {
                this.RaiseAndSetIfChanged(ref _targetTrackName, value);
                this.RaisePropertyChanged(nameof(TargetTrackDisplay));
            }
        }

        public string TargetTrackDisplay =>
            HasTargetTrack
                ? (TargetTrackLocked ? $"{TargetTrackName}（已锁定）" : TargetTrackName)
                : (RequiresTargetTrack ? "未选择轨道" : "按预设通道映射");

        /// <summary>最近一次预设操作结果，供面板提供非阻塞反馈。</summary>
        public string OperationStatus
        {
            get => _operationStatus;
            set
            {
                this.RaiseAndSetIfChanged(ref _operationStatus, value);
                this.RaisePropertyChanged(nameof(HasOperationStatus));
            }
        }

        public bool HasOperationStatus => !string.IsNullOrWhiteSpace(OperationStatus);

        /// <summary>选的预设的通道数描述</summary>
        public string SelectedPresetInfo
        {
            get
            {
                if (_selectedPreset == null)
                    return "未选择预设";
                return $"{_selectedPreset.Name} ({_selectedPreset.KeyframeCount} 个关键帧, {_selectedPreset.DurationMs / 1000:F1}s)";
            }
        }

        // ── 命令 ──

        /// <summary>快捷键: 切换面板</summary>
        public ReactiveCommand<Unit, Unit> TogglePanelCommand { get; }

        /// <summary>应用预设（打开参数对话框）</summary>
        public ReactiveCommand<Unit, Unit> ApplyPresetCommand { get; }

        /// <summary>确认应用（参数确认后触发）</summary>
        public ReactiveCommand<Unit, Unit> ConfirmApplyCommand { get; }

        /// <summary>取消应用</summary>
        public ReactiveCommand<Unit, Unit> CancelApplyCommand { get; }

        /// <summary>另存为预设 (触发事件, 由 TimelineEditorViewModel 处理对话框 + 收集轨道)</summary>
        public ReactiveCommand<Unit, Unit> SaveAsPresetCommand { get; }

        public ReactiveCommand<Unit, Unit> EditPresetCommand { get; }

        /// <summary>切换收藏 (参数: EffectPreset)。</summary>
        public ReactiveCommand<EffectPreset, Unit> ToggleFavoriteCommand { get; }

        // ── 事件（由 TimelineEditorViewModel 监听 / code-behind 处理） ──

        /// <summary>应用预设请求: 参数 (preset, options)</summary>
        public event Action<EffectPreset, PresetApplyOptions>? PresetApplyRequested;

        /// <summary>保存预设请求 (由 TimelineEditorViewModel 处理)</summary>
        public event EventHandler? SavePresetRequested;

        public event Action<EffectPreset>? EditPresetRequested;

        /// <summary>删除预设请求 (由 TimelineEditorViewModel 用确认框处理)。</summary>
        public event Action<EffectPreset>? DeletePresetRequested;

        /// <summary>重命名预设请求 (由 TimelineEditorViewModel 弹输入框)。</summary>
        public event Action<EffectPreset>? RenamePresetRequested;

        /// <summary>修改预设分类请求 (由 TimelineEditorViewModel 弹输入框)。</summary>
        public event Action<EffectPreset>? ChangeCategoryRequested;

        /// <summary>导出预设 JSON 请求 (由面板 code-behind 复制到剪贴板)。</summary>
        public event Action<EffectPreset>? ExportPresetRequested;

        public void SetTargetTrack(string? trackId, string? trackName, bool isLocked = false)
        {
            TargetTrackId = trackId;
            TargetTrackName = string.IsNullOrWhiteSpace(trackName) ? "当前轨道" : trackName;
            TargetTrackLocked = !string.IsNullOrWhiteSpace(trackId) && isLocked;
        }

        // ── 方法 ──

        /// <summary>刷新分类列表</summary>
        public void RefreshCategories()
        {
            var cats = _library.GetCategories();
            Categories.Clear();
            Categories.Add("全部");
            foreach (var c in cats)
                Categories.Add(c);

            if (!Categories.Contains(_selectedCategory))
            {
                _selectedCategory = "全部";
                this.RaisePropertyChanged(nameof(SelectedCategory));
            }
        }

        /// <summary>刷新预设列表</summary>
        public void RefreshPresets()
        {
            string? keyword = string.IsNullOrWhiteSpace(_searchText) ? null : _searchText.Trim();
            string? category =
                string.IsNullOrEmpty(_selectedCategory) || _selectedCategory == "全部"
                    ? null
                    : _selectedCategory;

            var results = _library.Search(keyword, category);
            FilteredPresets.Clear();
            foreach (var p in results)
            {
                p.IsFavorite = _library.IsFavorite(p.Id);
                p.IsBuiltIn = _library.IsBuiltIn(p.Id);
                FilteredPresets.Add(p);
            }

            this.RaisePropertyChanged(nameof(FilteredPresetCountDisplay));
            this.RaisePropertyChanged(nameof(HasFilteredPresets));
            this.RaisePropertyChanged(nameof(HasNoFilteredPresets));
        }

        /// <summary>删除预设 (由面板右键菜单触发, 委托给事件由宿主处理确认框)。</summary>
        public void RequestDeletePreset(EffectPreset preset)
        {
            if (preset != null)
                DeletePresetRequested?.Invoke(preset);
        }

        /// <summary>重命名预设 (委托宿主弹输入框)。</summary>
        public void RequestRenamePreset(EffectPreset preset)
        {
            if (preset != null)
                RenamePresetRequested?.Invoke(preset);
        }

        /// <summary>修改预设分类 (委托宿主弹输入框)。</summary>
        public void RequestChangeCategory(EffectPreset preset)
        {
            if (preset != null)
                ChangeCategoryRequested?.Invoke(preset);
        }

        /// <summary>导出预设 JSON (委托面板复制到剪贴板)。</summary>
        public void RequestExportPreset(EffectPreset preset)
        {
            if (preset != null)
                ExportPresetRequested?.Invoke(preset);
        }

        /// <summary>是否内置预设 (内置不可删除/重命名)。</summary>
        public bool IsBuiltInPreset(EffectPreset preset) =>
            preset != null && _library.IsBuiltIn(preset.Id);

        /// <summary>选中预设并显示参数对话框</summary>
        public void SelectAndApplyPreset(EffectPreset preset)
        {
            SelectedPreset = preset;
            this.RaisePropertyChanged(nameof(SelectedPresetInfo));
            OnApplyPreset();
        }

        /// <summary>
        /// 直接应用预设到时间线（无需参数对话框，使用默认参数）
        /// </summary>
        public void ApplyPresetDirect(EffectPreset preset)
        {
            if (preset == null)
                return;
            var options = new PresetApplyOptions
            {
                Intensity = preset.DefaultIntensity,
                Speed = 1.0,
                TargetDurationMs = preset.DurationMs,
            };
            PresetApplyRequested?.Invoke(preset, options);
        }

        /// <summary>
        /// 从指定的 Clip 组创建预设（供"另存为"功能调用）
        /// </summary>
        public EffectPreset CreatePresetFromClips(
            string name,
            string category,
            IReadOnlyList<(string Role, MotionClip Clip)> roleClips
        )
        {
            if (roleClips == null || roleClips.Count == 0)
                return null!;

            double minStart = roleClips.Min(c => c.Clip.StartMs);
            double maxEnd = roleClips.Max(c => c.Clip.EndMs);

            var preset = new EffectPreset
            {
                Id = Guid.NewGuid().ToString("N")[..8],
                Name = name,
                Category = string.IsNullOrEmpty(category) ? "用户自定义" : category,
                DurationMs = Math.Max(0, maxEnd - minStart),
                DefaultIntensity = 1.0f,
                MinIntensity = 0.2f,
                MaxIntensity = 1.2f,
            };

            if (roleClips.Count == 1)
            {
                var source = roleClips[0].Clip;
                preset.Keyframes = source.Keyframes
                    .Select(
                        k =>
                            new MotionKeyframe
                            {
                                TimeMs = k.TimeMs + source.StartMs - minStart,
                                Value = k.Value,
                                Interpolation = k.Interpolation,
                                TangentIn = k.TangentIn,
                                TangentOut = k.TangentOut,
                                Cp1x = k.Cp1x,
                                Cp2x = k.Cp2x,
                                Event = k.Event,
                            }
                    )
                    .ToList();
                return preset;
            }

            foreach (var (role, clip) in roleClips)
            {
                if (string.IsNullOrEmpty(role) || clip == null)
                    continue;

                preset.Channels.Add(
                    new PresetChannel
                    {
                        Role = role,
                        Scale = 1.0f,
                        Keyframes = clip.Keyframes
                            .Select(
                                kf =>
                                    new MotionKeyframe
                                    {
                                        TimeMs = kf.TimeMs + clip.StartMs - minStart,
                                        Value = kf.Value,
                                        Interpolation = kf.Interpolation,
                                        TangentIn = kf.TangentIn,
                                        TangentOut = kf.TangentOut,
                                        Cp1x = kf.Cp1x,
                                        Cp2x = kf.Cp2x,
                                        Event = kf.Event,
                                    }
                            )
                            .ToList(),
                    }
                );
            }

            return preset;
        }

        // ── 内部 ──

        private void OnApplyPreset()
        {
            if (_selectedPreset == null)
                return;
            if (HasTargetTrack && TargetTrackLocked)
            {
                OperationStatus = $"目标轨道“{TargetTrackName}”已锁定，无法应用预设";
                return;
            }

            // 预设参数
            ApplyIntensity = _selectedPreset.DefaultIntensity;
            ApplySpeed = 1.0;
            ApplyDurationMs = _selectedPreset.DurationMs;

            // 显示参数对话框
            ShowParameterDialog = true;
        }

        private void OnConfirmApply()
        {
            if (_selectedPreset == null)
                return;
            if (RequiresTargetTrack && !HasTargetTrack)
            {
                OperationStatus = "请先在时间轴中选择目标轨道";
                return;
            }
            if (HasTargetTrack && TargetTrackLocked)
            {
                OperationStatus = $"目标轨道“{TargetTrackName}”已锁定，无法应用预设";
                return;
            }

            var options = new PresetApplyOptions
            {
                Intensity = Math.Clamp(ApplyIntensity, 0.2f, 1.2f),
                Speed = Math.Max(0.5, ApplySpeed),
                TargetDurationMs = Math.Max(0, ApplyDurationMs),
            };

            ShowParameterDialog = false;
            PresetApplyRequested?.Invoke(_selectedPreset, options);
        }

        private void OnSaveAsPreset()
        {
            SavePresetRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnEditPreset()
        {
            if (_selectedPreset != null)
                EditPresetRequested?.Invoke(_selectedPreset);
        }
    }
}
