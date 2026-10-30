using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WindowSill.ClipboardHistory.Core;

/// <summary>
/// Password-protected, streaming archive. Every record authenticates its position and header;
/// an authenticated footer detects truncation. Callers must stage imports until reading succeeds.
/// </summary>
internal static class PortableClipboardArchive
{
    private static readonly byte[] Magic = "SILLCLP1"u8.ToArray();
    private const int HeaderSize = 40, MaxRecordBytes = 32 * 1024 * 1024, Iterations = 600_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { MaxDepth = 16 };

    internal static async Task WriteAsync(Stream output, string password, IAsyncEnumerable<PortableClipboardEntry> entries,
        CancellationToken cancellationToken = default)
    {
        if (password.Length is < 12 or > 1024) throw new ArgumentException("Use a passphrase of 12 to 1024 characters.");
        byte[] header = RandomNumberGenerator.GetBytes(HeaderSize);
        Magic.CopyTo(header, 0);
        byte[] key = DeriveKey(password, header);
        try
        {
            using var cipher = new AesGcm(key, 16);
            await output.WriteAsync(header, cancellationToken);
            ulong index = 0;
            await foreach (var entry in entries.WithCancellation(cancellationToken))
            {
                byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(entry, JsonOptions);
                try { await WriteRecordAsync(output, cipher, header, index++, 1, plaintext, cancellationToken); }
                finally { CryptographicOperations.ZeroMemory(plaintext); }
            }
            byte[] footer = new byte[8];
            BinaryPrimitives.WriteUInt64BigEndian(footer, index);
            await WriteRecordAsync(output, cipher, header, index, 0, footer, cancellationToken);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    internal static async Task<ulong> ReadToStagingAsync(Stream input, string password,
        Func<PortableClipboardEntry, CancellationToken, Task> stageEntry, CancellationToken cancellationToken = default)
    {
        if (password.Length is < 1 or > 1024) throw new ArgumentException("Enter the archive passphrase.");
        byte[] header = new byte[HeaderSize];
        await input.ReadExactlyAsync(header, cancellationToken);
        if (!header.AsSpan(0, Magic.Length).SequenceEqual(Magic)) throw new InvalidDataException("Unsupported archive format.");
        byte[] key = DeriveKey(password, header);
        try
        {
            using var cipher = new AesGcm(key, 16);
            ulong index = 0;
            while (true)
            {
                byte[] frame = new byte[5];
                await input.ReadExactlyAsync(frame, cancellationToken);
                byte kind = frame[0];
                int length = BinaryPrimitives.ReadInt32BigEndian(frame.AsSpan(1));
                if (kind > 1 || length < 0 || length > MaxRecordBytes || (kind == 0 && length != 8))
                    throw new InvalidDataException("Invalid archive record.");
                byte[] ciphertext = new byte[length], tag = new byte[16], plaintext = new byte[length];
                await input.ReadExactlyAsync(ciphertext, cancellationToken);
                await input.ReadExactlyAsync(tag, cancellationToken);
                try
                {
                    cipher.Decrypt(Nonce(header, index), ciphertext, tag, plaintext, AssociatedData(header, index, frame));
                    if (kind == 0)
                    {
                        if (BinaryPrimitives.ReadUInt64BigEndian(plaintext) != index) throw new InvalidDataException("Archive count mismatch.");
                        byte[] tail = new byte[1];
                        if (await input.ReadAsync(tail, cancellationToken) != 0) throw new InvalidDataException("Unexpected data after archive footer.");
                        return index;
                    }
                    var entry = JsonSerializer.Deserialize<PortableClipboardEntry>(plaintext, JsonOptions)
                        ?? throw new InvalidDataException("Invalid clipboard entry.");
                    await stageEntry(entry, cancellationToken);
                    index = checked(index + 1);
                }
                finally { CryptographicOperations.ZeroMemory(plaintext); }
            }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private static byte[] DeriveKey(string password, byte[] header)
        => Rfc2898DeriveBytes.Pbkdf2(password, header.AsSpan(8, 16), Iterations, HashAlgorithmName.SHA256, 32);

    private static async Task WriteRecordAsync(Stream output, AesGcm cipher, byte[] header, ulong index, byte kind,
        byte[] plaintext, CancellationToken cancellationToken)
    {
        if (plaintext.Length > MaxRecordBytes) throw new InvalidDataException("An archive entry exceeds the 32 MiB record safety limit.");
        byte[] frame = new byte[5]; frame[0] = kind;
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(1), plaintext.Length);
        byte[] ciphertext = new byte[plaintext.Length], tag = new byte[16];
        cipher.Encrypt(Nonce(header, index), plaintext, ciphertext, tag, AssociatedData(header, index, frame));
        await output.WriteAsync(frame, cancellationToken);
        await output.WriteAsync(ciphertext, cancellationToken);
        await output.WriteAsync(tag, cancellationToken);
    }

    private static byte[] Nonce(byte[] header, ulong index)
    {
        byte[] nonce = new byte[12];
        header.AsSpan(24, 4).CopyTo(nonce);
        BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4), index);
        return nonce;
    }

    private static byte[] AssociatedData(byte[] header, ulong index, byte[] frame)
    {
        byte[] data = new byte[HeaderSize + 8 + frame.Length];
        header.CopyTo(data, 0);
        BinaryPrimitives.WriteUInt64BigEndian(data.AsSpan(HeaderSize), index);
        frame.CopyTo(data, HeaderSize + 8);
        return data;
    }
}
