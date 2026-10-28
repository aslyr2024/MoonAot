using System;
using System.IO;
using System.Text;
using MoonAot.Store;

namespace MoonAot.Services;

/// <summary>极简文件日志（%LOCALAPPDATA%\MoonAot\logs\app.log，超过 4MB 轮转）。</summary>
public static class Log
{
    private static readonly object Lock = new();
    private static string? _path;

    private static string PathOf()
    {
        if (_path != null) return _path;
        var dir = Database.Instance.LogsDir;
        if (string.IsNullOrEmpty(dir))
            dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MoonAot", "logs");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "app.log");
        return _path;
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null)
        => Write("ERROR", ex == null ? message : message + " | " + ex.GetType().Name + ": " + ex.Message);

    private static void Write(string level, string message)
    {
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
            lock (Lock)
            {
                var path = PathOf();
                try
                {
                    var fi = new FileInfo(path);
                    if (fi.Exists && fi.Length > 4 * 1024 * 1024)
                        File.Move(path, path + ".1", true);
                }
                catch { }
                File.AppendAllText(path, line, Encoding.UTF8);
            }
        }
        catch { }
    }
}
