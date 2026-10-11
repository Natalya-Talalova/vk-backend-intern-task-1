using System.Text.Json;

namespace WukongBench;

/// <summary>
/// Результат одного прохода из JSON, который бенчмарк сам пишет в %TEMP%\b1\BenchMarkHistory\Tool\.
/// Поле CPUUsage/CPUAvg в файле всегда равно 1 и бесполезно, поэтому узкое место определяем
/// по времени кадра: CPUFrameTime против GPUFrameTime из массива Records.
/// </summary>
public sealed record BenchmarkResult
{
    private static readonly string[] SettingKeys =
    {
        "ScreenMode", "QualityLevel", "ImageQuality", "ViewDistance", "AntiAliasing", "PostProcessing",
        "ShadowQuality", "TextureQuality", "MaterialQuality", "VegetationQuality", "MotionBlur",
        "Rtx", "Dlss", "InsertFrame", "Dx12",
    };

    public long Timestamp { get; init; }
    public int FpsAvg { get; init; }
    public int FpsMax { get; init; }
    public int FpsMin { get; init; }
    /// <summary>Поле FPS95 в JSON; на экране результатов подписано «5-й перцентиль».</summary>
    public int Fps5thPercentile { get; init; }
    public double GpuLoadAvg { get; init; }
    public double VideoMemUsedGb { get; init; }
    public string GameVersion { get; init; } = "";
    public string OsVersion { get; init; } = "";
    public string CpuModel { get; init; } = "";
    public string GpuModel { get; init; } = "";
    /// <summary>Как есть из файла; бывает неверной (у ноутбука с NVIDIA там драйвер AMD).</summary>
    public string GpuDriverReported { get; init; } = "";
    public string VideoMemSize { get; init; } = "";
    public string SysMem { get; init; } = "";
    public string Resolution { get; init; } = "";
    public IReadOnlyDictionary<string, int> Settings { get; init; } = new Dictionary<string, int>();
    public double MeanCpuFrameMs { get; init; }
    public double MeanGpuFrameMs { get; init; }
    public int Samples { get; init; }

    public double GpuToCpuRatio => MeanCpuFrameMs > 0 ? MeanGpuFrameMs / MeanCpuFrameMs : double.NaN;

    /// <summary>Эвристика: пороги 1.15 и 0.85 выбраны на глаз, при желании подправьте.</summary>
    public string Bottleneck =>
        Samples == 0 ? "нет данных" :
        GpuToCpuRatio >= 1.15 ? "упор в видеокарту (GPU-bound)" :
        GpuToCpuRatio <= 0.85 ? "упор в процессор (CPU-bound)" :
        GpuToCpuRatio < 1 ? "смешанная нагрузка, чуть ближе к упору в процессор" :
        "смешанная нагрузка, чуть ближе к упору в видеокарту";

    public static BenchmarkResult Load(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var doc = JsonDocument.Parse(fs);
        var r = doc.RootElement;

        double sumCpu = 0, sumGpu = 0;
        int n = 0;
        if (r.TryGetProperty("Records", out var recs) && recs.ValueKind == JsonValueKind.Array)
        {
            foreach (var rec in recs.EnumerateArray())
            {
                sumCpu += Num(rec, "CPUFrameTime");
                sumGpu += Num(rec, "GPUFrameTime");
                n++;
            }
        }

        var settings = new Dictionary<string, int>();
        foreach (var key in SettingKeys)
            if (r.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number)
                settings[key] = v.GetInt32();

        return new BenchmarkResult
        {
            Timestamp = (long)Num(r, "TimeStamp"),
            FpsAvg = (int)Math.Round(Num(r, "FPSAvg")),
            FpsMax = (int)Math.Round(Num(r, "FPSMax")),
            FpsMin = (int)Math.Round(Num(r, "FPSMin")),
            Fps5thPercentile = (int)Math.Round(Num(r, "FPS95")),
            GpuLoadAvg = Num(r, "GPUAvg"),
            VideoMemUsedGb = Num(r, "VideoMem"),
            GameVersion = Str(r, "GameVer"),
            OsVersion = Str(r, "SysVer"),
            CpuModel = Str(r, "CPUModel"),
            GpuModel = Str(r, "GPUModel"),
            GpuDriverReported = Str(r, "GpuDriverVer"),
            VideoMemSize = Str(r, "VideoMemSize"),
            SysMem = Str(r, "SysMem"),
            Resolution = Str(r, "ScreenResolution"),
            Settings = settings,
            MeanCpuFrameMs = n > 0 ? sumCpu / n : 0,
            MeanGpuFrameMs = n > 0 ? sumGpu / n : 0,
            Samples = n,
        };
    }

    private static double Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";
}
