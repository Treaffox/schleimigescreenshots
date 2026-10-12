using System.Text;

namespace SchleimigeScreenshots;

/// <summary>Sammelt den kopierbaren Debug-Bericht eines Scans als Klartext.</summary>
internal sealed class ScanReport
{
    private readonly StringBuilder _sb = new();

    public bool HasError { get; private set; }

    public void Title(string text)
    {
        _sb.AppendLine(text);
        _sb.AppendLine(new string('=', text.Length));
    }

    public void Section(string name)
    {
        _sb.AppendLine();
        _sb.AppendLine($"--- {name} ---");
    }

    public void Line(string text = "") => _sb.AppendLine(text);

    public void Warn(string text) => _sb.AppendLine("WARNUNG: " + text);

    public void Error(string text)
    {
        HasError = true;
        _sb.AppendLine("FEHLER: " + text);
    }

    public void Error(string context, Exception ex)
    {
        HasError = true;
        _sb.AppendLine($"FEHLER ({context}): {ex.GetType().FullName}: {ex.Message}");
        foreach (var line in ex.ToString().Split('\n'))
            _sb.AppendLine("    " + line.TrimEnd('\r'));
    }

    public override string ToString() => _sb.ToString();
}
