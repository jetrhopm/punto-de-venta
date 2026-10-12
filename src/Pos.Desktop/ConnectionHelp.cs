using System.Net.Http;
using System.Net;

namespace Pos.Desktop;

internal static class ConnectionHelp
{
    public const string ApiUnavailable = "No se pudo conectar con JetVenta. Ve a Configuración > Diagnóstico y pulsa Levantar API.";
    public const string ApiUnavailableRetry = "JetVenta no respondió. Ve a Configuración > Diagnóstico, pulsa Levantar API y después vuelve a intentar.";
    public const string ApiUnavailableNotConfirmed = "JetVenta no respondió. La operación no se confirmó. Ve a Configuración > Diagnóstico, pulsa Levantar API y consulta el estado antes de repetirla.";
    public const string ApiUnavailableShiftProtected = "JetVenta no respondió. El turno no se cerró para proteger el corte. Ve a Configuración > Diagnóstico y pulsa Levantar API.";

    public static string FromException(Exception exception, string fallback)
    {
        if (exception is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized })
            return "JetVenta respondió, pero la sesión ya no está autorizada. Cierra sesión e inicia nuevamente.";
        if (exception is HttpRequestException { StatusCode: HttpStatusCode.Forbidden })
            return "JetVenta respondió, pero la cuenta no tiene permiso para completar esta acción. Solicita autorización al administrador.";
        if (exception is HttpRequestException { StatusCode: { } statusCode })
            return $"JetVenta respondió con error HTTP {(int)statusCode}. {fallback} Detalle: {exception.Message}";
        if (exception is HttpRequestException or TaskCanceledException)
            return ApiUnavailableRetry;
        return $"{fallback}: {exception.Message}";
    }
}
