namespace FluidVoice.Ui;

/// <summary>Curated UI-font options (Preferences → Appearance). Each maps to a WPF font stack.</summary>
public static class FontChoice
{
    public const string Default = "System";

    /// <summary>Display name → font stack. Order is the dropdown order.</summary>
    public static readonly (string Name, string Stack)[] Options =
    {
        ("System", "Segoe UI Variable Text, Segoe UI"),
        ("Segoe UI", "Segoe UI"),
        ("Inter", "Inter, Segoe UI Variable Text, Segoe UI"),          // if installed
        ("Verdana", "Verdana, Segoe UI"),
        ("Georgia (serif)", "Georgia, 'Times New Roman', serif"),
        ("Cambria (serif)", "Cambria, Georgia, serif"),
        ("Consolas (mono)", "Consolas, 'Cascadia Mono', monospace"),
        ("Comic Sans MS", "Comic Sans MS, Segoe UI"),
    };

    public static string Resolve(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Options[0].Stack;
        foreach (var (n, stack) in Options)
            if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
                return stack;
        // allow a raw family name too (advanced users)
        return name;
    }
}
