namespace LogOrganizer.Core;

/// <summary>
/// Moves inbox items into a numeric ticket folder. It never deletes source items silently:
/// a move only completes once the destination copy is verified, and collisions require an
/// explicit decision from the supplied resolver.
/// </summary>
public static class ArchiveService
{
    public static Task<ArchiveResult> ArchiveAsync(
        string inboxPath,
        string targetRoot,
        string ticketNumber,
        IReadOnlyList<string> items,
        ConflictResolver conflictResolver,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(conflictResolver);

        return Task.Run(
            () => Archive(inboxPath, targetRoot, ticketNumber, items, conflictResolver, cancellationToken),
            cancellationToken);
    }

    public static ArchiveResult Archive(
        string inboxPath,
        string targetRoot,
        string ticketNumber,
        IReadOnlyList<string> items,
        ConflictResolver conflictResolver,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(conflictResolver);

        if (!TicketNumber.TryParse(ticketNumber, out var ticket))
        {
            return ArchiveResult.Failure("问题单号只能包含数字。");
        }

        if (string.IsNullOrWhiteSpace(inboxPath))
        {
            return ArchiveResult.Failure("尚未设置中转目录。");
        }

        string inbox;
        try
        {
            inbox = Path.GetFullPath(inboxPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return ArchiveResult.Failure($"中转目录路径无效：{ex.Message}");
        }

        if (!Directory.Exists(inbox))
        {
            return ArchiveResult.Failure($"中转目录不存在：{inbox}");
        }

        if (PathSafety.HasReparsePointInChain(inbox))
        {
            return ArchiveResult.Failure("中转目录路径包含符号链接或联接点，已拒绝归档。");
        }

        if (string.IsNullOrWhiteSpace(targetRoot))
        {
            return ArchiveResult.Failure("尚未设置目标目录，请先在设置中配置。");
        }

        string target;
        try
        {
            target = Path.GetFullPath(targetRoot);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return ArchiveResult.Failure($"目标目录路径无效：{ex.Message}");
        }

        if (!Directory.Exists(target))
        {
            return ArchiveResult.Failure($"目标目录不存在：{target}");
        }

        if (PathSafety.HasReparsePointInChain(target))
        {
            return ArchiveResult.Failure("目标目录路径包含符号链接或联接点，已拒绝归档。");
        }

        var ticketFolder = Path.Combine(target, ticket);

        var createdFolder = false;
        if (!Directory.Exists(ticketFolder))
        {
            try
            {
                Directory.CreateDirectory(ticketFolder);
                createdFolder = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return ArchiveResult.Failure($"创建单号文件夹失败：{ex.Message}");
            }
        }

        var results = new List<ArchiveItemResult>();
        string? cancellation = null;

        foreach (var item in items)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancellation = "归档已取消。";
                break;
            }

            results.Add(ArchiveItem(inbox, ticketFolder, item, conflictResolver, cancellationToken));
        }

        if (createdFolder)
        {
            RemoveIfEmpty(ticketFolder);
        }

        return new ArchiveResult(ticketFolder, results, cancellation);
    }

    public static Task<ArchiveResult> ArchiveDroppedAsync(
        string targetRoot,
        string ticketNumber,
        IReadOnlyList<ArchiveDropItem> items,
        ConflictResolver conflictResolver,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(conflictResolver);

        return Task.Run(
            () => ArchiveDropped(targetRoot, ticketNumber, items, conflictResolver, cancellationToken),
            cancellationToken);
    }

    public static ArchiveResult ArchiveDropped(
        string targetRoot,
        string ticketNumber,
        IReadOnlyList<ArchiveDropItem> items,
        ConflictResolver conflictResolver,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(conflictResolver);

        if (!TicketNumber.TryParse(ticketNumber, out var ticket))
        {
            return ArchiveResult.Failure("问题单号只能包含数字。");
        }

        if (string.IsNullOrWhiteSpace(targetRoot))
        {
            return ArchiveResult.Failure("尚未设置目标目录，请先在设置中配置。");
        }

        string target;
        try
        {
            target = Path.GetFullPath(targetRoot);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return ArchiveResult.Failure($"目标目录路径无效：{ex.Message}");
        }

        if (!Directory.Exists(target))
        {
            return ArchiveResult.Failure($"目标目录不存在：{target}");
        }

        if (PathSafety.HasReparsePointInChain(target))
        {
            return ArchiveResult.Failure("目标目录路径包含符号链接或联接点，已拒绝归档。");
        }

        var validationResults = new List<ArchiveItemResult>();
        var validItems = new List<ArchiveDropItem>();
        var seenSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            string source;
            try
            {
                source = Path.GetFullPath(item.SourcePath);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                validationResults.Add(new ArchiveItemResult(item.SourcePath, ArchiveItemStatus.Failed, null, $"路径无效：{ex.Message}"));
                continue;
            }

            if (!seenSources.Add(source))
            {
                continue;
            }

            if (!PathSafety.IsSimpleName(item.TargetName))
            {
                validationResults.Add(new ArchiveItemResult(source, ArchiveItemStatus.Failed, null, "目标名称无效。"));
                continue;
            }

            if (!File.Exists(source) && !Directory.Exists(source))
            {
                validationResults.Add(new ArchiveItemResult(source, ArchiveItemStatus.Failed, null, "源项目已不存在。"));
                continue;
            }

            if (IsSameOrDescendant(source, target) || IsSameOrDescendant(target, source))
            {
                validationResults.Add(new ArchiveItemResult(source, ArchiveItemStatus.Failed, null, "不能将目标目录或其子目录作为待归档来源。"));
                continue;
            }

            try
            {
                if (PathSafety.HasReparsePointInChain(source) || ContainsNestedReparsePoint(source))
                {
                    validationResults.Add(new ArchiveItemResult(source, ArchiveItemStatus.Failed, null, "源项目路径包含符号链接或联接点，已拒绝处理。"));
                    continue;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                validationResults.Add(new ArchiveItemResult(source, ArchiveItemStatus.Failed, null, $"检查源项目失败：{ex.Message}"));
                continue;
            }

            validItems.Add(item with { SourcePath = source });
        }

        if (validationResults.Count > 0)
        {
            return new ArchiveResult(null, validationResults, "所选项目中包含无效来源，已取消本批归档以避免不完整移动。");
        }

        var ticketFolder = Path.Combine(target, ticket);
        var createdFolder = false;
        if (!Directory.Exists(ticketFolder))
        {
            try
            {
                Directory.CreateDirectory(ticketFolder);
                createdFolder = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return ArchiveResult.Failure($"创建单号文件夹失败：{ex.Message}");
            }
        }

        var results = new List<ArchiveItemResult>();
        foreach (var item in validItems)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var source = item.SourcePath;
            var tracker = new TransferTracker();
            try
            {
                Transfer(
                    source,
                    Path.Combine(ticketFolder, item.TargetName),
                    conflictResolver,
                    tracker,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                tracker.Errors.Add(ex.Message);
            }

            results.Add(tracker.ToResult(source));
        }

        if (createdFolder)
        {
            RemoveIfEmpty(ticketFolder);
        }

        return new ArchiveResult(ticketFolder, results, cancellationToken.IsCancellationRequested ? "归档已取消。" : null);
    }

    private static bool IsSameOrDescendant(string path, string root)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return relative == "."
            || (!Path.IsPathRooted(relative)
                && relative != ".."
                && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    private static bool ContainsNestedReparsePoint(string path)
    {
        if (!Directory.Exists(path))
        {
            return false;
        }

        var pending = new Stack<string>();
        pending.Push(path);
        while (pending.Count > 0)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                if (PathSafety.IsReparsePoint(entry))
                {
                    return true;
                }

                if (Directory.Exists(entry))
                {
                    pending.Push(entry);
                }
            }
        }

        return false;
    }

    private static ArchiveItemResult ArchiveItem(
        string inbox,
        string ticketFolder,
        string item,
        ConflictResolver conflictResolver,
        CancellationToken cancellationToken)
    {
        string source;
        try
        {
            source = Path.GetFullPath(item);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new ArchiveItemResult(item, ArchiveItemStatus.Failed, null, $"路径无效：{ex.Message}");
        }

        if (!File.Exists(source) && !Directory.Exists(source))
        {
            return new ArchiveItemResult(item, ArchiveItemStatus.Failed, null, "源项目已不存在。");
        }

        if (!string.Equals(Path.GetDirectoryName(source), inbox, StringComparison.OrdinalIgnoreCase))
        {
            return new ArchiveItemResult(item, ArchiveItemStatus.Failed, null, "源项目不在中转目录顶层，已跳过。");
        }

        if (PathSafety.IsReparsePoint(source))
        {
            return new ArchiveItemResult(item, ArchiveItemStatus.Failed, null, "源项目是符号链接或联接点，已拒绝处理。");
        }

        var tracker = new TransferTracker();
        var destination = Path.Combine(ticketFolder, Path.GetFileName(source));

        try
        {
            Transfer(source, destination, conflictResolver, tracker, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            tracker.Errors.Add(ex.Message);
        }

        return tracker.ToResult(item);
    }

    private static void Transfer(
        string source,
        string destination,
        ConflictResolver conflictResolver,
        TransferTracker tracker,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sourceIsDirectory = Directory.Exists(source);

        // Nested folders reach this method through MergeDirectory, so this also covers junctions
        // buried inside an archived folder: never traverse into them.
        if (sourceIsDirectory && PathSafety.IsReparsePoint(source))
        {
            throw new IOException("源项目是符号链接或联接点目录，已拒绝处理。");
        }

        if (sourceIsDirectory && Directory.Exists(destination))
        {
            MergeDirectory(source, destination, conflictResolver, tracker, cancellationToken);
            return;
        }

        if (File.Exists(destination) || Directory.Exists(destination))
        {
            var parent = Path.GetDirectoryName(destination)
                ?? throw new IOException("无法确定目标文件夹。");

            var decision = conflictResolver(
                new ConflictRequest(source, destination, SuggestUniqueName(parent, Path.GetFileName(source))));

            switch (decision.Action)
            {
                case ConflictAction.Skip:
                    tracker.Skipped++;
                    return;

                case ConflictAction.Overwrite:
                    DeleteExisting(destination);
                    tracker.Overwritten = true;
                    break;

                case ConflictAction.Rename:
                    var newName = decision.NewName;
                    if (!PathSafety.IsSimpleName(newName))
                    {
                        throw new IOException("重命名失败：新名称无效。");
                    }

                    var renamed = Path.Combine(parent, newName!);
                    if (File.Exists(renamed) || Directory.Exists(renamed))
                    {
                        throw new IOException($"重命名失败：目标已存在 {newName}。");
                    }

                    destination = renamed;
                    tracker.Renamed = true;
                    break;
            }
        }

        MoveItem(source, destination);
        tracker.Moved++;
        tracker.Destination ??= destination;
    }

    private static void MergeDirectory(
        string source,
        string destination,
        ConflictResolver conflictResolver,
        TransferTracker tracker,
        CancellationToken cancellationToken)
    {
        foreach (var child in Directory.EnumerateFileSystemEntries(source).ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                Transfer(
                    child,
                    Path.Combine(destination, Path.GetFileName(child)),
                    conflictResolver,
                    tracker,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                tracker.Errors.Add($"{Path.GetFileName(child)}: {ex.Message}");
            }
        }

        // The source folder is removed only when it is empty, i.e. every child was transferred.
        try
        {
            if (!Directory.EnumerateFileSystemEntries(source).Any())
            {
                Directory.Delete(source);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void MoveItem(string source, string destination)
    {
        if (IsSameVolume(source, destination))
        {
            Retry(() => MoveDirect(source, destination));
            return;
        }

        CopyVerifyCommitRemove(source, destination);
    }

    private static void CopyVerifyCommitRemove(string source, string destination)
    {
        var staging = destination + ".tfo-partial-" + Guid.NewGuid().ToString("N");

        try
        {
            Retry(() =>
            {
                if (Directory.Exists(source))
                {
                    CopyDirectory(source, staging);
                }
                else
                {
                    File.Copy(source, staging, false);
                }
            });

            VerifyCopy(source, staging);

            Retry(() => MoveDirect(staging, destination));
            VerifyCopy(source, destination);

            Retry(() =>
            {
                if (Directory.Exists(source))
                {
                    Directory.Delete(source, true);
                }
                else
                {
                    File.Delete(source);
                }
            });
        }
        catch (Exception)
        {
            TryCleanup(staging);
            throw;
        }
    }

    private static void MoveDirect(string source, string destination)
    {
        if (Directory.Exists(source))
        {
            Directory.Move(source, destination);
        }
        else
        {
            File.Move(source, destination);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        // Walked by hand instead of EnumerateFiles(AllDirectories): a junction inside the folder
        // would otherwise be followed, copying (and later deleting) content from outside the inbox.
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            var name = Path.GetFileName(entry);
            var target = Path.Combine(destination, name);
            if (Directory.Exists(entry))
            {
                if (PathSafety.IsReparsePoint(entry))
                {
                    throw new IOException($"文件夹 {name} 是符号链接或联接点，已停止复制。");
                }

                CopyDirectory(entry, target);
                continue;
            }

            File.Copy(entry, target, false);
        }
    }

    private static void VerifyCopy(string source, string destination)
    {
        if (Directory.Exists(source))
        {
            if (!Directory.Exists(destination))
            {
                throw new IOException("校验失败：目标文件夹不存在。");
            }

            var sourceFiles = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).ToArray();
            var destinationFiles = Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories).ToArray();

            if (sourceFiles.Length != destinationFiles.Length)
            {
                throw new IOException($"校验失败：文件数量不一致（源 {sourceFiles.Length}，目标 {destinationFiles.Length}）。");
            }

            foreach (var file in sourceFiles)
            {
                var counterpart = Path.Combine(destination, Path.GetRelativePath(source, file));
                if (!File.Exists(counterpart) || new FileInfo(counterpart).Length != new FileInfo(file).Length)
                {
                    throw new IOException($"校验失败：{Path.GetFileName(file)} 未完整复制。");
                }
            }

            return;
        }

        if (!File.Exists(destination) || new FileInfo(destination).Length != new FileInfo(source).Length)
        {
            throw new IOException($"校验失败：{Path.GetFileName(source)} 未完整复制。");
        }
    }

    private static void DeleteExisting(string destination)
    {
        if (Directory.Exists(destination))
        {
            Directory.Delete(destination, true);
            return;
        }

        var attributes = File.GetAttributes(destination);
        if ((attributes & FileAttributes.ReadOnly) != 0)
        {
            File.SetAttributes(destination, attributes & ~FileAttributes.ReadOnly);
        }

        File.Delete(destination);
    }

    private static bool IsSameVolume(string source, string destination) =>
        string.Equals(
            Path.GetPathRoot(Path.GetFullPath(source)),
            Path.GetPathRoot(Path.GetFullPath(destination)),
            StringComparison.OrdinalIgnoreCase);

    private static string SuggestUniqueName(string directory, string name)
    {
        if (!File.Exists(Path.Combine(directory, name)) && !Directory.Exists(Path.Combine(directory, name)))
        {
            return name;
        }

        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);

        for (var index = 2; index < 10_000; index++)
        {
            var candidate = $"{stem} ({index}){extension}";
            if (!File.Exists(Path.Combine(directory, candidate)) && !Directory.Exists(Path.Combine(directory, candidate)))
            {
                return candidate;
            }
        }

        return $"{stem} ({Guid.NewGuid():N}){extension}";
    }

    private static void Retry(Action action)
    {
        const int attempts = 4;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (attempt < attempts && ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(150);
            }
        }
    }

    private static void TryCleanup(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void RemoveIfEmpty(string folder)
    {
        try
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class TransferTracker
    {
        public int Moved { get; set; }

        public int Skipped { get; set; }

        public bool Overwritten { get; set; }

        public bool Renamed { get; set; }

        public string? Destination { get; set; }

        public List<string> Errors { get; } = new();

        public ArchiveItemResult ToResult(string source)
        {
            if (Errors.Count > 0)
            {
                return new ArchiveItemResult(source, ArchiveItemStatus.Failed, Destination, string.Join("；", Errors));
            }

            if (Skipped > 0 && Moved == 0)
            {
                return new ArchiveItemResult(source, ArchiveItemStatus.Skipped, Destination, "目标已存在同名项目，已按选择跳过。");
            }

            if (Renamed)
            {
                return new ArchiveItemResult(source, ArchiveItemStatus.Renamed, Destination, null);
            }

            if (Overwritten)
            {
                return new ArchiveItemResult(source, ArchiveItemStatus.Overwritten, Destination, null);
            }

            return new ArchiveItemResult(source, ArchiveItemStatus.Archived, Destination, null);
        }
    }
}