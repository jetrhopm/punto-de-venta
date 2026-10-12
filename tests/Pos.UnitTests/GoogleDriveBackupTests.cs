using System.Security.Cryptography;
using Pos.Infrastructure.GoogleDrive;

namespace Pos.UnitTests;

public sealed class GoogleDriveBackupTests
{
    // Iteraciones bajas sólo para que las pruebas sean rápidas; producción usa 600 000.
    private const int TestIterations = 1_000;

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(1024 * 1024)]
    [InlineData(1024 * 1024 + 1)]
    [InlineData(3 * 1024 * 1024 + 517)]
    public async Task EncryptAndDecryptRoundTrip(int size)
    {
        var original = RandomNumberGenerator.GetBytes(size);
        var encrypted = await EncryptAsync(original, "contraseña-segura");
        Assert.True(encrypted.Length > original.Length);
        Assert.Equal(original, await DecryptAsync(encrypted, "contraseña-segura"));
    }

    [Fact]
    public async Task WrongPasswordIsRejected()
    {
        var encrypted = await EncryptAsync(RandomNumberGenerator.GetBytes(2048), "correcta-123");
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => DecryptAsync(encrypted, "incorrecta-123"));
        Assert.Contains("contraseña", error.Message);
    }

    [Fact]
    public async Task TamperedFileIsRejected()
    {
        var encrypted = await EncryptAsync(RandomNumberGenerator.GetBytes(4096), "correcta-123");
        encrypted[^20] ^= 0x55;
        await Assert.ThrowsAsync<InvalidDataException>(() => DecryptAsync(encrypted, "correcta-123"));
    }

    [Fact]
    public async Task TruncatedFileIsRejected()
    {
        var encrypted = await EncryptAsync(RandomNumberGenerator.GetBytes(2 * 1024 * 1024 + 10), "correcta-123");
        // Quita el último bloque completo: el anterior no tiene la marca de final.
        var truncated = encrypted[..(encrypted.Length - (5 + 10 + 16))];
        await Assert.ThrowsAsync<InvalidDataException>(() => DecryptAsync(truncated, "correcta-123"));
    }

    [Fact]
    public async Task PlainDumpIsNotAcceptedAsEncrypted()
    {
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => DecryptAsync("PGDMP"u8.ToArray(), "correcta-123"));
        Assert.Contains("no es un respaldo cifrado", error.Message);
    }

    [Fact]
    public void KeepsAllBackupsOfTodayAndOnlyLastOfPreviousDay()
    {
        var today = new DateOnly(2026, 10, 12);
        var monday = new DateOnly(2026, 10, 11);
        var backups = Enumerable.Range(1, 6).Select(hour => Entry($"lunes-{hour}", monday, hour))
            .Concat(Enumerable.Range(1, 3).Select(hour => Entry($"hoy-{hour}", today, hour)))
            .ToArray();

        var deleted = GoogleDriveRetention.SelectForDeletion(backups, today);

        Assert.Equal(new[] { "lunes-5", "lunes-4", "lunes-3", "lunes-2", "lunes-1" }, deleted);
    }

    [Fact]
    public void KeepsOnlySevenPreviousDays()
    {
        var today = new DateOnly(2026, 10, 20);
        var backups = Enumerable.Range(1, 9).Select(daysAgo => Entry($"d{daysAgo}", today.AddDays(-daysAgo), 22)).ToArray();

        var deleted = GoogleDriveRetention.SelectForDeletion(backups, today);

        Assert.Equal(new[] { "d8", "d9" }, deleted.OrderBy(id => id));
    }

    [Fact]
    public void DaysWithoutBackupDoNotCountAgainstTheSeven()
    {
        var today = new DateOnly(2026, 10, 20);
        // Tienda cerrada fines de semana: 7 días con respaldo repartidos en 10 días.
        var days = new[] { 1, 2, 3, 6, 7, 8, 9, 10 };
        var backups = days.Select(daysAgo => Entry($"d{daysAgo}", today.AddDays(-daysAgo), 21)).ToArray();

        var deleted = GoogleDriveRetention.SelectForDeletion(backups, today);

        Assert.Equal(new[] { "d10" }, deleted);
    }

    [Fact]
    public void FutureDatedBackupsAreNeverDeleted()
    {
        var today = new DateOnly(2026, 10, 20);
        var deleted = GoogleDriveRetention.SelectForDeletion([Entry("futuro", today.AddDays(1), 9), Entry("hoy", today, 9)], today);
        Assert.Empty(deleted);
    }

    private static BackupRetentionEntry Entry(string id, DateOnly day, int hour) =>
        new(id, day, new DateTimeOffset(day.Year, day.Month, day.Day, hour, 0, 0, TimeSpan.Zero));

    private static async Task<byte[]> EncryptAsync(byte[] plain, string password)
    {
        await using var input = new MemoryStream(plain);
        await using var output = new MemoryStream();
        await BackupCipher.EncryptAsync(input, output, password, CancellationToken.None, TestIterations);
        return output.ToArray();
    }

    private static async Task<byte[]> DecryptAsync(byte[] encrypted, string password)
    {
        await using var input = new MemoryStream(encrypted);
        await using var output = new MemoryStream();
        await BackupCipher.DecryptAsync(input, output, password, CancellationToken.None);
        return output.ToArray();
    }
}
