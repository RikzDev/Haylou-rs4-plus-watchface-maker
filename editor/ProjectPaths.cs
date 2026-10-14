using System.IO;

namespace HaylouFaceEditor;

public partial class MainWindow
{
    static string FindWatchfaceRoot()
    {
        var configured = Environment.GetEnvironmentVariable("HAYLOU_PROJECT_ROOT");
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.Combine(Path.GetFullPath(configured), "watchface");
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var folder = new DirectoryInfo(start); folder != null; folder = folder.Parent)
            {
                var candidate = Path.Combine(folder.FullName, "watchface");
                if (File.Exists(Path.Combine(candidate, "build_live.py")))
                    return candidate;
            }
        }
        return Path.Combine(Directory.GetCurrentDirectory(), "watchface");
    }

    static string FindPython()
    {
        var configured = Environment.GetEnvironmentVariable("HAYLOU_PYTHON");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        var local = Path.Combine(Path.GetDirectoryName(WatchfaceRoot)!, ".venv", "Scripts", "python.exe");
        return File.Exists(local) ? local : "python";
    }
}
