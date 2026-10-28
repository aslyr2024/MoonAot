using System;
using System.IO;
using System.Linq;
using System.Threading;
using MoonAot.Server;
using MoonAot.Services;
using MoonAot.Store;
using MoonAot.Win;

namespace MoonAot;

/// <summary>
/// 启动前初始化（ModuleInitializer，AOT 支持）：注册 GB18030/GBK 等扩展编解码器。
/// </summary>
internal static class Bootstrap
{
    [ModuleInitializer]
    internal static void Init()
    {
        try
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        }
        catch { }
        try
        {
            SQLitePCL.Batteries_V2.Init();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("SQLite 初始化失败: " + ex.Message);
        }
        try
        {
            Store.Database.Instance.Initialize();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("数据目录初始化失败: " + ex.Message);
        }
    }
}

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Any(a => a is "--serve" or "-s") || args.Any(a => a.StartsWith("--serve=", StringComparison.Ordinal)))
        {
            RunServe(args);
            return;
        }

        string? target = args.FirstOrDefault(a =>
            !a.StartsWith("-", StringComparison.Ordinal) && (File.Exists(a) || Directory.Exists(a)));
        AppHost.Run(target);
    }

    /// <summary>开发/测试模式：只启动 HTTP 服务，不创建窗口。端口写入 data/dev-url.txt。</summary>
    private static void RunServe(string[] args)
    {
        int port = 0;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--port" && i + 1 < args.Length && int.TryParse(args[i + 1], out var p)) port = p;
            else if (args[i].StartsWith("--serve=", StringComparison.Ordinal) && int.TryParse(args[i]["--serve=".Length..], out var p2)) port = p2;
        }
        try
        {
            MoonServer.Instance.Start(port);
            Console.WriteLine("MoonAot server: " + MoonServer.Instance.BaseUrl);
            try
            {
                var file = Path.Combine(DbStore.Instance.DataDir, "dev-url.txt");
                File.WriteAllText(file, MoonServer.Instance.BaseUrl + Environment.NewLine);
            }
            catch { }

            if (args.Any(a => a == "--open"))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                        MoonServer.Instance.BaseUrl + "/shelf/") { UseShellExecute = true });
                }
                catch { }
            }

            var wait = new ManualResetEventSlim(false);
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                wait.Set();
            };
            AppDomain.CurrentDomain.ProcessExit += (_, _) => wait.Set();
            wait.Wait();
        }
        catch (Exception ex)
        {
            Log.Error("开发服务器启动失败", ex);
            Console.Error.WriteLine(ex.Message);
        }
        finally
        {
            MoonServer.Instance.Stop();
            DbStore.Instance.FlushAll();
            MediaStore.Instance.FlushAll();
        }
    }
}
