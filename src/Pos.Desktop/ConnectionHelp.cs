using System.Net.Http;

namespace Pos.Desktop;

internal static class ConnectionHelp
{
    public const string ApiUnavailable = "No se pudo conectar con JetVenta. Ve a Configuración > Diagnóstico y pulsa Levantar API.";
    public const string ApiUnavailableRetry = "JetVenta no respondió. Ve a Configuración > Diagnóstico, pulsa Levantar API y después vuelve a intentar.";
    public const string ApiUnavailableNotConfirmed = "JetVenta no respondió. La operación no se confirmó. Ve a Configuración > Diagnóstico, pulsa Levantar API y consulta el estado antes de repetirla.";
    public const string ApiUnavailableShiftProtected = "JetVenta no respondió. El turno no se cerró para proteger el corte. Ve a Configuración > Diagnóstico y pulsa Levantar API.";
    public const string LocalRepairConfirmation = "JetVenta abrirá una ventana de Windows con permisos de administrador para revisar PostgreSQL, la API y las migraciones. No cierres esa ventana: se cerrará sola al terminar. Esta reparación no borra productos, ventas ni respaldos.";

    public static string FromException(Exception exception, string fallback)
        => exception is HttpRequestException or TaskCanceledException
            ? ApiUnavailableRetry
            : $"{fallback}: {exception.Message}";
}
