/* ============ TinyEverything WebView UI — 虚拟滚动 ============ */
"use strict";

// OVERSCAN 加大缓冲行数；PREFETCH 预取可视区前后页数，避免快速拖动滚动条时出现空白/加载不出内容
const ROW_H = 26, PAGE = 200, OVERSCAN = 24, PREFETCH = 4;
const $ = (s) => document.querySelector(s);

const state = {
  q: "", sort: "size", dir: "desc",
  total: 0, version: -1, elapsed: 0,
};
const cache = new Map();      // pageIdx -> rows[]
const inflight = new Map();   // pageIdx -> Promise

const resultsEl = $("#results");
const rowsEl = $("#rows");

/* ---------- 图标缓存 ---------- */
// 1×1 透明占位，避免图标加载前露出破图。
const TRANSPARENT = "data:image/gif;base64,R0lGODlhAQABAAAAACH5BAEKAAEALAAAAAABAAEAAAICTAEAOw==";
const iconCache = new Map();   // key(扩展名|目录) -> objectURL（已加载，设置即显示，无网络、无闪烁）
const iconWant = new Map();    // key -> Set<HTMLImageElement>（等待加载的 img）
const iconKey = (e) => (e.isDir ? "D" : "F") + (e.ext || "").toLowerCase();
const iconSrc = (e) => `/tiny/api/icon?ext=${encodeURIComponent((e.ext || "").toLowerCase())}&dir=${e.isDir ? 1 : 0}`;

// 渲染后调用：命中缓存的同步设置 src；未命中的先放占位，加载完就地补上（不再整段重建）。
function hydrateIcons() {
  const imgs = rowsEl.querySelectorAll("img.ic");
  for (const img of imgs) {
    const key = img.dataset.ickey;
    const cached = iconCache.get(key);
    if (cached) {
      if (img.src !== cached) img.src = cached;
      continue;
    }
    if (!iconWant.has(key)) {
      iconWant.set(key, new Set());
      const url = img.dataset.icsrc;
      fetch(url).then((r) => (r.ok ? r.blob() : null)).then((b) => {
        if (!b) { iconWant.delete(key); return; }
        const objUrl = URL.createObjectURL(b);
        iconCache.set(key, objUrl);
        const set = iconWant.get(key);
        if (set) {
          for (const im of set) im.src = objUrl;
          iconWant.delete(key);
        }
      }).catch(() => iconWant.delete(key));
    }
    iconWant.get(key).add(img);
  }
}

/* ---------- 工具 ---------- */
const esc = (s) => String(s).replace(/[&<>"']/g, (c) =>
  ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));

function fmtSize(n) {
  if (n == null) return "";
  const u = ["B", "KB", "MB", "GB", "TB", "PB"];
  let i = 0, v = n;
  while (v >= 1024 && i < u.length - 1) { v /= 1024; i++; }
  return (i === 0 ? v : v.toFixed(v >= 100 ? 0 : v >= 10 ? 1 : 2)) + " " + u[i];
}
const TICKS_EPOCH_DIFF = 621355968000000000;
function fmtDate(ticks) {
  if (!ticks) return "";
  const ms = (ticks - TICKS_EPOCH_DIFF) / 10000;
  const d = new Date(ms);
  if (isNaN(d)) return "";
  const p = (x) => String(x).padStart(2, "0");
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}`;
}
const dirOf = (p) => {
  const i = p.lastIndexOf("\\");
  return i > 0 ? p.slice(0, i) : p;
};

/* ---------- Toast ---------- */
function toast(msg, ms) {
  let t = document.getElementById("toast");
  if (!t) {
    t = document.createElement("div");
    t.id = "toast";
    t.style.cssText = "position:fixed;left:50%;bottom:52px;transform:translateX(-50%);" +
      "background:#0e1218;color:#39ffe0;border:1px solid #2c2c32;padding:10px 18px;" +
      "border-radius:8px;font-size:13px;z-index:9999;box-shadow:0 6px 24px rgba(0,0,0,.5);" +
      "transition:opacity .3s;max-width:80vw;text-align:center;";
    document.body.appendChild(t);
  }
  t.textContent = msg;
  t.style.opacity = "1";
  clearTimeout(t._h);
  t._h = setTimeout(() => { t.style.opacity = "0"; }, ms || 3000);
}

/* ---------- 数据加载 ---------- */
function fetchPage(page, force) {
  if (!force && cache.has(page)) return Promise.resolve(cache.get(page));
  if (inflight.has(page)) return inflight.get(page);
  const url = `/tiny/api/search?q=${encodeURIComponent(state.q)}&sort=${state.sort}&dir=${state.dir}` +
              `&offset=${page * PAGE}&limit=${PAGE}`;
  const pr = fetch(url).then((r) => r.json()).then((data) => {
    if (page === 0) {
      state.total = data.total;
      state.elapsed = data.elapsedMs;
      updateStatusbar();
    }
    // 空页且并非真正末尾：视为本次加载异常，不缓存空结果，允许下一次滚动时重试，
    // 避免因瞬时失败导致这一页永远显示空白（滚动条拖太快“加载不出来”）。
    if (data.items.length === 0 && data.total > page * PAGE) {
      inflight.delete(page);
      return [];
    }
    cache.set(page, data.items);
    inflight.delete(page);
    return data.items;
  }).catch(() => { inflight.delete(page); return []; });
  inflight.set(page, pr);
  return pr;
}

// 预取一段页码（跳过已缓存/已在途的页），让快速滚动时数据已就绪
function prefetchPages(a, b) {
  const last = Math.ceil(state.total / PAGE);
  for (let p = a; p <= b; p++) {
    if (p < 0 || p > last) continue;
    if (!cache.has(p) && !inflight.has(p)) fetchPage(p);
  }
}

function pageOf(row) { return Math.floor(row / PAGE); }
function rowAt(i) {
  const p = pageOf(i);
  const rows = cache.get(p);
  return rows ? rows[i - p * PAGE] : undefined;
}

/* ---------- 渲染 ---------- */
// 合并连续滚动（如快速拖拽滚动条）：只在“停顿 40ms”后渲染一次，
// 避免对 100万+ 条结果在拖动过程中发起海量分页请求。
let renderTimer = null;
function scheduleRender() {
  if (renderTimer) clearTimeout(renderTimer);
  renderTimer = setTimeout(() => { renderTimer = null; render(); }, 40);
}

function render() {
  const total = state.total;
  if (total === 0) {
    rowsEl.innerHTML = `<div style="padding:40px;text-align:center;color:var(--dim)">
      ${state.q ? "— 无匹配结果 —" : "索引为空 / 正在建立索引…"}</div>`;
    rowsEl.style.paddingTop = "0px";
    rowsEl.style.paddingBottom = "0px";
    return;
  }
  const scrollTop = resultsEl.scrollTop;
  const start = Math.max(0, Math.floor(scrollTop / ROW_H) - OVERSCAN);
  const visible = Math.ceil(resultsEl.clientHeight / ROW_H) + OVERSCAN * 2;
  const end = Math.min(total, start + visible);

  // 加载可视区必需页面
  for (let p = pageOf(start); p <= pageOf(end - 1); p++) {
    if (!cache.has(p)) fetchPage(p).then(() => scheduleRender());
  }
  // 预取可视区前后各 PREFETCH 页：快速拖动滚动条时数据已缓存，不再出现大段空白
  prefetchPages(pageOf(start) - PREFETCH, pageOf(start) - 1);
  prefetchPages(pageOf(end - 1) + 1, pageOf(end - 1) + PREFETCH);

  let html = "";
  for (let i = start; i < end; i++) html += rowHtml(i, rowAt(i));
  rowsEl.innerHTML = html;
  rowsEl.style.paddingTop = start * ROW_H + "px";
  rowsEl.style.paddingBottom = Math.max(0, (total - end)) * ROW_H + "px";
  hydrateIcons();
}

function rowHtml(i, e) {
  if (!e) {
    return `<div class="row ghost"><div class="c-name"></div><div class="c-path"></div>
      <div class="c-size"></div><div class="c-type"></div><div class="c-date"></div></div>`;
  }
  const type = e.isDir ? "文件夹" : (e.ext ? esc(e.ext.toUpperCase()) + " 文件" : "文件");
  return `<div class="row${e.isDir ? " dir" : ""}" data-path="${esc(e.path)}" data-dir="${e.isDir ? 1 : 0}">
    <div class="c-name"><img class="ic" data-ickey="${iconKey(e)}" data-icsrc="${iconSrc(e)}" src="${TRANSPARENT}" alt=""><span title="${esc(e.name)}">${esc(e.name)}</span></div>
    <div class="c-path" title="${esc(dirOf(e.path))}">${esc(dirOf(e.path))}</div>
    <div class="c-size">${e.isDir ? "" : fmtSize(e.size)}</div>
    <div class="c-type">${type}</div>
    <div class="c-date">${fmtDate(e.mtime)}</div>
  </div>`;
}

/* ---------- 搜索 ---------- */
let searchTimer = null;
function onQueryInput() {
  clearTimeout(searchTimer);
  searchTimer = setTimeout(() => doSearch(true), 180);
}

async function doSearch(reset) {
  if (reset) {
    cache.clear();
    state.total = 0;
    resultsEl.scrollTop = 0;
  }
  state.q = $("#q").value.trim();
  await fetchPage(0);
  updateSortHeader();
  render();
}

function updateSortHeader() {
  document.querySelectorAll(".th").forEach((th) => {
    const on = th.dataset.sort === state.sort ||
      (th.dataset.sort === "date" && state.sort === "date");
    th.classList.toggle("active", on);
    th.classList.toggle("asc", on && state.dir === "asc");
    th.classList.toggle("desc", on && state.dir === "desc");
  });
}

/* ---------- 状态栏 ---------- */
function updateStatusbar() {
  $("#st-total").textContent = `共 ${(state.dbTotal ?? 0).toLocaleString()} 对象`;
  $("#st-match").textContent = state.q ? `匹配 ${state.total.toLocaleString()}` : "";
  $("#st-time").textContent = state.q || state.total ? `${state.elapsed} ms` : "";
}

// 索引版本变化时不立即重建列表（会闪屏），等其“稳定”（800ms 内无新变化）后再温和刷新一次，
// 且仅重绘当前可见窗口、保留滚动位置，避免持续闪屏与操作卡顿。
let lastVersion = -1;
let pendingRefresh = false;
async function refreshResults() {
  state.q = $("#q").value.trim();
  const scrollTop = resultsEl.scrollTop;
  const start = Math.max(0, Math.floor(scrollTop / ROW_H) - OVERSCAN);
  const visible = Math.ceil(resultsEl.clientHeight / ROW_H) + OVERSCAN * 2;
  const end = Math.min(state.total, start + visible);
  await fetchPage(0, true);
  const tasks = [];
  for (let p = pageOf(start); p <= pageOf(end - 1); p++) tasks.push(fetchPage(p, true));
  await Promise.all(tasks);
  updateSortHeader();
  render();
}

async function pollStatus() {
  try {
    const r = await fetch("/tiny/api/status");
    const s = await r.json();
    state.dbTotal = s.total;
    const dots = s.drives.map((d) => {
      const cls = d.state === "ready" ? "ok" : d.state === "error" ? "err" : "busy";
      const tip = `${d.letter}: ${d.mode} ${d.state} ${d.count.toLocaleString()} 项 ${d.message}`;
      return `<span class="drive-dot" title="${esc(tip)}"><span class="${cls}">●</span>${d.letter}</span>`;
    }).join("");
    $("#st-drives").innerHTML = dots;
    state.version = s.version;
    updateStatusbar();
    // 版本变化：延迟到索引稳定后再刷新（持续索引期间不会每秒重建）
    if (lastVersion !== -1 && s.version !== lastVersion && !pendingRefresh) {
      pendingRefresh = true;
      const snap = s.version;
      setTimeout(() => {
        pendingRefresh = false;
        if (state.version === snap) refreshResults(); // 期间无新变化 = 索引已稳定
      }, 800);
    }
    lastVersion = s.version;
  } catch { }
}

/* ---------- 文件操作 ---------- */
function post(url, body) {
  return fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body || {}),
  });
}
const openPath = (p) => post("/tiny/api/open", { path: p });
const revealPath = (p) => post("/tiny/api/reveal", { path: p });

function rowFromEvent(ev) {
  return ev.target.closest ? ev.target.closest(".row[data-path]") : null;
}

resultsEl.addEventListener("dblclick", (ev) => {
  const row = rowFromEvent(ev);
  if (row) openPath(row.dataset.path);
});

/* ---------- 右键菜单 ---------- */
const ctxMenu = $("#ctx-menu");
let ctxPath = null, ctxIsDir = false;
resultsEl.addEventListener("contextmenu", (ev) => {
  const row = rowFromEvent(ev);
  if (!row) return;
  ev.preventDefault();
  ctxPath = row.dataset.path;
  ctxIsDir = row.dataset.dir === "1";
  ctxMenu.classList.remove("hidden");
  const x = Math.min(ev.clientX, innerWidth - 190);
  const y = Math.min(ev.clientY, innerHeight - 150);
  ctxMenu.style.left = x + "px";
  ctxMenu.style.top = y + "px";
});
document.addEventListener("click", () => ctxMenu.classList.add("hidden"));
ctxMenu.addEventListener("click", (ev) => {
  const act = ev.target.dataset?.act;
  if (!act || !ctxPath) return;
  if (act === "open") openPath(ctxPath);
  else if (act === "reveal") revealPath(ctxPath);
  else if (act === "copy-path") navigator.clipboard.writeText(ctxPath);
  else if (act === "copy-name") navigator.clipboard.writeText(ctxPath.split("\\").pop());
});

/* ---------- 表头排序 ---------- */
document.querySelectorAll(".th").forEach((th) => {
  th.addEventListener("click", () => {
    const s = th.dataset.sort;
    if (state.sort === s) state.dir = state.dir === "desc" ? "asc" : "desc";
    else { state.sort = s; state.dir = s === "size" || s === "date" ? "desc" : "asc"; }
    doSearch(true);
  });
});

/* ---------- 设置 ---------- */
const drawer = $("#settings-drawer"), mask = $("#settings-mask");
async function openSettings() {
  const [settings, drives] = await Promise.all([
    fetch("/tiny/api/settings").then((r) => r.json()),
    fetch("/tiny/api/drives").then((r) => r.json()),
  ]);
  $("#ta-exclude").value = (settings.excludedFolders || []).join("\n");
  $("#ta-include").value = (settings.includedFolders || []).join("\n");
  const sel = $("#sel-rescan-interval");
  const hours = String(settings.rescanIntervalHours ?? 0);
  if (![...sel.options].some((o) => o.value === hours))
    sel.add(new Option(`每 ${hours} 小时`, hours));
  sel.value = hours;
  const enabled = new Set(settings.enabledDrives || []);
  $("#drive-list").innerHTML = drives.map((d) => `
    <label class="drive-item">
      <input type="checkbox" value="${d.letter}" ${enabled.size === 0 || enabled.has(d.letter) ? "checked" : ""}>
      <b>${d.letter}:</b>
      <span>${esc(d.label || "")}</span>
      <span class="fmt">${esc(d.format || "")}</span>
      ${d.removable ? '<span class="usb">[USB]</span>' : ""}
    </label>`).join("");
  drawer.classList.remove("hidden");
  mask.classList.remove("hidden");
}
function closeSettings() {
  drawer.classList.add("hidden");
  mask.classList.add("hidden");
}
$("#btn-settings").addEventListener("click", openSettings);
$("#btn-close-settings").addEventListener("click", closeSettings);
mask.addEventListener("click", closeSettings);
$("#btn-save-settings").addEventListener("click", async () => {
  const checked = [...document.querySelectorAll("#drive-list input:checked")].map((i) => i.value);
  const all = document.querySelectorAll("#drive-list input").length;
  const lines = (id) => $(id).value.split("\n").map((s) => s.trim()).filter(Boolean);
  await post("/tiny/api/settings", {
    excludedFolders: lines("#ta-exclude"),
    includedFolders: lines("#ta-include"),
    enabledDrives: checked.length === all ? [] : checked,
    rescanIntervalHours: parseInt($("#sel-rescan-interval").value, 10) || 0,
  });
  closeSettings();
});

$("#btn-rescan").addEventListener("click", () => post("/tiny/api/rescan", {}));

/* ---------- 打开所有：把搜索到的媒体文件加入内置播放器播放 ---------- */
$("#btn-openall").addEventListener("click", async () => {
  const q = state.q;
  if (!q) { toast("请先输入搜索条件（如 *.mp4）"); return; }
  toast("正在收集媒体文件…");
  try {
    const r = await post("/tiny/api/openall", { q, sort: state.sort, dir: state.dir }).then((x) => x.json());
    if (!r || !r.ok) { toast("打开失败：" + ((r && r.error) || "未知错误")); return; }
    const parts = [];
    if (r.audio) parts.push("音乐 " + r.audio);
    if (r.video) parts.push("视频 " + r.video);
    if (r.image) parts.push("图片 " + r.image);
    toast(parts.length ? ("已打开：" + parts.join("，")) : "没有匹配的媒体文件", 4000);
  } catch (e) {
    toast("打开失败：" + (e && e.message ? e.message : e));
  }
});

/* ---------- 键盘 ---------- */
document.addEventListener("keydown", (ev) => {
  if (ev.key === "F3" || (ev.ctrlKey && ev.key.toLowerCase() === "f")) {
    ev.preventDefault();
    $("#q").focus(); $("#q").select();
  } else if (ev.key === "Escape") {
    ctxMenu.classList.add("hidden");
    closeSettings();
  }
});

/* ---------- 启动 ---------- */
$("#q").addEventListener("input", onQueryInput);
resultsEl.addEventListener("scroll", scheduleRender);
window.addEventListener("resize", scheduleRender);
updateSortHeader();
doSearch(true);
pollStatus();
setInterval(pollStatus, 1000);
$("#q").focus();
