using System.Text;
using WukongBench;

Console.OutputEncoding = Encoding.UTF8;

// Использование:
//   WukongBench [папка_установки] [--manual-start] [--out report.md] [--profiles папка_с_cpu.ini_и_gpu.ini] [--mode "режим мощности"] [--click X:Y] [--delay секунд]
//   WukongBench --report result.json      (только отчёт по готовому JSON, без запуска игры)
var argList = args.ToList();
// Совместимость со старой командой запуска: раньше --try-start включал короткий диагностический прогон,
// который закрывал бенчмарк через 30 секунд после нажатий. Теперь этот флаг не сокращает тест:
// при его наличии всё равно выполняется полный сценарий CPU + GPU.
bool legacyTryStart = argList.Remove("--try-start");
if (legacyTryStart)
    Console.WriteLine("Обнаружен старый флаг --try-start. Запускаю полный сценарий CPU- и GPU-тестов, без раннего закрытия.");

bool manualStart = argList.Remove("--manual-start");
string outPath = TakeValue(argList, "--out") ?? "benchmark_report.md";
string? reportOnly = TakeValue(argList, "--report");
string? powerMode = TakeValue(argList, "--mode");
int startDelay = int.TryParse(TakeValue(argList, "--delay"), out var d) && d >= 0 ? d : 25;   // секунд до заставки «нажмите любую кнопку»
(double X, double Y)? click = ParseClick(TakeValue(argList, "--click"));   // например: --click 0.50:0.62   // например: --mode "Эффективность (Armoury Crate), от сети"
string profilesDir = TakeValue(argList, "--profiles") ?? Path.Combine(AppContext.BaseDirectory, "profiles");

if (reportOnly != null)
{
    var result = BenchmarkResult.Load(reportOnly);
    var text = ReportWriter.Build(SystemInfo.TryCollect(), new[] { new RunEntry("Прогон из файла", null, result) });
    Console.WriteLine(text);
    return 0;
}

string installDir = argList.FirstOrDefault()
    ?? @"C:\Program Files (x86)\Steam\steamapps\common\Black Myth Wukong Benchmark Tool";
string iniPath = Path.Combine(installDir, "b1", "Saved", "Config", "Windows", "GameUserSettings.ini");
if (!File.Exists(iniPath))
{
    Console.Error.WriteLine($"Не найден {iniPath}. Укажите папку установки первым аргументом.");
    return 1;
}

Profile cpuProfile, gpuProfile;
try
{
    cpuProfile = Profile.Load("CPU-тест", Path.Combine(profilesDir, "cpu.ini"));
    gpuProfile = Profile.Load("GPU-тест", Path.Combine(profilesDir, "gpu.ini"));
}
catch (Exception e) when (e is IOException or InvalidDataException)
{
    Console.Error.WriteLine($"Не удалось прочитать профили из {profilesDir}: {e.Message}");
    return 1;
}

var sys = SystemInfo.TryCollect();

// Питание сильно влияет на результат: на батарее ноутбук режет мощность CPU и GPU.
var power = SystemInfo.TryGetPower();
Console.WriteLine($"Питание: {power}");
if (power.OnBattery == true)
{
    Console.WriteLine("ПРЕДУПРЕЖДЕНИЕ: ноутбук работает от батареи. Продолжаю автоматически, но результаты могут быть ниже и менее стабильны. Для корректного сравнения подключите зарядное устройство.");
}
var editor = new IniEditor(iniPath);
var runner = new BenchmarkRunner(manualStart, click, startDelay);
var runs = new List<RunEntry>();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };   // Ctrl+C: дойти до finally и вернуть настройки

bool runFailed = false;
editor.Backup();
try
{
    foreach (var profile in new[] { cpuProfile, gpuProfile })
    {
        Console.WriteLine();
        Console.WriteLine($"== {profile.Name}: записываю настройки и запускаю бенчмарк");
        Console.WriteLine("   После запуска теста программа дождётся завершения и нового JSON-файла (до 15 минут).");
        editor.Apply(profile);
        var file = await runner.RunAsync(TimeSpan.FromMinutes(15), cts.Token);
        var result = BenchmarkResult.Load(file);
        Console.WriteLine($"   JSON получен: {Path.GetFileName(file)}");
        Console.WriteLine($"   готово: {result.FpsAvg} FPS, {result.Bottleneck}");
        runs.Add(new RunEntry(profile.Name, profile, result, SystemInfo.TryGetPower()));
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("Прервано пользователем.");
    runFailed = true;
}
catch (Exception e)
{
    Console.Error.WriteLine($"Ошибка во время бенчмарка: {e.Message}");
    runFailed = true;
}
finally
{
    BenchmarkRunner.KillAll();
    editor.Restore();   // исходные настройки пользователя возвращаются всегда
    Console.WriteLine("Исходные настройки Benchmark Tool восстановлены.");
}

if (runFailed || cts.IsCancellationRequested || runs.Count != 2)
{
    Console.Error.WriteLine($"Полный отчёт не создан: завершено {runs.Count} из 2 тестов. Исправьте ошибку и запустите программу снова.");
    return 1;
}

var report = ReportWriter.Build(sys, runs, powerMode);
Console.WriteLine();
File.WriteAllText(outPath, report, new UTF8Encoding(false));
Console.WriteLine($"Отчёт сохранён: {Path.GetFullPath(outPath)}");
return 0;

static string? TakeValue(List<string> list, string flag)
{
    int i = list.IndexOf(flag);
    if (i < 0 || i + 1 >= list.Count) return null;
    var value = list[i + 1];
    list.RemoveRange(i, 2);
    return value;
}

static (double X, double Y)? ParseClick(string? text)
{
    if (text == null) return null;
    var parts = text.Split(':');
    if (parts.Length == 2
        && double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x)
        && double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y)
        && x is >= 0 and <= 1 && y is >= 0 and <= 1)
        return (x, y);
    Console.Error.WriteLine("--click ожидает две доли от 0 до 1 через двоеточие, например 0.50:0.62");
    Environment.Exit(1);
    return null;
}
