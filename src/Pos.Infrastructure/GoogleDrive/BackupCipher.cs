using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure.GoogleDrive;

// Cifrado opcional de respaldos con contraseña del dueño.
// Formato JVENC1:
//   "JVENC1" | iteraciones (int32 BE) | sal (16) | prefijo de nonce (8) | tamaño de bloque (int32 BE)
//   bloques: final (1 byte: 0/1) | longitud (int32 BE) | cifrado | etiqueta GCM (16)
// Cada bloque se autentica con su índice y la marca de final, de modo que no se pueden
// reordenar, quitar ni truncar bloques sin que el descifrado falle.
public static class BackupCipher
{
    public const int DefaultIterations = 600_000;
    public const string EncryptedExtension = ".jvenc";
    private static readonly byte[] Magic = "JVENC1"u8.ToArray();
    private const int ChunkSize = 1024 * 1024;
    private const int SaltSize = 16;
    private const int NoncePrefixSize = 8;
    private const int TagSize = 16;
    private const int HeaderSize = 4 + SaltSize + NoncePrefixSize + 4;

    public static async Task EncryptAsync(Stream input, Stream output, string password, CancellationToken cancellationToken, int iterations = DefaultIterations)
    {
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("La contraseña del respaldo es obligatoria.", nameof(password));
        if (iterations < 1) throw new ArgumentOutOfRangeException(nameof(iterations));
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var prefix = RandomNumberGenerator.GetBytes(NoncePrefixSize);
        var key = DeriveKey(password, salt, iterations);
        try
        {
            var header = new byte[HeaderSize];
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), iterations);
            salt.CopyTo(header, 4);
            prefix.CopyTo(header, 4 + SaltSize);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4 + SaltSize + NoncePrefixSize, 4), ChunkSize);
            await output.WriteAsync(Magic, cancellationToken);
            await output.WriteAsync(header, cancellationToken);

            using var aes = new AesGcm(key, TagSize);
            var current = new byte[ChunkSize];
            var next = new byte[ChunkSize];
            var read = await ReadFullAsync(input, current, cancellationToken);
            uint index = 0;
            while (true)
            {
                var nextRead = read == ChunkSize ? await ReadFullAsync(input, next, cancellationToken) : 0;
                var final = nextRead == 0;
                var cipher = new byte[read];
                var tag = new byte[TagSize];
                aes.Encrypt(Nonce(prefix, index), current.AsSpan(0, read), cipher, tag, AssociatedData(index, final));
                var frame = new byte[5];
                frame[0] = final ? (byte)1 : (byte)0;
                BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(1, 4), read);
                await output.WriteAsync(frame, cancellationToken);
                await output.WriteAsync(cipher, cancellationToken);
                await output.WriteAsync(tag, cancellationToken);
                if (final) break;
                (current, next) = (next, current);
                read = nextRead;
                index = checked(index + 1);
            }
            await output.FlushAsync(cancellationToken);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    public static async Task DecryptAsync(Stream input, Stream output, string password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("Escribe la contraseña del respaldo.", nameof(password));
        var magic = new byte[Magic.Length];
        if (await ReadFullAsync(input, magic, cancellationToken) != magic.Length || !magic.AsSpan().SequenceEqual(Magic))
            throw new InvalidDataException("El archivo no es un respaldo cifrado de JetVenta.");
        var header = new byte[HeaderSize];
        if (await ReadFullAsync(input, header, cancellationToken) != HeaderSize) throw Incomplete();
        var iterations = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(0, 4));
        var salt = header.AsSpan(4, SaltSize).ToArray();
        var prefix = header.AsSpan(4 + SaltSize, NoncePrefixSize).ToArray();
        var chunkSize = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4 + SaltSize + NoncePrefixSize, 4));
        if (iterations is < 1 or > 10_000_000 || chunkSize is < 1 or > 16 * 1024 * 1024)
            throw new InvalidDataException("El encabezado del respaldo cifrado no es válido.");

        var key = DeriveKey(password, salt, iterations);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            var frame = new byte[5];
            uint index = 0;
            while (true)
            {
                if (await ReadFullAsync(input, frame, cancellationToken) != frame.Length) throw Incomplete();
                var final = frame[0] switch { 0 => false, 1 => true, _ => throw new InvalidDataException("El respaldo cifrado está dañado.") };
                var length = BinaryPrimitives.ReadInt32BigEndian(frame.AsSpan(1, 4));
                if (length < 0 || length > chunkSize) throw new InvalidDataException("El respaldo cifrado está dañado.");
                var cipher = new byte[length];
                var tag = new byte[TagSize];
                if (await ReadFullAsync(input, cipher, cancellationToken) != length || await ReadFullAsync(input, tag, cancellationToken) != TagSize) throw Incomplete();
                var plain = new byte[length];
                try { aes.Decrypt(Nonce(prefix, index), cipher, tag, plain, AssociatedData(index, final)); }
                catch (CryptographicException) { throw new InvalidDataException("La contraseña no es correcta o el respaldo está dañado."); }
                await output.WriteAsync(plain, cancellationToken);
                if (final) break;
                index = checked(index + 1);
            }
            if (await ReadFullAsync(input, new byte[1], cancellationToken) != 0) throw new InvalidDataException("El respaldo cifrado tiene datos de más.");
            await output.FlushAsync(cancellationToken);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    public static bool IsEncryptedName(string fileName) => fileName.EndsWith(EncryptedExtension, StringComparison.OrdinalIgnoreCase);

    private static byte[] DeriveKey(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, 32);

    private static byte[] Nonce(byte[] prefix, uint index)
    {
        var nonce = new byte[12];
        prefix.CopyTo(nonce, 0);
        BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(8, 4), index);
        return nonce;
    }

    private static byte[] AssociatedData(uint index, bool final)
    {
        var data = new byte[Magic.Length + 5];
        Magic.CopyTo(data, 0);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(Magic.Length, 4), index);
        data[^1] = final ? (byte)1 : (byte)0;
        return data;
    }

    private static InvalidDataException Incomplete() => new("El respaldo cifrado está incompleto.");

    private static async Task<int> ReadFullAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken);
            if (read == 0) break;
            total += read;
        }
        return total;
    }
}
