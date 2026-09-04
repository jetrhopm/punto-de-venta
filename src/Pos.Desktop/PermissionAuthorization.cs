using System.Net.Http.Json;
using System.Net.Http;
using System.Windows;

namespace Pos.Desktop;

public static class PermissionAuthorization
{
    private static readonly IReadOnlyDictionary<string, string> PermissionNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Sell"] = "Vender y cobrar tickets",
        ["SellOnCredit"] = "Cobrar a crédito",
        ["UseCommonProduct"] = "Utilizar producto común",
        ["ViewProducts"] = "Consultar productos",
        ["ManageProducts"] = "Administrar productos, promociones y kits",
        ["ChangeSalePrice"] = "Cambiar precios durante la venta",
        ["ApplyDiscounts"] = "Aplicar descuentos",
        ["UseWholesalePrice"] = "Aplicar precio de mayoreo",
        ["ViewInventory"] = "Consultar inventario",
        ["AdjustInventory"] = "Ajustar inventario",
        ["ViewCostsAndProfit"] = "Consultar costos y utilidades",
        ["CancelSaleLines"] = "Eliminar productos del ticket",
        ["CancelSales"] = "Cancelar ventas confirmadas",
        ["ProcessReturns"] = "Procesar devoluciones",
        ["ReprintTickets"] = "Reimprimir tickets",
        ["OpenCashDrawer"] = "Abrir cajón de dinero",
        ["RecordCashMovements"] = "Registrar entradas y salidas de efectivo",
        ["ViewSalesHistory"] = "Consultar historial de ventas",
        ["OpenShift"] = "Abrir turno",
        ["CloseShift"] = "Cerrar turno y realizar corte",
        ["ViewPreviousShifts"] = "Consultar cortes anteriores",
        ["ViewReports"] = "Consultar reportes",
        ["ManageCustomersAndCredit"] = "Administrar clientes y crédito",
        ["ManageSuppliersAndPurchases"] = "Administrar compras y proveedores",
        ["ProcessServicePayments"] = "Realizar recargas y pagos de servicios",
        ["ConfigurePrinters"] = "Configurar impresoras",
        ["ConfigureStore"] = "Modificar la configuración de la tienda",
        ["ManageUsers"] = "Administrar cajeros y permisos",
        ["ImportOrExportData"] = "Importar, exportar y respaldar datos"
    };

    public static string NameFor(string permission) => PermissionNames.TryGetValue(permission, out var name) ? name : permission;

    public static async Task<TemporaryPermissionLease?> RequestAsync(Window owner, string permission, string action)
    {
        if (SessionContext.HasPermission(permission)) return TemporaryPermissionLease.NotRequired;

        var window = new PermissionAuthorizationWindow(permission, action) { Owner = owner };
        if (window.ShowDialog() != true || window.Authorization is null) return null;
        return new TemporaryPermissionLease(permission, window.Authorization.GrantId, window.Authorization.AuthorizedBy);
    }
}

public sealed class TemporaryPermissionLease : IAsyncDisposable
{
    private readonly IDisposable? _permissionScope;
    private readonly Guid? _grantId;
    private bool _disposed;

    private TemporaryPermissionLease()
    {
    }

    internal TemporaryPermissionLease(string permission, Guid? grantId, string authorizedBy)
    {
        Permission = permission;
        AuthorizedBy = authorizedBy;
        _grantId = grantId;
        _permissionScope = SessionContext.BeginTemporaryPermission(permission);
    }

    public static TemporaryPermissionLease NotRequired { get; } = new();
    public string? Permission { get; }
    public string? AuthorizedBy { get; }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _permissionScope?.Dispose();
        if (_grantId is null || string.IsNullOrWhiteSpace(SessionContext.AccessToken)) return;
        try { await ApiClient.Client.DeleteAsync($"api/auth/temporary-permission/{_grantId.Value}"); }
        catch (HttpRequestException) { }
    }
}
