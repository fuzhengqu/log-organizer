namespace LogOrganizer.Core.Tests;

[TestClass]
public sealed class InboxWatcherTests
{
    private string _inbox = string.Empty;

    [TestInitialize]
    public void Setup() => _inbox = TestPaths.NewDirectory();

    [TestCleanup]
    public void Cleanup() => TestPaths.DeleteQuietly(_inbox);

    private InboxWatcher CreateWatcher() => new(_inbox);

    [TestMethod]
    public void AddPendingTracksAnExistingExternalPath()
    {
        using var watcher = CreateWatcher();
        var externalRoot = TestPaths.NewDirectory();
        var file = Path.Combine(externalRoot, "outside.txt");
        File.WriteAllText(file, "x");

        try
        {
            watcher.AddPending(file);

            CollectionAssert.Contains(watcher.SnapshotPending().ToArray(), Path.GetFullPath(file));
        }
        finally
        {
            TestPaths.DeleteQuietly(externalRoot);
        }
    }

    [TestMethod]
    public void AddPendingIgnoresMissingPaths()
    {
        using var watcher = CreateWatcher();
        var missingPath = Path.Combine(_inbox, "missing.txt");

        watcher.AddPending(missingPath);

        CollectionAssert.DoesNotContain(watcher.SnapshotPending().ToArray(), Path.GetFullPath(missingPath));
    }

    [TestMethod]
    public void FilesArePendingImmediatelyWithoutAnyDelay()
    {
        using var watcher = CreateWatcher();
        watcher.Start();

        var file = Path.Combine(_inbox, "650239.jpg");
        File.WriteAllText(file, "x");
        watcher.Rescan();

        CollectionAssert.Contains(watcher.SnapshotPending().ToArray(), file);
    }

    [TestMethod]
    public void FolderIsATopLevelItem()
    {
        using var watcher = CreateWatcher();
        watcher.Start();

        var folder = Path.Combine(_inbox, "screenshots");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "nested.png"), "x");
        watcher.Rescan();

        var pending = watcher.SnapshotPending().ToArray();
        CollectionAssert.AreEquivalent(new[] { folder }, pending);
    }

    [TestMethod]
    public void PartialDownloadsAreIgnored()
    {
        using var watcher = CreateWatcher();
        watcher.Start();

        File.WriteAllText(Path.Combine(_inbox, "big.zip.crdownload"), "x");
        File.WriteAllText(Path.Combine(_inbox, "setup.part"), "x");
        File.WriteAllText(Path.Combine(_inbox, "~$draft.docx"), "x");
        File.WriteAllText(Path.Combine(_inbox, ".hidden"), "x");
        watcher.Rescan();

        Assert.AreEqual(0, watcher.SnapshotPending().Count);
    }

    [TestMethod]
    public void PendingChangedFiresOnceForABatchOfExistingEntries()
    {
        File.WriteAllText(Path.Combine(_inbox, "a.log"), "x");
        File.WriteAllText(Path.Combine(_inbox, "b.log"), "y");

        using var watcher = CreateWatcher();
        var raised = 0;
        watcher.PendingChanged += (_, _) => raised++;

        watcher.Start();

        Assert.AreEqual(1, raised, "the initial rescan reports the whole batch once");
        Assert.AreEqual(2, watcher.SnapshotPending().Count);
    }

    [TestMethod]
    public void WatcherReportsAFileCreationWithoutWaiting()
    {
        using var watcher = CreateWatcher();
        using var raised = new ManualResetEventSlim();
        watcher.PendingChanged += (_, _) => raised.Set();
        watcher.Start();

        var file = Path.Combine(_inbox, "live.txt");
        File.WriteAllText(file, "x");

        Assert.IsTrue(raised.Wait(TimeSpan.FromSeconds(10)), "a created file must be reported right away");
        CollectionAssert.Contains(watcher.SnapshotPending().ToArray(), file);
    }

    [TestMethod]
    public void RemovePendingForgetsArchivedItems()
    {
        using var watcher = CreateWatcher();
        watcher.Start();

        var file = Path.Combine(_inbox, "1.txt");
        File.WriteAllText(file, "x");
        watcher.Rescan();

        watcher.RemovePending(new[] { file });

        Assert.AreEqual(0, watcher.SnapshotPending().Count);
    }

    [TestMethod]
    public void StartPicksUpExistingEntries()
    {
        var file = Path.Combine(_inbox, "already-there.png");
        File.WriteAllText(file, "x");

        using var watcher = CreateWatcher();
        watcher.Start();

        CollectionAssert.Contains(watcher.SnapshotPending().ToArray(), file);
    }

    [TestMethod]
    public void Constructor_CreatesAMissingInbox()
    {
        var missing = Path.Combine(_inbox, "first-run", "inbox");
        Assert.IsFalse(Directory.Exists(missing));

        using (var watcher = new InboxWatcher(missing))
        {
            Assert.IsTrue(Directory.Exists(missing), "the inbox is created on first run");
            watcher.Start();
            Assert.IsTrue(watcher.IsRunning);
        }
    }
}
