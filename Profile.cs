using System.Text.RegularExpressions;

namespace WukongBench;

/// <summary>
/// Профиль = готовый GameUserSettings.ini, снятый с самого лаунчера (profiles\cpu.ini, profiles\gpu.ini).
/// Целиком, потому что лаунчер берёт часть настроек (дальность прорисовки, растительность, масштаб
/// разрешения и т.д.) не из UISettingData, а из блока [ScalabilityGroups], и правка одной строки
/// оставляла такие настройки прежними.
/// Чтобы сменить профиль, выставьте настройки в лаунчере, закройте его и скопируйте ini в profiles\.
/// </summary>
public sealed record Profile(
    string Name,
    string TemplateText,
    IReadOnlyDictionary<string, string> Ui,
    int OutputWidth, int OutputHeight,
    int RenderWidth, int RenderHeight)
{
    public static Profile Load(string name, string path)
    {
        var text = File.ReadAllText(path);
        int outW = IntLine(text, "ResolutionSizeX"), outH = IntLine(text, "ResolutionSizeY");
        int renW = IntLine(text, "DesiredScreenWidth"), renH = IntLine(text, "DesiredScreenHeight");
        // в некоторых файлах строк DesiredScreen* нет вообще
        return new Profile(name, text, ParseUi(text, path), outW, outH, renW > 0 ? renW : outW, renH > 0 ? renH : outH);
    }

    private static Dictionary<string, string> ParseUi(string text, string path)
    {
        var line = Regex.Match(text, "^UISettingData=[^\\r\\n]*", RegexOptions.Multiline);
        if (!line.Success) throw new InvalidDataException($"В {path} нет строки UISettingData.");
        var ui = new Dictionary<string, string>();
        foreach (Match m in Regex.Matches(line.Value, "\\(\"([^\"]+)\",\\s*\"([^\"]*)\"\\)"))
            ui[m.Groups[1].Value] = m.Groups[2].Value;
        return ui;
    }

    private static int IntLine(string text, string key)
    {
        var m = Regex.Match(text, "^" + key + "=(\\d+)", RegexOptions.Multiline);
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }
}
