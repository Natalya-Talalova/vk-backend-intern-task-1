using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WukongBench;

/// <summary>
/// Запуск Benchmark Tool через Steam, ожидание нового JSON-результата и закрытие процесса.
/// Бенчмарк после окончания теста остаётся открытым на экране результатов, поэтому ждём не выхода
/// процесса, а появления нового файла в BenchMarkHistory\Tool.
/// </summary>
public sealed class BenchmarkRunner
{
    private const string SteamUri = "steam://rungameid/3132990";

    /// <summary>Процессы движка начинаются с "b1" (папки Saved, Temp\b1). Точное имя exe проверить в диспетчере задач.</summary>
    private const string ProcessPrefix = "b1";

    private static readonly string HistoryDir =
        Path.Combine(Path.GetTempPath(), "b1", "BenchMarkHistory", "Tool");

    private readonly bool _manualStart;
    private readonly (double X, double Y)? _click;
    private readonly int _startDelaySeconds;

    /// <summary>
    /// Кандидаты для пункта «Тест быстродействия» в главном меню (доли окна).
    /// Несколько точек покрывают небольшие сдвиги UI при разном aspect ratio / масштабе.
    /// </summary>
    private static readonly (double X, double Y)[] StartButtonCandidates =
    {
        (0.115, 0.45),
        (0.12, 0.42),
        (0.12, 0.48),
        (0.10, 0.45),
        (0.15, 0.45),
        (0.50, 0.55),   // запасной центр-низ, если меню выглядит иначе
    };

    /// <summary>Кандидаты для кнопки «Подтвердить» в диалоге запуска теста.</summary>
    private static readonly (double X, double Y)[] ConfirmButtonCandidates =
    {
        (0.394, 0.609),
        (0.40, 0.58),
        (0.40, 0.63),
        (0.35, 0.60),
        (0.45, 0.60),
        (0.50, 0.55),
    };

    /// <param name="click">Куда кликнуть в главном меню, в долях ширины и высоты окна (0..1). По умолчанию перебор кандидатов.</param>
    /// <param name="startDelaySeconds">Сколько ждать после появления окна, пока игра дойдёт до заставки «нажмите любую кнопку».</param>
    public BenchmarkRunner(bool manualStart, (double X, double Y)? click = null, int startDelaySeconds = 25)
    {
        _manualStart = manualStart;
        _click = click;
        _startDelaySeconds = startDelaySeconds;
        try { SetProcessDPIAware(); } catch { /* не Windows */ }
    }

    public async Task<string> RunAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        Directory.CreateDirectory(HistoryDir);
        var before = Directory.GetFiles(HistoryDir).ToHashSet(StringComparer.OrdinalIgnoreCase);

        KillAll();
        Process.Start(new ProcessStartInfo(SteamUri) { UseShellExecute = true });

        var proc = await WaitForProcessWindowAsync(TimeSpan.FromMinutes(3), ct)
                   ?? throw new TimeoutException("Окно Benchmark Tool не появилось. Проверьте ProcessPrefix и запуск через Steam.");

        if (_manualStart)
        {
            Console.WriteLine("  Нажмите «Тест быстродействия» в окне бенчмарка вручную; жду результат...");
        }
        else
        {
            await Task.Delay(TimeSpan.FromSeconds(_startDelaySeconds), ct);
            StartBenchmark(proc);
        }

        var file = await WaitForNewResultAsync(before, timeout, ct);
        KillAll();
        return file;
    }

    /// <summary>
    /// Автоматический запуск теста: пройти заставку, нажать «Тест быстродействия», подтвердить.
    /// Координаты — доли окна (GetWindowRect), поэтому не зависят от разрешения экрана.
    /// Для устойчивости: несколько точек клика + клавиатурный запасной путь + повторы.
    /// </summary>
    private void StartBenchmark(Process proc)
    {
        proc.Refresh();
        var hwnd = proc.MainWindowHandle;
        BringToFront(hwnd);
        Thread.Sleep(1000);
        if (GetForegroundWindow() != hwnd)
            Console.WriteLine("  Предупреждение: окно бенчмарка не стало активным, нажатия могут не дойти.");

        // 1) заставка «нажмите любую кнопку» — клавиатура + клик по центру
        Console.WriteLine("  Шаг 1: прохожу заставку «нажмите любую кнопку»");
        for (int i = 0; i < 3; i++)
        {
            BringToFront(hwnd);
            TapScanCode(ScanEnter);
            Thread.Sleep(80);
            TapScanCode(ScanSpace);
            Thread.Sleep(80);
            ClickInWindow(hwnd, 0.5, 0.4);
            Thread.Sleep(400);
        }

        // 2) главное меню: «Тест быстродействия»
        var startPoints = _click.HasValue
            ? new[] { _click.Value }
            : StartButtonCandidates;
        Console.WriteLine($"  Шаг 2: клик по пункту «Тест быстродействия» ({startPoints.Length} кандидат(ов))");
        foreach (var (x, y) in startPoints)
        {
            BringToFront(hwnd);
            ClickInWindow(hwnd, x, y);
            Thread.Sleep(350);
        }
        // запасной путь с клавиатуры: Enter часто подтверждает выделенный пункт
        TapScanCode(ScanEnter);
        Thread.Sleep(200);

        // 3) диалог подтверждения
        Thread.Sleep(800);
        Console.WriteLine($"  Шаг 3: подтверждаю запуск теста ({ConfirmButtonCandidates.Length} кандидат(ов) + Enter)");
        foreach (var (x, y) in ConfirmButtonCandidates)
        {
            BringToFront(hwnd);
            ClickInWindow(hwnd, x, y);
            Thread.Sleep(300);
        }
        // Enter / Space часто нажимают кнопку по умолчанию в диалоге
        for (int i = 0; i < 2; i++)
        {
            TapScanCode(ScanEnter);
            Thread.Sleep(150);
            TapScanCode(ScanSpace);
            Thread.Sleep(150);
        }
    }

    private static void BringToFront(IntPtr hwnd)
    {
        keybd_event(VkMenu, 0, 0, UIntPtr.Zero);
        keybd_event(VkMenu, 0, KeyEventKeyUp, UIntPtr.Zero);
        SetForegroundWindow(hwnd);
    }

    private static void ClickInWindow(IntPtr hwnd, double fx, double fy)
    {
        if (!GetWindowRect(hwnd, out var r)) throw new InvalidOperationException("Не удалось получить размер окна.");
        int w = r.Right - r.Left;
        int h = r.Bottom - r.Top;
        if (w < 50 || h < 50)
        {
            Console.WriteLine($"  Предупреждение: окно слишком маленькое ({w}x{h}), клик может промахнуться.");
        }
        int x = r.Left + (int)(w * fx);
        int y = r.Top + (int)(h * fy);

        // движение курсора обязательно: Unreal/UE-игры часто игнорируют «мгновенный» клик без mouse-move
        MoveMouse(x - 12, y - 12);
        Thread.Sleep(120);
        MoveMouse(x, y);
        Thread.Sleep(180);
        SendMouse(MouseLeftDown);
        Thread.Sleep(60);
        SendMouse(MouseLeftUp);
    }

    private static void MoveMouse(int x, int y)
    {
        int w = Math.Max(2, GetSystemMetrics(0)), h = Math.Max(2, GetSystemMetrics(1));
        int nx = (int)((long)Math.Clamp(x, 0, w - 1) * 65535 / (w - 1));
        int ny = (int)((long)Math.Clamp(y, 0, h - 1) * 65535 / (h - 1));
        Send(new INPUT { type = InputMouse, U = new InputUnion { mi = new MOUSEINPUT { dx = nx, dy = ny, dwFlags = MouseMove | MouseAbsolute } } });
    }

    private static void SendMouse(uint flag) =>
        Send(new INPUT { type = InputMouse, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = flag } } });

    private static void TapScanCode(ushort scan)
    {
        Send(new INPUT { type = InputKeyboard, U = new InputUnion { ki = new KEYBDINPUT { wScan = scan, dwFlags = KeyScan } } });
        Thread.Sleep(50);
        Send(new INPUT { type = InputKeyboard, U = new InputUnion { ki = new KEYBDINPUT { wScan = scan, dwFlags = KeyScan | KeyUp } } });
    }

    private static void Send(INPUT input) => SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());

    public static void KillAll()
    {
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (!p.ProcessName.StartsWith(ProcessPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                p.Kill(entireProcessTree: true);
                p.WaitForExit(10_000);
            }
            catch { /* процесс уже завершился или нет доступа */ }
        }
    }

    private static async Task<Process?> WaitForProcessWindowAsync(TimeSpan timeout, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            foreach (var p in Process.GetProcesses())
            {
                if (!p.ProcessName.StartsWith(ProcessPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                p.Refresh();
                if (p.MainWindowHandle != IntPtr.Zero) return p;
            }
            await Task.Delay(1000, ct);
        }
        return null;
    }

    private static async Task<string> WaitForNewResultAsync(HashSet<string> before, TimeSpan timeout, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            // файлы без расширения тоже бывают; берём все и фильтруем по возможности прочитать как JSON
            var candidates = Directory.GetFiles(HistoryDir)
                .Where(f => !before.Contains(f))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();
            foreach (var fresh in candidates)
            {
                for (int i = 0; i < 30; i++)
                {
                    try { BenchmarkResult.Load(fresh); return fresh; }
                    catch { await Task.Delay(1000, ct); }
                }
                throw new InvalidDataException("Файл результата появился, но не читается: " + fresh);
            }
            await Task.Delay(2000, ct);
        }
        throw new TimeoutException("Результат не появился за отведённое время.");
    }

    private const byte VkMenu = 0x12;
    private const uint KeyEventKeyUp = 0x0002;
    private const ushort ScanEnter = 0x1C;
    private const ushort ScanSpace = 0x39;

    private const uint InputMouse = 0, InputKeyboard = 1;
    private const uint KeyScan = 0x0008, KeyUp = 0x0002;
    private const uint MouseMove = 0x0001, MouseLeftDown = 0x0002, MouseLeftUp = 0x0004, MouseAbsolute = 0x8000;

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion U; }

    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
}
