namespace LogOrganizer.Core.Tests;

[TestClass]
public sealed class HotkeyGestureTests
{
    [TestMethod]
    public void Default_ParsesAndFormatsIdentically()
    {
        Assert.IsTrue(HotkeyGesture.TryParse(HotkeyGesture.DefaultText, out var gesture));

        Assert.AreEqual(HotkeyModifiers.Control | HotkeyModifiers.Shift, gesture.Modifiers);
        Assert.AreEqual((uint)'L', gesture.VirtualKey);
        Assert.AreEqual("Ctrl+Shift+L", gesture.ToString());
        Assert.AreEqual(HotkeyGesture.Default, gesture);
    }

    [TestMethod]
    public void TryParse_IsCaseAndSpacingInsensitive()
    {
        Assert.IsTrue(HotkeyGesture.TryParse("  shift + CTRL + l ", out var gesture));

        Assert.AreEqual("Ctrl+Shift+L", gesture.ToString());
    }

    [TestMethod]
    public void TryParse_RejectsWhatRegisterHotKeyCouldNotUse()
    {
        var rejected = new[]
        {
            null,
            "",
            "   ",
            "L",
            "Shift",
            "Ctrl+Shift",
            "Ctrl++L",
            "Ctrl+Shift+F12",
            "Ctrl+Shift+J+K",
            "Ctrl+Shift+中文",
            "Ctrl+Shift+MediaPlayPause",
            "Ctrl+Shift+F25",
            "Ctrl+Shift+0x41",
        };

        foreach (var text in rejected)
        {
            Assert.IsFalse(HotkeyGesture.TryParse(text, out _), $"'{text}' must not parse");
        }
    }

    [TestMethod]
    public void TryParse_CanonicalizesModifierOrderAndDuplicates()
    {
        Assert.IsTrue(HotkeyGesture.TryParse("Win+Shift+Alt+Ctrl+K", out var gesture));
        Assert.AreEqual("Ctrl+Alt+Shift+Win+K", gesture.ToString());

        Assert.IsTrue(HotkeyGesture.TryParse("ctrl+ctrl+K", out var duplicate));
        Assert.AreEqual("Ctrl+K", duplicate.ToString());
    }

    [TestMethod]
    public void TryParse_AcceptsEveryNameItCanFormat()
    {
        var keys = new[]
        {
            "A", "Z", "0", "9", "F1", "F11", "F13", "F24",
            "Space", "Enter", "Tab", "Backspace", "Delete", "Insert",
            "Home", "End", "PageUp", "PageDown", "Left", "Up", "Right", "Down",
            "PrintScreen", "Pause",
        };

        foreach (var key in keys)
        {
            var text = $"Ctrl+Alt+{key}";
            Assert.IsTrue(HotkeyGesture.TryParse(text, out var gesture), text);
            Assert.AreEqual(text, gesture.ToString(), text);
        }
    }

    [TestMethod]
    public void TryCreate_RejectsModifierOnlyAndUnnameableKeys()
    {
        Assert.IsFalse(HotkeyGesture.TryCreate(HotkeyModifiers.None, (uint)'L', out _));
        Assert.IsFalse(HotkeyGesture.TryCreate(HotkeyModifiers.Control, 0x11, out _), "Ctrl alone is not a shortcut");
        Assert.IsFalse(HotkeyGesture.TryCreate(HotkeyModifiers.Control, 0x7B, out _), "F12 belongs to the debugger");
        Assert.IsFalse(HotkeyGesture.TryCreate(HotkeyModifiers.Control, 0xAD, out _), "mute has no name");

        Assert.IsTrue(HotkeyGesture.TryCreate(HotkeyModifiers.Control | HotkeyModifiers.Alt, (uint)'K', out var gesture));
        Assert.AreEqual("Ctrl+Alt+K", gesture.ToString());
    }

    [TestMethod]
    public void IsModifierKey_CoversBothSidesOfEachModifier()
    {
        foreach (var key in new uint[] { 0x10, 0x11, 0x12, 0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 })
        {
            Assert.IsTrue(HotkeyGesture.IsModifierKey(key), $"0x{key:X2} is a modifier");
        }

        Assert.IsFalse(HotkeyGesture.IsModifierKey((uint)'L'));
    }
}