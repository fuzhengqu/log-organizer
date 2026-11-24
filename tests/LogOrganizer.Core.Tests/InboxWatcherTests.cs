namespace LogOrganizer.Core.Tests;

[TestClass]
public sealed class InboxWatcherTests
{
    private string _inbox = string.Empty;
    private DateTimeOffset _now;

    [TestInitialize]
    public void Setup()
    {
        _inbox = TestPaths.NewDirectory();
        _now = new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);
    }

    [TestCleanup]
    public void Cleanup()
    {
        TestPaths.DeleteQuietly(_inbox);
    }

    private InboxWatcher CreateWatcher() =>
        new(_inbox, TimeSpan.FromSeconds(10), () => _now);

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
    public void MarkActivityRaisesActivityRecorded()
    {
        using var watcher = CreateWatcher();
        var activityCount = 0;
        watcher.ActivityRecorded += (_, _) => activityCount++;

        watcher.MarkActivity();

        Assert.AreEqual(1, activityCount);
    }

    [TestMethod]
    public void BatchIsEmittedOnceAfterQuietPeriod()
    {
        using var watcher = CreateWatcher();
        watcher.Start();

        var file = Path.Combine(_inbox, "650239.jpg");
        File.WriteAllText(file, "x");
        watcher.Rescan();
        Thread.Sleep(250); // let queued watcher callbacks record their change first

        Assert.IsFalse(watcher.TryTakeReadyBatch(out _));

        _now = _now.AddSeconds(9);
        Assert.IsFalse(watcher.TryTakeReadyBatch(out _));

        _now = _now.AddSeconds(1);
        Assert.IsTrue(watcher.TryTakeReadyBatch(out var batch));
        CollectionAssert.Contains(batch.ToArray(), file);

        Assert.IsFalse(watcher.TryTakeReadyBatch(out _), "a batch must not be emitted twice");
        CollectionAssert.Contains(watcher.SnapshotPending().ToArray(), file, "pending items persist until removed");
    }

    [TestMethod]
    public void NewChangeRestartsTheQuietWindow()
    {
        using var watcher = CreateWatcher();
        watcher.Start();

        var first = Path.Combine(_inbox, "a.log");
        File.WriteAllText(first, "x");
        watcher.Rescan();

        _now = _now.AddSeconds(5);

        var second = Path.Combine(_inbox, "b.log");
        File.WriteAllText(second, "y");
        watcher.Rescan();
        Thread.Sleep(250);

        _now = _now.AddSeconds(9);
        Assert.IsFalse(watcher.TryTakeReadyBatch(out _), "the later change restarts the quiet period");

        _now = _now.AddSeconds(1);
        Assert.IsTrue(watcher.TryTakeReadyBatch(out var batch));
        CollectionAssert.AreEquivalent(new[] { first, second }, batch.ToArray());
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
        Thread.Sleep(250);

        _now = _now.AddSeconds(10);
        Assert.IsTrue(watcher.TryTakeReadyBatch(out var batch));
        CollectionAssert.AreEquivalent(new[] { folder }, batch.ToArray());
    }

    [TestMethod]
    public void PartialDownloadsAreIgnored()
    {
        using var watcher = CreateWatcher();
        watcher.Start();

        File.WriteAllText(Path.Combine(_inbox, "big.zip.crdownload"), "x");
        File.WriteAllText(Path.Combine(_inbox, "~$draft.docx"), "x");
        File.WriteAllText(Path.Combine(_inbox, ".hidden"), "x");
        watcher.Rescan();

        _now = _now.AddSeconds(30);
        Assert.IsFalse(watcher.TryTakeReadyBatch(out _));
        Assert.AreEqual(0, watcher.SnapshotPending().Count);
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
        _now = _now.AddSeconds(30);

        Assert.IsFalse(watcher.TryTakeReadyBatch(out _));
    }

    [TestMethod]
    public void PausedWatcherDoesNotEmitBatches()
    {
        using var watcher = CreateWatcher();
        watcher.Start();

        File.WriteAllText(Path.Combine(_inbox, "1.txt"), "x");
        watcher.Rescan();
        watcher.Pause();

        _now = _now.AddSeconds(30);
        Assert.IsFalse(watcher.TryTakeReadyBatch(out _));
        Assert.IsTrue(watcher.IsPaused);

        watcher.Resume();
        _now = _now.AddSeconds(11);
        Assert.IsTrue(watcher.TryTakeReadyBatch(out _));
    }

    [TestMethod]
    public void QuietPeriodCanBeSuspendedWhilePinnedAndResumedWhenUnpinned()
    {
        using var watcher = CreateWatcher();
        watcher.Start();
        var file = Path.Combine(_inbox, "manual.txt");
        File.WriteAllText(file, "x");
        watcher.Rescan();

        _now = _now.AddSeconds(7);
        watcher.ResetQuietPeriod();
        _now = _now.AddMinutes(2);
        Assert.IsFalse(watcher.TryTakeReadyBatch(out _));

        watcher.ResumeQuietPeriod();
        _now = _now.AddSeconds(2);
        Assert.IsFalse(watcher.TryTakeReadyBatch(out _));
        _now = _now.AddSeconds(1);
        Assert.IsTrue(watcher.TryTakeReadyBatch(out var batch));
        CollectionAssert.Contains(batch.ToArray(), file);
    }

    [TestMethod]
    public void ActivityWhileQuietPeriodIsSuspendedRestartsDelayAfterResume()
    {
        using var watcher = CreateWatcher();
        watcher.Start();
        File.WriteAllText(Path.Combine(_inbox, "first.txt"), "x");
        watcher.Rescan();

        _now = _now.AddSeconds(2);
        watcher.ResetQuietPeriod();
        _now = _now.AddMinutes(1);
        File.WriteAllText(Path.Combine(_inbox, "second.txt"), "y");
        watcher.Rescan();
        watcher.ResumeQuietPeriod();

        _now = _now.AddSeconds(9);
        Assert.IsFalse(watcher.TryTakeReadyBatch(out _));
        _now = _now.AddSeconds(1);
        Assert.IsTrue(watcher.TryTakeReadyBatch(out var batch));
        Assert.AreEqual(2, batch.Count);
    }

    [TestMethod]
    public void QuietPeriodSuspendedAtExpiryResumesImmediately()
    {
        using var watcher = CreateWatcher();
        watcher.Start();
        var file = Path.Combine(_inbox, "expired.txt");
        File.WriteAllText(file, "x");
        watcher.Rescan();

        _now = _now.AddSeconds(10);
        watcher.ResetQuietPeriod();
        watcher.ResumeQuietPeriod();

        Assert.IsTrue(watcher.TryTakeReadyBatch(out var batch));
        CollectionAssert.Contains(batch.ToArray(), file);
    }

    [TestMethod]
    public void ActivityConcurrentWithQuietPeriodResumeRestartsFullDelay()
    {
        using var resumeClockEntered = new ManualResetEventSlim();
        using var releaseResumeClock = new ManualResetEventSlim();
        var blockNextClock = 0;
        DateTimeOffset Clock()
        {
            if (Interlocked.Exchange(ref blockNextClock, 0) == 1)
            {
                resumeClockEntered.Set();
                if (!releaseResumeClock.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException("ResumeQuietPeriod did not release the test clock.");
                }
            }

            return _now;
        }

        File.WriteAllText(Path.Combine(_inbox, "pending.txt"), "x");
        using var watcher = new InboxWatcher(_inbox, TimeSpan.FromSeconds(10), Clock);
        watcher.Start();
        _now = _now.AddSeconds(2);
        watcher.ResetQuietPeriod();

        using var activityRecorded = new ManualResetEventSlim();
        watcher.ActivityRecorded += (_, _) => activityRecorded.Set();
        Interlocked.Exchange(ref blockNextClock, 1);

        var resumeTask = Task.Run(watcher.ResumeQuietPeriod);
        Assert.IsTrue(resumeClockEntered.Wait(TimeSpan.FromSeconds(5)), "resume should reach the blocked clock");

        var activityTask = Task.Run(watcher.MarkActivity);
        try
        {
            Assert.IsTrue(activityRecorded.Wait(TimeSpan.FromSeconds(5)), "concurrent activity should be recorded");
            activityTask.Wait(TimeSpan.FromMilliseconds(100));
        }
        finally
        {
            releaseResumeClock.Set();
        }

        Task.WaitAll(resumeTask, activityTask);

        Assert.IsFalse(watcher.TryTakeReadyBatch(out _), "activity concurrent with unpinning must restart the full quiet delay");
        _now = _now.AddSeconds(9);
        Assert.IsFalse(watcher.TryTakeReadyBatch(out _));
        _now = _now.AddSeconds(1);
        Assert.IsTrue(watcher.TryTakeReadyBatch(out _));
    }

    [TestMethod]
    public void PinnedStartKeepsWatchingAndResumesQuietPeriodAfterNewActivity()
    {
        var existing = Path.Combine(_inbox, "existing.txt");
        File.WriteAllText(existing, "x");

        using var watcher = CreateWatcher();
        watcher.Start(suspendQuietPeriod: true);

        Assert.IsTrue(watcher.IsRunning);
        CollectionAssert.Contains(watcher.SnapshotPending().ToArray(), existing);

        _now = _now.AddSeconds(7);
        var added = Path.Combine(_inbox, "added.txt");
        File.WriteAllText(added, "x");
        watcher.Rescan();
        Thread.Sleep(250);

        Assert.IsTrue(watcher.IsRunning);
        CollectionAssert.Contains(watcher.SnapshotPending().ToArray(), existing);
        CollectionAssert.Contains(watcher.SnapshotPending().ToArray(), added);

        _now = _now.AddMinutes(2);
        Assert.IsFalse(watcher.TryTakeReadyBatch(out _));

        watcher.ResumeQuietPeriod();
        _now = _now.AddSeconds(9);
        Assert.IsFalse(watcher.TryTakeReadyBatch(out _));
        _now = _now.AddSeconds(1);
        Assert.IsTrue(watcher.TryTakeReadyBatch(out var batch));
        CollectionAssert.AreEquivalent(new[] { existing, added }, batch.ToArray());
    }

    [TestMethod]
    public void StartPicksUpExistingEntries()
    {
        var file = Path.Combine(_inbox, "already-there.png");
        File.WriteAllText(file, "x");

        using var watcher = CreateWatcher();
        watcher.Start();
        _now = _now.AddSeconds(10);

        Assert.IsTrue(watcher.TryTakeReadyBatch(out var batch));
        CollectionAssert.Contains(batch.ToArray(), file);
    }

    [TestMethod]
    public void Constructor_CreatesAMissingInbox()
    {
        var missing = Path.Combine(_inbox, "first-run", "inbox");
        Assert.IsFalse(Directory.Exists(missing));

        using (var watcher = new InboxWatcher(missing, TimeSpan.FromSeconds(10), () => _now))
        {
            Assert.IsTrue(Directory.Exists(missing), "the inbox is created on first run");
            watcher.Start();
            Assert.IsTrue(watcher.IsRunning);
        }
    }
}