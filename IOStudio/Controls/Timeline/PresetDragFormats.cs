using Avalonia.Input;

namespace IOStudio.Controls.Timeline
{
    internal static class PresetDragFormats
    {
        public static readonly DataFormat<string> PresetId =
            DataFormat.CreateStringApplicationFormat("IOStudio.EffectPreset.Id");

        public static readonly DataFormat<string> DurationMs =
            DataFormat.CreateStringApplicationFormat("IOStudio.EffectPreset.DurationMs");
    }
}
