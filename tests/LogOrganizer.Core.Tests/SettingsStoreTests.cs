namespace LogOrganizer.Core.Tests;

[TestClass]
public sealed class SettingsStoreTests
{
    private string _root = string.Empty;
    private SettingsStore _store = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = TestPaths.NewDirectory();
        _store = new SettingsStore(_root);
    }

    [TestCleanup]
    public void Cleanup() => TestPaths.DeleteQuietly(_root);

    [TestMethod]
    public void FirstRun_UsesDocumentedDefaults()
    {
        var settings = _store.Load();

        Assert.AreEqual(AppSettings.DefaultInboxRoot, settings.InboxRoot);
        Assert.AreEqual(AppSettings.DefaultInboxFolderName, settings.InboxFolderName);
        Assert.AreEqual(AppSettings.DefaultInboxPath, settings.InboxPath);
        Assert.IsNull(settings.TargetRoot);
        Assert.AreEqual(10, settings.QuietSeconds);
        Assert.IsFalse(settings.StartWithWindows);
        Assert.IsFalse(settings.PanelPinned);
        Assert.AreEqual(BackdropMode.Mica, settings.Backdrop);
        Assert.AreEqual(AppSettings.DefaultPanelOpacity, settings.PanelOpacity);
        Assert.AreEqual(-1, settings.PanelLeft);
        Assert.AreEqual(-1, settings.PanelTop);
        Assert.IsFalse(File.Exists(_store.FilePath), "loading must not create the config file");
    }

    [TestMethod]
    public void SaveThenLoad_RoundTrips()
    {
        _store.Save(new AppSettings
        {
            InboxRoot = @"E:\Inbox",
            InboxFolderName = "_tickets",
            TargetRoot = @"E:\Archive",
            QuietSeconds = 25,
            StartWithWindows = true,
            WakeWithSpaceOrEnter = false,
            PanelPinned = true,
            Backdrop = BackdropMode.Acrylic,
            PanelOpacity = 0.65,
            PanelLeft = 123,
            PanelTop = 456,
        });

        var loaded = _store.Load();

        Assert.AreEqual(@"E:\Inbox", loaded.InboxRoot);
        Assert.AreEqual("_tickets", loaded.InboxFolderName);
        Assert.AreEqual(@"E:\Inbox\_tickets", loaded.InboxPath);
        Assert.AreEqual(@"E:\Archive", loaded.TargetRoot);
        Assert.AreEqual(25, loaded.QuietSeconds);
        Assert.IsTrue(loaded.StartWithWindows);
        Assert.IsFalse(loaded.WakeWithSpaceOrEnter);
        Assert.IsTrue(loaded.PanelPinned);
        Assert.AreEqual(BackdropMode.Acrylic, loaded.Backdrop);
        Assert.AreEqual(0.65, loaded.PanelOpacity, 0.0001);
        Assert.AreEqual(123, loaded.PanelLeft);
        Assert.AreEqual(456, loaded.PanelTop);
    }

    [TestMethod]
    public void LegacyMicaFlag_IsMigratedToBackdrop()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            Path.Combine(_root, "config.json"),
            """
            {
              "InboxRoot": "E:\\Inbox",
              "UseMicaBackdrop": false
            }
            """);

        Assert.AreEqual(BackdropMode.Solid, _store.Load().Backdrop);

        File.WriteAllText(
            Path.Combine(_root, "config.json"),
            """
            {
              "InboxRoot": "E:\\Inbox",
              "UseMicaBackdrop": true
            }
            """);

        Assert.AreEqual(BackdropMode.Mica, _store.Load().Backdrop);
    }

    [TestMethod]
    public void Backdrop_IsPersistedByName()
    {
        _store.Save(new AppSettings { Backdrop = BackdropMode.Acrylic });

        var json = File.ReadAllText(_store.FilePath);

        StringAssert.Contains(json, "\"Backdrop\": \"Acrylic\"");
        Assert.IsFalse(json.Contains("UseMicaBackdrop"), "legacy switch must not be written back");
        Assert.AreEqual(BackdropMode.Acrylic, _store.Load().Backdrop);
    }

    [TestMethod]
    public void CorruptFile_FallsBackToDefaults()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "config.json"), "{ this is not json ");

        var settings = _store.Load();

        Assert.AreEqual(AppSettings.DefaultInboxPath, settings.InboxPath);
        Assert.AreEqual(AppSettings.DefaultQuietSeconds, settings.QuietSeconds);
    }

    [TestMethod]
    public void InvalidValues_AreRepaired()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            Path.Combine(_root, "config.json"),
            """
            {
              "InboxRoot": "   ",
              "InboxFolderName": "..\\escape",
              "TargetRoot": "",
              "QuietSeconds": 100000
            }
            """);

        var settings = _store.Load();

        Assert.AreEqual(AppSettings.DefaultInboxRoot, settings.InboxRoot);
        Assert.AreEqual(AppSettings.DefaultInboxFolderName, settings.InboxFolderName);
        Assert.IsNull(settings.TargetRoot);
        Assert.AreEqual(AppSettings.MaxQuietSeconds, settings.QuietSeconds);
    }

    [TestMethod]
    public void QuietSecondsBelowMinimum_IsRaised()
    {
        var settings = SettingsStore.Normalize(new AppSettings { QuietSeconds = 0 });

        Assert.AreEqual(AppSettings.MinQuietSeconds, settings.QuietSeconds);
    }

    [TestMethod]
    public void PanelOpacity_IsClampedToSupportedRange()
    {
        var low = SettingsStore.Normalize(new AppSettings { PanelOpacity = 0.1 });
        var high = SettingsStore.Normalize(new AppSettings { PanelOpacity = 1.2 });

        Assert.AreEqual(AppSettings.MinPanelOpacity, low.PanelOpacity, 0.0001);
        Assert.AreEqual(1.0, high.PanelOpacity, 0.0001);
    }

    [TestMethod]
    public void NonFinitePanelOpacity_UsesDefault()
    {
        foreach (var opacity in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var settings = SettingsStore.Normalize(new AppSettings { PanelOpacity = opacity });

            Assert.AreEqual(AppSettings.DefaultPanelOpacity, settings.PanelOpacity, 0.0001);
        }
    }

    [TestMethod]
    public void PanelPositionBelowSentinel_UsesDefaultPosition()
    {
        var settings = SettingsStore.Normalize(new AppSettings { PanelLeft = -2, PanelTop = -8 });

        Assert.AreEqual(-1, settings.PanelLeft);
        Assert.AreEqual(-1, settings.PanelTop);
    }

    [TestMethod]
    public void FolderNameWithSeparators_IsReplaced()
    {
        var settings = SettingsStore.Normalize(new AppSettings { InboxFolderName = "a/b" });

        Assert.AreEqual(AppSettings.DefaultInboxFolderName, settings.InboxFolderName);
    }

    [TestMethod]
    public void InboxPath_CombinesRootAndFolderName()
    {
        var settings = new AppSettings { InboxRoot = @"D:\Downloads", InboxFolderName = "_收件" };

        Assert.AreEqual(@"D:\Downloads\_收件", settings.InboxPath);
    }
}