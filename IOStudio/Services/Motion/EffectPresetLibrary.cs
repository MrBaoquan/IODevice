using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using IOStudio.Models.Motion;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// 效果预设库服务 — 预设 CRUD + .mfx JSON 持久化 + 内置预设。
    /// <para>
    /// 生命周期: 加载时合并"内置预设 + 用户预设文件(.mfx)"；
    /// 保存时仅写用户预设到指定目录, 内置预设随程序分发不可覆盖。
    /// </para>
    /// </summary>
    public class EffectPresetLibrary
    {
        private readonly List<EffectPreset> _presets = new();

        private static readonly JsonSerializerOptions _jsonOptions =
            new()
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };

        /// <summary>用户预设目录 (默认相对程序运行目录 Config/EffectPresets)</summary>
        public string UserPresetDirectory { get; set; }

        /// <summary>所有预设 (内置 + 用户)</summary>
        public IReadOnlyList<EffectPreset> AllPresets => _presets;

        public EffectPresetLibrary(string? userPresetDirectory = null)
        {
            // 默认目录: 相对程序运行目录 Config/EffectPresets
            UserPresetDirectory =
                userPresetDirectory
                ?? Path.Combine(AppContext.BaseDirectory, "Config", "EffectPresets");
        }

        /// <summary>初始化: 载入内置预设 + 用户预设文件 + 收藏状态</summary>
        public void Initialize()
        {
            _presets.Clear();
            _presets.AddRange(BuildBuiltInPresets());

            LoadUserPresets();
            LoadFavorites();
        }

        /// <summary>按 ID 查找预设，并展开 BaseId 继承。</summary>
        public EffectPreset? FindPreset(string id)
        {
            return ResolvePreset(id, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>按名称模糊查找</summary>
        public List<EffectPreset> Search(string? keyword, string? category = null)
        {
            var query = _presets.AsEnumerable();

            if (!string.IsNullOrEmpty(category))
                query = query.Where(
                    p => string.Equals(p.Category, category, StringComparison.OrdinalIgnoreCase)
                );

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var kw = keyword.Trim();
                query = query.Where(
                    p =>
                        p.Name.Contains(kw, StringComparison.OrdinalIgnoreCase)
                        || p.Id.Contains(kw, StringComparison.OrdinalIgnoreCase)
                        || (
                            p.Description?.Contains(kw, StringComparison.OrdinalIgnoreCase) ?? false
                        )
                );
            }

            return query.Select(p => FindPreset(p.Id) ?? p).ToList();
        }

        private EffectPreset? ResolvePreset(string id, HashSet<string> resolving)
        {
            if (string.IsNullOrWhiteSpace(id) || !resolving.Add(id))
                return null;

            var source = _presets.FirstOrDefault(
                p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)
            );
            if (source == null)
                return null;

            var resolved = string.IsNullOrWhiteSpace(source.BaseId)
                ? ClonePreset(source)
                : ResolvePreset(source.BaseId, resolving);
            if (resolved == null)
                return null;

            if (!string.IsNullOrWhiteSpace(source.BaseId))
            {
                resolved.Id = source.Id;
                resolved.Revision = source.Revision;
                resolved.Name = string.IsNullOrWhiteSpace(source.Name)
                    ? resolved.Name
                    : source.Name;
                resolved.Category = string.IsNullOrWhiteSpace(source.Category)
                    ? resolved.Category
                    : source.Category;
                resolved.Description = source.Description ?? resolved.Description;
                resolved.DurationMs =
                    source.DurationMs > 0 ? source.DurationMs : resolved.DurationMs;
                resolved.DefaultIntensity = source.DefaultIntensity;
                resolved.MinIntensity = source.MinIntensity;
                resolved.MaxIntensity = source.MaxIntensity;
                resolved.BaseId = source.BaseId;
                if (source.Keyframes != null)
                    resolved.Keyframes = CloneKeyframes(source.Keyframes);

                foreach (var channel in source.Channels)
                {
                    int index = resolved.Channels.FindIndex(
                        c => string.Equals(c.Role, channel.Role, StringComparison.OrdinalIgnoreCase)
                    );
                    if (index >= 0)
                        resolved.Channels[index] = CloneChannel(channel);
                    else
                        resolved.Channels.Add(CloneChannel(channel));
                }
            }

            resolving.Remove(id);
            return resolved;
        }

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
                Keyframes = CloneKeyframes(source.Keyframes),
                Channels = source.Channels.Select(CloneChannel).ToList(),
            };

        private static List<MotionKeyframe>? CloneKeyframes(IEnumerable<MotionKeyframe>? source) =>
            source
                ?.Select(
                    k =>
                        new MotionKeyframe
                        {
                            TimeMs = k.TimeMs,
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

        private static PresetChannel CloneChannel(PresetChannel source) =>
            new()
            {
                Role = source.Role,
                Scale = source.Scale,
                PhaseOffsetMs = source.PhaseOffsetMs,
                TemplateRef = source.TemplateRef,
                Keyframes = CloneKeyframes(source.Keyframes),
            };

        /// <summary>所有分类</summary>
        public List<string> GetCategories()
        {
            return _presets
                .Select(p => p.Category)
                .Where(c => !string.IsNullOrEmpty(c))
                .Distinct()
                .ToList();
        }

        /// <summary>
        /// 添加预设。若 Id 已存在则替换, 返回是否新建。
        /// 新建预设自动分配 8 位 hex Id (未提供时)。
        /// </summary>
        public bool Upsert(EffectPreset preset)
        {
            if (preset == null)
                return false;

            if (string.IsNullOrEmpty(preset.Id))
                preset.Id = Guid.NewGuid().ToString("N")[..8];

            int idx = _presets.FindIndex(p => p.Id == preset.Id);
            bool isNew = idx < 0;
            if (isNew)
                _presets.Add(preset);
            else
                _presets[idx] = preset;

            return isNew;
        }

        /// <summary>按 Id 删除预设。内置预设不可删除, 返回 false。</summary>
        public bool Delete(string id)
        {
            if (string.IsNullOrEmpty(id))
                return false;

            // 内置预设不可删除 (校验: Id 不在内置集合中)
            if (IsBuiltIn(id))
                return false;

            int idx = _presets.FindIndex(p => p.Id == id);
            if (idx < 0)
                return false;

            _presets.RemoveAt(idx);
            return true;
        }

        /// <summary>是否内置预设</summary>
        public bool IsBuiltIn(string id)
        {
            return BuildBuiltInPresets().Any(p => p.Id == id);
        }

        // ── 持久化 (用户预设 .mfx) ──────────────────────────────

        /// <summary>保存单个用户预设为 .mfx 文件</summary>
        public bool SaveToFile(EffectPreset preset, string? filePath = null)
        {
            if (preset == null)
                return false;

            var dir = UserPresetDirectory;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var path = filePath ?? Path.Combine(dir, $"{SanitizeFileName(preset.Id)}.mfx");

            try
            {
                var json = JsonSerializer.Serialize(preset, _jsonOptions);
                File.WriteAllText(path, json);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[EffectPresetLibrary] Save failed: {ex.Message}"
                );
                return false;
            }
        }

        /// <summary>保存全部用户预设 (删除 matched 目录中的旧文件后重写)</summary>
        public bool SaveAll()
        {
            var dir = UserPresetDirectory;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            try
            {
                // 删除目录下旧 .mfx (避免已删除的预设残留)
                foreach (var f in Directory.GetFiles(dir, "*.mfx"))
                    File.Delete(f);

                bool ok = true;
                foreach (var preset in _presets)
                {
                    if (IsBuiltIn(preset.Id))
                        continue; // 内置预设不写入用户目录
                    ok &= SaveToFile(preset);
                }
                return ok;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[EffectPresetLibrary] SaveAll failed: {ex.Message}"
                );
                return false;
            }
        }

        /// <summary>从 .mfx 文件加载预设</summary>
        public EffectPreset? LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
                return null;

            try
            {
                var json = File.ReadAllText(filePath);
                var preset = JsonSerializer.Deserialize<EffectPreset>(json, _jsonOptions);
                return preset;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[EffectPresetLibrary] Load failed: {ex.Message}"
                );
                return null;
            }
        }

        /// <summary>加载用户目录下所有 .mfx 预设</summary>
        public void LoadUserPresets()
        {
            if (!Directory.Exists(UserPresetDirectory))
                return;

            foreach (var file in Directory.GetFiles(UserPresetDirectory, "*.mfx"))
            {
                var preset = LoadFromFile(file);
                if (preset != null && !string.IsNullOrEmpty(preset.Id))
                {
                    // 覆盖同名 (允许用户预设覆盖内置)
                    int idx = _presets.FindIndex(p => p.Id == preset.Id);
                    if (idx >= 0)
                        _presets[idx] = preset;
                    else
                        _presets.Add(preset);
                }
            }
        }

        // ── 收藏 (面板状态, favorites.json 持久化) ────────────

        private readonly HashSet<string> _favorites = new(StringComparer.OrdinalIgnoreCase);

        private string FavoritesPath => Path.Combine(UserPresetDirectory, "favorites.json");

        /// <summary>加载收藏 Id 列表 (Initialize 时调用)。</summary>
        public void LoadFavorites()
        {
            _favorites.Clear();
            try
            {
                if (File.Exists(FavoritesPath))
                {
                    var json = File.ReadAllText(FavoritesPath);
                    var ids = JsonSerializer.Deserialize<List<string>>(json, _jsonOptions);
                    if (ids != null)
                        foreach (var id in ids)
                            _favorites.Add(id);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[EffectPresetLibrary] LoadFavorites failed: {ex.Message}"
                );
            }
        }

        /// <summary>持久化收藏 Id 列表。</summary>
        public void SaveFavorites()
        {
            try
            {
                var dir = UserPresetDirectory;
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(_favorites.ToList(), _jsonOptions);
                File.WriteAllText(FavoritesPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[EffectPresetLibrary] SaveFavorites failed: {ex.Message}"
                );
            }
        }

        /// <summary>是否收藏。</summary>
        public bool IsFavorite(string id) => !string.IsNullOrEmpty(id) && _favorites.Contains(id);

        /// <summary>切换收藏, 返回切换后状态。</summary>
        public bool ToggleFavorite(string id)
        {
            if (string.IsNullOrEmpty(id))
                return false;
            bool fav = _favorites.Contains(id);
            if (fav)
                _favorites.Remove(id);
            else
                _favorites.Add(id);
            SaveFavorites();
            return !fav;
        }

        /// <summary>设置收藏状态 (供初始化时按现有收藏标记)。</summary>
        public void SetFavorite(string id, bool fav)
        {
            if (string.IsNullOrEmpty(id))
                return;
            if (fav)
                _favorites.Add(id);
            else
                _favorites.Remove(id);
        }

        /// <summary>导出预设为 JSON 字符串 (用于分享/复制)</summary>
        public string ExportJson(EffectPreset preset)
        {
            return preset == null ? "{}" : JsonSerializer.Serialize(preset, _jsonOptions);
        }

        /// <summary>从 JSON 字符串导入预设</summary>
        public EffectPreset? ImportJson(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<EffectPreset>(json, _jsonOptions);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>清理预设文件名中的非法字符</summary>
        private static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        // ── 内置预设 ────────────────────────────────────────────

        private static List<EffectPreset> BuildBuiltInPresets()
        {
            return new List<EffectPreset>
            {
                BuildWeightlessRise(),
                BuildWeightlessFall(),
                BuildDive(),
                BuildImpactVibration(),
                BuildEmergencyStop(),
            };
        }

        private static MotionKeyframe Kf(
            double timeMs,
            float value,
            string interp,
            float? tIn = null,
            float? tOut = null
        )
        {
            return new MotionKeyframe
            {
                TimeMs = timeMs,
                Value = value,
                Interpolation = interp,
                TangentIn = tIn,
                TangentOut = tOut,
            };
        }

        /// <summary>失重上升 — 升降主导 + 俯仰后仰 + 滚转微摆 (3s, 3通道)</summary>
        private static EffectPreset BuildWeightlessRise()
        {
            return new EffectPreset
            {
                Id = "fx_weightless_rise",
                Name = "失重上升",
                Category = "失重效果",
                Description = "快速上升失重感: 升降迅速上冲(超调) → 短暂悬浮回落 → 稳定回中; 俯仰后仰 + 滚转微摆联动错峰",
                DurationMs = 3000,
                DefaultIntensity = 0.8f,
                MinIntensity = 0.2f,
                MaxIntensity = 1.2f,
                Channels = new List<PresetChannel>
                {
                    new()
                    {
                        Role = "heave",
                        Scale = 1.0f,
                        Keyframes = new List<MotionKeyframe>
                        {
                            Kf(0, 0.50f, "bezier", null, 1.5f), // 静止
                            Kf(600, 0.88f, "bezier", 0.8f, 0.3f), // 快速上升(超调)
                            Kf(1800, 0.58f, "bezier", -0.3f, -0.5f), // 失重回落
                            Kf(3000, 0.50f, "ease_in_out"), // 稳定回中
                        }
                    },
                    new()
                    {
                        Role = "pitch",
                        Scale = 0.4f,
                        PhaseOffsetMs = 150,
                        Keyframes = new List<MotionKeyframe>
                        {
                            Kf(0, 0.50f, "bezier", null, 1.0f), // 静止
                            Kf(600, 0.62f, "bezier", 0.6f, 0.2f), // 后仰
                            Kf(2400, 0.55f, "bezier", -0.2f, -0.3f), // 回正
                            Kf(3000, 0.50f, "ease_in_out"),
                        }
                    },
                    new()
                    {
                        Role = "roll",
                        Scale = 0.2f,
                        PhaseOffsetMs = 300,
                        Keyframes = new List<MotionKeyframe>
                        {
                            Kf(0, 0.50f, "bezier", null, 0.6f), // 静止
                            Kf(900, 0.56f, "bezier", 0.4f, 0.1f), // 微倾
                            Kf(2100, 0.47f, "bezier", -0.3f, -0.2f), // 反向微倾
                            Kf(3000, 0.50f, "ease_in_out"),
                        }
                    }
                }
            };
        }

        /// <summary>失重下降 — 急降 + 悬浮 + 缓冲 (2.5s, 3通道)</summary>
        private static EffectPreset BuildWeightlessFall()
        {
            return new EffectPreset
            {
                Id = "fx_weightless_fall",
                Name = "失重下降",
                Category = "失重效果",
                Description = "自由落体失重感: 急速下沉 → 短暂失重悬浮 → 缓冲稳定; 俯仰前倾 + 滚转微摆联动错峰",
                DurationMs = 2500,
                DefaultIntensity = 0.8f,
                MinIntensity = 0.2f,
                MaxIntensity = 1.2f,
                Channels = new List<PresetChannel>
                {
                    new()
                    {
                        Role = "heave",
                        Scale = 1.0f,
                        Keyframes = new List<MotionKeyframe>
                        {
                            Kf(0, 0.50f, "bezier", null, -1.2f), // 静止
                            Kf(400, 0.12f, "bezier", 0.2f, 0.6f), // 急速下降
                            Kf(1200, 0.38f, "bezier", -0.2f, -0.4f), // 失重悬浮
                            Kf(2500, 0.50f, "ease_in_out"), // 缓冲稳定
                        }
                    },
                    new()
                    {
                        Role = "pitch",
                        Scale = 0.4f,
                        PhaseOffsetMs = 100,
                        Keyframes = new List<MotionKeyframe>
                        {
                            Kf(0, 0.50f, "bezier", null, -0.6f), // 静止
                            Kf(500, 0.38f, "bezier", 0.3f, 0.1f), // 前倾
                            Kf(1500, 0.45f, "bezier", -0.2f, -0.2f), // 回正
                            Kf(2500, 0.50f, "ease_in_out"),
                        }
                    },
                    new()
                    {
                        Role = "roll",
                        Scale = 0.2f,
                        PhaseOffsetMs = 200,
                        Keyframes = new List<MotionKeyframe>
                        {
                            Kf(0, 0.50f, "bezier", null, 0.4f), // 静止
                            Kf(600, 0.55f, "bezier", 0.3f, 0.1f), // 微倾
                            Kf(1600, 0.46f, "bezier", -0.2f, -0.2f), // 反向
                            Kf(2500, 0.50f, "ease_in_out"),
                        }
                    }
                }
            };
        }

        /// <summary>俯冲 — 俯仰前俯 + 升降低位 (2s, 2通道)</summary>
        private static EffectPreset BuildDive()
        {
            return new EffectPreset
            {
                Id = "fx_dive",
                Name = "俯冲",
                Category = "过山车",
                Description = "急速俯冲: 俯仰快速前俯并保持低位 → 回正; 升降同步下沉",
                DurationMs = 2000,
                DefaultIntensity = 0.9f,
                MinIntensity = 0.3f,
                MaxIntensity = 1.2f,
                Channels = new List<PresetChannel>
                {
                    new()
                    {
                        Role = "pitch",
                        Scale = 1.0f,
                        Keyframes = new List<MotionKeyframe>
                        {
                            Kf(0, 0.50f, "bezier", null, -1.0f), // 静止
                            Kf(400, 0.15f, "bezier", 0.2f, 0.8f), // 急速前俯
                            Kf(1200, 0.20f, "bezier", 0.3f, 0.5f), // 保持俯冲
                            Kf(2000, 0.50f, "ease_in_out"), // 回正
                        }
                    },
                    new()
                    {
                        Role = "heave",
                        Scale = 0.6f,
                        PhaseOffsetMs = 80,
                        Keyframes = new List<MotionKeyframe>
                        {
                            Kf(0, 0.50f, "bezier", null, -0.8f), // 静止
                            Kf(400, 0.30f, "bezier", 0.3f, 0.2f), // 下沉
                            Kf(1200, 0.35f, "bezier", 0.2f, 0.3f), // 保持低位
                            Kf(2000, 0.50f, "ease_in_out"), // 回中
                        }
                    }
                }
            };
        }

        /// <summary>撞击震动 — 单通道高频衰减震荡 (1s, 1通道)</summary>
        private static EffectPreset BuildImpactVibration()
        {
            return new EffectPreset
            {
                Id = "fx_impact_vibration",
                Name = "撞击震动",
                Category = "冲击震动",
                Description = "撞击冲击波: 瞬间冲击 → 高频衰减震荡 → 稳定回中",
                DurationMs = 1000,
                DefaultIntensity = 0.9f,
                MinIntensity = 0.3f,
                MaxIntensity = 1.2f,
                Channels = new List<PresetChannel>
                {
                    new()
                    {
                        Role = "impact",
                        Scale = 1.0f,
                        Keyframes = new List<MotionKeyframe>
                        {
                            Kf(0, 0.50f, "step"), // 起始
                            Kf(50, 0.80f, "bezier", null, -0.6f), // 冲击
                            Kf(150, 0.30f, "bezier", 0.4f, 0.5f), // 反弹
                            Kf(300, 0.60f, "bezier", -0.3f, -0.4f), // 衰减
                            Kf(450, 0.40f, "bezier", 0.2f, 0.3f), // 衰减
                            Kf(600, 0.55f, "bezier", -0.2f, -0.2f), // 趋稳
                            Kf(750, 0.45f, "bezier", 0.1f, 0.1f), // 趋稳
                            Kf(1000, 0.50f, "ease_in_out"), // 稳定
                        }
                    }
                }
            };
        }

        /// <summary>急停 — 快速停止 + 过冲修正 (0.5s, 1通道)</summary>
        private static EffectPreset BuildEmergencyStop()
        {
            return new EffectPreset
            {
                Id = "fx_emergency_stop",
                Name = "急停",
                Category = "冲击震动",
                Description = "快速急停: 瞬间减速 → 过冲修正 → 稳定回中",
                DurationMs = 500,
                DefaultIntensity = 0.8f,
                MinIntensity = 0.2f,
                MaxIntensity = 1.2f,
                Channels = new List<PresetChannel>
                {
                    new()
                    {
                        Role = "stop",
                        Scale = 1.0f,
                        Keyframes = new List<MotionKeyframe>
                        {
                            Kf(0, 0.50f, "step"), // 当前值
                            Kf(50, 0.35f, "bezier", null, -0.4f), // 急停过冲
                            Kf(150, 0.58f, "bezier", 0.3f, 0.2f), // 修正回弹
                            Kf(300, 0.48f, "bezier", -0.2f, -0.2f), // 趋稳
                            Kf(500, 0.50f, "ease_in_out"), // 稳定
                        }
                    }
                }
            };
        }
    }
}
