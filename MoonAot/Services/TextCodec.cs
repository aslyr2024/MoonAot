using System.IO;
using System.Text;

namespace MoonAot.Services;

/// <summary>TXT/MD 文件编码检测与解码（无需第三方依赖）。</summary>
public static class TextCodec
{
    /// <summary>读取文本文件并转成 UTF-8 字符串。自动识别 BOM / UTF-8 / GB 系编码。</summary>
    public static string ReadTxt(string path)
    {
        return Decode(File.ReadAllBytes(path));
    }

    public static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        // 无 BOM：优先尝试严格 UTF-8；失败回退 GB18030
        if (IsStrictUtf8(bytes, out var text)) return text;

        try
        {
            return Encoding.GetEncoding("GB18030").GetString(bytes);
        }
        catch
        {
            return "";
        }
    }

    /// <summary>严格 UTF-8 校验（含代理对）。合法则返回原文。</summary>
    private static bool IsStrictUtf8(byte[] bytes, out string text)
    {
        text = "";
        try
        {
            text = new UTF8Encoding(false, true).GetString(bytes);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
