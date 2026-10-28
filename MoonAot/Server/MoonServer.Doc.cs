using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;

namespace MoonAot.Server;

/// <summary>文本编辑与图片编辑的读写接口。</summary>
public sealed partial class MoonServer
{
    private const long MaxTextFileBytes = 32L * 1024 * 1024;
    private const int MaxImageBytes = 64 * 1024 * 1024;

    private void HandleFileText(HttpListenerContext ctx, HttpListenerRequest req, string query)
    {
        if (req.HttpMethod == "GET")
        {
            var path = QueryValue(query, "path");
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                SendJsonString(ctx, 404, "{\"ok\":false,\"error\":\"文件不存在\"}");
                return;
            }
            try
            {
                var fi = new FileInfo(path);
                if (fi.Length > MaxTextFileBytes)
                {
                    SendJsonString(ctx, 413, "{\"ok\":false,\"error\":\"文件太大，超过 32MB\"}");
                    return;
                }
                var bytes = File.ReadAllBytes(path);
                var (text, encoding) = DecodeText(bytes);
                SendJson(ctx, 200, new FileTextDto
                {
                    Ok = true,
                    Path = fi.FullName,
                    Name = fi.Name,
                    Ext = fi.Extension.ToLowerInvariant(),
                    Size = fi.Length,
                    Encoding = encoding,
                    Text = text,
                    Modified = new DateTimeOffset(fi.LastWriteTimeUtc).ToUnixTimeMilliseconds(),
                });
            }
            catch (Exception ex)
            {
                SendJsonString(ctx, 500, "{\"ok\":false,\"error\":" + JsonStr(ex.Message) + "}");
            }
            return;
        }

        if (req.HttpMethod != "POST") { SendText(ctx, 405, "method"); return; }
        using var body = ReadJson(req);
        if (body == null) { SendText(ctx, 400, "body required"); return; }
        var root = body.RootElement;
        var target = GetString(root, "path");
        if (string.IsNullOrWhiteSpace(target) || !File.Exists(target))
        {
            SendJsonString(ctx, 404, "{\"ok\":false,\"error\":\"文件不存在\"}");
            return;
        }
        var content = GetString(root, "text");
        if (content == null)
        {
            SendText(ctx, 400, "text required");
            return;
        }
        var enc = GetString(root, "encoding") ?? "utf-8";
        try
        {
            var encoding = EncodingFor(enc);
            File.WriteAllText(target!, content, encoding);
            SendJsonString(ctx, 200, "{\"ok\":true,\"size\":" + new FileInfo(target!).Length + "}");
        }
        catch (Exception ex)
        {
            SendJsonString(ctx, 500, "{\"ok\":false,\"error\":" + JsonStr(ex.Message) + "}");
        }
    }

    private void HandleImageEdit(HttpListenerContext ctx, HttpListenerRequest req)
    {
        if (req.HttpMethod != "POST") { SendText(ctx, 405, "method"); return; }
        using var body = ReadJson(req);
        if (body == null) { SendText(ctx, 400, "body required"); return; }
        var root = body.RootElement;
        var source = GetString(root, "path");
        var dataUrl = GetString(root, "dataUrl");
        var mode = GetString(root, "mode") ?? "copy";
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
        {
            SendJsonString(ctx, 404, "{\"ok\":false,\"error\":\"原图不存在\"}");
            return;
        }
        if (string.IsNullOrWhiteSpace(dataUrl) || !dataUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            SendText(ctx, 400, "dataUrl required");
            return;
        }
        try
        {
            var comma = dataUrl.IndexOf(',');
            if (comma < 0) { SendText(ctx, 400, "invalid dataUrl"); return; }
            var meta = dataUrl[5..comma]; // image/png;base64
            var isBase64 = meta.Contains("base64", StringComparison.OrdinalIgnoreCase);
            var payload = dataUrl[(comma + 1)..];
            var bytes = isBase64 ? Convert.FromBase64String(payload) : Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));
            if (bytes.Length > MaxImageBytes) { SendText(ctx, 413, "图片太大"); return; }

            var isJpeg = meta.StartsWith("image/jpeg", StringComparison.OrdinalIgnoreCase);
            var ext = isJpeg ? ".jpg" : ".png";
            string outPath;
            if (string.Equals(mode, "overwrite", StringComparison.OrdinalIgnoreCase))
            {
                outPath = source!;
                if (!string.Equals(Path.GetExtension(outPath), ext, StringComparison.OrdinalIgnoreCase) &&
                    !(ext == ".jpg" && Path.GetExtension(outPath).Equals(".jpeg", StringComparison.OrdinalIgnoreCase)))
                    outPath = Path.ChangeExtension(outPath, ext);
            }
            else
            {
                var dir = Path.GetDirectoryName(source!) ?? "";
                var name = Path.GetFileNameWithoutExtension(source!);
                outPath = Path.Combine(dir, name + "_edited_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ext);
            }
            File.WriteAllBytes(outPath, bytes);
            SendJsonString(ctx, 200, "{\"ok\":true,\"path\":" + JsonStr(outPath) + ",\"size\":" + bytes.Length + "}");
        }
        catch (Exception ex)
        {
            SendJsonString(ctx, 500, "{\"ok\":false,\"error\":" + JsonStr(ex.Message) + "}");
        }
    }

    private static (string Text, string Encoding) DecodeText(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return (Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), "utf-8-bom");
        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0 && bytes[3] == 0)
            return (new UTF32Encoding(false, false).GetString(bytes, 4, bytes.Length - 4), "utf-32le");
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return (Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), "utf-16le");
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return (Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), "utf-16be");
        try
        {
            var strict = new UTF8Encoding(false, true);
            return (strict.GetString(bytes), "utf-8");
        }
        catch
        {
            try
            {
                var gb = Encoding.GetEncoding(54936); // GB18030
                return (gb.GetString(bytes), "gb18030");
            }
            catch
            {
                return (Encoding.UTF8.GetString(bytes), "utf-8");
            }
        }
    }

    private static Encoding EncodingFor(string name) => name.ToLowerInvariant() switch
    {
        "utf-8-bom" => new UTF8Encoding(true),
        "utf-16le" => Encoding.Unicode,
        "utf-16be" => Encoding.BigEndianUnicode,
        "utf-32le" => new UTF32Encoding(false, false),
        "gb18030" => Encoding.GetEncoding(54936),
        _ => new UTF8Encoding(false),
    };

    public sealed class FileTextDto
    {
        public bool Ok { get; set; }
        public string Path { get; set; } = "";
        public string Name { get; set; } = "";
        public string Ext { get; set; } = "";
        public long Size { get; set; }
        public string Encoding { get; set; } = "utf-8";
        public string Text { get; set; } = "";
        public long Modified { get; set; }
    }
}
