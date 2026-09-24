using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;
using IOStudio.Models.Motion;

namespace IOStudio.Tests.Services
{
    /// <summary>
    /// .motion JSON Schema 与模型的防漂移测试。
    ///
    /// <para>为什么需要: schema 曾长期滞后于模型 —— <c>idle</c> / <c>override_keyframes</c> /
    /// <c>neutral_value</c> / <c>engineering_range</c> 等字段早已实现并被 .motion 使用，schema 里却没有。
    /// 任何"按 schema 校验"的下游 (CI、导入器、文档) 都会误判这些合法字段。本测试把模型上声明的
    /// 全部 <see cref="System.Text.Json.Serialization.JsonPropertyNameAttribute"/> 钉到 schema，
    /// 新增字段若不同步 schema 就会红。</para>
    ///
    /// <para>只覆盖真正参与 .motion 序列化的类型 (显式列举)，不含设备模板等其它模型。</para>
    /// </summary>
    public class MotionSchemaCoverageTests
    {
        private readonly ITestOutputHelper _out;

        public MotionSchemaCoverageTests(ITestOutputHelper output) => _out = output;

        /// <summary>参与 .motion 序列化的模型类型。</summary>
        private static readonly Type[] MotionTypes =
        {
            typeof(MotionTimeline),
            typeof(MotionTrack),
            typeof(MotionClip),
            typeof(MotionKeyframe),
            typeof(IdleLoop),
            typeof(ActionInstance),
            typeof(EffectPreset),
            typeof(PresetChannel),
            typeof(TrackGroup),
            typeof(TimelineEvent),
            typeof(TimelineMarker),
            typeof(EventLane),
            typeof(MarkerLane),
            typeof(MotionEvent),
        };

        [Fact]
        public void Schema_Covers_All_Serialized_Model_Properties()
        {
            string? schemaPath = FindSchemaPath();
            if (schemaPath is null)
            {
                _out.WriteLine("跳过: 未找到 IOStudio/Config/Motion/motion.schema.json");
                return;
            }
            _out.WriteLine($"schema={schemaPath}");

            var modelNames = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var type in MotionTypes)
            {
                foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    var attr = prop.GetCustomAttribute<System.Text.Json.Serialization.JsonPropertyNameAttribute>();
                    if (attr is not null)
                        modelNames.Add(attr.Name);
                }
            }
            Assert.NotEmpty(modelNames);

            var schemaNames = new HashSet<string>(StringComparer.Ordinal);
            using (var doc = JsonDocument.Parse(File.ReadAllText(schemaPath)))
                CollectPropertyNames(doc.RootElement, schemaNames);

            var missing = modelNames.Where(n => !schemaNames.Contains(n)).ToList();
            _out.WriteLine($"模型字段 {modelNames.Count} / schema 字段 {schemaNames.Count} / 缺失 {missing.Count}");

            Assert.True(
                missing.Count == 0,
                "motion.schema.json 缺少以下已序列化字段 (schema 已滞后于模型, 下游按 schema 校验会误判): "
                    + string.Join(", ", missing)
            );
        }

        /// <summary>递归收集 schema 中所有 properties 对象的键名。</summary>
        private static void CollectPropertyNames(JsonElement element, HashSet<string> sink)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    if (element.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var p in props.EnumerateObject())
                            sink.Add(p.Name);
                    }
                    foreach (var p in element.EnumerateObject())
                        CollectPropertyNames(p.Value, sink);
                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                        CollectPropertyNames(item, sink);
                    break;
            }
        }

        /// <summary>从测试输出目录向上找仓库根下的 IOStudio/Config/Motion/motion.schema.json。</summary>
        private static string? FindSchemaPath()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "IOStudio", "Config", "Motion", "motion.schema.json");
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }
    }
}
