using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Pos.Desktop;

/// <summary>Portable JetVenta backup container. The dump and its checksum travel together in one .bjv file.</summary>
internal sealed class JetVentaBackupArchive : IDisposable
{
    private const string ManifestEntryName = "manifest.json";
    private const string DumpEntryName = "database.dump";
    private readonly string? _temporaryDirectory;

    private JetVentaBackupArchive(string dumpPath, string? temporaryDirectory = null)
    {
        DumpPath = dumpPath;
        _temporaryDirectory = temporaryDirectory;
    }

    public string DumpPath { get; }

    public static async Task CreateAsync(string archivePath, string sourceFileName, byte[] dump, string sha256, DateTimeOffset createdAtUtc, CancellationToken cancellationToken = default)
    {
        var manifest = new BackupManifest("JetVentaBackup", 1, DumpEntryName, sourceFileName, sha256.Trim().ToUpperInvariant(), createdAtUtc);
        await using var stream = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);

        var manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
        await using (var manifestStream = manifestEntry.Open())
        {
            await JsonSerializer.SerializeAsync(manifestStream, manifest, cancellationToken: cancellationToken);
        }

        var dumpEntry = archive.CreateEntry(DumpEntryName, CompressionLevel.Optimal);
        await using var dumpStream = dumpEntry.Open();
        await dumpStream.WriteAsync(dump, cancellationToken);
    }

    public static async Task<JetVentaBackupArchive> OpenVerifiedAsync(string archivePath, CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(Path.GetTempPath(), "JetVenta", "backups", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            var manifestEntry = archive.GetEntry(ManifestEntryName) ?? throw new InvalidDataException("El archivo .bjv no contiene la información de JetVenta.");
            BackupManifest? manifest;
            await using (var manifestStream = manifestEntry.Open())
            {
                manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(manifestStream, cancellationToken: cancellationToken);
            }

            if (manifest is null || !string.Equals(manifest.Format, "JetVentaBackup", StringComparison.Ordinal) || manifest.Version != 1 || !string.Equals(manifest.DumpFile, DumpEntryName, StringComparison.Ordinal))
            {
                throw new InvalidDataException("El archivo .bjv no es un respaldo compatible de JetVenta.");
            }

            var dumpEntry = archive.GetEntry(DumpEntryName) ?? throw new InvalidDataException("El archivo .bjv no contiene la base de datos.");
            var dumpPath = Path.Combine(directory, DumpEntryName);
            await using (var source = dumpEntry.Open())
            await using (var destination = new FileStream(dumpPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await source.CopyToAsync(destination, cancellationToken);
            }

            await using var dump = File.OpenRead(dumpPath);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(dump, cancellationToken));
            if (!string.Equals(actual, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("El respaldo .bjv está dañado o fue modificado. No se restauró ningún dato.");
            }

            return new JetVentaBackupArchive(dumpPath, directory);
        }
        catch
        {
            try { Directory.Delete(directory, true); } catch { }
            throw;
        }
    }

    public void Dispose()
    {
        if (_temporaryDirectory is null) return;
        try { Directory.Delete(_temporaryDirectory, true); } catch { }
    }

    private sealed record BackupManifest(string Format, int Version, string DumpFile, string SourceFileName, string Sha256, DateTimeOffset CreatedAtUtc);
}
