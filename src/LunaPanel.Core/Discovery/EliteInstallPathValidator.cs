namespace LunaPanel.Core.Discovery;

/// <summary>
/// The one check both "point LunaPanel at your Elite Dangerous install by
/// hand" flows share - <c>AboutForm.OnSelectEliteInstall</c>
/// (<c>LunaPanel.Tray</c>, the original) and <c>EliteSetupForm</c>'s
/// first-launch equivalent. A picked folder is valid only if it contains a
/// <c>Products</c> subfolder - the same shape Steam/Epic install Elite
/// Dangerous under.
///
/// Only the pure check and its two user-facing strings live here - the
/// <c>FolderBrowserDialog</c> wiring and the decision of when to show the
/// message box stay per-form, matching this codebase's own stated preference elsewhere
/// (<c>AddDeviceForm</c>'s doc comment on this exact tradeoff for its
/// dark-title-bar P/Invoke, in <c>LunaPanel.Tray</c>) for small per-form
/// duplication over a shared type for two call sites. What must not drift
/// between the two forms is the wording, so the two strings live in one
/// place even though the dialog/message-box calls themselves do not.
/// </summary>
public static class EliteInstallPathValidator
{
    /// <summary>
    /// Deliberately short - this is the NATIVE Windows shell folder-picker's
    /// own description label, which has a narrow, effectively fixed width
    /// that this codebase cannot make reflow (unlike a WinForms label). The
    /// full explanation with the "why" lives instead as an actual,
    /// dynamically reflowing label inside <c>EliteSetupForm</c>, shown
    /// before the picker ever opens - see that form's own remarks.
    /// </summary>
    public const string FolderPickerDescription =
        "Select your Elite Dangerous folder (the one containing \"Products\").";

    public const string InvalidSelectionMessage =
        "That folder doesn't contain a \"Products\" folder, so LunaPanel won't find Elite Dangerous there. Pick the folder ONE LEVEL UP from wherever you found Products (for example, the folder Steam calls \"Elite Dangerous\", not \"Products\" itself or one of the folders inside it).";

    /// <summary>
    /// True if <paramref name="selectedPath"/> is a folder that itself
    /// contains a <c>Products</c> subfolder.
    /// </summary>
    public static bool IsValidEliteInstallRoot(string selectedPath) =>
        Directory.Exists(Path.Combine(selectedPath, "Products"));

    /// <summary>
    /// Recovers the "Elite Dangerous" install root - the folder a commander
    /// should actually pick - from an <c>EliteInstallation.ProductPath</c>,
    /// which points two levels deeper, at one specific product inside
    /// <c>Products</c> (e.g. <c>...\Elite Dangerous\Products\elite-dangerous-odyssey-64</c>).
    /// Used to pre-select/open a <see cref="System.Windows.Forms.FolderBrowserDialog"/>
    /// at the right neighbourhood instead of three folders too deep - the
    /// exact mistake a commander picking by hand was found making tonight.
    /// Returns <see langword="null"/> if the path is too shallow to have two
    /// parent directories (should not happen for a real discovered
    /// installation, but this must never throw over a malformed input).
    /// </summary>
    public static string? InstallRootFromProductPath(string productPath)
    {
        var productsDirectory = Path.GetDirectoryName(productPath);
        return productsDirectory is null ? null : Path.GetDirectoryName(productsDirectory);
    }
}
