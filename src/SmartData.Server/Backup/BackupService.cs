using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SmartData.Contracts;
using SmartData.Core;
using SmartData.Core.BinarySerialization;
using SmartData.Server.Providers;

namespace SmartData.Server.Backup;

public class BackupService
{
    private const string Extension = ".smartbackup";
    private const string RootDir = "_backups";
    private const string BackupsSubDir = "backups";
    private const string HistorySubDir = "history";
    private const string JobsSubDir = "jobs";
    private const string OldHistoryFile = "history.json";
    private const int ImportBatchSize = 1000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IDatabaseProvider _db;
    private readonly BackupJobQueue _jobQueue;
    private readonly BackupOptions _options;
    private readonly ConcurrentDictionary<string, BackupJob> _jobs = new();

    private readonly string _rootDir;
    private readonly string _backupsDir;
    private readonly string _historyDir;
    private readonly string _jobsDir;

    public BackupService(IDatabaseProvider dbProvider, BackupJobQueue jobQueue, IOptions<BackupOptions> options)
    {
        _db = dbProvider;
        _jobQueue = jobQueue;
        _options = options.Value;
        _rootDir = Path.Combine(dbProvider.DataDirectory, RootDir);
        _backupsDir = Path.Combine(_rootDir, BackupsSubDir);
        _historyDir = Path.Combine(_rootDir, HistorySubDir);
        _jobsDir = Path.Combine(_rootDir, JobsSubDir);
    }

    // --- Initialization (called by BackupJobRunner on startup) ---

    public void Initialize()
    {
        Directory.CreateDirectory(_backupsDir);
        Directory.CreateDirectory(_historyDir);
        Directory.CreateDirectory(_jobsDir);

        // Migrate flat layout: move *.smartbackup from root to backups/
        foreach (var file in Directory.GetFiles(_rootDir, $"*{Extension}"))
        {
            var dest = Path.Combine(_backupsDir, Path.GetFileName(file));
            if (!File.Exists(dest))
                File.Move(file, dest);
            else
                TryDelete(file);
        }

        // Generate missing sidecar manifests
        foreach (var zip in Directory.GetFiles(_backupsDir, $"*{Extension}"))
        {
            var id = Path.GetFileNameWithoutExtension(zip);
            var sidecar = Path.Combine(_backupsDir, $"{id}.json");
            if (!File.Exists(sidecar))
            {
                try
                {
                    using var archive = ZipFile.OpenRead(zip);
                    var manifest = ReadManifest(archive);
                    WriteSidecarManifest(manifest.Id, manifest);
                }
                catch { }
            }
        }

        // Migrate old history.json to individual files
        var oldHistoryPath = Path.Combine(_rootDir, OldHistoryFile);
        if (File.Exists(oldHistoryPath))
        {
            try
            {
                var entries = JsonSerializer.Deserialize<List<BackupHistoryEntry>>(
                    File.ReadAllText(oldHistoryPath), JsonOptions) ?? [];
                foreach (var entry in entries)
                    AppendHistoryEntry(entry);
                File.Move(oldHistoryPath, oldHistoryPath + ".migrated");
            }
            catch { }
        }

        // Clean up stale job files from previous crash
        foreach (var f in Directory.GetFiles(_jobsDir, "*.json"))
            TryDelete(f);

        RunCleanup();
    }

    // --- Job submission (return immediately, work runs in background) ---

    public BackupCreateResult SubmitCreateJob(string databases, string? user)
    {
        if (string.IsNullOrWhiteSpace(databases))
            throw new InvalidOperationException("Databases parameter is required. Use database names (comma-separated) or '*' for all.");

        var dbNames = ResolveDbNames(databases);
        if (dbNames.Count == 0)
            throw new InvalidOperationException("No databases found.");

        var backupId = IdGenerator.NewId();
        var jobId = IdGenerator.NewId();

        var job = new BackupJob
        {
            JobId = jobId,
            Operation = "create",
            BackupId = backupId,
            Databases = dbNames,
            User = user,
            StartedAt = DateTime.UtcNow
        };

        _jobs[jobId] = job;
        WriteJobFile(job);
        _jobQueue.Enqueue(job);

        return new BackupCreateResult { JobId = jobId, BackupId = backupId, Databases = dbNames };
    }

    public BackupRestoreResult SubmitRestoreJob(string backupId, bool force, string? user)
    {
        var zipPath = GetBackupPath(backupId);
        if (!File.Exists(zipPath))
            throw new InvalidOperationException($"Backup '{backupId}' not found.");

        var manifest = ReadManifestFromFile(zipPath);

        if (!force)
        {
            var conflicts = manifest.Databases.Where(name => _db.DatabaseExists(name)).ToList();
            if (conflicts.Count > 0)
                throw new InvalidOperationException(
                    $"Databases already exist: {string.Join(", ", conflicts)}. Use --force to overwrite.");
        }

        var jobId = IdGenerator.NewId();

        var job = new BackupJob
        {
            JobId = jobId,
            Operation = "restore",
            BackupId = backupId,
            Databases = manifest.Databases,
            Force = force,
            User = user,
            StartedAt = DateTime.UtcNow
        };

        _jobs[jobId] = job;
        WriteJobFile(job);
        _jobQueue.Enqueue(job);

        return new BackupRestoreResult { JobId = jobId, Message = "Restore job started.", Databases = manifest.Databases };
    }

    // --- Job status ---

    public BackupJobStatus? GetJobStatus(string jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
            return null;

        return new BackupJobStatus
        {
            JobId = job.JobId,
            Operation = job.Operation,
            Status = job.Status,
            BackupId = job.BackupId,
            Databases = job.Databases,
            Size = job.Size,
            Progress = job.Progress,
            ProgressMessage = job.ProgressMessage,
            ElapsedMs = job.ElapsedMs > 0 ? job.ElapsedMs
                : (long)(DateTime.UtcNow - job.StartedAt).TotalMilliseconds,
            Error = job.Error
        };
    }

    public bool CancelJob(string jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var job)) return false;
        if (job.Status is "completed" or "failed" or "cancelled") return false;
        job.Cts.Cancel();
        return true;
    }

    // --- Job execution (called by BackupJobRunner) ---

    internal async Task ExecuteCreateJob(BackupJob job, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var dbNames = job.Databases;
        var backupId = job.BackupId!;

        Directory.CreateDirectory(_backupsDir);

        var tempPath = Path.Combine(_backupsDir, $"{backupId}.tmp");
        var finalPath = GetBackupPath(backupId);
        var checksums = new Dictionary<string, string>();

        try
        {
            using (var zipStream = new FileStream(tempPath, FileMode.Create))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                for (var i = 0; i < dbNames.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    var dbName = dbNames[i];
                    job.Progress = (double)i / dbNames.Count;
                    job.ProgressMessage = $"Exporting '{dbName}' ({i + 1}/{dbNames.Count})";

                    // Write schema
                    var schema = ExportSchema(dbName);
                    var schemaPath = $"databases/{dbName}/_schema.json";
                    var schemaEntry = archive.CreateEntry(schemaPath);
                    using (var writer = new StreamWriter(schemaEntry.Open()))
                        await writer.WriteAsync(JsonSerializer.Serialize(schema, JsonOptions));

                    // Write table data
                    var tables = _db.Schema.GetTables(dbName);
                    foreach (var table in tables)
                    {
                        ct.ThrowIfCancellationRequested();

                        var entryPath = $"databases/{dbName}/{table.Name}.bin";
                        var entry = archive.CreateEntry(entryPath);

                        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                        using (var entryStream = entry.Open())
                        using (var hashStream = new CryptoStream(entryStream, new HashTransform(sha), CryptoStreamMode.Write))
                        {
                            using var reader = _db.RawData.OpenReader(dbName, table.Name);
                            var options = new SerializerOptions { UseKeyInterning = true };
                            BinarySerializer.Serialize<System.Data.IDataReader>(hashStream, reader, options);
                        }

                        checksums[entryPath] = Convert.ToHexString(sha.GetHashAndReset());
                    }
                }

                // Write manifest
                var manifest = new BackupManifest
                {
                    Id = backupId,
                    CreatedAt = DateTime.UtcNow,
                    Databases = dbNames,
                    Checksums = checksums
                };
                var manifestEntry = archive.CreateEntry("backup.json");
                using (var writer = new StreamWriter(manifestEntry.Open()))
                    await writer.WriteAsync(JsonSerializer.Serialize(manifest, JsonOptions));

                // Write sidecar after archive is closed (below)
                job.Progress = 1.0;
                job.ProgressMessage = "Finalizing backup";
            }

            File.Move(tempPath, finalPath);

            // Write sidecar manifest for fast listing
            var sidecarManifest = new BackupManifest
            {
                Id = backupId,
                CreatedAt = DateTime.UtcNow,
                Databases = dbNames,
                Checksums = checksums
            };
            WriteSidecarManifest(backupId, sidecarManifest);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }

        var size = new FileInfo(finalPath).Length;
        sw.Stop();

        job.Size = size;
        job.ElapsedMs = sw.ElapsedMilliseconds;
    }

    internal async Task ExecuteRestoreJob(BackupJob job, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var backupId = job.BackupId!;
        var force = job.Force;

        var zipPath = GetBackupPath(backupId);
        using var archive = ZipFile.OpenRead(zipPath);
        var manifest = ReadManifest(archive);
        var dbNames = manifest.Databases;

        for (var i = 0; i < dbNames.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var dbName = dbNames[i];
            job.Progress = (double)i / dbNames.Count;
            job.ProgressMessage = $"Restoring '{dbName}' ({i + 1}/{dbNames.Count})";

            _db.EnsureDatabase(dbName);

            // Read and apply schema
            var schemaEntry = archive.GetEntry($"databases/{dbName}/_schema.json")
                ?? throw new InvalidOperationException($"Schema not found for database '{dbName}' in backup.");

            BackupSchemaDefinition schema;
            using (var reader = new StreamReader(schemaEntry.Open()))
                schema = JsonSerializer.Deserialize<BackupSchemaDefinition>(await reader.ReadToEndAsync(ct), JsonOptions)
                    ?? throw new InvalidOperationException($"Invalid schema for database '{dbName}'.");

            // Create tables
            foreach (var table in schema.Tables)
            {
                ct.ThrowIfCancellationRequested();

                if (_db.Schema.TableExists(dbName, table.Name))
                {
                    if (force)
                        _db.SchemaOperations.DropTable(dbName, table.Name);
                    else
                        continue;
                }

                var columns = table.Columns.Select(c => new ColumnDefinition(
                    c.Name, c.Type, c.Nullable, c.PrimaryKey, c.Identity
                ));
                _db.SchemaOperations.CreateTable(dbName, table.Name, columns);

                foreach (var idx in table.Indexes)
                {
                    _db.SchemaOperations.CreateIndex(dbName, table.Name, idx.Name, idx.Columns, idx.Unique);
                }
            }

            // Import data
            foreach (var table in schema.Tables)
            {
                ct.ThrowIfCancellationRequested();

                var dataEntry = archive.GetEntry($"databases/{dbName}/{table.Name}.bin");
                if (dataEntry == null) continue;

                var columnTypes = table.Columns.ToDictionary(c => c.Name, c => c.Type, StringComparer.OrdinalIgnoreCase);

                using var entryStream = dataEntry.Open();
                using var dataReader = new BinaryDataReader(entryStream);

                while (dataReader.HasMore)
                {
                    ct.ThrowIfCancellationRequested();
                    var batch = dataReader.Read(ImportBatchSize);
                    if (batch.Count == 0) break;

                    foreach (var row in batch)
                        CoerceRowTypes(row, columnTypes);

                    _db.RawData.Import(dbName, table.Name, batch, "insert", truncate: false);
                }
            }
        }

        sw.Stop();
        job.Progress = 1.0;
        job.ProgressMessage = "Restore complete";
        job.ElapsedMs = sw.ElapsedMilliseconds;
    }

    internal void FinalizeJob(BackupJob job)
    {
        AppendHistoryEntry(new BackupHistoryEntry
        {
            Timestamp = DateTime.UtcNow,
            Operation = job.Operation,
            BackupId = job.BackupId ?? "",
            User = job.User,
            Status = job.Status,
            Error = job.Error,
            Databases = job.Databases,
            Size = job.Size,
            DurationMs = job.ElapsedMs
        });

        // Remove job file
        TryDelete(Path.Combine(_jobsDir, $"{job.JobId}.json"));

        RunCleanup();
    }

    // --- Listing ---

    public List<BackupListItem> ListBackups()
    {
        if (!Directory.Exists(_backupsDir))
            return [];

        return Directory.GetFiles(_backupsDir, "*.json")
            .Select(f =>
            {
                try
                {
                    var manifest = JsonSerializer.Deserialize<BackupManifest>(
                        File.ReadAllText(f), JsonOptions)!;
                    var zipPath = Path.Combine(_backupsDir, $"{manifest.Id}{Extension}");
                    var size = File.Exists(zipPath) ? new FileInfo(zipPath).Length : 0;
                    return new BackupListItem
                    {
                        BackupId = manifest.Id,
                        Databases = manifest.Databases,
                        Size = size,
                        Created = manifest.CreatedAt
                    };
                }
                catch { return null; }
            })
            .Where(x => x != null)
            .OrderByDescending(b => b!.Created)
            .ToList()!;
    }

    // --- Drop ---

    public void DropBackup(string backupId)
    {
        var zipPath = GetBackupPath(backupId);
        var sidecarPath = Path.Combine(_backupsDir, $"{backupId}.json");

        if (!File.Exists(zipPath) && !File.Exists(sidecarPath))
            throw new InvalidOperationException($"Backup '{backupId}' not found.");

        TryDelete(zipPath);
        TryDelete(sidecarPath);

        AppendHistoryEntry(new BackupHistoryEntry
        {
            Timestamp = DateTime.UtcNow,
            Operation = "drop",
            BackupId = backupId,
            Status = "success"
        });
    }

    // --- Chunked download/upload ---

    public async Task<BackupDownloadResult> DownloadChunk(string backupId, long offset, int chunkSize)
    {
        var zipPath = GetBackupPath(backupId);
        if (!File.Exists(zipPath))
            throw new InvalidOperationException($"Backup '{backupId}' not found.");

        var totalSize = new FileInfo(zipPath).Length;
        if (offset >= totalSize)
            return new BackupDownloadResult { Data = [], Offset = offset, TotalSize = totalSize, Done = true };

        var readSize = (int)Math.Min(chunkSize, totalSize - offset);
        var buffer = new byte[readSize];

        using var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        fs.Seek(offset, SeekOrigin.Begin);
        var bytesRead = await fs.ReadAsync(buffer.AsMemory(0, readSize));
        if (bytesRead < readSize)
            Array.Resize(ref buffer, bytesRead);

        var newOffset = offset + bytesRead;
        return new BackupDownloadResult { Data = buffer, Offset = newOffset, TotalSize = totalSize, Done = newOffset >= totalSize };
    }

    public async Task<BackupUploadResult> UploadChunk(string backupId, byte[] data, long offset, long totalSize)
    {
        Directory.CreateDirectory(_backupsDir);

        if (offset == 0 && string.IsNullOrEmpty(backupId))
            backupId = IdGenerator.NewId();

        if (string.IsNullOrEmpty(backupId))
            throw new InvalidOperationException("BackupId is required for continuation chunks.");

        var zipPath = Path.Combine(_backupsDir, $"{backupId}{Extension}");
        using var fs = new FileStream(zipPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
        fs.Seek(offset, SeekOrigin.Begin);
        await fs.WriteAsync(data);

        var newOffset = offset + data.Length;
        var done = newOffset >= totalSize;

        if (done)
        {
            try
            {
                string? manifestId;
                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    var manifest = ReadManifest(archive);
                    manifestId = manifest.Id;
                    WriteSidecarManifest(manifest.Id, manifest);
                }

                if (!string.IsNullOrEmpty(manifestId) && manifestId != backupId)
                {
                    var finalPath = Path.Combine(_backupsDir, $"{manifestId}{Extension}");
                    if (File.Exists(finalPath))
                    {
                        File.Delete(zipPath);
                        throw new InvalidOperationException($"Backup '{manifestId}' already exists on the server.");
                    }
                    File.Move(zipPath, finalPath);
                    backupId = manifestId;
                }

                AppendHistoryEntry(new BackupHistoryEntry
                {
                    Timestamp = DateTime.UtcNow,
                    Operation = "upload",
                    BackupId = backupId,
                    Status = "success"
                });

                RunCleanup();
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch
            {
                // If manifest can't be read, keep the generated filename
            }
        }

        return new BackupUploadResult { BackupId = backupId, Offset = newOffset, TotalSize = totalSize, Done = done };
    }

    public Stream OpenDownloadStream(string backupId)
    {
        var zipPath = GetBackupPath(backupId);
        if (!File.Exists(zipPath))
            throw new InvalidOperationException($"Backup '{backupId}' not found.");

        return new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    // --- History ---

    public List<BackupHistoryEntry> GetHistory()
    {
        if (!Directory.Exists(_historyDir))
            return [];

        return Directory.GetFiles(_historyDir, "*.json")
            .OrderByDescending(Path.GetFileName)
            .Select(f =>
            {
                try { return JsonSerializer.Deserialize<BackupHistoryEntry>(File.ReadAllText(f), JsonOptions); }
                catch { return null; }
            })
            .Where(x => x != null)
            .ToList()!;
    }

    // --- Private helpers ---

    private void AppendHistoryEntry(BackupHistoryEntry entry)
    {
        try
        {
            Directory.CreateDirectory(_historyDir);
            var timestamp = entry.Timestamp.ToString("yyyyMMddTHHmmss");
            var filename = $"{timestamp}_{entry.BackupId}_{entry.Operation}.json";
            File.WriteAllText(
                Path.Combine(_historyDir, filename),
                JsonSerializer.Serialize(entry, JsonOptions));
        }
        catch { }
    }

    private void WriteSidecarManifest(string backupId, BackupManifest manifest)
    {
        var path = Path.Combine(_backupsDir, $"{backupId}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, JsonOptions));
    }

    private void WriteJobFile(BackupJob job)
    {
        try
        {
            Directory.CreateDirectory(_jobsDir);
            var path = Path.Combine(_jobsDir, $"{job.JobId}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                job.JobId,
                job.Operation,
                job.BackupId,
                job.Databases,
                job.StartedAt
            }, JsonOptions));
        }
        catch { }
    }

    private void RunCleanup()
    {
        try
        {
            // Backup retention
            if (_options.MaxBackupAge.HasValue || _options.MaxBackupCount.HasValue)
            {
                var sidecars = Directory.GetFiles(_backupsDir, "*.json")
                    .Select(f => (Path: f, Manifest: TryReadManifest(f)))
                    .Where(x => x.Manifest != null)
                    .OrderByDescending(x => x.Manifest!.CreatedAt)
                    .ToList();

                var cutoffDate = _options.MaxBackupAge.HasValue
                    ? DateTime.UtcNow.AddDays(-_options.MaxBackupAge.Value)
                    : DateTime.MinValue;

                for (var i = 0; i < sidecars.Count; i++)
                {
                    var tooOld = sidecars[i].Manifest!.CreatedAt < cutoffDate;
                    var overCount = _options.MaxBackupCount.HasValue && i >= _options.MaxBackupCount.Value;

                    if (tooOld || overCount)
                    {
                        var id = sidecars[i].Manifest!.Id;
                        TryDelete(Path.Combine(_backupsDir, $"{id}{Extension}"));
                        TryDelete(sidecars[i].Path);
                    }
                }
            }

            // History retention
            if (_options.MaxHistoryAge.HasValue || _options.MaxHistoryCount.HasValue)
            {
                var files = Directory.GetFiles(_historyDir, "*.json")
                    .OrderByDescending(Path.GetFileName)
                    .ToList();

                var cutoffDate = _options.MaxHistoryAge.HasValue
                    ? DateTime.UtcNow.AddDays(-_options.MaxHistoryAge.Value)
                    : DateTime.MinValue;

                for (var i = 0; i < files.Count; i++)
                {
                    var overCount = _options.MaxHistoryCount.HasValue && i >= _options.MaxHistoryCount.Value;
                    var tooOld = TryParseTimestampFromFilename(files[i]) is { } ts && ts < cutoffDate;

                    if (tooOld || overCount)
                        TryDelete(files[i]);
                }
            }

            // Purge completed jobs from memory after 1 hour
            var expiry = DateTime.UtcNow.AddHours(-1);
            foreach (var kvp in _jobs)
            {
                if (kvp.Value.Status is "completed" or "failed" or "cancelled"
                    && kvp.Value.StartedAt < expiry)
                    _jobs.TryRemove(kvp.Key, out _);
            }
        }
        catch { }
    }

    private BackupSchemaDefinition ExportSchema(string dbName)
    {
        var tables = _db.Schema.GetTables(dbName);
        var schema = new BackupSchemaDefinition();

        foreach (var table in tables)
        {
            var columns = _db.Schema.GetColumns(dbName, table.Name);
            var indexes = _db.Schema.GetIndexes(dbName, table.Name);

            schema.Tables.Add(new BackupTableDefinition
            {
                Name = table.Name,
                Columns = columns.Select(c => new BackupColumnDefinition
                {
                    Name = c.Name,
                    Type = _db.SchemaOperations.MapTypeReverse(c.Type),
                    Nullable = c.IsNullable,
                    PrimaryKey = c.IsPrimaryKey,
                    Identity = c.IsIdentity
                }).ToList(),
                Indexes = indexes
                    .Where(i => i.Columns != null)
                    .Select(i => new BackupIndexDefinition
                    {
                        Name = i.Name,
                        Columns = i.Columns!,
                        Unique = i.IsUnique
                    }).ToList()
            });
        }

        return schema;
    }

    private List<string> ResolveDbNames(string databases)
    {
        if (databases.Trim() == "*")
            return _db.ListDatabases().ToList();

        var names = databases.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var name in names)
        {
            if (!_db.DatabaseExists(name))
                throw new InvalidOperationException($"Database '{name}' not found.");
        }

        return names;
    }

    private static void CoerceRowTypes(Dictionary<string, object?> row, Dictionary<string, string> columnTypes)
    {
        foreach (var key in row.Keys)
        {
            if (row[key] == null || !columnTypes.TryGetValue(key, out var logicalType))
                continue;

            row[key] = logicalType switch
            {
                "datetime" when row[key] is long ticks => DateTime.FromBinary(ticks),
                "bool" when row[key] is long n => n != 0,
                "int" when row[key] is long n => (int)n,
                "decimal" when row[key] is double d => (decimal)d,
                "guid" when row[key] is string s => Guid.Parse(s),
                _ => row[key]
            };
        }
    }

    private string GetBackupPath(string backupId) =>
        Path.Combine(_backupsDir, $"{backupId}{Extension}");

    private static BackupManifest ReadManifest(ZipArchive archive)
    {
        var entry = archive.GetEntry("backup.json")
            ?? throw new InvalidOperationException("Backup manifest not found.");

        using var reader = new StreamReader(entry.Open());
        return JsonSerializer.Deserialize<BackupManifest>(reader.ReadToEnd(),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
            ?? throw new InvalidOperationException("Invalid backup manifest.");
    }

    private static BackupManifest ReadManifestFromFile(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        return ReadManifest(archive);
    }

    private BackupManifest? TryReadManifest(string sidecarPath)
    {
        try { return JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(sidecarPath), JsonOptions); }
        catch { return null; }
    }

    private static DateTime? TryParseTimestampFromFilename(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        var parts = name.Split('_', 2);
        if (parts.Length > 0 && DateTime.TryParseExact(parts[0], "yyyyMMddTHHmmss",
            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var ts))
            return ts;
        return null;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }

    private sealed class HashTransform : ICryptoTransform
    {
        private readonly IncrementalHash _hash;

        public HashTransform(IncrementalHash hash) => _hash = hash;

        public int InputBlockSize => 1;
        public int OutputBlockSize => 1;
        public bool CanTransformMultipleBlocks => true;
        public bool CanReuseTransform => false;

        public int TransformBlock(byte[] inputBuffer, int inputOffset, int inputCount, byte[]? outputBuffer, int outputOffset)
        {
            _hash.AppendData(inputBuffer, inputOffset, inputCount);
            Buffer.BlockCopy(inputBuffer, inputOffset, outputBuffer!, outputOffset, inputCount);
            return inputCount;
        }

        public byte[] TransformFinalBlock(byte[] inputBuffer, int inputOffset, int inputCount)
        {
            _hash.AppendData(inputBuffer, inputOffset, inputCount);
            var output = new byte[inputCount];
            Buffer.BlockCopy(inputBuffer, inputOffset, output, 0, inputCount);
            return output;
        }

        public void Dispose() { }
    }
}
