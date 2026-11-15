using System.Text.Json;
using DiskWatch.Core;
using Microsoft.Data.Sqlite;

namespace DiskWatch.Storage;

/// <summary>一份已保存的文件级明细（压缩后大小 Size 字节）。</summary>
public sealed record FileIndexInfo(string SnapshotId, string Root, DateTimeOffset Finished, string RuleVersion, long Files, long Size, FileComparisonContext? Context = null);

public sealed class Repository
{
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string directory;
    private readonly string connectionString;
    private readonly object gate = new();
    private readonly Settings settings;
    public Repository(string directory, Settings settings)
    {
        this.directory = directory; this.settings = settings;
        Directory.CreateDirectory(directory);
        connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "diskwatch.db"), Mode = SqliteOpenMode.ReadWriteCreate, DefaultTimeout = 10 }.ToString();
        using var db = Open();
        Execute(db, "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON; PRAGMA auto_vacuum=INCREMENTAL;");
        using (var pageQuery = db.CreateCommand())
        {
            pageQuery.CommandText = "PRAGMA page_size";
            long pageSize = Convert.ToInt64(pageQuery.ExecuteScalar());
            Execute(db, $"PRAGMA max_page_count={settings.DatabaseLimitMiB * 1048576L / pageSize}; PRAGMA journal_size_limit=16777216;");
        }
        Execute(db, """
            CREATE TABLE IF NOT EXISTS samples (volume_id TEXT NOT NULL, time TEXT NOT NULL, root TEXT NOT NULL, total INTEGER NOT NULL, free INTEGER NOT NULL, PRIMARY KEY(volume_id,time));
            CREATE TABLE IF NOT EXISTS snapshots (id TEXT PRIMARY KEY, volume_id TEXT NOT NULL, root TEXT NOT NULL, finished TEXT NOT NULL, status TEXT NOT NULL, body TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_snapshots_root ON snapshots(root,finished DESC);
            CREATE TABLE IF NOT EXISTS alerts (id TEXT PRIMARY KEY, time TEXT NOT NULL, body TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS state (key TEXT PRIMARY KEY, body TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS file_indexes (snapshot_id TEXT PRIMARY KEY, root TEXT NOT NULL, finished TEXT NOT NULL, rule_version TEXT NOT NULL, files INTEGER NOT NULL, size INTEGER NOT NULL, body BLOB NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_file_indexes_root ON file_indexes(root,finished DESC);
            CREATE TABLE IF NOT EXISTS file_index_coverage (snapshot_id TEXT PRIMARY KEY, body TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS app_usage (snapshot_id TEXT PRIMARY KEY, root TEXT NOT NULL, finished TEXT NOT NULL, body TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_app_usage_root ON app_usage(root,finished DESC);
            PRAGMA user_version=1;
            """);
    }
    private SqliteConnection Open() { var db = new SqliteConnection(connectionString); db.Open(); return db; }
    private static void Execute(SqliteConnection db, string sql, SqliteTransaction? transaction = null, params (string, object?)[] values)
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = sql; cmd.Transaction = transaction;
        foreach (var (key, value) in values) cmd.Parameters.AddWithValue(key, value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
    private void CheckDiskSpace()
    {
        if (new DriveInfo(Path.GetPathRoot(directory)!).AvailableFreeSpace < 128L * 1024 * 1024) throw new IOException("数据所在磁盘剩余空间不足 128 MiB，暂停写入以保护磁盘。");
    }
    public List<Alert> RecordSample(VolumeSample sample)
    {
        lock (gate)
        {
            CheckDiskSpace(); using var db = Open(); using var transaction = db.BeginTransaction();
            using (var duplicate = db.CreateCommand())
            {
                duplicate.Transaction = transaction;
                duplicate.CommandText = "SELECT 1 FROM samples WHERE volume_id=$v AND time=$t";
                duplicate.Parameters.AddWithValue("$v", sample.VolumeId);
                duplicate.Parameters.AddWithValue("$t", sample.Time.ToUniversalTime().ToString("O"));
                if (duplicate.ExecuteScalar() != null) return [];
            }
            var state = ReadState<AlertState>(db, "alert:" + sample.VolumeId, transaction) ?? new();
            var alerts = AlertEngine.Evaluate(sample, state, settings);
            Execute(db, "INSERT OR IGNORE INTO samples VALUES($v,$t,$r,$s,$f)", transaction, ("$v", sample.VolumeId), ("$t", sample.Time.ToUniversalTime().ToString("O")), ("$r", sample.Root), ("$s", sample.TotalBytes), ("$f", sample.FreeBytes));
            WriteState(db, "alert:" + sample.VolumeId, state, transaction);
            foreach (var alert in alerts) Execute(db, "INSERT INTO alerts VALUES($id,$t,$b)", transaction, ("$id", alert.Id), ("$t", alert.Time.ToUniversalTime().ToString("O")), ("$b", JsonSerializer.Serialize(alert)));
            transaction.Commit(); return alerts;
        }
    }
    public void SaveSnapshot(Snapshot snapshot)
    {
        lock (gate)
        {
            CheckDiskSpace(); using var db = Open(); using var transaction = db.BeginTransaction();
            Execute(db, "INSERT INTO snapshots VALUES($id,$v,$r,$t,$s,$b)", transaction, ("$id", snapshot.Id), ("$v", snapshot.VolumeId), ("$r", snapshot.Root), ("$t", snapshot.Finished.ToUniversalTime().ToString("O")), ("$s", snapshot.Status), ("$b", JsonSerializer.Serialize(snapshot)));
            Execute(db, "DELETE FROM snapshots WHERE root=$r AND id NOT IN (SELECT id FROM snapshots WHERE root=$r ORDER BY finished DESC LIMIT $n)", transaction, ("$r", snapshot.Root), ("$n", settings.SnapshotRetention));
            // 应用排行：正常结束的扫描单独保存各应用占用（几百行，很小），随快照一起按保留份数裁剪。
            if (snapshot.Apps is { Count: > 0 } && snapshot.Status is "Complete" or "Partial")
                Execute(db, "INSERT OR REPLACE INTO app_usage VALUES($id,$r,$t,$b)", transaction, ("$id", snapshot.Id), ("$r", snapshot.Root), ("$t", snapshot.Finished.ToUniversalTime().ToString("O")), ("$b", JsonSerializer.Serialize(AppUsageRecord.From(snapshot))));
            Execute(db, "DELETE FROM app_usage WHERE snapshot_id NOT IN (SELECT id FROM snapshots)", transaction);
            transaction.Commit();
            Execute(db, "PRAGMA wal_checkpoint(TRUNCATE)");
        }
    }
    /// <summary>
    /// 保存一次完整扫描的文件级明细（已压缩），并把同一扫描范围的明细裁剪到最近 FileDetailRetention 份；更早的快照只剩目录级统计。
    /// 返回写入的字节数；保留份数为 0 时不写入并清掉该范围已有明细。
    /// </summary>
    public long SaveFileIndex(Snapshot snapshot, FileIndex index)
    {
        // 不完整扫描不挤掉最后可用的基线；旧记录不补造覆盖证明。
        if (settings.FileDetailRetention > 0 && !ScanCoverage.Verified(snapshot)) return 0;
        if (!Classifier.Normalize(snapshot.Root).Equals(Classifier.Normalize(index.Root), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("快照与明细范围不一致。");
        var body = settings.FileDetailRetention > 0 ? index.Serialize() : [];
        lock (gate)
        {
            CheckDiskSpace(); using var db = Open(); using var transaction = db.BeginTransaction();
            if (body.Length > 0)
            {
                Execute(db, "INSERT OR REPLACE INTO file_indexes VALUES($id,$r,$t,$v,$n,$s,$b)", transaction, ("$id", snapshot.Id), ("$r", snapshot.Root), ("$t", snapshot.Finished.ToUniversalTime().ToString("O")),
                    ("$v", snapshot.RuleVersion), ("$n", (long)index.Files.Count), ("$s", (long)body.Length), ("$b", body));
                Execute(db, "INSERT OR REPLACE INTO file_index_coverage VALUES($id,$b)", transaction, ("$id", snapshot.Id), ("$b", JsonSerializer.Serialize(FileComparisonContext.From(snapshot))));
            }
            Execute(db, "DELETE FROM file_indexes WHERE root=$r AND snapshot_id NOT IN (SELECT snapshot_id FROM file_indexes WHERE root=$r ORDER BY finished DESC LIMIT $n)", transaction,
                ("$r", snapshot.Root), ("$n", settings.FileDetailRetention));
            Execute(db, "DELETE FROM file_index_coverage WHERE snapshot_id NOT IN (SELECT snapshot_id FROM file_indexes)", transaction);
            transaction.Commit();
            Execute(db, "PRAGMA wal_checkpoint(TRUNCATE); PRAGMA incremental_vacuum(4096);");
        }
        return body.Length;
    }
    public List<FileIndexInfo> FileIndexes(string root)
    {
        lock (gate)
        {
            using var db = Open(); using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT f.snapshot_id,f.root,f.finished,f.rule_version,f.files,f.size,c.body FROM file_indexes f LEFT JOIN file_index_coverage c ON c.snapshot_id=f.snapshot_id WHERE f.root=$r ORDER BY f.finished DESC";
            cmd.Parameters.AddWithValue("$r", root);
            using var reader = cmd.ExecuteReader(); List<FileIndexInfo> result = [];
            while (reader.Read()) result.Add(new(reader.GetString(0), reader.GetString(1), DateTimeOffset.Parse(reader.GetString(2)), reader.GetString(3), reader.GetInt64(4), reader.GetInt64(5), reader.IsDBNull(6) ? null : JsonSerializer.Deserialize<FileComparisonContext>(reader.GetString(6))));
            return result;
        }
    }
    public FileIndex? LoadFileIndex(string snapshotId)
    {
        byte[]? body; FileComparisonContext? context = null;
        lock (gate)
        {
            using var db = Open(); using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT f.body,c.body FROM file_indexes f LEFT JOIN file_index_coverage c ON c.snapshot_id=f.snapshot_id WHERE f.snapshot_id=$id"; cmd.Parameters.AddWithValue("$id", snapshotId);
            using var reader = cmd.ExecuteReader();
            body = reader.Read() ? (byte[])reader.GetValue(0) : null;
            if (body != null && !reader.IsDBNull(1)) context = JsonSerializer.Deserialize<FileComparisonContext>(reader.GetString(1));
        }
        if (body == null) return null;
        var result = FileIndex.Deserialize(body); result.Context = context; return result;
    }
    /// <summary>可对比的一对明细：最新一份，以及它之前最近的一份同口径（RuleVersion 相同）明细。不跨口径对比，避免 MFT 与目录遍历的差异被误报为变化。</summary>
    public (FileIndexInfo Before, FileIndexInfo After)? ComparablePair(string root)
    {
        var list = FileIndexes(root);
        if (list.Count < 2) return null;
        var before = list.Skip(1).FirstOrDefault(x => x.Context?.Matches(list[0].Context) == true);
        return before == null ? null : (before, list[0]);
    }
    /// <summary>同一扫描范围的应用归属记录，最新的在前。</summary>
    public List<AppUsageRecord> AppUsageHistory(string root, int count = 60)
    {
        lock (gate)
        {
            using var db = Open(); using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT body FROM app_usage WHERE root=$r ORDER BY finished DESC LIMIT $n";
            cmd.Parameters.AddWithValue("$r", root); cmd.Parameters.AddWithValue("$n", count);
            using var reader = cmd.ExecuteReader(); List<AppUsageRecord> result = [];
            while (reader.Read()) result.Add(JsonSerializer.Deserialize<AppUsageRecord>(reader.GetString(0))!);
            return result;
        }
    }
    /// <summary>写入一条程序生成的提醒记录（例如骤降抓现场的结果）。</summary>
    public void AddAlert(Alert alert)
    {
        lock (gate) { CheckDiskSpace(); using var db = Open(); Execute(db, "INSERT OR IGNORE INTO alerts VALUES($id,$t,$b)", null, ("$id", alert.Id), ("$t", alert.Time.ToUniversalTime().ToString("O")), ("$b", JsonSerializer.Serialize(alert))); }
    }
    public List<Snapshot> LatestSnapshots(string root, int count = 2)
    {
        lock (gate)
        {
            using var db = Open(); using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT body FROM snapshots WHERE root=$r ORDER BY finished DESC LIMIT $n";
            cmd.Parameters.AddWithValue("$r", root); cmd.Parameters.AddWithValue("$n", count);
            using var reader = cmd.ExecuteReader(); List<Snapshot> result = [];
            while (reader.Read()) result.Add(JsonSerializer.Deserialize<Snapshot>(reader.GetString(0))!);
            return result;
        }
    }
    public List<VolumeSample> Samples(DateTimeOffset since)
    {
        lock (gate)
        {
            using var db = Open(); using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT volume_id,root,time,total,free FROM samples WHERE time >= $t ORDER BY time";
            cmd.Parameters.AddWithValue("$t", since.ToUniversalTime().ToString("O")); using var reader = cmd.ExecuteReader();
            List<VolumeSample> result = [];
            while (reader.Read()) result.Add(new(reader.GetString(0), reader.GetString(1), DateTimeOffset.Parse(reader.GetString(2)), reader.GetInt64(3), reader.GetInt64(4)));
            return result;
        }
    }
    public List<Alert> RecentAlerts(int count = 200)
    {
        lock (gate)
        {
            using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT body FROM alerts ORDER BY time DESC LIMIT $n"; cmd.Parameters.AddWithValue("$n", count);
            using var reader = cmd.ExecuteReader(); List<Alert> result = [];
            while (reader.Read()) result.Add(JsonSerializer.Deserialize<Alert>(reader.GetString(0))!);
            return result;
        }
    }
    private static T? ReadState<T>(SqliteConnection db, string key, SqliteTransaction? transaction = null)
    {
        using var cmd = db.CreateCommand(); cmd.Transaction = transaction; cmd.CommandText = "SELECT body FROM state WHERE key=$k"; cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() is string json ? JsonSerializer.Deserialize<T>(json) : default;
    }
    private static void WriteState<T>(SqliteConnection db, string key, T value, SqliteTransaction? transaction = null) =>
        Execute(db, "INSERT INTO state VALUES($k,$b) ON CONFLICT(key) DO UPDATE SET body=excluded.body", transaction, ("$k", key), ("$b", JsonSerializer.Serialize(value)));
    public T? GetState<T>(string key) { lock (gate) { using var db = Open(); return ReadState<T>(db, key); } }
    public void SetState<T>(string key, T value) { lock (gate) { CheckDiskSpace(); using var db = Open(); WriteState(db, key, value); } }
    public void Maintain()
    {
        lock (gate)
        {
            using var db = Open();
            var cutoff = DateTimeOffset.UtcNow.AddDays(-settings.HistoryDays).ToString("O");
            Execute(db, "DELETE FROM samples WHERE time < $t; DELETE FROM alerts WHERE time < $t;", null, ("$t", cutoff));
            Execute(db, "PRAGMA wal_checkpoint(TRUNCATE); PRAGMA incremental_vacuum(2048);");
        }
    }
    public string IntegrityCheck() { lock (gate) { using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "PRAGMA integrity_check"; return Convert.ToString(cmd.ExecuteScalar()) ?? "unknown"; } }
    public string EngineVersion() { lock (gate) { using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT sqlite_version()"; return Convert.ToString(cmd.ExecuteScalar()) ?? "unknown"; } }
}
