# 开发问题与解决记录

本文记录 MoonAot 开发过程中遇到的主要问题、定位过程与最终方案。数值均来自本机实测（.NET 10 / Windows 11 / 112,333 本书库）。

## 1. NativeAOT 下没有 WinForms/WPF

**问题**：常规 WebView2 宿主依赖 WinForms/WPF，NativeAOT 不可用或体积/反射不可控。

**方案**：使用 `WebView2Aot`（MIT）直接调用 Win32/DirectComposition 创建窗口与 WebView2，自建 `Window`/`Application` 消息循环。所有窗口类继承 `WebViewWindowBase`，统一处理 DPI、窗口状态、脚本注入、WebMessage。

## 2. 持久化从 JSON 改为 SQLite

**问题**：JSON 全量读写不适合 10 万本书；FreeSql/EF 在 AOT 下反射与动态代码不可控。

**方案**：`Microsoft.Data.Sqlite` + 手写 SQL，WAL 模式，连接池；`Database.EnsureColumn` 做列迁移；JSON 仅用于 API DTO，并使用 `JsonSerializerContext` 源生成。

## 3. HttpListener 路由分段错误

**问题**：`/zimapi/info` 被解析成 `["", "zimapi", "info"]`，处理函数取 `seg[1]` 得到 `"zimapi"`，所有 ZIM API 都返回 `unknown zim api`。

**方案**：API 入口统一 `path["/api/".Length..].Split('/', RemoveEmptyEntries)`；ZIM/Tiny 路由直接传 action 字符串。后来导入接口也出现同类“取错段”问题，统一按 `s0/s1/s2` 语义处理。

## 4. 图片浏览器只能扫描到几十张

**问题**：选择漫画根目录后，图片浏览器用“第一张图片所在子目录”重新扫描，导致只看到某个子文件夹的几十张。

**方案**：引入 `rootFolder`，从选择文件夹时的根路径递归扫描；路由用 `root` 参数贯穿；轮询期间增量合并而不是清空列表；文件上限从 30 万改为 1000 万。

## 5. 大目录扫描卡死与轮询闪烁

**问题**：同步递归枚举几十万文件阻塞 UI；轮询刷新清空列表导致闪屏。

**方案**：`ImageScanService` 后台扫描 + 前端 600ms 轮询；扫描中只增量合并；按文件夹缓存 5 分钟；索引可用时直接枚举内存索引。

## 6. 全盘搜索索引与实时更新

**问题**：仅初次扫描，文件增删后搜索结果过期；非 NTFS 盘无实时能力。

**方案**：
- NTFS 读 MFT → `EntryStore`（数组 FRN 存储）→ `MemoryIndex`；
- `UsnWatcher` 轮询 `FSCTL_READ_USN_JOURNAL`，批量 upsert/delete，处理目录子树删除与重命名；
- 非 NTFS 用 `GenericWatcher`（FileSystemWatcher + 600ms 防抖）；
- 5 秒盘符轮询处理热插拔；到期全盘重扫；设置持久化到 `data/tiny-settings.json`。

## 7. 搜索与媒体库点击不调用内置阅读器

**问题**：全盘搜索双击走 `/tiny/api/open`，原生执行 `window.__host.openTarget(...)`，但 `window.__host` 由 `installHostBridge` 注入，缺少 `openTarget/openViewer`，异常被 `try{}catch{}` 吞掉，表现为“点了没反应”。

**方案**：在 `host.ts` 的 bridge 中注入 `openTarget/openViewer/openBrowser`；统一由 `AppWindows.OpenTarget` 按扩展名分发。用真实 GUI 验证：搜索打开 JSON → 代码编辑器窗口；打开 ZIM → 书架切到维基。

## 8. 页面切换在窗口不可见时卡住

**问题**：`<transition mode="out-in">` 依赖动画结束事件；窗口隐藏/最小化时 Chromium 暂停动画，路由切换卡在旧页面。

**方案**：移除书架 `router-view` 的 out-in 过渡，只保留 keep-alive；导航始终即时挂载。

## 9. 112,333 本书：封面加载极慢、滚动卡顿

**定位**：
1. 批量导入 `ImportPaths` 只写数据库，**从未调用 `EnsureCover`**，所以 11 万本书只有 711 张封面；
2. 书架卡片对“可提取封面”的书直接请求 `/thumb/{id}`，服务端在请求线程里打开 EPUB/MOBI/PDF 提取封面，并发仅 3；
3. 因此滚动会连续触发展开电子书，越滑越慢，封面长时间不出现。

**方案**：
- 书架只有 `hasCover=true` 才请求封面；未提取的书立即显示缺省封面/占位；
- 批量导入收集可提取封面的书目，后台串行补齐；启动时也补齐缺失封面（受“导入时提取封面”开关控制）；
- 失败写 `.none` 标记避免反复解析损坏文件；
- 封面压缩到 **≤48KB**（质量与尺寸自适应）；
- v1.0.1 起书架与详情共用同一份封面，不再生成第二份缩略图；启动时清理旧版 `thumbs/*.jpg`，保留 `.none` 标记与 `thumbs/path/` 媒体缩略图。

`data/covers` 是封面（书架/详情共用）；`data/thumbs` 仅保留无封面标记与 `path/` 媒体缩略图。

## 10. 深翻页 0.4 秒

**定位**：默认排序 `COALESCE(last_read_at, added_at)` 无索引，每页对 11 万行排序；且每页重复 `COUNT(*)`。

**方案**：
- 增加表达式索引 `ix_books_recent(COALESCE(last_read_at, added_at) DESC)`，以及 size/author+title/format+title 索引；
- 前端第 2 页起传 `count=0`，后端跳过 COUNT，返回 `total=-1`。

**实测**：offset=111000 从 376–421ms 降到 8.9ms（AOT）；第 1 页 6.7ms。

## 11. 打开图书非常慢

**定位**：阅读器静态构建会从 Google Fonts、jsDelivr、imagekit 加载多份字体 CSS。受限网络下这些请求长期挂起，属于渲染阻塞资源。

**方案**：替换阅读器构建产物中的远程字体前缀为本地 `/empty-font.css`（服务端返回空 CSS，长缓存）。AOT 实测外部请求为 0，页面 load 163ms，9.4MB TXT 正文接口 185ms。

## 12. 特定 EPUB 永远打不开（于丹趣品人生等）

**共同点**：`META-INF/container.xml` 中 `full-path="ops/Content.opf"`，而 ZIP 内实际条目为 `OPS/Content.opf`。ZIP 规范区分大小写，Readest/foliate 报 `Failed to load package document`，界面一直转圈。

**方案**：`EpubRepairService` 在首次打开 EPUB 时检查 rootfile 路径：
- 大小写一致 → 直接返回原文件；
- 不一致 → 复制原文件到 `data/cache/epub/{id}.epub`，只修正 `container.xml` 的 `full-path`，再提供给阅读器；原文件不变。

已实测修复：于丹趣品人生、香奈儿和她的浪漫传奇、五千载无人知的秘密均能立即打开。

## 13. ZIM 解析与路由

**问题**：ZIM 使用 zstd/xz 压缩，.NET 无内置解码；简繁转换需 OpenCC；路由 action 取错段。

**方案**：移植/适配 ZIM 读取器，引入 ZstdSharp.Port、SharpCompress、OpenCC.NET；修复 API 路由；`/zim/*` 流式返回条目，重定向条目 302；维基 UI 以 iframe 嵌入 `/wiki/`。

## 14. 搜索结果默认打开方式

**方案**：`AppWindows.OpenTarget` 统一分发：
- 书籍 → 导入并打开阅读器；
- 图片/音频/视频/zip → 内置查看器；
- `.zim` → 打开 ZIM 并切到维基；
- `.html/.htm/.xhtml` → 内置浏览器；
- `code/doc/sheet/slide/model/cad` → 对应内置查看器；
- 目录 → 资源管理器；
- 其他 → 系统默认程序。

## 15. NativeAOT 细节

- 所有 JSON 类型加入 `JsonContext`，包括 `FileTextDto`、TinySearch 设置等；
- Newtonsoft.Json 仅由 OpenCC 传递依赖引入，产生 IL2104/IL3053 提示，无编译错误；
- GDI+、SQLite 原生库、`System.IO.Compression` 均已 AOT 验证；
- 不使用反射序列化、动态代理、运行时代码生成。

## 16. 测试方法

- 每个修复先构造“只改变一个变量”的对照；
- 性能数字取自实际 HTTP/AOT 运行，而非估算；
- 大库测试使用现有 112,333 本书库副本，避免污染用户数据；
- UI 使用真实 WebView2/GUI 截图与 DOM 检查；
- 发布前 `dotnet build` 0 警告 0 错误，并做 AOT 冒烟。

## 17. 已知限制

- 旧版 `.doc`/`.ppt` 与 `.dwg` 需要外部转换或系统程序；
- GDI+ 无法解码 AVIF 时封面回退原图，由 Chromium 显示；
- 内置阅读器基于 AGPL-3.0 的 Readest/nn-reader，再分发需遵守其许可。

## 18. v1.0.2 交互修复

- **阅读器方向键**：书籍内容位于 iframe 时，原生 WM_KEYDOWN/加速键不一定到达宿主。改为用 `AddScriptToExecuteOnDocumentCreatedAsync` 注入捕获阶段 keydown；iframe 通过 `top.postMessage` 转发，宿主 `prevBook/nextBook` 切书；原生路径加 250ms 去抖避免双触发。
- **图片显示模式**：NSelect 原宽度 94px，选项被省略成“自动…/适应…”。加宽到 132px 并给菜单 minWidth 144px；新增自动居中、适应高度。
- **图片全屏只留图片**：页面维护 immersive 状态 + CSS 隐藏标题栏/缩略图；原生 `OnFullscreenChanged` 通过 `moon-fullscreen` 事件同步 F11/Esc 的真实全屏状态。
- **幻灯片与背景音乐**：间隔改为 double（0.1–60s）并迁移 SQLite 字段；背景音乐支持文件夹递归扫描、曲目索引持久化。
- **音乐播放器**：`loadPlaylist` 改为直接用返回的 PlaylistRecord 调 `loadQueue`，避免 `loadTracks` 从旧 route.query 覆盖列表；背景层固定 z-index，磨玻璃从 blur(28px)/inset -60px 调整为 blur(14px)/inset -18px。
- **电源**：`shutdown.exe /s|/r /t N`、`/h` 休眠、`/a` 取消；定时开机依赖主板 RTC/BIOS，软件层只提供定时关机/重启。

## 19. v1.0.3 阅读顺序与背景轮播

- **阅读器切书顺序**：开书时把书架当前的 q/sort/desc/filter/format/author/folder 作为 `nav` 上下文传给 `ReaderWindow`；`DbStore.ReadingOrder(LibraryQuery)` 复用书架同一套 `BuildWhere/BuildOrder`，首次切书时缓存 ID 顺序，↑/↓ 严格按书架顺序执行。无上下文时才退回全库标题顺序。
- **音乐背景控制**：新增 `musicBackgroundRandom/Fit/Blur` 与 double 间隔字段；`MusicPlayer` 用背景设置面板提供滑块/上一张/下一张/随机/显示方式/磨玻璃 0–10；背景层 z-index 固定，工具条允许换行。

## 20. v1.0.4 切书顺序与音乐面板

- **最近阅读顺序被开书动作改变**：`ReaderWindow` 打开时会写 `last_read_at`，若随后才计算导航列表，当前书会跳到第一位，导致上键无效、下键走到原第二本。改为在构造时先调用 `ReadingOrder(nav)` 冻结列表，再写进度。
- **音乐背景设置黑屏**：`MusicPlayer` 模板引用了未定义的 `mediaSettings`，点击面板触发渲染异常。补上 `computed(() => state.settings.media)` 后恢复。
- **顶栏重叠**：音乐窗口工具条增加 9px 顶部内边距与最小高度，并保留全局工具条换行；设置版本号统一为 1.0.4。

## 21. v1.0.5 书架顺序与音乐布局

- **书架顺序稳定**：打开书籍不再写 `last_read_at`，`HandleBookConfig` 保存进度也不再广播书库变更，避免书架重新查询导致当前书被“最近阅读”挪到第一位。阅读器在构造时冻结 `ReadingOrder(nav)`，上下键只在该固定序列内移动。
- **音乐布局**：`.music-layout` 改为 1fr/2fr 网格，左侧信息与列表，右侧歌词与背景图；背景层和遮罩限制在右侧容器，磨玻璃/显示方式由设置面板控制。

## 22. v1.0.6 歌词焦点与自然背景

- 不再渲染全部歌词，`lyricView` 只取 activeLine±1，使用 `<Transition>` 做淡入淡出；当前行居中放大。
- 去掉左右卡片边框和右侧独立背景容器，背景层回到整窗，遮罩采用左侧深、右侧浅的水平渐变，实现自然过渡。

## 23. v1.0.7 背景居中作用域

- 背景层从整窗移回 `.music-right`（右侧 2/3），`background-position:50% 50%` 在右侧区域内居中；左侧 1/3 保持深色，无卡片边框。

## 24. v1.0.8 切歌标题闪现旧名

- `loadTrack` 在设置新 path 前未清空 `meta`，标题 computed 先取到上一首的 `meta.title`，等 `audioMeta` 返回后才更新。切歌开始时立即 `meta.value = null`，标题先用新文件名，元数据返回后自然替换。

## 25. v1.0.9 切歌时界面上下跳动

- v1.0.8 在切歌开始清空 `meta` 后，模板里的 `<span v-if="meta?.album">` 整行消失，`.music-info` 高度变小，下方进度条、控制按钮和播放列表整体上移；新元数据返回后又下移。
- 同时标题/歌手没有强制单行，遇到长名称换行也会改变信息区高度。
- 修复：`.music-info` 设固定 `min-height:80px` 并改为纵向 flex；专辑名行始终渲染（内容为空也占 `15px`）；标题、歌手、专辑名统一 `white-space:nowrap` + `text-overflow:ellipsis`。切歌过程中左栏各块位置不再变化。

## 26. v1.0.10 播放列表删除与全屏抽屉

- 后端 `DELETE /api/media/playlists/{id}` 与前端 `api.deletePlaylist` 早已存在，但音乐播放器没有入口。新增“播放列表管理”弹窗：列出 `kind=audio` 的已保存列表，可载入或删除；删除走二次确认，只删 `playlists/playlist_items`，不动音乐文件；若删的是当前列表，清空 `playlistId/playlistName` 与路由 `playlist` 参数，队列继续播放。
- 全屏抽屉：`MediaViewerWindow.OnFullscreenChanged` 会派发 `moon-fullscreen`，前端监听后设置 `immersive`（同时监听 `document.fullscreenchange` 作为浏览器回退）。进入全屏后 `.music-layout` 变单列，`.music-left` 改为 `position:fixed` + `translateX(-102%)`；左侧 10px 热区 `mouseenter` 展开，面板 `mouseleave` 收起。
- `fixed` 不受父级 `overflow:hidden` 裁剪，前提是祖先没有 `transform/filter`；`.viewer` 满足该条件。

## 27. v1.0.11 图片背景音乐列表与重复标注

- 图片浏览器：新增设置列 `media_image_bgm_playlist`（`ImageBackgroundMusicPlaylist`）。幻灯片设置里列出 `kind=audio` 的已保存列表，选中后通过 `/api/media/playlists/{id}` 取队列；列表、文件夹、单文件三种来源互斥，选择其一时清空另外两项。启动时优先按列表恢复，其次文件夹，最后单文件。
- 重复标注根因：阅读器先创建高亮（booknote A，带 color，note 为空），再给该高亮写笔记时会新建 booknote B（同 CFI/原文，带 note，color 为空），两条都留在 `config_json.booknotes` 里，因此笔记页显示两次。
- 存储层：`DbStore.DedupeNotes` 按 `(book_id,type,cfi,text)` 合并，优先保留未删除、带笔记、较新的记录，继承非空 color/style，取最早 createdAt 与最晚 updatedAt；启动时跑一遍，`SyncNotes` 每本书同步后再跑一遍。
- 配置层：`NormalizeBooknotes` 在 `UpsertConfig` 与 `GET /api/bookconfig/{id}` 时合并重复 booknote，阅读器重新加载后标注列表也只剩一条；无重复时原样返回，不改写 JSON。
- 删除：`MarkNoteDeleted` 把同 CFI/类型/原文的兄弟 booknote 一起标记 `deletedAt`，否则下次同步会以另一条 ID 复活。
- 验证：用真实库副本启动 JIT 构建，5 条标注合并为 3 条；`GET /api/bookconfig` 返回的 `booknotes` 由 2 条变为 1 条且保留 note 与 color。
