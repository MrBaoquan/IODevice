using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace IOStudio.ViewModels;

/// <summary>
/// 录波器：录制采样帧序列，保存/加载 CSV（列 = 时间戳 ms + 各通道）。
/// </summary>
public class OutputRecorder
{
    private readonly object _lock = new();
    private readonly List<double[]> _frames = new();
    private List<string> _channelNames = new();

    public int FrameCount
    {
        get
        {
            lock (_lock)
            {
                return _frames.Count;
            }
        }
    }

    public void Begin(IReadOnlyList<string> channelNames, int framesToKeep)
    {
        lock (_lock)
        {
            _frames.Clear();
            _channelNames = channelNames.ToList();
        }
    }

    public void Append(double[] values)
    {
        if (values == null || values.Length == 0)
            return;
        lock (_lock)
        {
            _frames.Add((double[])values.Clone());
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _frames.Clear();
            _channelNames.Clear();
        }
    }

    public void Save(string path, double sampleIntervalMs)
    {
        lock (_lock)
        {
            if (_frames.Count == 0)
                return;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            sb.Append("ms");
            for (int c = 0; c < _channelNames.Count; c++)
                sb.Append(',').Append(_channelNames[c]);
            sb.AppendLine();

            var inv = CultureInfo.InvariantCulture;
            for (int i = 0; i < _frames.Count; i++)
            {
                sb.Append((i * sampleIntervalMs).ToString("0.###", inv));
                for (int c = 0; c < _frames[i].Length; c++)
                    sb.Append(',').Append(_frames[i][c].ToString("0.###", inv));
                sb.AppendLine();
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }
    }

    /// <summary>加载 CSV，返回 (通道数据[通道数][帧数], 采样间隔 ms, 通道名)。</summary>
    public static (double[][] channels, double intervalMs, string[] names) Load(string path)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length < 2)
            return (Array.Empty<double[]>(), 10, Array.Empty<string>());

        var names = lines[0].Split(',').Skip(1).ToArray();
        var rows = new List<double[]>();
        double? prevMs = null;
        double interval = 10;
        var inv = CultureInfo.InvariantCulture;

        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var parts = line.Split(',');
            if (parts.Length < 2)
                continue;
            double ms = double.Parse(parts[0], inv);
            if (prevMs.HasValue)
                interval = ms - prevMs.Value;
            prevMs = ms;
            var row = new double[parts.Length - 1];
            for (int c = 0; c < row.Length; c++)
                row[c] = double.Parse(parts[c + 1], inv);
            rows.Add(row);
        }

        int channelCount = names.Length;
        var channels = new double[channelCount][];
        for (int c = 0; c < channelCount; c++)
        {
            channels[c] = new double[rows.Count];
            for (int i = 0; i < rows.Count; i++)
                channels[c][i] = rows[i][c];
        }
        return (channels, interval > 0 ? interval : 10, names);
    }
}