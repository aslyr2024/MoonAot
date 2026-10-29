# 构建与发布

## 1. 环境要求

- Windows 10/11 x64
- [.NET 10 SDK](https://dotnet.microsoft.com/)
- Visual Studio 2022（或 Build Tools）安装 **使用 C++ 的桌面开发**（NativeAOT 需要 MSVC 链接器）
- Node.js 20+ / npm
- Microsoft Edge WebView2 Runtime（运行期）

验证：

```bash
dotnet --info
node -v
npm -v
```

## 2. 仓库布局

```
MoonAot/                 主项目
WebView2Aot-main/WebView2  WebView2Aot 依赖（MIT）
docs/
```

`MoonAot/MoonAot.csproj` 通过 `..\WebView2Aot-main\WebView2\WebView2.csproj` 引用 WebView2Aot，因此两个目录必须保持同级。

## 3. 构建前端

```bash
cd MoonAot/frontend
npm install
npm run build
```

构建输出在 `MoonAot/Assets/shelf`（Vite `base` 为 `/shelf/`）。

## 4. 构建/运行（JIT）

```bash
cd MoonAot
dotnet build MoonAot.csproj -c Release -p:Platform=x64
```

开发调试可直接运行 `MoonAot/bin/x64/Release/net10.0/MoonAot.exe --serve --port 5188`，只启动 HTTP 服务，便于浏览器调试前端；数据位于该目录的 `data/`。

## 5. NativeAOT 发布

```bash
cd MoonAot
dotnet publish MoonAot.csproj -c Release -r win-x64 -p:Platform=x64 -o publish
```

产物：

- `publish/MoonAot.exe`（约 24MB）
- `publish/e_sqlite3.dll`
- `publish/Assets/`（前端、阅读器、字体、tiny/wiki 静态资源）
- `publish/*.pdb`（可选，发布包可不带）

**发布前务必删除 `publish/data`**（可能包含本机书库、封面、日志等个人数据），并确认 `.gitignore` 已排除。

## 6. 制作 GitHub Release

```bash
mkdir -p dist/MoonAot-v1.0.4-win-x64
cp publish/MoonAot.exe publish/e_sqlite3.dll dist/MoonAot-v1.0.4-win-x64/
cp -r publish/Assets dist/MoonAot-v1.0.4-win-x64/
# 可选：写入 README.txt
cd dist
powershell Compress-Archive -Path MoonAot-v1.0.4-win-x64 -DestinationPath MoonAot-v1.0.4-win-x64.zip
```

上传 zip 到 GitHub Releases；附 SHA256：

```powershell
Get-FileHash MoonAot-v1.0.4-win-x64.zip -Algorithm SHA256
```

## 7. 常见构建问题

- **`vcruntime`/`link.exe` 找不到**：安装 VS C++ 桌面开发工作负载，或在“x64 Native Tools Command Prompt”中构建。
- **NuGet 还原失败**：检查网络/代理；`dotnet nuget locals all --clear` 后重试。
- **AOT 时间很长/内存高**：属正常，首次 1–3 分钟；增量发布更快。
- **前端修改未生效**：确认执行了 `npm run build`，并检查 `Assets/shelf` 的 `index-*.js` 哈希已变化。
- **运行提示缺少 WebView2**：安装 Microsoft Edge WebView2 Runtime。
- **端口占用**：`--serve` 模式可指定其他端口；正式 GUI 自动选择随机端口。
- **路径过长**：Windows 长路径 + 深层目录可能导致复制失败，可在仓库根启用长路径支持或缩短目录。

## 8. 数据与隐私

- 仓库与 Release 都不应包含 `data/`。
- `data/cache/epub`、`data/covers`、`data/thumbs` 可以删除，程序会按需重建。
- 日志 `data/logs/app.log` 可能包含本机文件路径，提交/上传前请清理。
