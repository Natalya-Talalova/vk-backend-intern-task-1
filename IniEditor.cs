using System.Text.RegularExpressions;

namespace WukongBench;

/// <summary>
/// Подменяет GameUserSettings.ini профилем целиком. Единственное, что берём из исходного файла пользователя,
/// это MainMonitorID (он привязан к конкретному монитору). Исходный файл сохраняется рядом и возвращается в Restore().
/// </summary>
public sealed class IniEditor
{
    private readonly string _path;
    private readonly string _backup;

    public IniEditor(string path)
    {
        _path = path;
        _backup = path + ".bench-backup";
    }

    /// <summary>Если бэкап остался от аварийно завершённого запуска, исходным считаем его.</summary>
    public void Backup()
    {
        if (File.Exists(_backup)) File.Copy(_backup, _path, overwrite: true);
        else File.Copy(_path, _backup);
    }

    public void Apply(Profile profile) =>
        File.WriteAllText(_path, Merge(profile.TemplateText, File.ReadAllText(_backup)));

    public void Restore()
    {
        if (!File.Exists(_backup)) return;
        File.Copy(_backup, _path, overwrite: true);
        File.Delete(_backup);
    }

    public static string Merge(string template, string original)
    {
        var re = new Regex("^MainMonitorID=[^\\r\\n]*", RegexOptions.Multiline);
        var monitor = re.Match(original);
        return monitor.Success ? re.Replace(template, _ => monitor.Value, 1) : template;
    }
}
