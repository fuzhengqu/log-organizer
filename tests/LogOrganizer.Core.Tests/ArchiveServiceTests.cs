namespace LogOrganizer.Core.Tests;

[TestClass]
public sealed class ArchiveServiceTests
{
    private string _root = string.Empty;
    private string _inbox = string.Empty;
    private string _target = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _root = TestPaths.NewDirectory();
        _inbox = Path.Combine(_root, "inbox");
        _target = Path.Combine(_root, "target");
        Directory.CreateDirectory(_inbox);
        Directory.CreateDirectory(_target);
    }

    [TestCleanup]
    public void Cleanup() => TestPaths.DeleteQuietly(_root);

    private static ConflictResolver AlwaysSkip => _ => ConflictDecision.Skip;

    /// <summary>Creates a directory junction, the only reparse point Windows allows without SeCreateSymbolicLinkPrivilege.</summary>
    private static bool TryCreateJunction(string link, string target, out string error)
    {
        var start = new System.Diagnostics.ProcessStartInfo(
            "cmd.exe",
            $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = System.Diagnostics.Process.Start(start);
        if (process is null)
        {
            error = "无法启动 cmd.exe";
            return false;
        }

        error = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit(15_000);
        return process.HasExited && process.ExitCode == 0;
    }

    private string AddInboxFile(string name, string content = "data")
    {
        var path = Path.Combine(_inbox, name);
        File.WriteAllText(path, content);
        return path;
    }

    [TestMethod]
    public void Archive_CreatesTicketFolderAndMovesFiles()
    {
        var first = AddInboxFile("1.jpg", "one");
        var second = AddInboxFile("2.jpg", "two");

        var result = ArchiveService.Archive(_inbox, _target, "650239", new[] { first, second }, AlwaysSkip);

        Assert.IsTrue(result.Succeeded, result.Error);
        Assert.AreEqual(2, result.MovedCount);
        var ticketFolder = Path.Combine(_target, "650239");
        Assert.AreEqual(ticketFolder, result.TicketFolder);
        Assert.IsTrue(File.Exists(Path.Combine(ticketFolder, "1.jpg")));
        Assert.IsTrue(File.Exists(Path.Combine(ticketFolder, "2.jpg")));
        Assert.IsFalse(File.Exists(first));
        Assert.IsFalse(File.Exists(second));
    }

    [TestMethod]
    public void Archive_MergesIntoExistingTicketFolder()
    {
        var existingFolder = Path.Combine(_target, "650239");
        Directory.CreateDirectory(existingFolder);
        File.WriteAllText(Path.Combine(existingFolder, "old.txt"), "old");
        var source = AddInboxFile("new.txt", "new");

        var result = ArchiveService.Archive(_inbox, _target, "650239", new[] { source }, AlwaysSkip);

        Assert.IsTrue(result.Succeeded, result.Error);
        Assert.IsTrue(File.Exists(Path.Combine(existingFolder, "old.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(existingFolder, "new.txt")));
    }

    [TestMethod]
    public void Archive_MergesNestedFolders_AndAsksOnlyAboutSameNameFiles()
    {
        var ticketFolder = Path.Combine(_target, "650239");
        var existingNested = Path.Combine(ticketFolder, "logs");
        Directory.CreateDirectory(existingNested);
        File.WriteAllText(Path.Combine(existingNested, "keep.txt"), "keep");

        var incoming = Path.Combine(_inbox, "logs");
        Directory.CreateDirectory(incoming);
        File.WriteAllText(Path.Combine(incoming, "extra.txt"), "extra");

        var requests = new List<ConflictRequest>();
        ConflictResolver resolver = request =>
        {
            requests.Add(request);
            return ConflictDecision.Skip;
        };

        var result = ArchiveService.Archive(_inbox, _target, "650239", new[] { incoming }, resolver);

        Assert.IsTrue(result.Succeeded, result.Error);
        Assert.IsTrue(File.Exists(Path.Combine(existingNested, "keep.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(existingNested, "extra.txt")));
        Assert.AreEqual(0, requests.Count, "merging folders is not a conflict by itself");
        Assert.IsFalse(Directory.Exists(incoming));
    }

    [TestMethod]
    public void Archive_SkipKeepsTheSourceFile()
    {
        var ticketFolder = Path.Combine(_target, "650239");
        Directory.CreateDirectory(ticketFolder);
        File.WriteAllText(Path.Combine(ticketFolder, "same.txt"), "existing");
        var source = AddInboxFile("same.txt", "incoming");

        var result = ArchiveService.Archive(_inbox, _target, "650239", new[] { source }, AlwaysSkip);

        Assert.IsTrue(result.Succeeded, result.Error);
        Assert.AreEqual(ArchiveItemStatus.Skipped, result.Items[0].Status);
        Assert.IsTrue(File.Exists(source), "a skipped source must stay in the inbox");
        Assert.AreEqual("existing", File.ReadAllText(Path.Combine(ticketFolder, "same.txt")));
    }

    [TestMethod]
    public void Archive_OverwriteReplacesTheExistingFile()
    {
        var ticketFolder = Path.Combine(_target, "650239");
        Directory.CreateDirectory(ticketFolder);
        File.WriteAllText(Path.Combine(ticketFolder, "same.txt"), "existing");
        var source = AddInboxFile("same.txt", "incoming");

        var result = ArchiveService.Archive(_inbox, _target, "650239", new[] { source }, _ => ConflictDecision.Overwrite);

        Assert.IsTrue(result.Succeeded, result.Error);
        Assert.AreEqual(ArchiveItemStatus.Overwritten, result.Items[0].Status);
        Assert.AreEqual("incoming", File.ReadAllText(Path.Combine(ticketFolder, "same.txt")));
        Assert.IsFalse(File.Exists(source));
    }

    [TestMethod]
    public void Archive_RenameStoresUnderTheChosenName()
    {
        var ticketFolder = Path.Combine(_target, "650239");
        Directory.CreateDirectory(ticketFolder);
        File.WriteAllText(Path.Combine(ticketFolder, "same.txt"), "existing");
        var source = AddInboxFile("same.txt", "incoming");

        ConflictDecision? decision = null;
        var result = ArchiveService.Archive(
            _inbox,
            _target,
            "650239",
            new[] { source },
            request =>
            {
                decision = ConflictDecision.Rename("same (2).txt");
                return decision.Value;
            });

        Assert.IsTrue(result.Succeeded, result.Error);
        Assert.AreEqual(ArchiveItemStatus.Renamed, result.Items[0].Status);
        Assert.IsTrue(File.Exists(Path.Combine(ticketFolder, "same (2).txt")));
        Assert.AreEqual("existing", File.ReadAllText(Path.Combine(ticketFolder, "same.txt")));
        Assert.IsFalse(File.Exists(source));
    }

    [TestMethod]
    public void Archive_SuggestsANonConflictingName()
    {
        var ticketFolder = Path.Combine(_target, "650239");
        Directory.CreateDirectory(ticketFolder);
        File.WriteAllText(Path.Combine(ticketFolder, "same.txt"), "existing");
        File.WriteAllText(Path.Combine(ticketFolder, "same (2).txt"), "existing 2");
        var source = AddInboxFile("same.txt", "incoming");

        string? suggestion = null;
        ArchiveService.Archive(
            _inbox,
            _target,
            "650239",
            new[] { source },
            request =>
            {
                suggestion = request.SuggestedName;
                return ConflictDecision.Skip;
            });

        Assert.AreEqual("same (3).txt", suggestion);
    }

    [TestMethod]
    public void Archive_RejectsInvalidTicketNumbersWithoutTouchingTheInbox()
    {
        var source = AddInboxFile("1.jpg");

        foreach (var ticket in new[] { "", "  ", "12a", "abc", "١٢٣", "1 2" })
        {
            var result = ArchiveService.Archive(_inbox, _target, ticket, new[] { source }, AlwaysSkip);

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(File.Exists(source), $"source must stay for ticket '{ticket}'");
            Assert.AreEqual(0, Directory.GetFileSystemEntries(_target).Length);
        }
    }

    [TestMethod]
    public void ArchiveDropped_MovesExternalFilesUsingRequestedTargetName()
    {
        var outside = Path.Combine(_root, "original report.txt");
        File.WriteAllText(outside, "data");

        var result = ArchiveService.ArchiveDropped(
            _target,
            "650239",
            new[] { new ArchiveDropItem(outside, "customer-notes.txt") },
            AlwaysSkip);

        var destination = Path.Combine(_target, "650239", "customer-notes.txt");
        Assert.IsTrue(result.Succeeded, result.Error);
        Assert.AreEqual(1, result.MovedCount);
        Assert.IsTrue(File.Exists(destination));
        Assert.AreEqual("data", File.ReadAllText(destination));
        Assert.IsFalse(File.Exists(outside), "successful drop archiving moves the original");
    }

    [TestMethod]
    public void ArchiveDropped_RejectsInvalidTargetNameAndKeepsSource()
    {
        var outside = Path.Combine(_root, "original.txt");
        File.WriteAllText(outside, "data");

        var result = ArchiveService.ArchiveDropped(
            _target,
            "650239",
            new[] { new ArchiveDropItem(outside, "..\\escape.txt") },
            AlwaysSkip);

        Assert.AreEqual(ArchiveItemStatus.Failed, result.Items[0].Status);
        Assert.IsTrue(File.Exists(outside));
        Assert.AreEqual(0, Directory.GetFileSystemEntries(_target).Length);
    }

    [TestMethod]
    public void ArchiveDropped_DeduplicatesRepeatedSourcePaths()
    {
        var outside = Path.Combine(_root, "original.txt");
        File.WriteAllText(outside, "data");

        var result = ArchiveService.ArchiveDropped(
            _target,
            "650239",
            new[]
            {
                new ArchiveDropItem(outside, "renamed.txt"),
                new ArchiveDropItem(outside, "renamed-again.txt"),
            },
            AlwaysSkip);

        Assert.AreEqual(1, result.Items.Count);
        Assert.IsTrue(File.Exists(Path.Combine(_target, "650239", "renamed.txt")));
        Assert.IsFalse(File.Exists(outside));
    }

    [TestMethod]
    public void ArchiveDropped_InvalidItemPreventsPartialBatchMovement()
    {
        var goodSource = Path.Combine(_root, "good.txt");
        var invalidSource = Path.Combine(_root, "invalid.txt");
        File.WriteAllText(goodSource, "good");
        File.WriteAllText(invalidSource, "invalid");

        var result = ArchiveService.ArchiveDropped(
            _target,
            "650239",
            new[]
            {
                new ArchiveDropItem(goodSource, "good.txt"),
                new ArchiveDropItem(invalidSource, "..\\escape.txt"),
            },
            AlwaysSkip);

        Assert.AreEqual(0, result.MovedCount);
        Assert.AreEqual(1, result.FailedCount);
        Assert.IsTrue(File.Exists(goodSource));
        Assert.IsTrue(File.Exists(invalidSource));
        Assert.AreEqual(0, Directory.GetFileSystemEntries(_target).Length);
    }

    [TestMethod]
    public void ArchiveDropped_RejectsSourcesAlreadyInsideTargetRoot()
    {
        var source = Path.Combine(_target, "already-archived.txt");
        File.WriteAllText(source, "keep");

        var result = ArchiveService.ArchiveDropped(
            _target,
            "650239",
            new[] { new ArchiveDropItem(source, "renamed.txt") },
            AlwaysSkip);

        Assert.AreEqual(ArchiveItemStatus.Failed, result.Items[0].Status);
        Assert.IsTrue(File.Exists(source));
        Assert.AreEqual("keep", File.ReadAllText(source));
    }

    [TestMethod]
    public void ArchiveDropped_RejectsDirectoryContainingTargetRoot()
    {
        var source = Path.Combine(_root, "source-folder");
        Directory.CreateDirectory(Path.Combine(source, "archive"));
        Directory.CreateDirectory(Path.Combine(source, "inputs"));
        File.WriteAllText(Path.Combine(source, "inputs", "file.txt"), "keep");
        var targetInsideSource = Path.Combine(source, "archive");

        var result = ArchiveService.ArchiveDropped(
            targetInsideSource,
            "650239",
            new[] { new ArchiveDropItem(source, "renamed-folder") },
            AlwaysSkip);

        Assert.AreEqual(ArchiveItemStatus.Failed, result.Items[0].Status);
        Assert.IsTrue(File.Exists(Path.Combine(source, "inputs", "file.txt")));
        Assert.IsFalse(Directory.Exists(Path.Combine(targetInsideSource, "650239")));
    }

    [TestMethod]
    public void Archive_RejectsUnsetOrMissingTarget()
    {
        var source = AddInboxFile("1.jpg");

        var unset = ArchiveService.Archive(_inbox, string.Empty, "650239", new[] { source }, AlwaysSkip);
        Assert.IsFalse(unset.Succeeded);

        var missing = ArchiveService.Archive(_inbox, Path.Combine(_root, "nope"), "650239", new[] { source }, AlwaysSkip);
        Assert.IsFalse(missing.Succeeded);

        Assert.IsTrue(File.Exists(source));
    }

    [TestMethod]
    public void Archive_ReportsItemsOutsideTheInbox()
    {
        var outside = Path.Combine(_root, "outside.txt");
        File.WriteAllText(outside, "x");

        var result = ArchiveService.Archive(_inbox, _target, "650239", new[] { outside }, AlwaysSkip);

        Assert.AreEqual(1, result.Items.Count);
        Assert.AreEqual(ArchiveItemStatus.Failed, result.Items[0].Status);
        Assert.IsTrue(File.Exists(outside));
    }

    [TestMethod]
    public void Archive_KeepsTheSourceWhenTheTransferFails()
    {
        var source = AddInboxFile("locked.txt", "locked");
        var result = ArchiveService.Archive(_inbox, _target, "650239", new[] { source }, AlwaysSkip, CancellationToken.None);
        Assert.IsTrue(result.Succeeded, result.Error);

        // Move it back to the inbox behind a lock so the transfer fails again.
        var restored = Path.Combine(_inbox, "locked2.txt");
        File.Move(Path.Combine(_target, "650239", "locked.txt"), restored);

        using (new FileStream(restored, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var failing = ArchiveService.Archive(_inbox, _target, "650239", new[] { restored }, AlwaysSkip);

            Assert.AreEqual(ArchiveItemStatus.Failed, failing.Items[0].Status);
            Assert.IsTrue(File.Exists(restored), "a failed transfer must keep the source in the inbox");
        }
    }

    [TestMethod]
    public void Archive_RefusesToFollowALinkInsideAFolderBeingMerged()
    {
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "secret");

        var folder = Path.Combine(_inbox, "bundle");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "keep.txt"), "keep");

        var link = Path.Combine(folder, "link");
        // mklink /J needs no elevated token, unlike Directory.CreateSymbolicLink.
        if (!TryCreateJunction(link, outside, out var linkError))
        {
            Assert.Inconclusive($"无法创建联接点，跳过该用例：{linkError}");
            return;
        }

        // Merge path: the destination folder already exists, so the transfer walks the children.
        var ticket = Path.Combine(_target, "650239");
        Directory.CreateDirectory(Path.Combine(ticket, "bundle"));

        var result = ArchiveService.Archive(_inbox, _target, "650239", new[] { folder }, AlwaysSkip);

        Assert.AreEqual(ArchiveItemStatus.Failed, result.Items[0].Status);
        Assert.IsTrue(result.Items[0].Error!.Contains("符号链接", StringComparison.Ordinal), result.Items[0].Error);
        Assert.IsTrue(File.Exists(Path.Combine(outside, "secret.txt")), "content behind the link must not be touched");
        Assert.IsFalse(File.Exists(Path.Combine(ticket, "bundle", "link", "secret.txt")), "nothing may be copied through the link");
    }

    [TestMethod]
    public void Archive_RemovesTheTicketFolderWhenNothingWasStored()
    {
        var first = AddInboxFile("a.txt");
        AddInboxFile("b.txt");

        // Archive one item, then remove it from the folder so the next archive finds an empty target.
        ArchiveService.Archive(_inbox, _target, "777", new[] { first }, AlwaysSkip);
        Directory.Delete(Path.Combine(_target, "777"), true);

        var result = ArchiveService.Archive(_inbox, _target, "888", Array.Empty<string>(), AlwaysSkip);

        Assert.IsTrue(result.Succeeded, result.Error);
        Assert.IsFalse(Directory.Exists(Path.Combine(_target, "888")), "an empty ticket folder is not kept");
    }
}