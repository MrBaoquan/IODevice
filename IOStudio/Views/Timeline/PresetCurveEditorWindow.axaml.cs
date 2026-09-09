using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using IOStudio.Controls.Timeline;
using IOStudio.Models.Motion;

namespace IOStudio.Views.Timeline;

public partial class PresetCurveEditorWindow : Window
{
    private readonly EffectPreset _source;
    private readonly EffectPreset _edited;
    private readonly List<MotionClip> _clips = new();

    public EffectPreset EditedPreset => _edited;

    public PresetCurveEditorWindow()
        : this(
            new EffectPreset
            {
                Name = "预设曲线",
                DurationMs = 3000,
                Keyframes = new List<MotionKeyframe>
                {
                    new() { TimeMs = 0, Value = 0.5f },
                    new() { TimeMs = 3000, Value = 0.5f },
                },
            }
        ) { }

    public PresetCurveEditorWindow(EffectPreset preset)
    {
        InitializeComponent();
        _source = preset ?? throw new ArgumentNullException(nameof(preset));
        _edited = ClonePreset(preset);
        var duration = _edited.DurationMs > 0 ? _edited.DurationMs : 3000;
        if (_edited.Keyframes is { Count: > 0 })
        {
            _clips.Add(
                new MotionClip
                {
                    StartMs = 0,
                    EndMs = duration,
                    Keyframes = GetKeyframes(_edited)
                }
            );
        }
        else
        {
            foreach (var channel in _edited.Channels)
                _clips.Add(
                    new MotionClip
                    {
                        StartMs = 0,
                        EndMs = duration,
                        Keyframes = channel.Keyframes?.Select(CloneKeyframe).ToList() ?? new()
                    }
                );
        }

        PresetTitle.Text = $"{_edited.Name}  ·  预设曲线";
        CurveEditor.DurationMs = duration;
        CurveEditor.CurveTracks = _clips
            .Select(
                (clip, index) =>
                    new CurveTrackData
                    {
                        Label = _edited.Keyframes is { Count: > 0 }
                            ? "数值曲线"
                            : _edited.Channels[index].Role,
                        Color = index == 0 ? "#4FC3F7" : "#a78bfa",
                        Clips = new List<MotionClip> { clip },
                    }
            )
            .ToList();
        UpdateSummary();

        CurveEditor.KeyframeMoved += (_, _, _, _, _) => UpdateSummary();
        CurveEditor.AddKeyframeRequested += (trackIndex, time, value) =>
        {
            if (trackIndex < 0 || trackIndex >= _clips.Count)
                return;
            _clips[trackIndex].Keyframes.Add(new MotionKeyframe { TimeMs = time, Value = value });
            _clips[trackIndex].Keyframes.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));
            CurveEditor.InvalidateVisual();
            UpdateSummary();
        };
        CurveEditor.InterpolationChanged += (trackIndex, _, index, interpolation) =>
        {
            if (
                trackIndex >= 0
                && trackIndex < _clips.Count
                && index >= 0
                && index < _clips[trackIndex].Keyframes.Count
            )
                _clips[trackIndex].Keyframes[index].Interpolation = interpolation;
        };
        CurveEditor.TangentChanged += (trackIndex, _, index, tin, tout, cp1x, cp2x) =>
        {
            if (
                trackIndex >= 0
                && trackIndex < _clips.Count
                && index >= 0
                && index < _clips[trackIndex].Keyframes.Count
            )
            {
                var keyframe = _clips[trackIndex].Keyframes[index];
                keyframe.TangentIn = tin;
                keyframe.TangentOut = tout;
                keyframe.Cp1x = cp1x;
                keyframe.Cp2x = cp2x;
            }
        };
    }

    /// <summary>带上下文标签的构造 — 用于区分"编辑动作实例曲线"与"编辑预设模板"。</summary>
    public PresetCurveEditorWindow(EffectPreset preset, string? contextLabel)
        : this(preset)
    {
        if (string.IsNullOrEmpty(contextLabel))
            return;
        PresetTitle.Text = $"{_edited.Name}  ·  {contextLabel}";
        PresetHint.Text =
            contextLabel == "动作曲线"
                ? "编辑时间轴中该动作的关键帧；保存后立即生效，可播放预览（Ctrl+Z 可撤销）。"
                : "编辑预设模板曲线；保存后生成新的预设修订。";
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        _edited.DurationMs = Math.Max(
            _edited.DurationMs,
            _clips.Select(c => c.EndMs).DefaultIfEmpty(0).Max()
        );
        if (_edited.Keyframes is { Count: > 0 })
            _edited.Keyframes = _clips[0].Keyframes.Select(CloneKeyframe).ToList();
        else
        {
            for (var i = 0; i < _edited.Channels.Count && i < _clips.Count; i++)
                _edited.Channels[i].Keyframes = _clips[i].Keyframes.Select(CloneKeyframe).ToList();
        }
        _edited.Revision = Math.Max(1, _source.Revision + 1);
        Close(true);
    }

    private void UpdateSummary() =>
        KeyframeSummary.Text =
            $"{_clips.Sum(c => c.Keyframes.Count)} 个关键帧 · {_edited.DurationMs / 1000.0:F2}s";

    private static List<MotionKeyframe> GetKeyframes(EffectPreset preset) =>
        preset.Keyframes is { Count: > 0 }
            ? preset.Keyframes.Select(CloneKeyframe).ToList()
            : preset.Channels.FirstOrDefault()?.Keyframes?.Select(CloneKeyframe).ToList() ?? new();

    private static EffectPreset ClonePreset(EffectPreset source) =>
        new()
        {
            Id = source.Id,
            Revision = source.Revision,
            Name = source.Name,
            Category = source.Category,
            Description = source.Description,
            DurationMs = source.DurationMs,
            DefaultIntensity = source.DefaultIntensity,
            MinIntensity = source.MinIntensity,
            MaxIntensity = source.MaxIntensity,
            BaseId = source.BaseId,
            Keyframes = source.Keyframes?.Select(CloneKeyframe).ToList(),
            Channels = source.Channels
                .Select(
                    c =>
                        new PresetChannel
                        {
                            Role = c.Role,
                            Scale = c.Scale,
                            PhaseOffsetMs = c.PhaseOffsetMs,
                            TemplateRef = c.TemplateRef,
                            Keyframes = c.Keyframes?.Select(CloneKeyframe).ToList(),
                        }
                )
                .ToList(),
        };

    private static MotionKeyframe CloneKeyframe(MotionKeyframe source) =>
        new()
        {
            TimeMs = source.TimeMs,
            Value = source.Value,
            Interpolation = source.Interpolation,
            TangentIn = source.TangentIn,
            TangentOut = source.TangentOut,
            Cp1x = source.Cp1x,
            Cp2x = source.Cp2x,
            Event = source.Event,
        };
}
