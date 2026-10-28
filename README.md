# MoonAot — Windows NativeAOT 电子书阅读器

MoonAot（静读天下 AOT 版）是一个完全本地运行的 Windows 桌面阅读器与媒体中心。使用 **.NET 10 NativeAOT** 编译为原生单文件程序，界面由 **WebView2 + Vue 3 / Naive UI** 渲染，同时集成电子书阅读、图片/音乐/视频播放、全盘搜索、离线维基、代码编辑、Office/3D/CAD 查看、按键精灵等能力。

> 所有数据保存在程序目录的 `data` 文件夹中，便携、可备份、可迁移。

## 核心能力

- **书库**：TXT / EPUB / PDF / MOBI / AZW3 / Markdown，虚拟网格书架、深分页、封面压缩、最近阅读、笔记、收藏、统计。
- **阅读器**：内置 Readest/foliate 阅读内核，目录、主题、字体、进度、标注、快捷键；兼容 OPF 路径大小写不规范的 EPUB。
- **媒体**：图片浏览器（缩略图滑块、缩放/旋转记忆、右键编辑）、音乐播放器（歌词、背景幻灯片）、视频播放器、压缩包浏览。
- **全盘搜索**：NTFS MFT 秒级索引 + USN 实时更新 + Everything 风格查询语言，非 NTFS 盘 FileSystemWatcher 回退。
- **离线百科**：ZIM 解析（zstd/xz + OpenCC 简繁转换），全盘搜索中的 `.zim` 默认用内置维基打开。
- **文档/模型**：DOCX、XLSX/XLS/CSV、PPTX、FBX/OBJ/GLB/GLTF/STL/3DS/DAE/PLY/3MF、DXF；代码/文本用 CodeMirror 打开并保存。
- **其他**：内置多标签浏览器、网站抓取/托管、工具箱（进程/端口/截图/压缩）、按键精灵宏引擎。

## 技术栈

| 层 | 技术 |
|---|---|
| 运行时 | .NET 10、NativeAOT、Win32 / DirectComposition |
| 窗口 / WebView | WebView2Aot（MIT）、DirectNAot |
| 前端 | Vue 3、Vite、Naive UI、lucide-vue-next |
| 数据 | SQLite（Microsoft.Data.Sqlite，WAL，原生 SQL） |
| 图片 | GDI+（封面/缩略图/截图）、Canvas 2D 编辑器 |
| 搜索 | NTFS MFT、USN Journal、Everything 查询语法 |
| 文档 | docx-preview、SheetJS、pptx-preview、three.js、dxf-parser、CodeMirror 6 |
| 百科 | 自研 ZIM 读取、ZstdSharp、SharpCompress、OpenCC.NET |

## 目录结构

```
MoonAot/                 主程序（Win / Server / Store / Services / TinySearch / frontend / Assets）
WebView2Aot-main/        WebView2Aot 源码（MIT，构建依赖）
docs/                    架构、调用图、开发札记、用户手册、构建说明
```

`MoonAot/Assets` 包含前端构建产物、阅读器内核、图标与字体；`MoonAot/frontend` 是 Vue 源码；运行数据位于发布目录的 `data/`（不会提交到仓库）。

## 快速开始

### 使用发布包

1. 从 [Releases](https://github.com/aslyr2024/MoonAot/releases) 下载 `MoonAot-v*-win-x64.zip`。
2. 解压到任意目录，确保已安装 **Microsoft Edge WebView2 Runtime**（Windows 10/11 通常自带）。
3. 运行 `MoonAot.exe`。首次启动会在同目录创建 `data/`。

### 从源码构建

```bash
# 1. 前端
cd MoonAot/frontend
npm install
npm run build

# 2. 后端（需要 .NET 10 SDK 与 VS 2022 C++ 工具链）
cd ../..
dotnet publish MoonAot/MoonAot.csproj -c Release -r win-x64 -p:Platform=x64 -o publish
```

发布前请删除 `publish/data`（可能含本机书库），再复制 `Assets` 与 `e_sqlite3.dll` 打包。

详细步骤见 [构建说明](docs/BUILD.md)。

## 文档

- [架构设计](docs/ARCHITECTURE.md)
- [调用图](docs/CALL_GRAPHS.md)
- [开发问题与解决记录](docs/DEVELOPMENT_NOTES.md)
- [用户手册](docs/USER_MANUAL.md)
- [构建与发布](docs/BUILD.md)
- [第三方组件与许可](docs/THIRD_PARTY_NOTICES.md)

## 数据目录

| 路径 | 内容 |
|---|---|
| `data/moonaot.db` | 书库、设置、笔记、进度、宏、收藏等（SQLite WAL） |
| `data/covers/` | 提取/自定义封面（JPEG，≤48KB） |
| `data/thumbs/` | 书架缩略图（320×480，≤40KB）、无封面标记、媒体缩略图 |
| `data/cache/epub/` | 大小写不规范的 EPUB 修复副本（可删除，按需重建） |
| `data/library/` | 网页上传的书籍副本 |
| `data/media/`、`lyrics/`、`uploads/`、`logs/` | 媒体、歌词、上传临时文件、日志 |

备份时直接复制整个 `data` 目录即可。

## 已知限制

- 旧版二进制 `.doc` / `.ppt` 与专有 `.dwg` 不在内置查看器支持范围内，会交给系统默认程序。
- AVIF 封面依赖 Chromium 显示；GDI+ 无法解码时会回退原图。
- 阅读器内置字体与第三方素材的再分发请遵守各自许可，详见第三方声明。

## 许可

本项目整体按 **GNU Affero General Public License v3.0** 发布，主要因为内置阅读器内核基于 AGPL-3.0 的 Readest/nn-reader 构建。WebView2Aot、DirectNAot 等组件遵循各自的 MIT 许可。详见 [LICENSE](LICENSE) 与 [第三方声明](docs/THIRD_PARTY_NOTICES.md)。
