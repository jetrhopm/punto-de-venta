using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Globalization;
using System.Text;

namespace Pos.Infrastructure;

public sealed record InitialSetupCommand(string StoreName, string BusinessType, string UserName, string Password, string AdministratorName, string RegisterName, string CurrencySymbol = "$", string DefaultWeightUnit = "Kilogramo");
public sealed record InitialSetupResult(Guid StoreId, Guid AdministratorId, Guid RegisterId);

public sealed class InitialSetupService(PosDbContext database, PasswordHasher<UserRecord> passwordHasher)
{
    private static readonly HashSet<string> BusinessTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Abarrotes", "Minisuper", "Farmacia", "Papeleria", "Ferreteria", "Refaccionaria",
        "Ropa y calzado", "Electronica", "Cosmeticos", "Panaderia", "Carniceria",
        "Frutas y verduras", "Restaurante o alimentos", "Dulceria", "Vinos y licores",
        "Comercio general", "Otro"
    };

    public async Task<InitialSetupResult> ExecuteAsync(InitialSetupCommand command, CancellationToken cancellationToken)
    {
        Validate(command);
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (await database.Stores.AnyAsync(cancellationToken)) throw new InvalidOperationException("La tienda ya fue configurada.");

        var now = DateTimeOffset.UtcNow;
        var store = new StoreRecord { Id = Guid.NewGuid(), Name = command.StoreName.Trim(), BusinessType = command.BusinessType.Trim(), CurrencySymbol = command.CurrencySymbol.Trim(), DefaultWeightUnit = command.DefaultWeightUnit.Trim(), CreatedAtUtc = now };
        var user = new UserRecord { Id = Guid.NewGuid(), NormalizedUserName = NormalizeUserName(command.UserName), DisplayName = command.AdministratorName.Trim(), IsAdministrator = true, IsActive = true, CreatedAtUtc = now };
        user.PasswordHash = passwordHasher.HashPassword(user, command.Password);
        var register = new RegisterRecord { Id = Guid.NewGuid(), StoreId = store.Id, Name = command.RegisterName.Trim(), IsActive = true };

        database.AddRange(store, user, register);
        database.Permissions.AddRange(Enum.GetNames<Pos.Domain.Permission>().Select(code => new PermissionRecord { Id = Guid.NewGuid(), UserId = user.Id, Code = code }));
        await database.SaveChangesAsync(cancellationToken);
        // Las migraciones pueden haber creado departamentos predeterminados antes
        // de que exista una tienda. EnsureAsync completa los faltantes sin volver
        // a insertar Abarrotes, Limpieza u otros nombres ya existentes.
        await DepartmentDefaults.EnsureAsync(database, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new InitialSetupResult(store.Id, user.Id, register.Id);
    }

    public static string NormalizeUserName(string userName) => userName.Trim().ToUpperInvariant();

    public static bool IsSupportedBusinessType(string? businessType) =>
        !string.IsNullOrWhiteSpace(businessType) && BusinessTypes.Contains(FoldForComparison(businessType));

    private static void Validate(InitialSetupCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.StoreName) || command.StoreName.Trim().Length > 160) throw new ArgumentException("El nombre de la tienda es obligatorio y debe tener maximo 160 caracteres.");
        if (!IsSupportedBusinessType(command.BusinessType)) throw new ArgumentException("El giro de negocio no es válido. Selecciona una opción de la lista.");
        if (string.IsNullOrWhiteSpace(command.UserName) || command.UserName.Trim().Length > 80) throw new ArgumentException("El usuario es obligatorio y debe tener maximo 80 caracteres.");
        if (string.IsNullOrEmpty(command.Password) || command.Password.Length > 256) throw new ArgumentException("La contrasena es obligatoria y debe tener maximo 256 caracteres.");
        if (string.IsNullOrWhiteSpace(command.AdministratorName) || command.AdministratorName.Trim().Length > 160) throw new ArgumentException("El nombre del administrador es obligatorio y debe tener maximo 160 caracteres.");
        if (string.IsNullOrWhiteSpace(command.RegisterName) || command.RegisterName.Trim().Length > 80) throw new ArgumentException("El nombre de la caja es obligatorio y debe tener maximo 80 caracteres.");
        if (string.IsNullOrWhiteSpace(command.CurrencySymbol) || command.CurrencySymbol.Trim().Length > 8) throw new ArgumentException("El simbolo de moneda es obligatorio y debe tener maximo 8 caracteres.");
        if (string.IsNullOrWhiteSpace(command.DefaultWeightUnit) || command.DefaultWeightUnit.Trim().Length > 40) throw new ArgumentException("La unidad de peso es obligatoria y debe tener maximo 40 caracteres.");
    }

    private static string FoldForComparison(string value)
    {
        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        return string.Concat(decomposed.Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark))
            .Normalize(NormalizationForm.FormC);
    }
}
