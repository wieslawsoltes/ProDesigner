using ProDesigner.Persistence;
using Xunit;

namespace ProDesigner.Tests;

public class JournalRecoveryTests
{
    [Fact]
    public async Task AReopenedStoreListsReviewsRestoresAndReplaysOriginalBytes()
    {
        var token = TestContext.Current.CancellationToken; var directory = TempDirectory();
        try
        {
            var path = Path.Combine(directory, "View.axaml"); var journal = Path.Combine(directory, "journals");
            await File.WriteAllTextAsync(path, "<Original/>\r\n", new System.Text.UnicodeEncoding(false, true), token);
            var original = await File.ReadAllBytesAsync(path, token);
            var store = new JournaledFileTransaction(journal);
            var receipt = await store.ApplyAsync([new(path, "<Original/>\r\n", "<Renamed/>\r\n")], token);
            var after = await File.ReadAllBytesAsync(path, token);
            store = new JournaledFileTransaction(journal);
            var summary = Assert.Single(await store.ListAsync(token)); Assert.Equal("Committed", summary.State);
            await Assert.ThrowsAsync<InvalidDataException>(() => store.ReviewAsync(receipt, [], token));
            var review = await store.ReviewAsync(receipt, [path], token); Assert.True(review.CanRestore); Assert.Equal(JournalFileState.Applied, review.Files.Single().State);
            await store.RevertAsync(receipt, [path], token);
            review = await store.ReviewAsync(receipt, [path], token); Assert.True(review.CanReplay);
            var replayed = await store.ReplayAsync(receipt, [path], token); Assert.NotEqual(receipt.Id, replayed.Id); Assert.Equal(after, await File.ReadAllBytesAsync(path, token));
            await store.RevertAsync(replayed, [path], token); Assert.Equal(original, await File.ReadAllBytesAsync(path, token));
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task ReviewReportsConflictsAndRedoRefusesEncodingOnlyExternalChanges()
    {
        var token = TestContext.Current.CancellationToken; var directory = TempDirectory();
        try
        {
            var path = Path.Combine(directory, "File.cs"); await File.WriteAllTextAsync(path, "original", token);
            var store = new JournaledFileTransaction(Path.Combine(directory, "journals"));
            var receipt = await store.ApplyAsync([new(path, "original", "changed")], token);
            await File.WriteAllTextAsync(path, "external", token);
            var review = await store.ReviewAsync(receipt, [path], token); Assert.False(review.CanRestore); Assert.Equal(JournalFileState.Conflict, review.Files.Single().State);
            await File.WriteAllTextAsync(path, "changed", token); await store.RevertAsync(receipt, [path], token);
            await File.WriteAllTextAsync(path, "original", new System.Text.UTF8Encoding(true), token);
            await Assert.ThrowsAsync<IOException>(() => store.ReplayAsync(receipt, [path], token));
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task CorruptedJournalsAreListedAsInvalidWithoutHidingHealthyEntries()
    {
        var token = TestContext.Current.CancellationToken; var directory = TempDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json"), "not json", token);
            var store = new JournaledFileTransaction(directory); var result = Assert.Single(await store.ListAsync(token));
            Assert.Equal("Invalid", result.State); Assert.NotNull(result.Error);
        }
        finally { Directory.Delete(directory, true); }
    }
    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "pd-journal-review-" + Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsMacOS() && path.StartsWith("/var/", StringComparison.Ordinal)) path = "/private" + path;
        Directory.CreateDirectory(path); return path;
    }
}
