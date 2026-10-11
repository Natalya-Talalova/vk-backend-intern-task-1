using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace WukongBench;

public sealed record GpuInfo(string Name, string DriverVersion);

/// <summary>Питание по данным Windows. OnBattery == null: батареи нет (настольный ПК) или данные недоступны.</summary>
public sealed record PowerState(bool? OnBattery, int? ChargePercent, string? Plan)
{
    public override string ToString() => (OnBattery switch
    {
        true => "от батареи",
        false => "от сети",
        null => "источник питания неизвестен",
    }) + (ChargePercent is int c ? $", заряд {c}%" : "") + (string.IsNullOrEmpty(Plan) ? "" : $", схема «{Plan}»");
}

public sealed record SystemSnapshot(
    string CpuName,
    int Cores,
    int Threads,
    double RamGb,
    string OsCaption,
    IReadOnlyList<GpuInfo> Gpus,
    string MachineName,
    string DotNetVersion,
    string? Motherboard,
    string? DisplayResolution,
    string? SystemType)
{
    /// <summary>Версия драйвера именно той видеокарты, что назвал бенчмарк.</summary>
    public string? DriverFor(string gpuModel) =>
        Gpus.FirstOrDefault(g => g.Name.Equals(gpuModel, StringComparison.OrdinalIgnoreCase)
                              || gpuModel.Contains(g.Name, StringComparison.OrdinalIgnoreCase))?.DriverVersion;
}

/// <summary>
/// Характеристики ПК через WMI. Вызываем PowerShell (Get-CimInstance), чтобы не тянуть NuGet-пакеты.
/// Если что-то пошло не так (не Windows, нет PowerShell), возвращаем null, и отчёт обойдётся данными из JSON.
/// </summary>
public static class SystemInfo
{
    private const string Script = @"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1 Name,NumberOfCores,NumberOfLogicalProcessors
$gpu = @(Get-CimInstance Win32_VideoController | Select-Object Name,DriverVersion)
$board = $null
try { $board = (Get-CimInstance Win32_BaseBoard | Select-Object -First 1 Manufacturer,Product) } catch {}
$cs = Get-CimInstance Win32_ComputerSystem | Select-Object -First 1 TotalPhysicalMemory,SystemType,Name
$os = Get-CimInstance Win32_OperatingSystem | Select-Object -First 1 Caption
$res = $null
try {
  $v = Get-CimInstance Win32_VideoController | Where-Object { $_.CurrentHorizontalResolution -gt 0 } | Select-Object -First 1
  if ($v) { $res = ""$($v.CurrentHorizontalResolution)x$($v.CurrentVerticalResolution)"" }
} catch {}
[pscustomobject]@{
  cpu = $cpu
  gpu = $gpu
  ram = $cs.TotalPhysicalMemory
  os  = $os.Caption
  machine = $cs.Name
  systemType = $cs.SystemType
  board = if ($board) { ""$($board.Manufacturer) $($board.Product)"".Trim() } else { $null }
  display = $res
} | ConvertTo-Json -Compress -Depth 4";

    private const string PowerScript = @"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$b = Get-CimInstance Win32_Battery | Select-Object -First 1 BatteryStatus,EstimatedChargeRemaining
$plan = $null
try { $plan = (Get-CimInstance -Namespace root\cimv2\power -ClassName Win32_PowerPlan | Where-Object IsActive).ElementName } catch {}
[pscustomobject]@{ battery = $b; plan = $plan } | ConvertTo-Json -Compress -Depth 3";

    /// <summary>
    /// Работает ли ноутбук от батареи. От этого сильно зависят результаты: на батарее процессор и видеокарта
    /// обычно получают меньший лимит мощности, независимо от режима в утилите производителя.
    /// Режим Armoury Crate и подобных программ Windows не показывает, его надо фиксировать вручную.
    /// </summary>
    public static PowerState TryGetPower()
    {
        try
        {
            var output = RunPowerShell(PowerScript);
            if (string.IsNullOrWhiteSpace(output)) return new PowerState(null, null, null);
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;
            string? plan = root.TryGetProperty("plan", out var pl) && pl.ValueKind == JsonValueKind.String ? pl.GetString() : null;
            if (!root.TryGetProperty("battery", out var b) || b.ValueKind != JsonValueKind.Object)
                return new PowerState(null, null, plan);
            int status = b.TryGetProperty("BatteryStatus", out var st) && st.ValueKind == JsonValueKind.Number ? st.GetInt32() : 0;
            int? charge = b.TryGetProperty("EstimatedChargeRemaining", out var ch) && ch.ValueKind == JsonValueKind.Number ? ch.GetInt32() : null;
            bool? onBattery = status == 0 ? null : status is 1 or 4 or 5;
            return new PowerState(onBattery, charge, plan);
        }
        catch
        {
            return new PowerState(null, null, null);
        }
    }

    private static string RunPowerShell(string script)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -EncodedCommand " + encoded)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("powershell не запустился");
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(30_000);
        return output;
    }

    public static SystemSnapshot? TryCollect()
    {
        try
        {
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(Script));
            var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -EncodedCommand " + encoded)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            using var p = Process.Start(psi);
            if (p == null) return null;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(30_000);
            if (string.IsNullOrWhiteSpace(output)) return null;

            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;
            var cpu = root.GetProperty("cpu");
            var gpuEl = root.GetProperty("gpu");
            var items = gpuEl.ValueKind == JsonValueKind.Array ? gpuEl.EnumerateArray().ToArray() : new[] { gpuEl };
            var gpus = items.Select(g => new GpuInfo(
                g.GetProperty("Name").GetString() ?? "",
                g.TryGetProperty("DriverVersion", out var dv) ? dv.GetString() ?? "" : "")).ToList();

            string? board = root.TryGetProperty("board", out var bd) && bd.ValueKind == JsonValueKind.String ? bd.GetString() : null;
            string? display = root.TryGetProperty("display", out var ds) && ds.ValueKind == JsonValueKind.String ? ds.GetString() : null;
            string machine = root.TryGetProperty("machine", out var mn) && mn.ValueKind == JsonValueKind.String ? mn.GetString() ?? "" : Environment.MachineName;
            string? sysType = root.TryGetProperty("systemType", out var st) && st.ValueKind == JsonValueKind.String ? st.GetString() : null;

            return new SystemSnapshot(
                (cpu.GetProperty("Name").GetString() ?? "").Trim(),
                cpu.GetProperty("NumberOfCores").GetInt32(),
                cpu.GetProperty("NumberOfLogicalProcessors").GetInt32(),
                Math.Round(root.GetProperty("ram").GetDouble() / (1024.0 * 1024 * 1024), 1),
                root.GetProperty("os").GetString() ?? "",
                gpus,
                machine,
                Environment.Version.ToString(),
                board,
                display,
                sysType);
        }
        catch
        {
            return null;
        }
    }
}
