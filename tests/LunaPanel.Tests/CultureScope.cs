using System.Globalization;

namespace LunaPanel.Tests;

/// <summary>
/// Runs a body with <see cref="CultureInfo.CurrentCulture"/> set to a culture
/// whose default calendar is NOT Gregorian, then restores the previous one in
/// a finally. CurrentCulture is per-thread/async-flow, so restoring it on the
/// same thread is enough to keep it from leaking into other (parallel) tests.
/// </summary>
public static class CultureScope
{
    /// <summary>Non-Gregorian-default cultures: ar-SA = Um Al-Qura (Hijri), th-TH = Thai Buddhist.</summary>
    public static readonly string[] NonGregorianCultures = { "ar-SA", "th-TH" };

    public static void Run(string cultureName, Action body)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            // Guard against a runtime that silently falls back to Gregorian
            // (e.g. invariant-globalization mode): the test would be vacuous.
            Assert.False(
                culture.DateTimeFormat.Calendar is GregorianCalendar,
                $"{cultureName} must have a non-Gregorian default calendar for this test to mean anything.");
            CultureInfo.CurrentCulture = culture;
            body();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
