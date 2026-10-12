namespace Pos.Infrastructure.GoogleDrive;

public sealed record BackupRetentionEntry(string Id, DateOnly Day, DateTimeOffset CreatedAtUtc);

// Regla de conservación:
// - Del día en curso se conservan todos los respaldos (uno por cada cierre).
// - De cada día anterior sólo se conserva el más reciente.
// - Se conservan los 7 días anteriores más recientes; los demás se borran.
public static class GoogleDriveRetention
{
    public const int DaysToKeep = 7;

    public static IReadOnlyList<string> SelectForDeletion(IEnumerable<BackupRetentionEntry> backups, DateOnly today, int daysToKeep = DaysToKeep)
    {
        var deletions = new List<string>();
        var previousDays = backups
            .Where(item => item.Day < today)
            .GroupBy(item => item.Day)
            .OrderByDescending(group => group.Key)
            .ToList();

        for (var position = 0; position < previousDays.Count; position++)
        {
            var newestFirst = previousDays[position]
                .OrderByDescending(item => item.CreatedAtUtc)
                .ThenByDescending(item => item.Id, StringComparer.Ordinal)
                .ToList();
            deletions.AddRange((position < daysToKeep ? newestFirst.Skip(1) : newestFirst).Select(item => item.Id));
        }

        return deletions;
    }
}
