using System.Text;

namespace WukongBench;

public sealed record RunEntry(string Title, Profile? Requested, BenchmarkResult Result, PowerState? Power = null);

/// <summary>Отчёт в Markdown: читается и в консоли, и в любом просмотрщике.</summary>
public static class ReportWriter
{
    public static string Build(SystemSnapshot? sys, IReadOnlyList<RunEntry> runs, string? userPowerMode = null)
    {
        var sb = new StringBuilder();
        var first = runs[0].Result;

        sb.AppendLine("# Black Myth: Wukong Benchmark Tool: отчёт");
        sb.AppendLine();
        sb.AppendLine("## Характеристики ПК");
        if (sys != null)
        {
            sb.AppendLine($"- Имя компьютера: {sys.MachineName}");
            sb.AppendLine($"- Процессор: {sys.CpuName} ({sys.Cores} ядер / {sys.Threads} потоков)");
            sb.AppendLine($"- Оперативная память: {sys.RamGb} ГБ");
            if (!string.IsNullOrWhiteSpace(sys.Motherboard))
                sb.AppendLine($"- Материнская плата: {sys.Motherboard}");
            if (!string.IsNullOrWhiteSpace(sys.SystemType))
                sb.AppendLine($"- Тип системы: {sys.SystemType}");
            sb.AppendLine($"- ОС: {sys.OsCaption}");
            if (!string.IsNullOrWhiteSpace(sys.DisplayResolution))
                sb.AppendLine($"- Разрешение рабочего стола: {sys.DisplayResolution}");
            foreach (var g in sys.Gpus)
                sb.AppendLine($"- Видеоадаптер: {g.Name}, драйвер {g.DriverVersion}");
            sb.AppendLine($"- .NET runtime: {sys.DotNetVersion}");
        }
        else
        {
            sb.AppendLine($"- Процессор: {first.CpuModel}");
            sb.AppendLine($"- Оперативная память: {first.SysMem}");
            sb.AppendLine($"- Видеокарта: {first.GpuModel}");
            sb.AppendLine($"- .NET runtime: {Environment.Version}");
            sb.AppendLine("- (данные WMI недоступны, использованы поля из JSON бенчмарка)");
        }
        sb.AppendLine($"- Видеопамять (по данным бенчмарка): {first.VideoMemSize}");
        sb.AppendLine($"- Версия игры/бенчмарка: {first.GameVersion}, ОС по данным бенчмарка: {first.OsVersion}");
        sb.AppendLine($"- Режим производительности ноутбука (указан пользователем, Windows его не показывает): {(string.IsNullOrWhiteSpace(userPowerMode) ? "не указан" : userPowerMode)}");
        sb.AppendLine($"- Дата отчёта: {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();

        foreach (var run in runs) AppendRun(sb, run);
        AppendLimitations(sb, runs);

        return sb.ToString();
    }

    private static void AppendLimitations(StringBuilder sb, IReadOnlyList<RunEntry> runs)
    {
        sb.AppendLine("## Условия и ограничения");
        sb.AppendLine();
        sb.AppendLine("- Наборы настроек (profiles\\cpu.ini и profiles\\gpu.ini) подобраны опытным путём на этой конфигурации и сняты с самого лаунчера бенчмарка. Выводы «упор в CPU/GPU» делаются по среднему времени кадра CPU и GPU из результата; пороги в 15% это эвристика.");
        sb.AppendLine("- Баланс между процессором и видеокартой зависит от их сочетания: на машине с более слабым процессором или более мощной видеокартой тот же CPU-профиль даст более явный упор в процессор.");
        sb.AppendLine("- Результаты зависят от режима мощности ноутбука и от питания. Здесь они получены в одном фиксированном режиме и при питании от сети; другие режимы не проверялись и могут сместить цифры в обоих тестах.");
        sb.AppendLine("- Клики по меню заданы в долях окна (не зависят от разрешения экрана) и дублируются несколькими точками + клавишами Enter/Space. При сильно отличающемся оформлении меню или нестандартном aspect ratio используйте `--manual-start` или `--click X:Y`.");
        if (runs.Count > 0 && runs[0].Result.Bottleneck.StartsWith("смешанная"))
            sb.AppendLine("- В CPU-тесте полного упора в процессор достичь не удалось: даже на минимальных настройках видеокарта остаётся близким по времени кадра ограничителем. Дальнейшее снижение разрешения почти не уменьшало время кадра GPU, а рост дальности прорисовки не увеличивал время кадра CPU.");
        sb.AppendLine();
    }

    private static void AppendRun(StringBuilder sb, RunEntry run)
    {
        var r = run.Result;
        sb.AppendLine($"## {run.Title}");
        sb.AppendLine();
        if (run.Power != null)
        {
            sb.AppendLine($"Питание во время прогона: {run.Power}");
            if (run.Power.OnBattery == true)
                sb.AppendLine("> Внимание: прогон шёл от батареи, результаты на ноутбуке при этом заметно хуже и нестабильнее. Для сравнимых цифр подключите зарядное устройство.");
            sb.AppendLine();
        }
        sb.AppendLine("Результаты (из JSON самого бенчмарка):");
        sb.AppendLine($"- Средний FPS: {r.FpsAvg}, максимум: {r.FpsMax}, минимум: {r.FpsMin}, 5-й перцентиль: {r.Fps5thPercentile}");
        sb.AppendLine($"- Средняя загрузка GPU: {r.GpuLoadAvg:0}%, видеопамять: {r.VideoMemUsedGb:0.0} ГБ");
        sb.AppendLine($"- Среднее время кадра: CPU {r.MeanCpuFrameMs:0.0} мс, GPU {r.MeanGpuFrameMs:0.0} мс (замеров: {r.Samples})");
        if (r.MeanCpuFrameMs > 0 && r.MeanGpuFrameMs > 0)
            sb.AppendLine($"- Потолок FPS по времени кадра: процессор {1000 / r.MeanCpuFrameMs:0}, видеокарта {1000 / r.MeanGpuFrameMs:0}; получено {r.FpsAvg}");
        sb.AppendLine($"- Вывод: {r.Bottleneck}");
        sb.AppendLine();

        sb.AppendLine("Применённые настройки (по данным бенчмарка):");
        sb.AppendLine($"- Разрешение: {r.Resolution}");
        sb.AppendLine("- " + string.Join(", ", r.Settings.Select(kv => $"{Label(kv.Key)}={Value(kv.Key, kv.Value)}")));
        sb.AppendLine();

        foreach (var warning in Mismatches(run))
            sb.AppendLine($"> Внимание: {warning}");
        sb.AppendLine();
    }

    /// <summary>Сверяем то, что мы записали в ini, с тем, что бенчмарк реально применил.</summary>
    private static IEnumerable<string> Mismatches(RunEntry run)
    {
        if (run.Requested == null) yield break;
        var p = run.Requested;
        var r = run.Result;

        // какое из двух разрешений (выходное или внутреннее) бенчмарк пишет в отчёт, заранее неизвестно
        bool resOk = r.Resolution == $"{p.OutputWidth} × {p.OutputHeight}"
                  || r.Resolution == $"{p.RenderWidth} × {p.RenderHeight}"
                  || r.Resolution.Contains($"{p.OutputWidth}") && r.Resolution.Contains($"{p.OutputHeight}");
        if (!resOk)
            yield return $"в профиле разрешение {p.OutputWidth} × {p.OutputHeight} (внутреннее {p.RenderWidth} × {p.RenderHeight}), бенчмарк сообщил {r.Resolution}";

        foreach (var (key, want) in Expected(p))
            if (r.Settings.TryGetValue(key, out var applied) && applied != want)
                yield return $"{Label(key)}: ожидалось {Value(key, want)}, применено {Value(key, applied)}";
    }

    /// <summary>
    /// Ожидаемые значения в терминах полей JSON. Они называются не так, как ключи в UISettingData
    /// (выяснено сопоставлением нескольких запусков):
    /// Dlss в JSON = SuperResolutionSampling (тип апскейлера), Rtx в JSON = уровень трассировки (RtxLevel, если Rtx включён),
    /// ImageQuality в ini = высота внутреннего разрешения в пикселях, а в JSON = процент от высоты экрана.
    /// QualityLevel лаунчер пересчитывает сам, его не сравниваем.
    /// </summary>
    private static Dictionary<string, int> Expected(Profile p)
    {
        var e = new Dictionary<string, int>();
        foreach (var (key, value) in p.Ui)
            if (int.TryParse(value, out var v)) e[key] = v;

        if (e.TryGetValue("SuperResolutionSampling", out var upscaler)) e["Dlss"] = upscaler;
        if (e.TryGetValue("Rtx", out var rtx) && e.TryGetValue("RtxLevel", out var level)) e["Rtx"] = rtx == 0 ? 0 : level;
        if (e.TryGetValue("ImageQuality", out var lines) && p.OutputHeight > 0)
            e["ImageQuality"] = (int)Math.Floor(lines * 100.0 / p.OutputHeight);
        e.Remove("QualityLevel");
        return e;
    }

    private static string Value(string key, int value) => key switch
    {
        "Dlss" => value switch { 0 => "0 (FSR)", 3 => "3 (TSR)", _ => value.ToString() },
        "Rtx" => value == 0 ? "выкл" : value.ToString(),
        "InsertFrame" => value == 0 ? "выкл" : value.ToString(),
        "Dx12" => value == 1 ? "12" : value.ToString(),
        _ => value.ToString(),
    };

    private static string Label(string key) => key switch
    {
        "Dlss" => "Апскейлер (код типа)",
        "ImageQuality" => "ImageQuality (% разрешения)",
        "Rtx" => "Rtx (уровень трассировки)",
        "QualityLevel" => "пресет качества",
        "ViewDistance" => "дальность",
        "AntiAliasing" => "сглаживание",
        "PostProcessing" => "пост-обработка",
        "ShadowQuality" => "тени",
        "TextureQuality" => "текстуры",
        "MaterialQuality" => "материалы",
        "VegetationQuality" => "растительность",
        "MotionBlur" => "размытие движения",
        "InsertFrame" => "генерация кадров",
        "Dx12" => "DirectX",
        "ScreenMode" => "режим экрана",
        _ => key,
    };
}
