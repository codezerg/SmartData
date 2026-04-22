using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using SmartData.Core.BinarySerialization;

namespace SmartData.Cli.Commands;

public static class BackupCommand
{
    public static async Task Run(string[] args, SdConfig config, ApiClient client)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: sd backup <create|restore|list|drop|download|upload|history|status|cancel|verify> [args]");
            return;
        }

        var sub = args[0].ToLowerInvariant();
        var rest = args[1..];

        switch (sub)
        {
            case "create":
                if (rest.Length < 1) { Console.Error.WriteLine("Usage: sd backup create <db1,db2 or *> [--no-wait]"); return; }
                {
                    var result = await client.SendAsync("sp_backup_create", new() { ["Databases"] = rest[0] });
                    if (!result.Success) { Console.Error.WriteLine($"Error: {result.Error}"); return; }
                    var jobResult = result.GetData<JobResult>();
                    if (jobResult?.jobId == null) { Console.Error.WriteLine("No job ID returned."); return; }
                    if (ArgParser.HasFlag(rest, "--no-wait"))
                        Console.WriteLine($"Job started: {jobResult.jobId}");
                    else
                        await PollJob(client, jobResult.jobId);
                }
                break;

            case "restore":
                if (rest.Length < 1) { Console.Error.WriteLine("Usage: sd backup restore <backup-id> [--force] [--no-wait]"); return; }
                {
                    var spArgs = new Dictionary<string, object> { ["BackupId"] = rest[0] };
                    if (ArgParser.HasFlag(rest, "--force")) spArgs["Force"] = true;
                    var result = await client.SendAsync("sp_backup_restore", spArgs);
                    if (!result.Success) { Console.Error.WriteLine($"Error: {result.Error}"); return; }
                    var jobResult = result.GetData<JobResult>();
                    if (jobResult?.jobId == null) { Console.Error.WriteLine("No job ID returned."); return; }
                    if (ArgParser.HasFlag(rest, "--no-wait"))
                        Console.WriteLine($"Job started: {jobResult.jobId}");
                    else
                        await PollJob(client, jobResult.jobId);
                }
                break;

            case "list":
                await TimedSend(client, "sp_backup_list");
                break;

            case "drop":
                if (rest.Length < 1) { Console.Error.WriteLine("Usage: sd backup drop <backup-id>"); return; }
                await TimedSend(client, "sp_backup_drop", new() { ["BackupId"] = rest[0] });
                break;

            case "download":
                await Download(rest, client);
                break;

            case "upload":
                await Upload(rest, client);
                break;

            case "history":
                await TimedSend(client, "sp_backup_history");
                break;

            case "status":
                if (rest.Length < 1) { Console.Error.WriteLine("Usage: sd backup status <job-id>"); return; }
                await client.SendAndPrint("sp_backup_status", new() { ["JobId"] = rest[0] });
                break;

            case "cancel":
                if (rest.Length < 1) { Console.Error.WriteLine("Usage: sd backup cancel <job-id>"); return; }
                await client.SendAndPrint("sp_backup_cancel", new() { ["JobId"] = rest[0] });
                break;

            case "verify":
                if (rest.Length < 1) { Console.Error.WriteLine("Usage: sd backup verify <file.smartbackup>"); return; }
                Verify(rest[0]);
                break;

            default:
                Console.Error.WriteLine($"Unknown backup command: {sub}");
                break;
        }
    }

    private static async Task TimedSend(ApiClient client, string command, Dictionary<string, object>? args = null)
    {
        var sw = Stopwatch.StartNew();
        await client.SendAndPrint(command, args);
        sw.Stop();
        Console.WriteLine($"Completed in {FormatDuration(sw.ElapsedMilliseconds)}");
    }

    private static async Task Download(string[] args, ApiClient client)
    {
        if (args.Length < 1) { Console.Error.WriteLine("Usage: sd backup download <backup-id> [--out file.smartbackup]"); return; }

        var backupId = args[0];
        var outFile = ArgParser.GetFlag(args, "--out") ?? $"{backupId}.smartbackup";
        var chunkSize = 1024 * 1024; // 1MB

        var sw = Stopwatch.StartNew();
        long offset = 0;
        using var fs = new FileStream(outFile, FileMode.Create, FileAccess.Write);

        while (true)
        {
            var result = await client.SendAsync("sp_backup_download", new()
            {
                ["BackupId"] = backupId,
                ["Offset"] = offset,
                ["ChunkSize"] = chunkSize
            });

            if (!result.Success)
            {
                Console.Error.WriteLine($"Error: {result.Error}");
                return;
            }

            var data = result.GetData<DownloadChunk>();
            if (data == null) break;

            if (data.data is { Length: > 0 })
            {
                await fs.WriteAsync(data.data);
                offset = data.offset;
                Console.Write($"\rDownloading: {offset}/{data.totalSize} bytes");
            }

            if (data.done) break;
        }

        sw.Stop();
        Console.WriteLine($"\nDownloaded to {outFile} in {FormatDuration(sw.ElapsedMilliseconds)}");
    }

    private static async Task Upload(string[] args, ApiClient client)
    {
        var file = ArgParser.GetFlag(args, "--file");

        if (string.IsNullOrEmpty(file))
        {
            Console.Error.WriteLine("Usage: sd backup upload --file backup.smartbackup");
            return;
        }

        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"File not found: {file}");
            return;
        }

        var fileInfo = new FileInfo(file);
        var totalSize = fileInfo.Length;
        var chunkSize = 1024 * 1024; // 1MB
        long offset = 0;
        string? backupId = null;

        var sw = Stopwatch.StartNew();
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read);

        while (offset < totalSize)
        {
            var readSize = (int)Math.Min(chunkSize, totalSize - offset);
            var buffer = new byte[readSize];
            var bytesRead = await fs.ReadAsync(buffer.AsMemory(0, readSize));
            if (bytesRead < readSize)
                Array.Resize(ref buffer, bytesRead);

            var spArgs = new Dictionary<string, object>
            {
                ["Data"] = buffer,
                ["Offset"] = offset,
                ["TotalSize"] = totalSize
            };
            if (backupId != null)
                spArgs["BackupId"] = backupId;

            var result = await client.SendAsync("sp_backup_upload", spArgs);

            if (!result.Success)
            {
                Console.Error.WriteLine($"\nError: {result.Error}");
                return;
            }

            var chunk = result.GetData<UploadResult>();
            if (chunk != null)
                backupId = chunk.backupId;

            offset += bytesRead;
            Console.Write($"\rUploading: {offset}/{totalSize} bytes");
        }

        sw.Stop();
        Console.WriteLine($"\nUpload complete. Backup ID: {backupId} in {FormatDuration(sw.ElapsedMilliseconds)}");
    }

    private static void Verify(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"File not found: {filePath}");
            return;
        }

        var sw = Stopwatch.StartNew();
        using var archive = ZipFile.OpenRead(filePath);

        // Read manifest
        var manifestEntry = archive.GetEntry("backup.json");
        if (manifestEntry == null)
        {
            Console.Error.WriteLine("Invalid backup: missing backup.json manifest.");
            return;
        }

        Dictionary<string, object?>? manifest;
        using (var reader = new StreamReader(manifestEntry.Open()))
        {
            manifest = JsonSerializer.Deserialize<Dictionary<string, object?>>(reader.ReadToEnd(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        if (manifest == null)
        {
            Console.Error.WriteLine("Invalid backup: could not parse manifest.");
            return;
        }

        // Extract checksums
        var checksums = new Dictionary<string, string>();
        if (manifest.TryGetValue("checksums", out var checksumObj) && checksumObj is JsonElement el && el.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in el.EnumerateObject())
                checksums[prop.Name] = prop.Value.GetString() ?? "";
        }

        if (checksums.Count == 0)
        {
            Console.WriteLine("No checksums in manifest — nothing to verify.");
            return;
        }

        // Verify each entry
        var passed = 0;
        var failed = 0;

        foreach (var (entryPath, expectedHash) in checksums)
        {
            var entry = archive.GetEntry(entryPath);
            if (entry == null)
            {
                Console.WriteLine($"  MISSING  {entryPath}");
                failed++;
                continue;
            }

            using var stream = entry.Open();
            var actualHash = Convert.ToHexString(SHA256.HashData(stream));

            if (string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"  OK       {entryPath}");
                passed++;
            }
            else
            {
                Console.WriteLine($"  FAILED   {entryPath}");
                Console.WriteLine($"           expected: {expectedHash}");
                Console.WriteLine($"           actual:   {actualHash}");
                failed++;
            }
        }

        sw.Stop();
        Console.WriteLine();
        if (failed == 0)
            Console.WriteLine($"Verification passed: {passed}/{passed} entries OK in {FormatDuration(sw.ElapsedMilliseconds)}.");
        else
            Console.WriteLine($"Verification FAILED: {failed} of {passed + failed} entries corrupt. ({FormatDuration(sw.ElapsedMilliseconds)})");
    }

    private static async Task PollJob(ApiClient client, string jobId)
    {
        while (true)
        {
            await Task.Delay(500);
            var result = await client.SendAsync("sp_backup_status", new() { ["JobId"] = jobId });
            if (!result.Success) { Console.Error.WriteLine($"\nError: {result.Error}"); return; }

            var status = result.GetData<JobStatus>();
            if (status == null) { Console.Error.WriteLine("\nFailed to read job status."); return; }

            var progress = (int)(status.progress * 100);
            Console.Write($"\r{status.progressMessage ?? status.status} [{progress}%] ({FormatDuration(status.elapsedMs)})    ");

            if (status.status is "completed" or "failed" or "cancelled")
            {
                Console.WriteLine();
                if (status.status == "completed")
                    Console.WriteLine($"Done: {status.backupId} — {status.databases?.Count} database(s), {FormatSize(status.size ?? 0)}, {FormatDuration(status.elapsedMs)}");
                else if (status.status == "cancelled")
                    Console.WriteLine("Job was cancelled.");
                else if (status.error != null)
                    Console.Error.WriteLine($"Error: {status.error}");
                return;
            }
        }
    }

    private static string FormatSize(long bytes) =>
        bytes < 1024 ? $"{bytes} B"
        : bytes < 1024 * 1024 ? $"{bytes / 1024.0:0.#} KB"
        : bytes < 1024 * 1024 * 1024 ? $"{bytes / (1024.0 * 1024):0.#} MB"
        : $"{bytes / (1024.0 * 1024 * 1024):0.#} GB";

    private static string FormatDuration(long ms) =>
        ms < 1000 ? $"{ms}ms" : $"{ms / 1000.0:F1}s";

    private class DownloadChunk
    {
        public byte[]? data { get; set; }
        public long offset { get; set; }
        public long totalSize { get; set; }
        public bool done { get; set; }
    }

    private class UploadResult
    {
        public string? backupId { get; set; }
        public long offset { get; set; }
        public long totalSize { get; set; }
        public bool done { get; set; }
    }

    private class JobResult
    {
        public string? jobId { get; set; }
    }

    private class JobStatus
    {
        public string status { get; set; } = "";
        public string? backupId { get; set; }
        public List<string>? databases { get; set; }
        public long? size { get; set; }
        public long elapsedMs { get; set; }
        public string? error { get; set; }
        public double progress { get; set; }
        public string? progressMessage { get; set; }
    }
}
