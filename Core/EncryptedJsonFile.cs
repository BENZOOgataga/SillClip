using System.Security.Cryptography;
using System.Text.Json;
using Path = System.IO.Path;

namespace WindowSill.ClipboardHistory.Core;

/// <summary>Atomic DPAPI storage for small metadata documents. The caller serializes concurrent writes.</summary>
internal sealed class EncryptedJsonFile<T>(string path) where T : class
{
    private readonly string _path = Path.GetFullPath(path);

    internal async Task<T?> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return null;
        byte[] encrypted = await File.ReadAllBytesAsync(_path, cancellationToken).ConfigureAwait(false);
        byte[] plaintext = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        try { return JsonSerializer.Deserialize<T>(plaintext, JsonSerializerOptions.Web) ?? throw new InvalidDataException("Metadata is empty."); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    internal async Task WriteAsync(T value, CancellationToken cancellationToken = default)
    {
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(value, JsonSerializerOptions.Web);
        byte[] encrypted;
        try { encrypted = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, encrypted, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, _path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
