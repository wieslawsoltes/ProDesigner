using System.Text.Json;

namespace ProDesigner.Persistence;

public sealed record FileJournalSummary(FileTransactionReceipt Receipt, string State, string[] Paths, string? Error = null);
public enum JournalFileState { Original, Applied, Conflict, Missing }
public sealed record JournalFileReview(string Path, JournalFileState State, string Before, string After);
public sealed record FileJournalReview(FileTransactionReceipt Receipt, string State, JournalFileReview[] Files)
{
    public bool CanRestore => State != "Reverted" && Files.Length > 0 && Files.All(f => f.State is JournalFileState.Original or JournalFileState.Applied);
    public bool CanReplay => State == "Reverted" && Files.Length > 0 && Files.All(f => f.State == JournalFileState.Original);
}

public partial class JournaledFileTransaction
{
    /// <summary>Lists bounded journal metadata only; no target project files are opened.</summary>
    public async Task<IReadOnlyList<FileJournalSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_journalRoot)) return [];
        ValidateDirectory(_journalRoot);
        var result = new List<FileJournalSummary>(); long totalBytes = 0;
        foreach (var path in Directory.EnumerateFiles(_journalRoot, "*.json", SearchOption.TopDirectoryOnly).OrderByDescending(File.GetLastWriteTimeUtc).Take(128))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out _)) continue;
            var receipt = new FileTransactionReceipt(Path.GetFileNameWithoutExtension(path), path);
            try
            {
                totalBytes += new FileInfo(path).Length;
                if (totalBytes > MaximumBytes) break;
                var journal = await ReadJournalAsync(receipt, cancellationToken).ConfigureAwait(false);
                result.Add(new(receipt, journal.State, journal.Entries.Select(e => e.Path).ToArray()));
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
            { result.Add(new(receipt, "Invalid", [], ex.Message)); }
        }
        return result;
    }
    /// <summary>Inspects target files only after the caller explicitly approves the displayed path set.</summary>
    public async Task<FileJournalReview> ReviewAsync(FileTransactionReceipt receipt, IReadOnlyCollection<string> allowedPaths, CancellationToken cancellationToken = default)
    {
        var journal = await ReadJournalAsync(receipt, cancellationToken).ConfigureAwait(false);
        ApprovePaths(journal, allowedPaths);
        var files = new List<JournalFileReview>();
        foreach (var entry in journal.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested(); var state = JournalFileState.Missing;
            if (File.Exists(entry.Path))
            {
                ValidatePath(entry.Path);
                var hash = Hash(await ReadBoundedAsync(entry.Path, cancellationToken).ConfigureAwait(false));
                state = hash == entry.BeforeHash ? JournalFileState.Original : hash == entry.AfterHash ? JournalFileState.Applied : JournalFileState.Conflict;
            }
            files.Add(new(entry.Path, state, Decode(entry.Before), Decode(entry.After)));
        }
        return new(receipt, journal.State, files.ToArray());
    }
    /// <summary>Replays a reverted operation as a new durable transaction; original journal history is not overwritten.</summary>
    public async Task<FileTransactionReceipt> ReplayAsync(FileTransactionReceipt receipt, IReadOnlyCollection<string> allowedPaths, CancellationToken cancellationToken = default)
    {
        var journal = await ReadJournalAsync(receipt, cancellationToken).ConfigureAwait(false);
        ApprovePaths(journal, allowedPaths);
        if (journal.State != "Reverted") throw new InvalidOperationException("Only a reverted transaction can be replayed.");
        return await ApplyAsync(journal.Entries.Select(e => new FileTextChange(e.Path, Decode(e.Before), Decode(e.After), e.BeforeHash)).ToArray(), cancellationToken).ConfigureAwait(false);
    }
    private async Task<FileTransactionJournal> ReadJournalAsync(FileTransactionReceipt receipt, CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(receipt.JournalPath);
        if (!Guid.TryParseExact(receipt.Id, "N", out _) || !PathComparer.Equals(Path.GetDirectoryName(path), _journalRoot) || Path.GetFileName(path) != receipt.Id + ".json")
            throw new InvalidDataException("Receipt does not belong to this journal store.");
        ValidatePath(path);
        var bytes = await ReadBoundedAsync(path, cancellationToken).ConfigureAwait(false);
        var journal = JsonSerializer.Deserialize(bytes, FileTransactionJsonContext.Default.FileTransactionJournal) ?? throw new InvalidDataException("Empty journal.");
        if (journal.Id != receipt.Id || journal.State is not "Prepared" and not "Committed" and not "Reverted" || journal.Entries is null || journal.Entries.Length is < 1 or > 512)
            throw new InvalidDataException("Invalid journal metadata.");
        long total = 0; var paths = new HashSet<string>(PathComparer);
        foreach (var entry in journal.Entries)
        {
            if (entry is null || !Path.IsPathFullyQualified(entry.Path) || !PathComparer.Equals(entry.Path, Path.GetFullPath(entry.Path)) || !paths.Add(entry.Path) || entry.Before is null || entry.After is null ||
                (total += entry.Before.Length + (long)entry.After.Length) > MaximumBytes / 2 || Hash(entry.Before) != entry.BeforeHash || Hash(entry.After) != entry.AfterHash)
                throw new InvalidDataException("Invalid path or corrupted journal snapshot.");
        }
        return journal;
    }
    private static void ApprovePaths(FileTransactionJournal journal, IReadOnlyCollection<string> allowedPaths)
    {
        var approved = allowedPaths.Select(Path.GetFullPath).ToHashSet(PathComparer);
        if (journal.Entries.Any(e => !approved.Contains(e.Path))) throw new InvalidDataException("Approve every displayed target path before opening or restoring project files.");
    }
    private static string Decode(byte[] bytes)
    {
        var (encoding, preamble) = DetectEncoding(bytes); return encoding.GetString(bytes, preamble, bytes.Length - preamble);
    }
}
