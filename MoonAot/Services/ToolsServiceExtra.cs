using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using MoonAot.Store;

namespace MoonAot.Services;

/// <summary>工具箱高级能力：进程、端口、电源、截图、压缩包。</summary>
public static partial class ToolsService
{
    public static List<ProcessItemDto> Processes(string? query = null)
    {
        var list = new List<ProcessItemDto>();
        var needle = (query ?? "").Trim();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id <= 4) continue;
                var name = p.ProcessName;
                if (needle.Length > 0 && name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) continue;
                string? title = null;
                try { title = string.IsNullOrWhiteSpace(p.MainWindowTitle) ? null : p.MainWindowTitle; } catch { }
                long started = 0;
                try { started = new DateTimeOffset(p.StartTime).ToUnixTimeMilliseconds(); } catch { }
                list.Add(new ProcessItemDto
                {
                    Id = p.Id,
                    Name = name,
                    Title = title,
                    Memory = p.WorkingSet64,
                    StartedAt = started,
                });
            }
            catch { }
            finally { try { p.Dispose(); } catch { } }
        }
        list.Sort((a, b) => b.Memory.CompareTo(a.Memory));
        return list.Count > 400 ? list.GetRange(0, 400) : list;
    }

    public static bool KillProcess(int id, out string error)
    {
        error = "";
        try
        {
            if (id <= 4 || id == Environment.ProcessId) { error = "不能结束系统进程或当前程序"; return false; }
            using var p = Process.GetProcessById(id);
            p.Kill();
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    public static List<PortItemDto> Ports()
    {
        var list = new List<PortItemDto>();
        try
        {
            var psi = new ProcessStartInfo("netstat.exe", "-ano -p TCP")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return list;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            foreach (var raw in output.Split('\n'))
            {
                var line = raw.Trim();
                if (!line.StartsWith("TCP", StringComparison.OrdinalIgnoreCase)) continue;
                var parts = Regex.Split(line, @"\s+");
                if (parts.Length < 5) continue;
                var local = parts[1];
                var remote = parts[2];
                var state = parts[3];
                if (!int.TryParse(parts[4], out var pid)) continue;
                var idx = local.LastIndexOf(':');
                int port = idx >= 0 && int.TryParse(local[(idx + 1)..], out var lp) ? lp : 0;
                list.Add(new PortItemDto { Proto = "TCP", Local = local, Remote = remote, State = state, Pid = pid, Port = port });
                if (list.Count >= 3000) break;
            }
        }
        catch { }
        return list;
    }

    public static bool KillByPort(int port, out string error)
    {
        error = "";
        try
        {
            var item = Ports().FirstOrDefault(x => x.Port == port && x.State == "LISTENING");
            if (item == null) { error = "没有找到监听该端口的进程"; return false; }
            return KillProcess(item.Pid, out error);
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern bool LockWorkStation();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public static string? ScreenshotFull(out string error)
    {
        error = "";
        try
        {
            int x = GetSystemMetrics(76), y = GetSystemMetrics(77);
            int w = Math.Max(1, GetSystemMetrics(78)), h = Math.Max(1, GetSystemMetrics(79));
            var dir = Path.Combine(DbStore.Instance.DataDir, "screenshots");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
            using var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
                g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(w, h));
            bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
            return file;
        }
        catch (Exception ex) { error = ex.Message; return null; }
    }

    public static bool Power(string action, int delaySeconds, out string error)
    {
        error = "";
        try
        {
            switch (action)
            {
                case "lock":
                    if (!LockWorkStation()) { error = "锁屏失败"; return false; }
                    return true;
                case "screenoff":
                    SendMessageW(new IntPtr(0xFFFF), 0x0112, new IntPtr(0xF170), new IntPtr(2));
                    return true;
                case "sleep":
                    Process.Start(new ProcessStartInfo("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0") { UseShellExecute = false, CreateNoWindow = true });
                    return true;
                case "hibernate":
                    {
                        var p = Process.Start(new ProcessStartInfo("shutdown.exe", "/h") { UseShellExecute = false, CreateNoWindow = true });
                        if (p != null && p.WaitForExit(2500) && p.ExitCode != 0)
                        {
                            error = "休眠失败：系统可能未启用休眠（powercfg /hibernate on）";
                            return false;
                        }
                        return true;
                    }
                case "shutdown":
                    {
                        delaySeconds = Math.Clamp(delaySeconds, 0, 7 * 24 * 3600);
                        Process.Start(new ProcessStartInfo("shutdown.exe", "/s /t " + delaySeconds) { UseShellExecute = false, CreateNoWindow = true });
                        return true;
                    }
                case "restart":
                    {
                        delaySeconds = Math.Clamp(delaySeconds, 0, 7 * 24 * 3600);
                        Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t " + delaySeconds) { UseShellExecute = false, CreateNoWindow = true });
                        return true;
                    }
                case "cancel":
                    {
                        var p = Process.Start(new ProcessStartInfo("shutdown.exe", "/a") { UseShellExecute = false, CreateNoWindow = true });
                        if (p != null && p.WaitForExit(2000) && p.ExitCode != 0)
                        {
                            error = "没有可取消的定时关机";
                            return false;
                        }
                        return true;
                    }
                default:
                    error = "未知电源操作";
                    return false;
            }
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    public static bool CompressFolder(string folder, string zipPath, out string error)
    {
        error = "";
        try
        {
            if (!Directory.Exists(folder)) { error = "源文件夹不存在"; return false; }
            if (File.Exists(zipPath)) File.Delete(zipPath);
            var dir = Path.GetDirectoryName(zipPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            System.IO.Compression.ZipFile.CreateFromDirectory(folder, zipPath,
                System.IO.Compression.CompressionLevel.Optimal, false);
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    public static bool ExtractZip(string zipPath, string dest, out string error)
    {
        error = "";
        try
        {
            if (!File.Exists(zipPath)) { error = "ZIP 不存在"; return false; }
            Directory.CreateDirectory(dest);
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, dest, true);
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }
}
