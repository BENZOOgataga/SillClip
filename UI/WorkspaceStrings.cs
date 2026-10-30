using WindowSill.API;

namespace WindowSill.ClipboardHistory.UI;

internal static class WorkspaceStrings
{
    internal static string Get(string key) => ("/WindowSill.ClipboardHistory/Misc/" + key).GetLocalizedString();
    internal static string Format(string key, params object[] values) => string.Format(System.Globalization.CultureInfo.CurrentCulture, Get(key), values);
}
