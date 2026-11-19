namespace Lumina.Data.Migrations;

/// <summary>
/// 迁移目录。v1 为工程方案 5.2（P0-01 修订版）的 14 张核心表；
/// 后续版本只追加，不修改已发布迁移的 SQL。
/// </summary>
public static class MigrationCatalog
{
    public const int InitialVersion = 1;
    public const int LatestVersion = 11;

    public static IReadOnlyList<Migration> All { get; } = new[]
    {
        new Migration
        {
            Version = InitialVersion,
            Description = "初始 14 张核心表",
            Sql = InitialSchemaSql,
        },
        new Migration
        {
            Version = 2,
            Description = "世界时钟预设城市",
            Sql = """
                INSERT INTO world_city (iana_id, display_name, sort_order)
                SELECT 'America/New_York', '纽约', 1
                WHERE NOT EXISTS (SELECT 1 FROM world_city WHERE iana_id = 'America/New_York');
                INSERT INTO world_city (iana_id, display_name, sort_order)
                SELECT 'Europe/London', '伦敦', 2
                WHERE NOT EXISTS (SELECT 1 FROM world_city WHERE iana_id = 'Europe/London');
                INSERT INTO world_city (iana_id, display_name, sort_order)
                SELECT 'Asia/Tokyo', '东京', 3
                WHERE NOT EXISTS (SELECT 1 FROM world_city WHERE iana_id = 'Asia/Tokyo');
                INSERT INTO world_city (iana_id, display_name, sort_order)
                SELECT 'Australia/Sydney', '悉尼', 4
                WHERE NOT EXISTS (SELECT 1 FROM world_city WHERE iana_id = 'Australia/Sydney');
                INSERT INTO world_city (iana_id, display_name, sort_order)
                SELECT 'Europe/Paris', '巴黎', 5
                WHERE NOT EXISTS (SELECT 1 FROM world_city WHERE iana_id = 'Europe/Paris');
                INSERT INTO world_city (iana_id, display_name, sort_order)
                SELECT 'Asia/Dubai', '迪拜', 6
                WHERE NOT EXISTS (SELECT 1 FROM world_city WHERE iana_id = 'Asia/Dubai');
                """,
        },
        new Migration
        {
            Version = 3,
            Description = "日程旧数据默认值补齐",
            Sql = """
                UPDATE schedule_event SET location = '' WHERE location IS NULL;
                UPDATE schedule_event SET priority = 'normal'
                  WHERE priority IS NULL OR priority NOT IN ('high', 'normal', 'low');
                UPDATE schedule_event SET remind = 'none'
                  WHERE remind IS NULL OR remind NOT IN ('none', 'ontime', 'm5', 'm15', 'm30', 'h1', 'd1');
                UPDATE schedule_event SET done = 0 WHERE done IS NULL;
                """,
        },
        new Migration
        {
            Version = 4,
            Description = "历史 once 周期迁移为单次日程",
            Sql = """
                UPDATE schedule_event SET repeat_rule = NULL
                WHERE repeat_rule IS NOT NULL
                  AND (repeat_rule LIKE '%"mode":"once"%' OR repeat_rule LIKE '%"Mode":"once"%');
                """,
        },
        new Migration
        {
            Version = 5,
            Description = "剪贴板软删除标记",
            Sql = """
                ALTER TABLE clipboard_item ADD COLUMN deleted_at TEXT;
                CREATE INDEX IF NOT EXISTS idx_clip_deleted ON clipboard_item(deleted_at);
                """,
        },
        new Migration
        {
            Version = 6,
            Description = "倒计时旧日期反推锚点并对齐重复口径",
            Sql = """
                -- 非法枚举先归一，避免后续锚点推导找不到分支。
                UPDATE countdown_item SET repeat = 'none'
                  WHERE repeat IS NULL OR repeat NOT IN ('none', 'weekly', 'monthly', 'yearly');
                UPDATE countdown_item SET category = 'other'
                  WHERE category IS NULL OR category NOT IN ('work', 'life', 'exam', 'anniv', 'other');
                UPDATE countdown_item SET remind = 'd1'
                  WHERE remind IS NULL OR remind NOT IN ('none', 'ontime', 'm5', 'm15', 'm30', 'h1', 'd1');

                -- 旧 repeat + 旧 date（现 anchor_date）反推锚点：只补缺失的锚点，不覆盖已有值。
                UPDATE countdown_item SET anchor_weekday = CAST(strftime('%w', anchor_date) AS INTEGER)
                  WHERE repeat = 'weekly' AND anchor_date IS NOT NULL AND anchor_date <> ''
                    AND (anchor_weekday IS NULL OR anchor_weekday < 0 OR anchor_weekday > 6);

                UPDATE countdown_item SET anchor_day = CAST(strftime('%d', anchor_date) AS INTEGER)
                  WHERE repeat = 'monthly' AND anchor_date IS NOT NULL AND anchor_date <> ''
                    AND IFNULL(anchor_month_end, 0) = 0
                    AND (anchor_day IS NULL OR anchor_day < 1 OR anchor_day > 31);

                UPDATE countdown_item SET anchor_month = CAST(strftime('%m', anchor_date) AS INTEGER)
                  WHERE repeat = 'yearly' AND anchor_date IS NOT NULL AND anchor_date <> ''
                    AND (anchor_month IS NULL OR anchor_month < 1 OR anchor_month > 12);

                UPDATE countdown_item SET anchor_day = CAST(strftime('%d', anchor_date) AS INTEGER)
                  WHERE repeat = 'yearly' AND anchor_date IS NOT NULL AND anchor_date <> ''
                    AND (anchor_day IS NULL OR anchor_day < 1 OR anchor_day > 31);

                -- 同一时刻只保留当前 repeat 对应的锚点，并删除旧 date。
                UPDATE countdown_item SET anchor_weekday = NULL, anchor_day = NULL,
                    anchor_month = NULL, anchor_month_end = 0
                  WHERE repeat = 'none';
                UPDATE countdown_item SET anchor_date = NULL, anchor_day = NULL,
                    anchor_month = NULL, anchor_month_end = 0
                  WHERE repeat = 'weekly';
                UPDATE countdown_item SET anchor_date = NULL, anchor_weekday = NULL,
                    anchor_month = NULL
                  WHERE repeat = 'monthly';
                UPDATE countdown_item SET anchor_day = NULL
                  WHERE repeat = 'monthly' AND anchor_month_end = 1;
                UPDATE countdown_item SET anchor_date = NULL, anchor_weekday = NULL,
                    anchor_month_end = 0
                  WHERE repeat = 'yearly';
                """,
        },
        new Migration
        {
            Version = 7,
            Description = "便签补旧 ID 列，对齐原型导入映射",
            Sql = """
                ALTER TABLE note_item ADD COLUMN legacy_id TEXT;
                CREATE INDEX IF NOT EXISTS idx_note_pinned_updated ON note_item(pinned, updated_at);
                """,
        },
        new Migration
        {
            Version = 8,
            Description = "待办补便签回链列",
            Sql = """
                ALTER TABLE todo_item ADD COLUMN note_id INTEGER;
                CREATE INDEX IF NOT EXISTS idx_todo_note ON todo_item(note_id);
                """,
        },
        new Migration
        {
            Version = 9,
            Description = "保险库补 KDF 迭代次数与分类索引",
            Sql = """
                ALTER TABLE vault_meta ADD COLUMN kdf_iterations INTEGER NOT NULL DEFAULT 600000;
                CREATE INDEX IF NOT EXISTS idx_vault_category ON vault_entry(category, title);
                """,
        },
        new Migration
        {
            Version = 10,
            Description = "时间戳统一为 UTC ISO-8601",
            Sql = """
                -- 存量时间戳（含本地偏移）统一转成 UTC 的 "O" 格式，
                -- 使字符串比较与时间顺序一致；已转换过的行由 GLOB 守卫跳过。
                UPDATE clipboard_item SET created_at = strftime('%Y-%m-%dT%H:%M:%f', created_at) || '0000+00:00'
                  WHERE created_at IS NOT NULL AND created_at NOT GLOB '____-__-__T__:__:__.*+00:00'
                    AND strftime('%Y-%m-%dT%H:%M:%f', created_at) IS NOT NULL;
                UPDATE clipboard_item SET updated_at = strftime('%Y-%m-%dT%H:%M:%f', updated_at) || '0000+00:00'
                  WHERE updated_at IS NOT NULL AND updated_at NOT GLOB '____-__-__T__:__:__.*+00:00'
                    AND strftime('%Y-%m-%dT%H:%M:%f', updated_at) IS NOT NULL;
                UPDATE clipboard_item SET deleted_at = strftime('%Y-%m-%dT%H:%M:%f', deleted_at) || '0000+00:00'
                  WHERE deleted_at IS NOT NULL AND deleted_at NOT GLOB '____-__-__T__:__:__.*+00:00'
                    AND strftime('%Y-%m-%dT%H:%M:%f', deleted_at) IS NOT NULL;
                UPDATE clipboard_item SET pinned_at = strftime('%Y-%m-%dT%H:%M:%f', pinned_at) || '0000+00:00'
                  WHERE pinned_at IS NOT NULL AND pinned_at NOT GLOB '____-__-__T__:__:__.*+00:00'
                    AND strftime('%Y-%m-%dT%H:%M:%f', pinned_at) IS NOT NULL;

                UPDATE schedule_event SET created_at = strftime('%Y-%m-%dT%H:%M:%f', created_at) || '0000+00:00'
                  WHERE created_at IS NOT NULL AND created_at NOT GLOB '____-__-__T__:__:__.*+00:00'
                    AND strftime('%Y-%m-%dT%H:%M:%f', created_at) IS NOT NULL;
                UPDATE schedule_event SET updated_at = strftime('%Y-%m-%dT%H:%M:%f', updated_at) || '0000+00:00'
                  WHERE updated_at IS NOT NULL AND updated_at NOT GLOB '____-__-__T__:__:__.*+00:00'
                    AND strftime('%Y-%m-%dT%H:%M:%f', updated_at) IS NOT NULL;

                UPDATE todo_item SET created_at = strftime('%Y-%m-%dT%H:%M:%f', created_at) || '0000+00:00'
                  WHERE created_at IS NOT NULL AND created_at NOT GLOB '____-__-__T__:__:__.*+00:00'
                    AND strftime('%Y-%m-%dT%H:%M:%f', created_at) IS NOT NULL;
                UPDATE todo_item SET updated_at = strftime('%Y-%m-%dT%H:%M:%f', updated_at) || '0000+00:00'
                  WHERE updated_at IS NOT NULL AND updated_at NOT GLOB '____-__-__T__:__:__.*+00:00'
                    AND strftime('%Y-%m-%dT%H:%M:%f', updated_at) IS NOT NULL;
                UPDATE todo_item SET done_at = strftime('%Y-%m-%dT%H:%M:%f', done_at) || '0000+00:00'
                  WHERE done_at IS NOT NULL AND done_at NOT GLOB '____-__-__T__:__:__.*+00:00'
                    AND strftime('%Y-%m-%dT%H:%M:%f', done_at) IS NOT NULL;

                UPDATE countdown_item SET created_at = strftime('%Y-%m-%dT%H:%M:%f', created_at) || '0000+00:00'
                  WHERE created_at IS NOT NULL AND created_at NOT GLOB '____-__-__T__:__:__.*+00:00'
                    AND strftime('%Y-%m-%dT%H:%M:%f', created_at) IS NOT NULL;
                UPDATE countdown_item SET updated_at = strftime('%Y-%m-%dT%H:%M:%f', updated_at) || '0000+00:00'
                  WHERE updated_at IS NOT NULL AND updated_at NOT GLOB '____-__-__T__:__:__.*+00:00'
                    AND strftime('%Y-%m-%dT%H:%M:%f', updated_at) IS NOT NULL;

                UPDATE note_item SET created_at = strftime('%Y-%m-%dT%H:%M:%f', created_at) || '0000+00:00'
                  WHERE created_at IS NOT NULL AND created_at NOT GLOB '____-__-__T__:__:__.*+00:00'
                    AND strftime('%Y-%m-%dT%H:%M:%f', created_at) IS NOT NULL;
                UPDATE note_item SET updated_at = strftime('%Y-%m-%dT%H:%M:%f', updated_at) || '0000+00:00'
                  WHERE updated_at IS NOT NULL AND updated_at NOT GLOB '____-__-__T__:__:__.*+00:00'
                    AND strftime('%Y-%m-%dT%H:%M:%f', updated_at) IS NOT NULL;

                UPDATE screenshot_meta SET created_at = strftime('%Y-%m-%dT%H:%M:%f', created_at) || '0000+00:00'
                  WHERE created_at IS NOT NULL AND created_at NOT GLOB '____-__-__T__:__:__.*+00:00'
                    AND strftime('%Y-%m-%dT%H:%M:%f', created_at) IS NOT NULL;

                UPDATE focus_session SET started_at = strftime('%Y-%m-%dT%H:%M:%f', started_at) || '0000+00:00'
                  WHERE started_at IS NOT NULL AND started_at NOT GLOB '____-__-__T__:__:__.*+00:00'
                    AND strftime('%Y-%m-%dT%H:%M:%f', started_at) IS NOT NULL;
                UPDATE focus_session SET ended_at = strftime('%Y-%m-%dT%H:%M:%f', ended_at) || '0000+00:00'
                  WHERE ended_at IS NOT NULL AND ended_at NOT GLOB '____-__-__T__:__:__.*+00:00'
                    AND strftime('%Y-%m-%dT%H:%M:%f', ended_at) IS NOT NULL;

                UPDATE reminder_dedup SET fired_at = strftime('%Y-%m-%dT%H:%M:%f', fired_at) || '0000+00:00'
                  WHERE fired_at IS NOT NULL AND fired_at NOT GLOB '____-__-__T__:__:__.*+00:00'
                    AND strftime('%Y-%m-%dT%H:%M:%f', fired_at) IS NOT NULL;

                UPDATE vault_entry SET created_at = strftime('%Y-%m-%dT%H:%M:%f', created_at) || '0000+00:00'
                  WHERE created_at IS NOT NULL AND created_at NOT GLOB '____-__-__T__:__:__.*+00:00'
                    AND strftime('%Y-%m-%dT%H:%M:%f', created_at) IS NOT NULL;
                UPDATE vault_entry SET updated_at = strftime('%Y-%m-%dT%H:%M:%f', updated_at) || '0000+00:00'
                  WHERE updated_at IS NOT NULL AND updated_at NOT GLOB '____-__-__T__:__:__.*+00:00'
                    AND strftime('%Y-%m-%dT%H:%M:%f', updated_at) IS NOT NULL;
                UPDATE vault_entry SET used_at = strftime('%Y-%m-%dT%H:%M:%f', used_at) || '0000+00:00'
                  WHERE used_at IS NOT NULL AND used_at NOT GLOB '____-__-__T__:__:__.*+00:00'
                    AND strftime('%Y-%m-%dT%H:%M:%f', used_at) IS NOT NULL;

                UPDATE vault_meta SET last_locked_at = strftime('%Y-%m-%dT%H:%M:%f', last_locked_at) || '0000+00:00'
                  WHERE last_locked_at IS NOT NULL AND last_locked_at NOT GLOB '____-__-__T__:__:__.*+00:00'
                    AND strftime('%Y-%m-%dT%H:%M:%f', last_locked_at) IS NOT NULL;

                CREATE INDEX IF NOT EXISTS idx_focus_ended ON focus_session(ended_at);
                """,
        },
        new Migration
        {
            Version = 11,
            Description = "保险库安全问题找回与剪贴板多来源",
            Sql = """
                ALTER TABLE vault_meta ADD COLUMN recovery_question TEXT;
                ALTER TABLE vault_meta ADD COLUMN recovery_answer_salt BLOB;
                ALTER TABLE vault_meta ADD COLUMN recovery_blob BLOB;
                ALTER TABLE vault_meta ADD COLUMN recovery_key_blob BLOB;
                ALTER TABLE clipboard_item ADD COLUMN sources TEXT;
                """,
        },
    };

    internal const string InitialSchemaSql = """
        -- 应用通用设置（非敏感）
        CREATE TABLE IF NOT EXISTS app_setting (
          key          TEXT PRIMARY KEY,
          value        TEXT NOT NULL,
          module       TEXT,
          updated_at   TEXT NOT NULL
        );

        -- 快捷键绑定
        CREATE TABLE IF NOT EXISTS shortcut_binding (
          action       TEXT PRIMARY KEY,   -- 如 hotkey.capture / hotkey.note
          modifiers    TEXT,
          key_code     TEXT,
          scope        TEXT               -- global / module
        );

        -- 世界时钟城市
        CREATE TABLE IF NOT EXISTS world_city (
          id           INTEGER PRIMARY KEY AUTOINCREMENT,
          iana_id      TEXT NOT NULL,
          display_name TEXT NOT NULL,
          sort_order   INTEGER DEFAULT 0
        );

        -- 日程（起止拆日期与时间；time 空=全天，end_time 空且 start_time 存在=开放结束）
        CREATE TABLE IF NOT EXISTS schedule_event (
          id            INTEGER PRIMARY KEY AUTOINCREMENT,
          legacy_id     TEXT,                              -- 原型旧 id，用于迁移回链
          title         TEXT NOT NULL,
          start_date    TEXT NOT NULL,                     -- ISO YYYY-MM-DD
          start_time    TEXT,                              -- HH:mm，空=全天
          end_date      TEXT,                              -- 空=与 start_date 相同
          end_time      TEXT,                              -- 空且 start_time 存在=开放结束
          location      TEXT DEFAULT '',
          note          TEXT,
          priority      TEXT DEFAULT 'normal',             -- high / normal / low
          repeat_rule   TEXT,                              -- JSON：mode/endType/endDate/count/weekStart/weekEnd/monthStart/monthEnd/weekdays/monthday
          remind        TEXT DEFAULT 'none',               -- none/ontime/m5/m15/m30/h1/d1
          done          INTEGER DEFAULT 0,
          meta          TEXT,
          created_at    TEXT NOT NULL,
          updated_at    TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_event_start ON schedule_event(start_date);

        -- 待办（字段以 4.2 待办模块字段模型为准；无分组与手工排序列）
        CREATE TABLE IF NOT EXISTS todo_item (
          id           INTEGER PRIMARY KEY AUTOINCREMENT,
          legacy_id    TEXT,                    -- 原型旧 id（td_ 前缀）
          text         TEXT NOT NULL,           -- 标题，保存时 trim
          due_date     TEXT,                    -- ISO YYYY-MM-DD，可空
          due_time     TEXT,                    -- HH:mm，可空
          priority     TEXT DEFAULT 'normal',   -- high / normal / low
          done         INTEGER DEFAULT 0,
          done_at      TEXT,                    -- 取消完成时清空
          note         TEXT DEFAULT '',
          created_at   TEXT NOT NULL,
          updated_at   TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_todo_due ON todo_item(due_date, done);

        -- 剪贴板历史（补齐去重、文件与时间字段）
        CREATE TABLE IF NOT EXISTS clipboard_item (
          id              INTEGER PRIMARY KEY AUTOINCREMENT,
          legacy_id       TEXT,                 -- 原型旧 id（cb_ 前缀）
          kind            TEXT NOT NULL,        -- text/link/image/html/file
          text_content    TEXT,
          file_path       TEXT,                 -- 原图/文件落盘路径
          thumb_path      TEXT,
          url             TEXT,
          mime            TEXT,
          width           INTEGER,
          height          INTEGER,
          content_hash    TEXT,                 -- 内容哈希，与 normalized_text 联合去重
          normalized_text TEXT,                 -- 去首尾空白/折叠空白/统一换行
          pinned          INTEGER DEFAULT 0,
          pinned_at       TEXT,                 -- 固定时间，固定区按此稳定排序
          note            TEXT,
          tags            TEXT,                 -- JSON 数组，≤5 项、每项≤12 字
          source_window   TEXT,
          dup_count       INTEGER DEFAULT 1,    -- 重复合并次数
          created_at      TEXT NOT NULL,
          updated_at      TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_clip_kind ON clipboard_item(kind);
        CREATE INDEX IF NOT EXISTS idx_clip_pin  ON clipboard_item(pinned, pinned_at);
        CREATE INDEX IF NOT EXISTS idx_clip_hash ON clipboard_item(content_hash);

        -- 截图元数据（原图落目录）
        CREATE TABLE IF NOT EXISTS screenshot_meta (
          id           INTEGER PRIMARY KEY AUTOINCREMENT,
          capture_type TEXT,
          file_path    TEXT NOT NULL,
          thumb_path   TEXT,
          ocr_text     TEXT,
          width        INTEGER,
          height       INTEGER,
          created_at   TEXT NOT NULL
        );

        -- 倒计时（name + 锚点列；不再保存 direction/target_date/repeat_rule）
        CREATE TABLE IF NOT EXISTS countdown_item (
          id               INTEGER PRIMARY KEY AUTOINCREMENT,
          legacy_id        TEXT,                        -- 原型旧 id（cd_ 前缀）
          name             TEXT NOT NULL,
          category         TEXT DEFAULT 'other',        -- work / life / exam / anniv / other
          repeat           TEXT DEFAULT 'none',         -- none / weekly / monthly / yearly
          remind           TEXT DEFAULT 'd1',           -- none/ontime/m5/m15/m30/h1/d1
          anchor_date      TEXT,                        -- none：ISO YYYY-MM-DD
          anchor_weekday   INTEGER,                     -- weekly：0–6，0=周日
          anchor_day       INTEGER,                     -- monthly 的日号 1–31 / yearly 的日
          anchor_month     INTEGER,                     -- yearly：1–12
          anchor_month_end INTEGER DEFAULT 0,           -- monthly：1=月底
          legacy_color     TEXT,                        -- 旧字段，保留但不参与渲染
          created_at       TEXT NOT NULL,
          updated_at       TEXT NOT NULL
        );

        -- 专注会话（session_key 为幂等键，保证会话结束只写一次）
        CREATE TABLE IF NOT EXISTS focus_session (
          id           INTEGER PRIMARY KEY AUTOINCREMENT,
          session_key  TEXT NOT NULL UNIQUE,  -- 幂等 ID
          source       TEXT NOT NULL,         -- pomodoro / timer
          started_at   TEXT NOT NULL,
          ended_at     TEXT NOT NULL,
          duration_sec INTEGER NOT NULL,
          finished     INTEGER DEFAULT 1,
          note         TEXT
        );
        CREATE INDEX IF NOT EXISTS idx_focus_start ON focus_session(started_at);

        -- 每日目标
        CREATE TABLE IF NOT EXISTS daily_goal (
          date          TEXT PRIMARY KEY,
          target_minutes INTEGER DEFAULT 0
        );

        -- 便签
        CREATE TABLE IF NOT EXISTS note_item (
          id          INTEGER PRIMARY KEY AUTOINCREMENT,
          title       TEXT,
          content     TEXT NOT NULL,
          color       TEXT,
          pos_x       REAL,
          pos_y       REAL,
          width       REAL,
          height      REAL,
          pinned      INTEGER DEFAULT 0,
          created_at  TEXT NOT NULL,
          updated_at  TEXT NOT NULL
        );

        -- 密码条目（敏感字段加密）
        CREATE TABLE IF NOT EXISTS vault_entry (
          id                 INTEGER PRIMARY KEY AUTOINCREMENT,
          category           TEXT NOT NULL,   -- login/bank/card/id/note/favorite
          title              TEXT NOT NULL,
          username           TEXT,
          enc_password       BLOB,            -- AES-GCM 密文
          url                TEXT,
          enc_note           BLOB,            -- 可选加密备注
          favorite           INTEGER DEFAULT 0,
          created_at         TEXT,
          updated_at         TEXT,
          used_at            TEXT
        );

        -- 保险库元数据（不存主密码明文）
        CREATE TABLE IF NOT EXISTS vault_meta (
          id                  INTEGER PRIMARY KEY CHECK (id = 1),
          kdf_salt            BLOB NOT NULL,
          master_hash         BLOB NOT NULL,   -- 主密码校验哈希
          auto_lock_minutes   INTEGER DEFAULT 5,
          clear_clipboard_sec INTEGER DEFAULT 20,
          last_locked_at      TEXT
        );

        -- 提醒去重（P1-12：5 分钟窗口内只触发一次；超过 600 条按 fired_at 淘汰至 500 条）
        CREATE TABLE IF NOT EXISTS reminder_dedup (
          trigger_key TEXT PRIMARY KEY,   -- sched:<id>:<实例日期>:<档位> 或 countdown:<id>:<下次日期>:<档位>
          fired_at    TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_reminder_fired ON reminder_dedup(fired_at);
        """;
}
