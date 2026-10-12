using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Web;

namespace Pos.Infrastructure.GoogleDrive;

public enum GoogleDriveConnectState { None, Waiting, Completed, Failed }
public sealed record GoogleDriveConnectStatus(GoogleDriveConnectState State, string Message);

// Autorización de app de escritorio (RFC 8252): JetVenta abre un puerto temporal en
// 127.0.0.1, el navegador regresa ahí con el código y se cambia por tokens con PKCE.
// Se usa TcpListener para no requerir permisos de administrador (HttpListener los pide).
public sealed class GoogleDriveAuthorizationFlow
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);
    private readonly object _gate = new();
    private CancellationTokenSource? _current;
    private GoogleDriveConnectStatus _status = new(GoogleDriveConnectState.None, string.Empty);

    public GoogleDriveConnectStatus Status { get { lock (_gate) return _status; } }

    // Devuelve la URL de Google. onCode recibe (code, codeVerifier, redirectUri) y debe
    // cambiarlo por tokens y guardarlos; su mensaje de error se muestra al usuario.
    public string Start(string clientId, Func<string, string, string, Task> onCode)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var redirectUri = $"http://127.0.0.1:{port}/";
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(32));

        CancellationTokenSource cancellation;
        lock (_gate)
        {
            _current?.Cancel();
            _current = cancellation = new CancellationTokenSource(Timeout);
            _status = new(GoogleDriveConnectState.Waiting, "Esperando que aceptes el permiso en Google...");
        }
        _ = Task.Run(() => ListenAsync(listener, state, verifier, redirectUri, onCode, cancellation));
        return GoogleDriveClient.BuildAuthorizationUrl(clientId, redirectUri, state, challenge);
    }

    private async Task ListenAsync(TcpListener listener, string expectedState, string verifier, string redirectUri, Func<string, string, string, Task> onCode, CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                using var connection = await listener.AcceptTcpClientAsync(token);
                bool finished;
                try { finished = await HandleConnectionAsync(connection, expectedState, verifier, redirectUri, onCode, token); }
                catch (Exception exception) when (!token.IsCancellationRequested && exception is OperationCanceledException or IOException or SocketException)
                {
                    // Conexiones vacías o cortadas (por ejemplo, la precarga del navegador) se ignoran.
                    continue;
                }
                if (finished) return;
            }
        }
        catch (OperationCanceledException)
        {
            if (Status.State == GoogleDriveConnectState.Waiting) SetStatus(GoogleDriveConnectState.Failed, "Se agotó el tiempo para autorizar. Vuelve a pulsar Conectar Google Drive.");
        }
        catch (Exception exception) when (exception is SocketException or IOException)
        {
            SetStatus(GoogleDriveConnectState.Failed, "Se interrumpió la conexión local con el navegador. Vuelve a intentarlo.");
        }
        finally
        {
            listener.Stop();
            cancellation.Dispose();
        }
    }

    private async Task<bool> HandleConnectionAsync(TcpClient connection, string expectedState, string verifier, string redirectUri, Func<string, string, string, Task> onCode, CancellationToken token)
    {
        await using var stream = connection.GetStream();
        var target = await ReadRequestTargetAsync(stream, token);
        if (target is null || !target.StartsWith("/?", StringComparison.Ordinal) && target != "/")
        {
            await RespondAsync(stream, 404, "No encontrado", "Esta dirección sólo se usa para conectar Google Drive con JetVenta.", token);
            return false;
        }

        var query = HttpUtility.ParseQueryString(target.Length > 2 ? target[2..] : string.Empty);
        if (!FixedEquals(query["state"], expectedState))
        {
            await RespondAsync(stream, 400, "Solicitud no válida", "Esta autorización no corresponde a JetVenta. Vuelve a pulsar Conectar Google Drive.", token);
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query["error"]) || string.IsNullOrWhiteSpace(query["code"]))
        {
            SetStatus(GoogleDriveConnectState.Failed, query["error"] == "access_denied" ? "No se concedió el permiso en Google." : "Google no completó la autorización.");
            await RespondAsync(stream, 200, "Autorización cancelada", "No se concedió el permiso. Puedes cerrar esta ventana y volver a intentarlo desde JetVenta.", token);
            return true;
        }

        try
        {
            await onCode(query["code"]!, verifier, redirectUri);
            SetStatus(GoogleDriveConnectState.Completed, "Google Drive conectado.");
            await RespondAsync(stream, 200, "Google Drive conectado", "Listo. Regresa a JetVenta: tus respaldos se guardarán en tu Google Drive.", token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SetStatus(GoogleDriveConnectState.Failed, exception.Message);
            await RespondAsync(stream, 200, "No se pudo conectar", "JetVenta no pudo completar la conexión. Regresa al programa para ver el detalle.", token);
        }
        return true;
    }

    private void SetStatus(GoogleDriveConnectState state, string message)
    {
        lock (_gate) _status = new(state, message);
    }

    private static async Task<string?> ReadRequestTargetAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var buffer = new byte[8192];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), timeout.Token);
            if (read == 0) break;
            total += read;
            var text = Encoding.ASCII.GetString(buffer, 0, total);
            var end = text.IndexOf("\r\n", StringComparison.Ordinal);
            if (end < 0) continue;
            var parts = text[..end].Split(' ');
            return parts.Length >= 2 && parts[0] == "GET" ? parts[1] : null;
        }
        return null;
    }

    private static async Task RespondAsync(NetworkStream stream, int status, string title, string message, CancellationToken cancellationToken)
    {
        var ok = status == 200 && !title.StartsWith("No ", StringComparison.Ordinal) && !title.Contains("cancelada", StringComparison.Ordinal);
        var color = ok ? "#137333" : "#b3261e";
        var html = $$"""
            <!doctype html><html lang="es"><head><meta charset="utf-8"><title>{{WebUtility.HtmlEncode(title)}} · JetVenta</title>
            <style>body{margin:0;font-family:"Segoe UI",sans-serif;background:#f4f6f8;color:#1c2430;display:flex;min-height:100vh;align-items:center;justify-content:center}
            main{background:#fff;max-width:440px;margin:16px;padding:32px;border-radius:12px;box-shadow:0 2px 12px rgba(0,0,0,.08)}h1{font-size:22px;margin:0 0 12px;color:{{color}}}p{line-height:1.5;margin:0}</style></head>
            <body><main><h1>{{WebUtility.HtmlEncode(title)}}</h1><p>{{WebUtility.HtmlEncode(message)}}</p></main></body></html>
            """;
        var body = Encoding.UTF8.GetBytes(html);
        var reason = status switch { 200 => "OK", 400 => "Bad Request", _ => "Not Found" };
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {reason}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(body, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static bool FixedEquals(string? received, string expected)
    {
        var left = Encoding.UTF8.GetBytes(received ?? string.Empty);
        var right = Encoding.UTF8.GetBytes(expected);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
