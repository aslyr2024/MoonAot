using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MoonAot.Server;
using MoonAot.Services;
using MoonAot.Store;

namespace MoonAot.Win;

/// <summary>
/// 内置浏览器：顶部本地 Vue 工具栏（标签页/地址栏/书签/历史/翻译/注入/DevTools），
/// 下方每个标签一个独立 WebView2。所有标签共享同一 WebView2 环境。
/// </summary>
public sealed class BrowserWindow : WebViewWindowBase
{
    private const int MinBarHeight = 78;
    private const int MaxBarHeight = 420;
    private const string Home = "https://www.bing.com";

    private sealed class Tab
    {
        public string Id = Guid.NewGuid().ToString("N");
        public bool Primary;
        public IComObject<ICoreWebView2Controller>? Controller;
        public IComObject<ICoreWebView2>? Web;
        public CoreWebView2Events? Events;
        public string Url = Home;
        public string Title = "新标签页";
        public bool Loading;
    }

    private readonly List<Tab> _tabs = new();
    private Tab? _active;
    private int _barHeight = MinBarHeight;
    private string _pendingUrl;
    private bool _disposedTabs;

    public static new BrowserWindow? Active { get; private set; }

    public BrowserWindow(string? url = null) : base("浏览器")
    {
        _pendingUrl = string.IsNullOrWhiteSpace(url) ? Home : url.Trim();
        Active = this;
        Closing += (_, _) =>
        {
            if (ReferenceEquals(Active, this)) Active = null;
            AppWindows.OnBrowserClosed(this);
        };
    }

    protected override bool EnableDefaultContextMenu => true;
    protected override int ToolbarHeight => _barHeight;
    protected override string? ToolbarUrl => MoonServer.Instance.BaseUrl + "/shelf/#/browserbar";
    protected override WindowState GetSavedState(AppSettings s) => s.BrowserWindow;

    protected override RECT GetContentBounds()
    {
        var r = base.GetContentBounds();
        r.top += _barHeight;
        return r;
    }

    private Tab? ActiveTab => _active != null && _tabs.Contains(_active) ? _active : _tabs.FirstOrDefault();
    private IComObject<ICoreWebView2>? ActiveWeb => ActiveTab?.Web;

    protected override void OnWebViewCreated()
    {
        if (Web == null) return;
        var tab = new Tab
        {
            Primary = true,
            Web = Web,
            Controller = Controller,
            Events = WebEvents,
            Url = Normalize(_pendingUrl),
        };
        _tabs.Add(tab);
        WireState(tab);
        _active = tab;
        try { Web.Navigate(tab.Url); } catch (Exception ex) { Log.Warn("浏览器导航失败: " + ex.Message); }
        PushState();
    }

    public void NavigateTo(string? input)
    {
        var url = Normalize(input);
        if (_tabs.Count == 0) { _pendingUrl = url; return; }
        var tab = ActiveTab!;
        tab.Url = url;
        try { tab.Web?.Navigate(url); } catch (Exception ex) { Log.Warn("浏览器导航失败: " + ex.Message); }
    }

    private static string Normalize(string? input)
    {
        var text = (input ?? "").Trim();
        if (text.Length == 0) return Home;
        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return text;
        try
        {
            if (File.Exists(text)) return new Uri(Path.GetFullPath(text)).AbsoluteUri;
            if (Directory.Exists(text)) return new Uri(Path.GetFullPath(text) + Path.DirectorySeparatorChar).AbsoluteUri;
        }
        catch { }
        if (text.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("127.", StringComparison.Ordinal) ||
            text.StartsWith("192.168.", StringComparison.Ordinal) ||
            text.StartsWith("10.", StringComparison.Ordinal))
            return "http://" + text;
        if (!text.Contains(' ') && (text.Contains('.') || text.Contains(':')))
            return "https://" + text;
        return "https://www.bing.com/search?q=" + Uri.EscapeDataString(text);
    }

    private async Task<Tab?> CreateTabAsync(string? url, bool activate)
    {
        try
        {
            var env = await GetEnvironmentAsync().ConfigureAwait(true);
            if (env == null) return null;
            var controller = await env.CreateCoreWebView2ControllerAsync(Handle).ConfigureAwait(true);
            if (controller == null) return null;
            controller.Bounds = GetContentBounds();
            try { controller.IsVisible = false; } catch { }
            var web = controller.CoreWebView2;
            if (web == null) { try { controller.Object.Close(); } catch { } controller.Dispose(); return null; }
            try
            {
                var st = web.Settings;
                if (st != null)
                {
                    var s = st.Object;
                    s.IsStatusBarEnabled = false;
                    s.AreDevToolsEnabled = true;
                    s.AreDefaultContextMenusEnabled = true;
                    s.IsZoomControlEnabled = false;
                    s.IsSwipeNavigationEnabled = false;
                    s.IsPinchZoomEnabled = false;
                    s.AreBrowserAcceleratorKeysEnabled = true;
                }
            }
            catch { }
            var tab = new Tab
            {
                Controller = controller,
                Web = web,
                Url = Normalize(url),
            };
            var events = new CoreWebView2Events(web);
            tab.Events = events;
            WireState(tab);
            events.WebMessageReceived += (_, e) =>
            {
                try
                {
                    var msg = e.TryGetWebMessageAsString();
                    if (!string.IsNullOrEmpty(msg)) OnContentMessage(tab, msg);
                }
                catch { }
            };
            events.NewWindowRequested += (_, e) =>
            {
                try
                {
                    e.Handled = true;
                    var uri = e.Uri;
                    if (!string.IsNullOrEmpty(uri)) _ = CreateTabAsync(uri, true);
                }
                catch { }
            };
            _tabs.Add(tab);
            if (activate) Activate(tab); else PushState();
            try { web.Navigate(tab.Url); } catch { }
            if (activate) _ = FocusActiveAsync();
            return tab;
        }
        catch (Exception ex)
        {
            Log.Warn("新建浏览器标签失败: " + ex.Message);
            return null;
        }
    }

    private void WireState(Tab tab)
    {
        if (tab.Events == null) return;
        tab.Events.NavigationStarting += (_, _) =>
        {
            if (!_tabs.Contains(tab)) return;
            tab.Loading = true;
            if (ReferenceEquals(tab, ActiveTab)) PushState();
        };
        tab.Events.NavigationCompleted += (_, _) =>
        {
            if (!_tabs.Contains(tab)) return;
            tab.Loading = false;
            UpdateTabInfo(tab);
            PushState();
        };
        tab.Events.SourceChanged += (_, _) =>
        {
            if (!_tabs.Contains(tab)) return;
            UpdateTabInfo(tab);
            if (ReferenceEquals(tab, ActiveTab)) PushState();
        };
        tab.Events.DocumentTitleChanged += (_, _) =>
        {
            if (!_tabs.Contains(tab)) return;
            UpdateTabInfo(tab);
            PushState();
        };
    }

    private void UpdateTabInfo(Tab tab)
    {
        try
        {
            if (tab.Web == null) return;
            var url = tab.Web.Object.Source ?? tab.Url;
            if (!string.IsNullOrEmpty(url)) tab.Url = url;
            var title = tab.Web.Object.DocumentTitle;
            if (!string.IsNullOrWhiteSpace(title))
                tab.Title = title.Length > 60 ? title[..60] : title;
        }
        catch { }
        if (ReferenceEquals(tab, ActiveTab))
            Text = (tab.Title == "新标签页" ? "浏览器" : tab.Title + " - 浏览器");
    }

    private void Activate(Tab tab)
    {
        if (!_tabs.Contains(tab)) return;
        foreach (var t in _tabs)
        {
            try { if (t.Controller != null) t.Controller.IsVisible = ReferenceEquals(t, tab); } catch { }
        }
        _active = tab;
        UpdateTabInfo(tab);
        PushState();
        _ = FocusActiveAsync();
    }

    private async Task FocusActiveAsync()
    {
        try
        {
            await Task.Yield();
            ActiveTab?.Controller?.MoveFocus(COREWEBVIEW2_MOVE_FOCUS_REASON.COREWEBVIEW2_MOVE_FOCUS_REASON_PROGRAMMATIC, false);
        }
        catch { }
    }

    protected override bool OnFocusChanged(bool setOrKill)
    {
        if (setOrKill)
        {
            try
            {
                var c = ActiveTab?.Controller;
                if (c != null)
                {
                    c.MoveFocus(COREWEBVIEW2_MOVE_FOCUS_REASON.COREWEBVIEW2_MOVE_FOCUS_REASON_PROGRAMMATIC, false);
                    return true;
                }
            }
            catch { }
        }
        return base.OnFocusChanged(setOrKill);
    }

    protected override bool OnResized(WindowResizedType type, SIZE size)
    {
        var result = base.OnResized(type, size);
        ApplyTabBounds();
        return result;
    }

    private void ApplyTabBounds()
    {
        var bounds = GetContentBounds();
        foreach (var t in _tabs)
        {
            if (t.Primary) continue;
            try { if (t.Controller != null) t.Controller.Bounds = bounds; } catch { }
        }
    }

    private void CloseTab(string id)
    {
        var tab = _tabs.FirstOrDefault(t => t.Id == id);
        if (tab == null) return;
        if (_tabs.Count <= 1)
        {
            NavigateTo(Home);
            return;
        }
        int index = _tabs.IndexOf(tab);
        if (tab.Primary)
        {
            // 首个标签使用基类创建的 WebView，窗口销毁时统一释放；这里只隐藏并移出列表
            try { if (tab.Controller != null) tab.Controller.IsVisible = false; } catch { }
        }
        else
        {
            try { tab.Events?.Dispose(); } catch { }
            try { tab.Web?.Dispose(); } catch { }
            try { tab.Controller?.Object.Close(); } catch { }
            try { tab.Controller?.Dispose(); } catch { }
        }
        _tabs.Remove(tab);
        if (ReferenceEquals(_active, tab))
        {
            _active = null;
            Activate(_tabs[Math.Max(0, index - 1)]);
        }
        else PushState();
    }

    private void PushState()
    {
        var tab = ActiveTab;
        string url = "", title = "";
        bool canBack = false, canForward = false;
        try
        {
            if (tab?.Web != null)
            {
                url = tab.Web.Object.Source ?? tab.Url;
                title = tab.Web.Object.DocumentTitle ?? tab.Title;
                canBack = tab.Web.Object.CanGoBack;
                canForward = tab.Web.Object.CanGoForward;
            }
        }
        catch { }
        var sb = new StringBuilder(512);
        sb.Append("{\"url\":").Append(JsonStr(url));
        sb.Append(",\"title\":").Append(JsonStr(title));
        sb.Append(",\"loading\":").Append(tab?.Loading == true ? "true" : "false");
        sb.Append(",\"canBack\":").Append(canBack ? "true" : "false");
        sb.Append(",\"canForward\":").Append(canForward ? "true" : "false");
        sb.Append(",\"barHeight\":").Append(_barHeight);
        sb.Append(",\"tabs\":[");
        for (int i = 0; i < _tabs.Count; i++)
        {
            var t = _tabs[i];
            if (i > 0) sb.Append(',');
            sb.Append("{\"id\":").Append(JsonStr(t.Id))
              .Append(",\"title\":").Append(JsonStr(t.Title))
              .Append(",\"url\":").Append(JsonStr(t.Url))
              .Append(",\"loading\":").Append(t.Loading ? "true" : "false")
              .Append(",\"active\":").Append(ReferenceEquals(t, tab) ? "true" : "false").Append('}');
        }
        sb.Append("]}");
        var json = sb.ToString();
        SendToToolbar("try{window.__browser&&window.__browser.onState(" + json + ")}catch(e){}");
    }

    protected override void OnToolbarMessageReceived(string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var t) || t.ValueKind != JsonValueKind.String) return;
            string? S(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            switch (t.GetString())
            {
                case "navigate": NavigateTo(S("url")); return;
                case "back": try { ActiveWeb?.GoBack(); } catch { } return;
                case "forward": try { ActiveWeb?.GoForward(); } catch { } return;
                case "reload": try { ActiveWeb?.Reload(); } catch { } return;
                case "stop":
                    try { ActiveWeb?.Stop(); } catch { }
                    if (ActiveTab != null) ActiveTab.Loading = false;
                    PushState();
                    return;
                case "home": NavigateTo(Home); return;
                case "newTab": _ = CreateTabAsync(S("url"), true); return;
                case "closeTab": CloseTab(S("id") ?? ""); return;
                case "activateTab":
                    {
                        var id = S("id");
                        var tab = _tabs.FirstOrDefault(x => x.Id == id);
                        if (tab != null) Activate(tab);
                        return;
                    }
                case "barHeight":
                    {
                        if (root.TryGetProperty("height", out var hv) && hv.TryGetInt32(out var h))
                        {
                            _barHeight = Math.Clamp(h, MinBarHeight, MaxBarHeight);
                            ApplyContentBounds();
                            ApplyTabBounds();
                        }
                        return;
                    }
                case "devtools":
                    try { ActiveWeb?.OpenDevToolsWindow(); } catch (Exception ex) { Log.Warn("打开开发者工具失败: " + ex.Message); }
                    return;
                case "inject":
                    {
                        var code = S("code");
                        if (!string.IsNullOrWhiteSpace(code))
                        {
                            try { ActiveWeb?.ExecuteScript(code!); } catch (Exception ex) { Log.Warn("注入脚本失败: " + ex.Message); }
                        }
                        return;
                    }
                case "persistInject":
                    {
                        var code = S("code");
                        if (!string.IsNullOrWhiteSpace(code))
                        {
                            _ = ActiveWeb?.Object.AddScriptToExecuteOnDocumentCreatedAsync(code);
                        }
                        return;
                    }
                case "translate":
                    TranslateActive(S("lang") ?? "en|zh-CN");
                    return;
                case "external":
                    {
                        var url = S("url");
                        if (!string.IsNullOrEmpty(url))
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                        return;
                    }
                case "ready": PushState(); return;
            }
        }
        catch (Exception ex) { Log.Warn("浏览器工具栏消息失败: " + ex.Message); }
    }

    private void TranslateActive(string langpair)
    {
        var tab = ActiveTab;
        if (tab?.Web == null) return;
        var js = """
(function(){
  try {
    var nodes = [];
    var walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT, { acceptNode: function(n) {
      var p = n.parentElement; if (!p) return NodeFilter.FILTER_REJECT;
      var tag = p.tagName;
      if (/^(SCRIPT|STYLE|NOSCRIPT|TEXTAREA|INPUT|SELECT|OPTION|CODE|PRE)$/.test(tag)) return NodeFilter.FILTER_REJECT;
      var s = (n.nodeValue || '').trim(); if (s.length < 2) return NodeFilter.FILTER_REJECT;
      return NodeFilter.FILTER_ACCEPT;
    }});
    var n; while ((n = walker.nextNode()) && nodes.length < 120) nodes.push(n);
    if (!nodes.length) return;
    var id = 't' + Date.now();
    window.__moonTrans = { id: id, targets: nodes };
    window.__moonTranslateApply = function(id2, arr) {
      if (!window.__moonTrans || window.__moonTrans.id !== id2) return;
      for (var i = 0; i < arr.length && i < window.__moonTrans.targets.length; i++) {
        var v = arr[i]; if (typeof v === 'string' && v) window.__moonTrans.targets[i].nodeValue = v;
      }
    };
    chrome.webview.postMessage(JSON.stringify({
      type: 'translateTexts', id: id, lang: __LANG__,
      texts: nodes.map(function(x) { return (x.nodeValue || '').trim().slice(0, 500); })
    }));
  } catch (e) { }
})();
""".Replace("__LANG__", JsonStr(langpair));
        try { tab.Web.ExecuteScript(js); } catch (Exception ex) { Log.Warn("启动翻译失败: " + ex.Message); }
    }

    protected override void OnWebMessageReceived(string message)
    {
        var primary = _tabs.FirstOrDefault(t => t.Primary);
        if (primary != null) OnContentMessage(primary, message);
    }

    private void OnContentMessage(Tab tab, string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var t) || t.ValueKind != JsonValueKind.String) return;
            if (t.GetString() != "translateTexts") return;
            var id = root.TryGetProperty("id", out var iv) && iv.ValueKind == JsonValueKind.String ? iv.GetString() ?? "" : "";
            var lang = root.TryGetProperty("lang", out var lv) && lv.ValueKind == JsonValueKind.String ? lv.GetString() ?? "en|zh-CN" : "en|zh-CN";
            var texts = new List<string>();
            if (root.TryGetProperty("texts", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var e in arr.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.String) texts.Add(e.GetString() ?? "");
            if (texts.Count == 0 || string.IsNullOrEmpty(id)) return;
            _ = TranslateAndApply(tab, id, lang, texts);
        }
        catch { }
    }

    private async Task TranslateAndApply(Tab tab, string id, string langpair, List<string> texts)
    {
        try
        {
            var result = await TranslateService.TranslateAsync(texts, langpair).ConfigureAwait(false);
            if (!_tabs.Contains(tab) || tab.Web == null) return;
            var json = JsonSerializer.Serialize(result, Info<List<string>>.I);
            var js = "try{window.__moonTranslateApply(" + JsonStr(id) + "," + json + ")}catch(e){}";
            SendToTab(tab, js);
        }
        catch (Exception ex) { Log.Warn("翻译失败: " + ex.Message); }
    }

    private void SendToTab(Tab tab, string js)
    {
        if (IsDisposed || IsDisposing) return;
        if (IsRunningAsUIThread)
        {
            try { tab.Web?.ExecuteScript(js); } catch { }
            return;
        }
        try { _ = RunTaskOnUIThread(() => { try { tab.Web?.ExecuteScript(js); } catch { } }); } catch { }
    }

    protected override void HandleNewWindow(string uri)
    {
        _ = CreateTabAsync(uri, true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposedTabs)
        {
            _disposedTabs = true;
            foreach (var t in _tabs.ToList())
            {
                if (t.Primary) continue;
                try { t.Events?.Dispose(); } catch { }
                try { t.Web?.Dispose(); } catch { }
                try { t.Controller?.Object.Close(); } catch { }
                try { t.Controller?.Dispose(); } catch { }
            }
            _tabs.Clear();
        }
        if (ReferenceEquals(Active, this)) Active = null;
        base.Dispose(disposing);
    }
}
