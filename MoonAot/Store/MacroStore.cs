using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using MoonAot.Services;

namespace MoonAot.Store;

/// <summary>
/// 按键精灵数据（SQLite）：macros / macro_steps / meta.macro_enabled。
/// 步骤顺序由 ord 保证；保存时整组替换，避免增量同步出错。
/// </summary>
public sealed class MacroStore
{
    public static MacroStore Instance { get; } = Create();

    private static MacroStore Create()
    {
        try { return new MacroStore(); }
        catch (Exception ex)
        {
            Log.Error("按键精灵存储初始化失败", ex);
            throw;
        }
    }

    private MacroStore()
    {
        Database.Instance.Initialize();
        SeedSample();
    }

    private static SqliteCommand Cmd(SqliteConnection cn, string sql, SqliteTransaction? tx = null)
    {
        var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        if (tx != null) cmd.Transaction = tx;
        return cmd;
    }

    private static void P(SqliteCommand c, string name, object? value)
        => c.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static string JoinKeys(List<string>? keys)
        => keys == null || keys.Count == 0 ? "" : string.Join('+', keys);

    private static List<string> SplitKeys(string? text)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return list;
        foreach (var part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            list.Add(part);
        return list;
    }

    private static MacroStep ReadStep(SqliteDataReader r) => new()
    {
        Kind = r.GetString(2),
        Keys = SplitKeys(r.GetString(3)),
        HoldMs = r.GetInt32(4),
        DelayMs = r.GetInt32(5),
        X = r.GetInt32(6),
        Y = r.GetInt32(7),
        Button = r.IsDBNull(8) ? null : r.GetString(8),
        Wheel = r.GetInt32(9),
        Text = r.IsDBNull(10) ? null : r.GetString(10),
    };

    private static MacroRecord ReadMacro(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        Name = r.GetString(1),
        Enabled = r.GetInt32(2) != 0,
        TriggerMode = r.GetString(3),
        Trigger = r.GetString(4),
        Repeat = r.GetInt32(5),
        RepeatDelayMs = r.GetInt32(6),
        LeadInMs = r.GetInt32(7),
        SendMode = r.GetString(8),
        CreatedAt = r.GetInt64(9),
        UpdatedAt = r.GetInt64(10),
    };

    private const string MacroColumns =
        "id,name,enabled,trigger_mode,trigger,repeat,repeat_delay_ms,lead_in_ms,send_mode,created_at,updated_at";

    public List<MacroRecord> List()
    {
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            var list = new List<MacroRecord>();
            using (var cmd = Cmd(cn, "SELECT " + MacroColumns + " FROM macros ORDER BY updated_at DESC;"))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read()) list.Add(ReadMacro(r));
            }
            if (list.Count == 0) return list;

            var index = new Dictionary<string, MacroRecord>(StringComparer.Ordinal);
            foreach (var m in list) index[m.Id] = m;
            using (var cmd = Cmd(cn, "SELECT macro_id,ord,kind,keys,hold_ms,delay_ms,x,y,button,wheel,text FROM macro_steps ORDER BY macro_id,ord;"))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    var id = r.GetString(0);
                    if (index.TryGetValue(id, out var m)) m.Steps.Add(ReadStep(r));
                }
            }
            return list;
        }
    }

    public List<MacroRecord> ListEnabled()
    {
        var all = List();
        var result = new List<MacroRecord>();
        foreach (var m in all) if (m.Enabled) result.Add(m);
        return result;
    }

    public MacroRecord? Get(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            MacroRecord? rec = null;
            using (var cmd = Cmd(cn, "SELECT " + MacroColumns + " FROM macros WHERE id=$id;"))
            {
                P(cmd, "$id", id);
                using var r = cmd.ExecuteReader();
                if (!r.Read()) return null;
                rec = ReadMacro(r);
            }
            using (var cmd = Cmd(cn, "SELECT macro_id,ord,kind,keys,hold_ms,delay_ms,x,y,button,wheel,text FROM macro_steps WHERE macro_id=$id ORDER BY ord;"))
            {
                P(cmd, "$id", id);
                using var r = cmd.ExecuteReader();
                while (r.Read()) rec.Steps.Add(ReadStep(r));
            }
            return rec;
        }
    }

    public int Count()
    {
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var cmd = Cmd(cn, "SELECT COUNT(*) FROM macros;");
            return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        }
    }

    /// <summary>新增或整体更新一条宏，返回落库后的记录。</summary>
    public MacroRecord Save(MacroRecord rec)
    {
        if (rec == null) throw new ArgumentNullException(nameof(rec));
        if (string.IsNullOrWhiteSpace(rec.Id)) rec.Id = Guid.NewGuid().ToString("N");
        rec.Name = string.IsNullOrWhiteSpace(rec.Name) ? "新宏" : rec.Name.Trim();
        rec.TriggerMode = rec.TriggerMode == "remap" ? "remap" : "hotkey";
        rec.Trigger = (rec.Trigger ?? "").Trim().ToUpperInvariant();
        rec.SendMode = rec.SendMode == "virtual" ? "virtual" : "scan";
        rec.Repeat = Math.Clamp(rec.Repeat, 1, 999);
        rec.RepeatDelayMs = Math.Clamp(rec.RepeatDelayMs, 0, 600_000);
        rec.LeadInMs = Math.Clamp(rec.LeadInMs, 0, 60_000);
        var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        if (rec.CreatedAt <= 0) rec.CreatedAt = now;
        rec.UpdatedAt = now;

        // 步骤清洗：去掉空按键，限制时间范围，避免手滑输入 999999 毫秒
        var steps = new List<MacroStep>();
        foreach (var s in rec.Steps ?? new List<MacroStep>())
        {
            if (s == null) continue;
            var kind = s.Kind switch
            {
                "delay" or "mouse_move" or "mouse_click" or "mouse_wheel" or "text" => s.Kind,
                _ => "key",
            };
            var step = new MacroStep
            {
                Kind = kind,
                HoldMs = Math.Clamp(s.HoldMs, 1, 10_000),
                DelayMs = Math.Clamp(s.DelayMs, 0, 600_000),
                X = s.X,
                Y = s.Y,
                Button = (s.Button ?? "left").ToLowerInvariant(),
                Wheel = s.Wheel,
                Text = s.Text,
            };
            if (kind == "key")
            {
                var keys = new List<string>();
                foreach (var k in s.Keys ?? new List<string>())
                {
                    if (string.IsNullOrWhiteSpace(k)) continue;
                    var name = k.Trim().ToUpperInvariant();
                    if (!keys.Contains(name)) keys.Add(name);
                }
                if (keys.Count == 0) continue;
                step.Keys = keys;
            }
            else if (kind == "text")
            {
                if (string.IsNullOrEmpty(s.Text)) continue;
            }
            else if (kind == "mouse_click")
            {
                if (step.Button is not ("left" or "right" or "middle" or "x1" or "x2")) step.Button = "left";
            }
            else if (kind == "mouse_wheel")
            {
                step.Wheel = Math.Clamp(s.Wheel, -24 * 120, 24 * 120);
            }
            steps.Add(step);
        }
        rec.Steps = steps;

        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var tx = cn.BeginTransaction();
            using (var cmd = Cmd(cn, """
INSERT INTO macros(id,name,enabled,trigger_mode,trigger,repeat,repeat_delay_ms,lead_in_ms,send_mode,created_at,updated_at)
VALUES($id,$n,$e,$tm,$t,$rp,$rd,$li,$sm,$c,$u)
ON CONFLICT(id) DO UPDATE SET name=excluded.name,enabled=excluded.enabled,trigger_mode=excluded.trigger_mode,
trigger=excluded.trigger,repeat=excluded.repeat,repeat_delay_ms=excluded.repeat_delay_ms,
lead_in_ms=excluded.lead_in_ms,send_mode=excluded.send_mode,updated_at=excluded.updated_at;
""", tx))
            {
                P(cmd, "$id", rec.Id); P(cmd, "$n", rec.Name); P(cmd, "$e", rec.Enabled ? 1 : 0);
                P(cmd, "$tm", rec.TriggerMode); P(cmd, "$t", rec.Trigger);
                P(cmd, "$rp", rec.Repeat); P(cmd, "$rd", rec.RepeatDelayMs);
                P(cmd, "$li", rec.LeadInMs); P(cmd, "$sm", rec.SendMode);
                P(cmd, "$c", rec.CreatedAt); P(cmd, "$u", rec.UpdatedAt);
                cmd.ExecuteNonQuery();
            }
            using (var del = Cmd(cn, "DELETE FROM macro_steps WHERE macro_id=$id;", tx))
            {
                P(del, "$id", rec.Id);
                del.ExecuteNonQuery();
            }
            using (var ins = Cmd(cn, """
INSERT INTO macro_steps(macro_id,ord,kind,keys,hold_ms,delay_ms,x,y,button,wheel,text)
VALUES($id,$o,$k,$keys,$h,$d,$x,$y,$b,$w,$tx);
""", tx))
            {
                var pId = ins.Parameters.Add("$id", SqliteType.Text);
                var pOrd = ins.Parameters.Add("$o", SqliteType.Integer);
                var pKind = ins.Parameters.Add("$k", SqliteType.Text);
                var pKeys = ins.Parameters.Add("$keys", SqliteType.Text);
                var pHold = ins.Parameters.Add("$h", SqliteType.Integer);
                var pDelay = ins.Parameters.Add("$d", SqliteType.Integer);
                var pX = ins.Parameters.Add("$x", SqliteType.Integer);
                var pY = ins.Parameters.Add("$y", SqliteType.Integer);
                var pButton = ins.Parameters.Add("$b", SqliteType.Text);
                var pWheel = ins.Parameters.Add("$w", SqliteType.Integer);
                var pText = ins.Parameters.Add("$tx", SqliteType.Text);
                pId.Value = rec.Id;
                for (int i = 0; i < steps.Count; i++)
                {
                    var s = steps[i];
                    pOrd.Value = i;
                    pKind.Value = s.Kind;
                    pKeys.Value = JoinKeys(s.Keys);
                    pHold.Value = s.HoldMs;
                    pDelay.Value = s.DelayMs;
                    pX.Value = s.X;
                    pY.Value = s.Y;
                    pButton.Value = s.Button;
                    pWheel.Value = s.Wheel;
                    pText.Value = (object?)s.Text ?? DBNull.Value;
                    ins.ExecuteNonQuery();
                }
            }
            tx.Commit();
        }
        return rec;
    }

    public bool Delete(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var tx = cn.BeginTransaction();
            int n;
            using (var cmd = Cmd(cn, "DELETE FROM macro_steps WHERE macro_id=$id; DELETE FROM macros WHERE id=$id;", tx))
            {
                P(cmd, "$id", id);
                n = cmd.ExecuteNonQuery();
            }
            tx.Commit();
            return n > 0;
        }
    }

    public bool SetEnabled(string id, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var cmd = Cmd(cn, "UPDATE macros SET enabled=$e,updated_at=$u WHERE id=$id;");
            P(cmd, "$e", enabled ? 1 : 0);
            P(cmd, "$u", DateTimeOffset.Now.ToUnixTimeMilliseconds());
            P(cmd, "$id", id);
            return cmd.ExecuteNonQuery() > 0;
        }
    }

    // ==================== 全局开关 ====================

    public bool GetGlobalEnabled()
    {
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var cmd = Cmd(cn, "SELECT value FROM meta WHERE key='macro_enabled';");
            var v = cmd.ExecuteScalar() as string;
            return v is null or "1";
        }
    }

    public void SetGlobalEnabled(bool enabled)
    {
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var cmd = Cmd(cn, "INSERT OR REPLACE INTO meta(key,value) VALUES('macro_enabled',$v);");
            P(cmd, "$v", enabled ? "1" : "0");
            cmd.ExecuteNonQuery();
        }
    }

    // ==================== 示例 ====================

    /// <summary>首次运行时写入一条默认关闭的连招示例，便于直接改成自己的宏。</summary>
    private void SeedSample()
    {
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using (var check = Cmd(cn, "SELECT value FROM meta WHERE key='macro_seeded';"))
            {
                if ((check.ExecuteScalar() as string) == "1") return;
            }
            if (Count() == 0)
            {
                Save(new MacroRecord
                {
                    Name = "拳皇一键连招（示例）",
                    Enabled = false,
                    TriggerMode = "hotkey",
                    Trigger = "F1",
                    LeadInMs = 1500,
                    Steps = new List<MacroStep>
                    {
                        new() { Kind = "key", Keys = new List<string> { "S" }, HoldMs = 35 },
                        new() { Kind = "key", Keys = new List<string> { "S", "D" }, HoldMs = 45 },
                        new() { Kind = "key", Keys = new List<string> { "D" }, HoldMs = 35, DelayMs = 200 },
                        new() { Kind = "key", Keys = new List<string> { "I", "K" }, HoldMs = 45, DelayMs = 50 },
                        new() { Kind = "key", Keys = new List<string> { "K" }, HoldMs = 30 },
                        new() { Kind = "key", Keys = new List<string> { "K" }, HoldMs = 30 },
                        new() { Kind = "key", Keys = new List<string> { "K" }, HoldMs = 30 },
                    },
                });
            }
            using var mark = Cmd(cn, "INSERT OR REPLACE INTO meta(key,value) VALUES('macro_seeded','1');");
            mark.ExecuteNonQuery();
        }
    }
}
