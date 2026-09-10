using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace Pos.Infrastructure;

public static class DepartmentDefaults
{
    public const string UnassignedName = "Sin Departamento";

    private static readonly string[] Names =
    [
        UnassignedName,
        "Abarrotes",
        "Bebidas",
        "Botanas",
        "Lácteos",
        "Limpieza",
        "Higiene personal",
        "Papelería",
        "Carnes frías",
        "Frutas y verduras",
        "Panadería",
        "Otros"
    ];

    public static IEnumerable<DepartmentRecord> CreateRecords(DateTimeOffset createdAtUtc) =>
        Names.Select(name => new DepartmentRecord
        {
            Id = Guid.NewGuid(),
            Name = name,
            NormalizedName = Normalize(name),
            IsActive = true,
            CreatedAtUtc = createdAtUtc
        });

    public static async Task EnsureAsync(PosDbContext database, CancellationToken cancellationToken)
    {
        var existing = await database.Departments.ToListAsync(cancellationToken);
        var byName = existing
            .GroupBy(item => Normalize(item.Name), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var changed = false;

        foreach (var name in Names)
        {
            var normalized = Normalize(name);
            if (byName.TryGetValue(normalized, out var department))
            {
                if (department.IsActive || !string.Equals(normalized, Normalize(UnassignedName), StringComparison.Ordinal)) continue;
                department.IsActive = true;
                changed = true;
                continue;
            }

            department = new DepartmentRecord
            {
                Id = Guid.NewGuid(),
                Name = name,
                NormalizedName = normalized,
                IsActive = true,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            database.Departments.Add(department);
            byName.Add(normalized, department);
            changed = true;
        }

        if (changed) await database.SaveChangesAsync(cancellationToken);
    }

    public static string Normalize(string value)
    {
        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        return new string(decomposed.Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark).ToArray()).ToUpperInvariant();
    }
}
