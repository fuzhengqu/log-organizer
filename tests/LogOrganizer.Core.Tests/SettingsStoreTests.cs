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
        Assert.IsFalse(settings.StartWithWindows);
        Assert.IsFalse(settings.WakeHotkeyEnabled);
        Assert.AreEqual(HotkeyGesture.DefaultText, settings.WakeHotkey);
        Assert.IsFalse(settings.StartPinned);
        Assert.IsFalse(settings.PanelPinned);
        Assert.AreEqual(BackdropMode.Mica, settings.Backdrop);
        Assert.AreEqual(AppSettings.DefaultPanelOpacity, settings.PanelOpacity);
        Assert.AreEqual(-1, settings.PanelLeft);
        Assert.AreEqual(-1, settings.PanelTop);
        Assert.AreEqual(0, settings.PanelWidth);
        Assert.AreEqual(0, settings.PanelHeight);
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
            StartWithWindows = true,
            WakeHotkeyEnabled = true,
            WakeHotkey = "Ctrl+Alt+K",
            StartPinned = true,
            Backdrop = BackdropMode.Acrylic,
            PanelOpacity = 0.65,
            PanelLeft = 123,
            PanelTop = 456,
            PanelWidth = 900,
            PanelHeight = 760,
        });

        var loaded = _store.Load();

        Assert.AreEqual(@"E:\Inbox", loaded.InboxRoot);
        Assert.AreEqual("_tickets", loaded.InboxFolderName);
        Assert.AreEqual(@"E:\Inbox\_tickets", loaded.InboxPath);
        Assert.AreEqual(@"E:\Archive", loaded.TargetRoot);
        Assert.IsTrue(loaded.StartWithWindows);
        Assert.IsTrue(loaded.WakeHotkeyEnabled);
        Assert.AreEqual("Ctrl+Alt+K", loaded.WakeHotkey);
        Assert.IsTrue(loaded.StartPinned);
        Assert.AreEqual(BackdropMode.Acrylic, loaded.Backdrop);
        Assert.AreEqual(0.65, loaded.PanelOpacity, 0.0001);
        Assert.AreEqual(123, loaded.PanelLeft);
        Assert.AreEqual(456, loaded.PanelTop);
        Assert.AreEqual(900, loaded.PanelWidth);
        Assert.AreEqual(760, loaded.PanelHeight);
    }

    /// <summary>
    /// The title-bar pin writes <see cref="AppSettings.PanelPinned"/> constantly, but that is live
    /// state: saving it would resurrect the 0.05 bug where pinning silently toggled the "启动时置顶"
    /// preference in the settings window.
    /// </summary>
    [TestMethod]
    public void PanelPinned_IsNotPersisted()
    {
        _store.Save(new AppSettings { StartPinned = false, PanelPinned = true });

        var loaded = _store.Load();

        Assert.IsFalse(loaded.StartPinned, "the launch preference must not be rewritten by the live pin state");
        Assert.IsFalse(File.ReadAllText(_store.FilePath).Contains(nameof(AppSettings.PanelPinned)));
    }

    /// <summary>
    /// Up to 0.05 the persisted <c>PanelPinned</c> flag was the only pin setting, so an upgraded
    /// config must carry it over as the launch preference rather than dropping the user's choice.
    /// </summary>
    [TestMethod]
    public void LegacyPanelPinned_LoadsAsStartPinned()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            Path.Combine(_root, "config.json"),
            """
            {
              "InboxRoot": "E:\\Inbox",
              "PanelPinned": true
            }
            """);

        var settings = _store.Load();

        Assert.IsTrue(settings.StartPinned);
    }

    /// <summary>
    /// The retired Space/Enter switch was a global keyboard hook, which swallowed those keys in
    /// every other app. The replacement hotkey starts disabled, so an upgraded config stays quiet.
    /// </summary>
    [TestMethod]
    public void LegacyWakeSwitch_LoadsAsDisabledHotkey()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            Path.Combine(_root, "config.json"),
            """
            {
              "InboxRoot": "E:\\Inbox",
              "WakeWithSpaceOrEnter": true
            }
            """);

        var settings = _store.Load();

        Assert.IsFalse(settings.WakeHotkeyEnabled);
        Assert.AreEqual(HotkeyGesture.DefaultText, settings.WakeHotkey);

        _store.Save(settings);
        Assert.IsFalse(
            File.ReadAllText(_store.FilePath).Contains("WakeWithSpaceOrEnter"),
            "the retired switch must not be written back");
    }

    [TestMethod]
    public void UnparsableHotkey_FallsBackToDefault()
    {
        var settings = SettingsStore.Normalize(new AppSettings { WakeHotkey = "L", WakeHotkeyEnabled = true });

        Assert.AreEqual(HotkeyGesture.DefaultText, settings.WakeHotkey);
        Assert.IsTrue(settings.WakeHotkeyEnabled);
    }

    [TestMethod]
    public void HotkeyText_IsNormalizedToCanonicalOrder()
    {
        var settings = SettingsStore.Normalize(new AppSettings { WakeHotkey = " shift + ctrl + l " });

        Assert.AreEqual("Ctrl+Shift+L", settings.WakeHotkey);
    }

    [TestMethod]
    public void PanelSize_BelowMinimumOrUnset_IsRepaired()
    {
        Assert.AreEqual(0, SettingsStore.Normalize(new AppSettings { PanelWidth = 0 }).PanelWidth);
        Assert.AreEqual(0, SettingsStore.Normalize(new AppSettings { PanelHeight = -40 }).PanelHeight);
        Assert.AreEqual(
            AppSettings.MinStoredPanelWidth,
            SettingsStore.Normalize(new AppSettings { PanelWidth = 50 }).PanelWidth);
        Assert.AreEqual(
            AppSettings.MaxStoredPanelSize,
            SettingsStore.Normalize(new AppSettings { PanelHeight = 999999 }).PanelHeight);
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
              "TargetRoot": ""
            }
            """);

        var settings = _store.Load();

        Assert.AreEqual(AppSettings.DefaultInboxRoot, settings.InboxRoot);
        Assert.AreEqual(AppSettings.DefaultInboxFolderName, settings.InboxFolderName);
        Assert.IsNull(settings.TargetRoot);
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