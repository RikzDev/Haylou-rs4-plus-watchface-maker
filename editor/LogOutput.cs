using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace HaylouFaceEditor;

public enum LogLevel { Info, Warning, Error, Notice }

public partial class MainWindow
{
    static LogLevel ClassifyLogLine(string line, bool errorStream = false)
    {
        var text = line.Trim();
        if (text.StartsWith("[NOTICE]", StringComparison.OrdinalIgnoreCase) || text.StartsWith("Upload note:", StringComparison.OrdinalIgnoreCase))
            return LogLevel.Notice;
        if (text.StartsWith("[ERROR]", StringComparison.OrdinalIgnoreCase)) return LogLevel.Error;
        if (text.StartsWith("[INFO]", StringComparison.OrdinalIgnoreCase)) return LogLevel.Info;
        if (text.StartsWith("Written ", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Package validated ", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Retargeted binary ", StringComparison.OrdinalIgnoreCase)) return LogLevel.Info;
        if (text.StartsWith("[WARNING]", StringComparison.OrdinalIgnoreCase) || text.Contains("warning:", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Pairing attempt failed", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Direct connection failed", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Reconnecting", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(text, @"\berr\s*[:=]?\s*(?:9|14)\b", RegexOptions.IgnoreCase))
            return LogLevel.Warning;
        if (text.StartsWith("[ERROR]", StringComparison.OrdinalIgnoreCase) || text.StartsWith("error:", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Traceback", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(text, @"\b(?:err|error)\s*[:=]?\s*(?:[1-9]\d*|None|null)\b", RegexOptions.IgnoreCase)
            || text.Contains("failed", StringComparison.OrdinalIgnoreCase) || text.Contains("not found", StringComparison.OrdinalIgnoreCase)
            || text.Contains("no response", StringComparison.OrdinalIgnoreCase) || errorStream)
            return LogLevel.Error;
        return LogLevel.Info;
    }

    void Log(string line, LogLevel? level = null)
    {
        var severity = level ?? LogLevel.Info;
        var color = severity switch
        {
            LogLevel.Warning => Brushes.Gold,
            LogLevel.Error => Brushes.Red,
            LogLevel.Notice => Brushes.DeepSkyBlue,
            _ => Brushes.White,
        };
        LogBox.Document.Blocks.Add(new Paragraph(new Run(line) { Foreground = color }) { Margin = new Thickness(0) });
        while (LogBox.Document.Blocks.Count > 2000)
            LogBox.Document.Blocks.Remove(LogBox.Document.Blocks.FirstBlock);
        LogBox.ScrollToEnd();
    }

    void DispatchLog(string line, bool errorStream = false) =>
        Dispatcher.Invoke(() => Log(line, ClassifyLogLine(line, errorStream)));

    void ShowUploadNotice() => Log(
        "Upload note: On this setup, the first connection/pairing attempt usually fails. The uploader reconnects automatically; wait for the retry.",
        LogLevel.Notice);
}
