namespace LogOrganizer.Core;

/// <summary>Win32 MOD_* values, so a gesture can be passed straight to RegisterHotKey.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8,
}

/// <summary>
/// A global shortcut: at least one of Ctrl/Alt/Shift/Win plus one non-modifier key.
/// <para>
/// The gesture is stored as text in <c>config.json</c>, so parsing and formatting must round-trip.
/// Only keys this type can name are accepted, which keeps a recorded shortcut reloadable.
/// </para>
/// </summary>
public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, uint VirtualKey)
{
    /// <summary>Unclaimed by common editors; the user can still change it.</summary>
    public const string DefaultText = "Ctrl+Shift+L";

    /// <summary>F12 is reserved by the debugger, so it never reaches RegisterHotKey.</summary>
    private const uint VkF12 = 0x7B;

    private static readonly (string Name, uint VirtualKey)[] NamedKeys =
    {
        ("Space", 0x20),
        ("Enter", 0x0D),
        ("Tab", 0x09),
        ("Backspace", 0x08),
        ("Delete", 0x2E),
        ("Insert", 0x2D),
        ("Home", 0x24),
        ("End", 0x23),
        ("PageUp", 0x21),
        ("PageDown", 0x22),
        ("Left", 0x25),
        ("Up", 0x26),
        ("Right", 0x27),
        ("Down", 0x28),
        ("PrintScreen", 0x2C),
        ("Pause", 0x13),
    };

    public bool HasAnyModifier => Modifiers != HotkeyModifiers.None;

    public static HotkeyGesture Default =>
        TryParse(DefaultText, out var gesture) ? gesture : default;

    /// <summary>Builds a gesture from a virtual-key code; false when the key cannot be named.</summary>
    public static bool TryCreate(HotkeyModifiers modifiers, uint virtualKey, out HotkeyGesture gesture)
    {
        gesture = default;
        if (modifiers == HotkeyModifiers.None || !TryFormatKey(virtualKey, out _))
        {
            return false;
        }

        gesture = new HotkeyGesture(modifiers, virtualKey);
        return true;
    }

    /// <summary>
    /// Parses "Ctrl+Shift+L" style text. Order and case do not matter; the result is canonical,
    /// so two spellings of the same shortcut compare equal and save identically.
    /// </summary>
    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        uint key = 0;
        var sawKey = false;

        foreach (var raw in text.Split('+'))
        {
            var token = raw.Trim();
            if (token.Length == 0)
            {
                return false;
            }

            if (TryParseModifier(token, out var modifier))
            {
                modifiers |= modifier;
                continue;
            }

            if (sawKey || !TryParseKey(token, out key))
            {
                return false;
            }

            sawKey = true;
        }

        if (!sawKey || modifiers == HotkeyModifiers.None)
        {
            return false;
        }

        gesture = new HotkeyGesture(modifiers, key);
        return true;
    }

    /// <summary>True for key-down events that are themselves only a modifier, which are never a shortcut.</summary>
    public static bool IsModifierKey(uint virtualKey) => virtualKey is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C
        or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;

    /// <summary>Canonical text, also the value written to <c>config.json</c>.</summary>
    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(TryFormatKey(VirtualKey, out var name) ? name : $"0x{VirtualKey:X2}");
        return string.Join('+', parts);
    }

    private static bool TryParseModifier(string token, out HotkeyModifiers modifier)
    {
        switch (token.ToLowerInvariant())
        {
            case "ctrl" or "control":
                modifier = HotkeyModifiers.Control;
                return true;
            case "alt":
                modifier = HotkeyModifiers.Alt;
                return true;
            case "shift":
                modifier = HotkeyModifiers.Shift;
                return true;
            case "win" or "windows" or "meta":
                modifier = HotkeyModifiers.Windows;
                return true;
            default:
                modifier = HotkeyModifiers.None;
                return false;
        }
    }

    private static bool TryParseKey(string token, out uint virtualKey)
    {
        virtualKey = 0;
        if (token.Length == 1)
        {
            var ch = char.ToUpperInvariant(token[0]);
            if (ch is >= 'A' and <= 'Z')
            {
                virtualKey = ch;
                return true;
            }

            if (ch is >= '0' and <= '9')
            {
                virtualKey = ch;
                return true;
            }

            return false;
        }

        if (token.Length is 2 or 3 && (token[0] is 'F' or 'f') && int.TryParse(token[1..], out var index))
        {
            if (index is >= 1 and <= 24 && index != 12)
            {
                virtualKey = (uint)(0x70 + index - 1);
                return true;
            }

            return false;
        }

        foreach (var (name, key) in NamedKeys)
        {
            if (string.Equals(name, token, StringComparison.OrdinalIgnoreCase))
            {
                virtualKey = key;
                return true;
            }
        }

        return false;
    }

    private static bool TryFormatKey(uint virtualKey, out string name)
    {
        if (virtualKey is >= 'A' and <= 'Z')
        {
            name = ((char)virtualKey).ToString();
            return true;
        }

        if (virtualKey is >= '0' and <= '9')
        {
            name = ((char)virtualKey).ToString();
            return true;
        }

        if (virtualKey is >= 0x70 and <= 0x87 && virtualKey != VkF12)
        {
            name = $"F{virtualKey - 0x70 + 1}";
            return true;
        }

        foreach (var (candidate, key) in NamedKeys)
        {
            if (key == virtualKey)
            {
                name = candidate;
                return true;
            }
        }

        name = string.Empty;
        return false;
    }
}