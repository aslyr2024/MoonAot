# 第三方组件与许可

MoonAot 自身代码按 AGPL-3.0 发布。下列组件/资源由第三方提供，遵循其各自许可。公开发布前请再次确认字体、阅读器内核与素材的再分发授权。

## 运行时与原生

| 组件 | 许可 | 用途 |
|---|---|---|
| WebView2Aot (Simon Mourier) | MIT | Windows WebView2 / DirectComposition 宿主 |
| DirectNAot / DirectNAot.Extensions | MIT | Win32/COM 互操作 |
| Microsoft.Data.Sqlite / SQLitePCLRaw / e_sqlite3 | MIT / SQLite Public Domain | 本地数据库 |
| System.Drawing.Common（GDI+） | MIT | 封面、缩略图、截图 |
| ZstdSharp.Port | MIT | ZIM zstd 解压 |
| SharpCompress | MIT | ZIM xz 等压缩格式 |
| OpenCC.NET | Apache-2.0 | 简繁转换 |
| Newtonsoft.Json | MIT | OpenCC 传递依赖 |

## 前端与查看器

| 组件 | 许可 | 用途 |
|---|---|---|
| Vue 3、Vue Router | MIT | 前端框架 |
| Vite | MIT | 构建工具 |
| Naive UI | MIT | UI 组件 |
| lucide-vue-next | ISC | 图标 |
| CodeMirror 6 及语言包 | MIT | 代码编辑器 |
| docx-preview | MIT | DOCX 渲染 |
| pptx-preview | 见 npm 包许可 | PPTX 渲染 |
| SheetJS (xlsx) | Apache-2.0 | 表格/CSV |
| three.js | MIT | 3D / DXF 渲染 |
| dxf-parser | MIT | DXF 解析 |

## 阅读器内核与素材

| 组件 | 许可 | 说明 |
|---|---|---|
| Readest / nn-reader 构建产物 | **AGPL-3.0** | `Assets/nnreader`，内置阅读器前端；本项目对其中的远程字体地址做了本地化修改 |
| pdf.js | Apache-2.0 | PDF 渲染（随阅读器构建） |
| 字体文件（`Assets/reader/*.ttf` 等） | 各自权利人 | 方正等字体可能为商业授权，公开再分发前请确认授权；也可删除后使用系统字体 |

## 数据与内容

仓库不包含任何用户书籍、封面、笔记、数据库或日志。`data/` 已被 `.gitignore` 排除。

## 免责声明

本项目仅用于个人学习与本地数据管理。用户应确保导入的书籍、字体、ZIM 离线包等素材拥有合法使用权。
