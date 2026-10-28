using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using MoonAot.Store;

namespace MoonAot.Services;

/// <summary>工具箱：系统信息、文件哈希。</summary>
public static partial class ToolsService
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    public static SystemInfoDto SystemInfo()
    {
        var dto = new SystemInfoDto
        {
            Os = RuntimeInformation.OSDescription,
            Machine = RuntimeInformation.OSArchitecture.ToString(),
            User = Environment.UserName,
            ProcessId = Environment.ProcessId,
            ProcessName = Path.GetFileName(Environment.ProcessPath ?? "MoonAot"),
            CpuCount = Environment.ProcessorCount,
            CpuName = ReadCpuName(),
            UptimeSeconds = Environment.TickCount64 / 1000,
            AppVersion = "1.0.0",
            DataDir = DbStore.Instance.DataDir,
        };
        try { dto.WorkingSet = Process.GetCurrentProcess().WorkingSet64; } catch { }
        try
        {
            var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref mem))
            {
                dto.MemoryTotal = (long)mem.ullTotalPhys;
                dto.MemoryAvailable = (long)mem.ullAvailPhys;
            }
        }
        catch { }
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady || d.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
                    dto.Disks.Add(new DiskInfoDto { Name = d.Name, Total = d.TotalSize, Free = d.AvailableFreeSpace });
                }
                catch { }
            }
        }
        catch { }
        return dto;
    }

    private static string ReadCpuName()
    {
        try { return Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? ""; }
        catch { return ""; }
    }

    public static HashResultDto HashFile(string path, string algo)
    {
        var dto = new HashResultDto { Algo = algo, Path = path };
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                dto.Error = "文件不存在";
                return dto;
            }
            var fi = new FileInfo(path);
            dto.Size = fi.Length;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20);
            using HashAlgorithm h = algo.ToLowerInvariant() switch
            {
                "md5" => MD5.Create(),
                "sha1" => SHA1.Create(),
                "sha512" => SHA512.Create(),
                _ => SHA256.Create(),
            };
            dto.Hash = Convert.ToHexString(h.ComputeHash(fs)).ToLowerInvariant();
            dto.Algo = algo.ToLowerInvariant();
            dto.Ok = true;
        }
        catch (Exception ex)
        {
            dto.Error = ex.Message;
        }
        return dto;
    }
}
