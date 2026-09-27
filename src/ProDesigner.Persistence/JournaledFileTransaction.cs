using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProDesigner.Persistence;

public sealed record FileTextChange(string Path, string ExpectedText, string NewText, string? ExpectedHash = null);
public sealed record FileTransactionEntry(string Path, byte[] Before, byte[] After, string BeforeHash, string AfterHash, int? UnixMode);
public sealed record FileTransactionJournal(string Id, string State, FileTransactionEntry[] Entries);
public sealed record FileTransactionReceipt(string Id, string JournalPath);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(FileTransactionJournal))]
public partial class FileTransactionJsonContext : JsonSerializerContext;

/// <summary>Preflighted multi-file replacement with a durable before/after journal and conflict-aware rollback.</summary>
/// <remarks>Each file is replaced atomically. Filesystems do not offer atomic visibility across multiple paths;
/// external editors must cooperate to eliminate the final check/replace race. No external change is intentionally overwritten.</remarks>
public partial class JournaledFileTransaction
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly string _journalRoot;
    private const int MaximumBytes = 64 * 1024 * 1024;
    public JournaledFileTransaction(string journalRoot) => _journalRoot = Path.GetFullPath(journalRoot);
    public async Task<FileTransactionReceipt> ApplyAsync(IReadOnlyList<FileTextChange> changes, CancellationToken cancellationToken = default)
    {
        if (changes.Count is < 1 or > 512) throw new ArgumentException("A file transaction needs 1–512 existing files.", nameof(changes));
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = new List<FileTransactionEntry>(); var names = new HashSet<string>(PathComparer); long bytes = 0;
            foreach (var change in changes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = Path.GetFullPath(change.Path); ValidatePath(path);
                if (!names.Add(path)) throw new InvalidOperationException("Duplicate file in transaction.");
                var before = await ReadBoundedAsync(path, cancellationToken).ConfigureAwait(false);
                if (change.ExpectedHash is not null && Hash(before) != change.ExpectedHash) throw new IOException("The file bytes changed after journal review: " + path);
                var (encoding, preamble) = DetectEncoding(before);
                var text = encoding.GetString(before, preamble, before.Length - preamble);
                if (text != change.ExpectedText) throw new IOException("The file changed after refactoring was prepared: " + path);
                var encoded = encoding.GetBytes(change.NewText);
                var after = before.Take(preamble).Concat(encoded).ToArray(); bytes += before.Length + after.Length;
                if (bytes > MaximumBytes / 2) throw new IOException("Transaction exceeds its 32 MiB binary snapshot budget.");
                int? mode = OperatingSystem.IsWindows() ? null : (int)File.GetUnixFileMode(path);
                entries.Add(new(path, before, after, Hash(before), Hash(after), mode));
            }
            Directory.CreateDirectory(_journalRoot); ValidateDirectory(_journalRoot);
            var id = Guid.NewGuid().ToString("N"); var journalPath = Path.Combine(_journalRoot, id + ".json");
            var journal = new FileTransactionJournal(id, "Prepared", entries.ToArray());
            await WriteJournalAsync(journalPath, journal, cancellationToken).ConfigureAwait(false);
            var completed = new List<FileTransactionEntry>();
            try
            {
                for (var index = 0; index < entries.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested(); var entry = entries[index];
                    await ReplaceAsync(entry, entry.BeforeHash, entry.After, id, index, cancellationToken).ConfigureAwait(false);
                    completed.Add(entry);
                }
                await WriteJournalAsync(journalPath, journal with { State = "Committed" }, cancellationToken).ConfigureAwait(false);
                return new(id, journalPath);
            }
            catch (Exception failure)
            {
                try
                {
                    foreach (var entry in completed.AsEnumerable().Reverse())
                        await ReplaceAsync(entry, entry.AfterHash, entry.Before, id + "-rollback", -1, CancellationToken.None).ConfigureAwait(false);
                    await WriteJournalAsync(journalPath, journal with { State = "Reverted" }, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception rollbackFailure) { throw new AggregateException("Commit failed and rollback requires journal recovery. No conflicting external file was overwritten.", failure, rollbackFailure); }
                throw;
            }
        }
        finally { Gate.Release(); }
    }
    public async Task RevertAsync(FileTransactionReceipt receipt, IReadOnlyCollection<string> allowedPaths, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = Path.GetFullPath(receipt.JournalPath);
            if (!Guid.TryParseExact(receipt.Id, "N", out _) || !PathComparer.Equals(Path.GetDirectoryName(path), _journalRoot) || Path.GetFileName(path) != receipt.Id + ".json") throw new InvalidDataException("Receipt does not belong to this journal store.");
            ValidatePath(path);
            var data = await ReadBoundedAsync(path, cancellationToken).ConfigureAwait(false);
            var journal = JsonSerializer.Deserialize(data, FileTransactionJsonContext.Default.FileTransactionJournal) ?? throw new InvalidDataException("Empty transaction journal.");
            if (journal.Id != receipt.Id || journal.Entries is null || journal.Entries.Length is < 1 or > 512 || journal.State is not "Prepared" and not "Committed" and not "Reverted") throw new InvalidDataException("Invalid transaction journal.");
            if (journal.State == "Reverted") return;
            var allowed = allowedPaths.Select(Path.GetFullPath).ToHashSet(PathComparer); var used = new HashSet<string>(PathComparer);
            var restore = new List<FileTransactionEntry>();
            // Preflight every file before restoring any, including journals left behind by process termination.
            foreach (var entry in journal.Entries)
            {
                if (entry is null || !allowed.Contains(entry.Path) || !used.Add(entry.Path) || entry.Before is null || entry.After is null || Hash(entry.Before) != entry.BeforeHash || Hash(entry.After) != entry.AfterHash) throw new InvalidDataException("Unapproved path or corrupted transaction snapshot.");
                ValidatePath(entry.Path);
                var hash = Hash(await ReadBoundedAsync(entry.Path, cancellationToken).ConfigureAwait(false));
                if (hash == entry.AfterHash) restore.Add(entry);
                else if (hash != entry.BeforeHash) throw new IOException("A file was edited after the transaction; rollback was not started: " + entry.Path);
            }
            foreach (var entry in restore.AsEnumerable().Reverse())
                await ReplaceAsync(entry, entry.AfterHash, entry.Before, receipt.Id + "-revert", -1, cancellationToken).ConfigureAwait(false);
            await WriteJournalAsync(path, journal with { State = "Reverted" }, cancellationToken).ConfigureAwait(false);
        }
        finally { Gate.Release(); }
    }
    protected virtual void ReplaceStagedFile(string stagedPath, string destination, int index) => File.Move(stagedPath, destination, overwrite: true);
    private async Task ReplaceAsync(FileTransactionEntry entry, string expectedHash, byte[] bytes, string id, int index, CancellationToken cancellationToken)
    {
        ValidatePath(entry.Path);
        var temporary = entry.Path + ".pd-" + id + ".tmp"; var created = false;
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough))
            {
                created = true; await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false); stream.Flush(flushToDisk: true);
            }
            if (!OperatingSystem.IsWindows() && entry.UnixMode is { } mode) File.SetUnixFileMode(temporary, (UnixFileMode)mode);
            if (Hash(await ReadBoundedAsync(entry.Path, cancellationToken).ConfigureAwait(false)) != expectedHash) throw new IOException("A concurrent writer changed the file: " + entry.Path);
            ReplaceStagedFile(temporary, entry.Path, index);
        }
        finally { if (created && File.Exists(temporary)) File.Delete(temporary); }
    }
    private static async Task WriteJournalAsync(string path, FileTransactionJournal journal, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(journal, FileTransactionJsonContext.Default.FileTransactionJournal);
        if (bytes.Length > MaximumBytes) throw new IOException("Journal exceeds its size budget.");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough))
            { await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false); stream.Flush(flushToDisk: true); }
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static async Task<byte[]> ReadBoundedAsync(string path, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path); if (!info.Exists || info.Length > MaximumBytes) throw new IOException("File is missing or exceeds the transaction size limit.");
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        if (bytes.Length > MaximumBytes) throw new IOException("File grew beyond the transaction size limit."); return bytes;
    }
    private static (Encoding Encoding, int Preamble) DetectEncoding(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe, 0, 0 })) return (new UTF32Encoding(false, true, true), 4);
        if (bytes.AsSpan().StartsWith(new byte[] { 0, 0, 0xfe, 0xff })) return (new UTF32Encoding(true, true, true), 4);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe })) return (new UnicodeEncoding(false, true, true), 2);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff })) return (new UnicodeEncoding(true, true, true), 2);
        return (new UTF8Encoding(false, true), bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0);
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static void ValidatePath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Symbolic links and relative transaction paths are not supported.");
        ValidateDirectory(Path.GetDirectoryName(path)!);
    }
    private static void ValidateDirectory(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Transaction paths cannot traverse symbolic links.");
    }
}
