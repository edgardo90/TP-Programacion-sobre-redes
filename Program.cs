using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Text.Json;
using System.IO.Compression;
using System.Linq;

// --- Función de logging ---
void LogRequest(string clientIp, string method, string path, string body = "")
{
    string date = DateTime.Now.ToString("yyyy-MM-dd");
    string logFile = Path.Combine(Directory.GetCurrentDirectory(), $"{date}.log");

    string logLine = $"[{DateTime.Now}] {clientIp} - {method} {path}";
    if (!string.IsNullOrEmpty(body))
        logLine += $" - Body: {body}";
    logLine += "\n";

    File.AppendAllText(logFile, logLine);
}

// --- Funciones de compresión ---
byte[] CompressGzip(byte[] data)
{
    using var output = new MemoryStream();
    using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        gzip.Write(data, 0, data.Length);
    return output.ToArray();
}

byte[] CreateZip(string folderPath)
{
    using var mem = new MemoryStream();
    using (var zip = new ZipArchive(mem, ZipArchiveMode.Create, true))
    {
        foreach (var file in Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories))
            zip.CreateEntryFromFile(file, Path.GetRelativePath(folderPath, file));
    }
    return mem.ToArray();
}

// --- Función para obtener tipo MIME ---
string GetContentType(string filePath)
{
    string extension = Path.GetExtension(filePath).ToLower();
    return extension switch
    {
        ".html" => "text/html; charset=utf-8",
        ".htm" => "text/html; charset=utf-8",
        ".css" => "text/css",
        ".js" => "application/javascript",
        ".json" => "application/json",
        ".png" => "image/png",
        ".jpg" => "image/jpeg",
        ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".svg" => "image/svg+xml",
        ".ico" => "image/x-icon",
        _ => "application/octet-stream"
    };
}

// --- Leer archivo de configuración ---
string configText = File.ReadAllText("config.json");
using var json = JsonDocument.Parse(configText);
var root = json.RootElement;
string host = root.GetProperty("host").GetString()!;
int port = root.GetProperty("port").GetInt32();
string wwwroot = root.GetProperty("wwwroot").GetString()!;
string welcomeFile = root.GetProperty("welcomeFile").GetString()!;

// --- Resolver host a IP (soporta "localhost") ---
IPHostEntry entry = Dns.GetHostEntry(host);
IPAddress ip = entry.AddressList.First(a => a.AddressFamily == AddressFamily.InterNetwork);

// --- Servidor asincrónico ---
TcpListener server = new TcpListener(ip, port);
server.Start();
Console.WriteLine($"Servidor escuchando en http://{host}:{port}/ ...");

while (true)
{
    try
    {
        TcpClient client = await server.AcceptTcpClientAsync();
        _ = HandleClientAsync(client);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error aceptando cliente: {ex.Message}");
    }
}

// --- Manejo de cliente asincrónico ---
async Task HandleClientAsync(TcpClient client)
{
    try
    {
        using NetworkStream stream = client.GetStream();
        byte[] buffer = new byte[4096];
        int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
        string requestText = Encoding.UTF8.GetString(buffer, 0, bytesRead);

        string[] requestLines = requestText.Split("\r\n");
        string[] parts = requestLines[0].Split(' ');
        string method = parts[0];
        string path = parts[1];

        if (path == "/") path = "/" + welcomeFile;

        string filePath = Path.Combine(wwwroot, path.TrimStart('/'));
        string clientIp = ((IPEndPoint)client.Client.RemoteEndPoint!).Address.ToString();

        if (method == "GET")
            await HandleGetAsync(stream, path, clientIp, requestText);
        else if (method == "POST")
            await HandlePostAsync(stream, path, clientIp, requestText, bytesRead, buffer);

    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error manejando cliente: {ex.Message}");
        try
        {
            if (client.Connected)
            {
                byte[] errorResp = Encoding.UTF8.GetBytes(
                    "HTTP/1.1 500 Internal Server Error\r\nContent-Length: 0\r\n\r\n"
                );
                await client.GetStream().WriteAsync(errorResp);
            }
        }
        catch { /* Ignorar errores al enviar respuesta de error */ }
    }
    finally
    {
        client.Close(); // Siempre cerrar el cliente
    }
}

// --- Función GET asincrónica ---
async Task HandleGetAsync(NetworkStream stream, string path, string clientIp, string requestText)
{
    try
    {
        string pathOnly = path;
        string query = "";
        int qIndex = path.IndexOf('?');
        if (qIndex >= 0)
        {
            pathOnly = path.Substring(0, qIndex);
            query = path.Substring(qIndex + 1);
        }

        if (pathOnly == "/") pathOnly = "/" + welcomeFile;
        string requestedFile = Path.Combine(wwwroot, pathOnly.TrimStart('/'));

        LogRequest(clientIp, "GET", pathOnly);
        if (!string.IsNullOrEmpty(query))
            LogRequest(clientIp, "GET", pathOnly, "Query: " + query);

        bool aceptaGzip = requestText.Contains("Accept-Encoding: gzip");

        // Descarga ZIP del sitio
        if (query.Contains("download=sitezip", StringComparison.OrdinalIgnoreCase))
        {
            byte[] zipBytes = CreateZip(wwwroot);
            string header =
                $"HTTP/1.1 200 OK\r\nContent-Type: application/zip\r\n" +
                $"Content-Disposition: attachment; filename=\"wwwroot.zip\"\r\n" +
                $"Content-Length: {zipBytes.Length}\r\n\r\n";

            await stream.WriteAsync(Encoding.UTF8.GetBytes(header));
            await stream.WriteAsync(zipBytes);
            return;
        }

        if (File.Exists(requestedFile))
        {
            byte[] body = await File.ReadAllBytesAsync(requestedFile);
            string contentType = GetContentType(requestedFile);
            bool descargaGzip = query.Contains("download=gzip", StringComparison.OrdinalIgnoreCase);

            if (aceptaGzip && !descargaGzip)
            {
                body = CompressGzip(body);
                string header = $"HTTP/1.1 200 OK\r\nContent-Type: {contentType}\r\nContent-Encoding: gzip\r\nContent-Length: {body.Length}\r\n\r\n";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(header));
                await stream.WriteAsync(body);
            }
            else if (descargaGzip)
            {
                byte[] gz = CompressGzip(body);
                string header =
                    $"HTTP/1.1 200 OK\r\nContent-Type: application/gzip\r\n" +
                    $"Content-Disposition: attachment; filename=\"{Path.GetFileName(requestedFile)}.gz\"\r\n" +
                    $"Content-Length: {gz.Length}\r\n\r\n";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(header));
                await stream.WriteAsync(gz);
            }
            else
            {
                string header = $"HTTP/1.1 200 OK\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\n\r\n";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(header));
                await stream.WriteAsync(body);
            }
        }
        else
        {
            LogRequest(clientIp, "GET", pathOnly, "Archivo no encontrado");
            string notFoundPath = Path.Combine(wwwroot, "404.html");
            byte[] body = File.Exists(notFoundPath) ? await File.ReadAllBytesAsync(notFoundPath) : Encoding.UTF8.GetBytes("<h1>404 - Archivo no encontrado</h1>");
            string header = $"HTTP/1.1 404 Not Found\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\n\r\n";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(header));
            await stream.WriteAsync(body);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error en GET: {ex.Message}");
        byte[] errorResp = Encoding.UTF8.GetBytes(
            "HTTP/1.1 500 Internal Server Error\r\nContent-Length: 0\r\n\r\n"
        );
        await stream.WriteAsync(errorResp);
    }
}

// --- Función POST asincrónica ---
async Task HandlePostAsync(NetworkStream stream, string path, string clientIp, string requestText, int bytesRead, byte[] buffer)
{
    try
    {
        string[] requestLines = requestText.Split("\r\n");
        int contentLength = 0;
        foreach (var line in requestLines)
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                contentLength = int.Parse(line.Split(':')[1].Trim());

        int headerEndIndex = requestText.IndexOf("\r\n\r\n") + 4;
        string body = bytesRead > headerEndIndex ? Encoding.UTF8.GetString(buffer, headerEndIndex, bytesRead - headerEndIndex) : "";

        if (body.Length < contentLength)
        {
            int remaining = contentLength - body.Length;
            byte[] restBuffer = new byte[remaining];
            int read = await stream.ReadAsync(restBuffer, 0, remaining);
            body += Encoding.UTF8.GetString(restBuffer, 0, read);
        }

        LogRequest(clientIp, "POST", path, body);

        string response = "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n";
        await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error en POST: {ex.Message}");
        byte[] errorResp = Encoding.UTF8.GetBytes(
            "HTTP/1.1 500 Internal Server Error\r\nContent-Length: 0\r\n\r\n"
        );
        await stream.WriteAsync(errorResp);
    }
}
