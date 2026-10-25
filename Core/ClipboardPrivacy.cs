namespace WindowSill.ClipboardHistory.Core;

internal static class ClipboardPrivacy
{
    internal const string Mask = "********";

    internal static string Protect(string text, bool hidePasswords)
        => hidePasswords && LooksSensitive(text) ? Mask : text;

    internal static bool LooksSensitive(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length is < 8 or > 128)
        {
            return false;
        }

        bool upper = false, lower = false, digit = false, special = false;
        foreach (char character in text)
        {
            if (char.IsWhiteSpace(character)) return false;
            upper |= char.IsUpper(character);
            lower |= char.IsLower(character);
            digit |= char.IsDigit(character);
            special |= !char.IsLetterOrDigit(character);
        }
        return upper && lower && digit && special;
    }
}
