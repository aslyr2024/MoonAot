using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MoonAot.Win;

/// <summary>
/// 原生「选择文件夹」对话框（Win32 SHBrowseForFolder，AOT 友好，无 UI 框架依赖）。
/// 必须在主线程（STA、COM 已初始化）调用。
/// </summary>
internal static class FolderPicker
{
    private const uint BIF_RETURNONLYFSDIRS = 0x0001;
    private const uint BIF_NEWDIALOGSTYLE = 0x0040;

    [StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Unicode)]
    private struct BROWSEINFOW
    {
        public IntPtr hwndOwner;
        public IntPtr pidlRoot;
        public IntPtr pszDisplayName;
        public string lpszTitle;
        public uint ulFlags;
        public IntPtr lpfn;
        public IntPtr lParam;
        public int iImage;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHBrowseForFolderW(ref BROWSEINFOW lpbi);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SHGetPathFromIDListW(IntPtr pidl, StringBuilder pszPath);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr pv);

    /// <summary>显示目录选择对话框；取消返回 null。</summary>
    public static string? Show(IntPtr hwndOwner, string title)
    {
        var displayName = Marshal.AllocCoTaskMem(520); // MAX_PATH*2 in chars
        IntPtr pidl = IntPtr.Zero;
        try
        {
            var bi = new BROWSEINFOW
            {
                hwndOwner = hwndOwner,
                pidlRoot = IntPtr.Zero,
                pszDisplayName = displayName,
                lpszTitle = string.IsNullOrEmpty(title) ? "选择文件夹" : title,
                ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE,
                lpfn = IntPtr.Zero,
                lParam = IntPtr.Zero,
                iImage = 0,
            };
            pidl = SHBrowseForFolderW(ref bi);
            if (pidl == IntPtr.Zero) return null;
            var sb = new StringBuilder(1024);
            if (!SHGetPathFromIDListW(pidl, sb) || sb.Length == 0) return null;
            return sb.ToString();
        }
        catch
        {
            return null;
        }
        finally
        {
            if (pidl != IntPtr.Zero) CoTaskMemFree(pidl);
            Marshal.FreeCoTaskMem(displayName);
        }
    }
}
