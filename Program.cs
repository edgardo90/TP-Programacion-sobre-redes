using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Text.Json;
using System.IO.Compression;
using System.Linq;
using System.Collections.Concurrent;
using System.IO;

// Cola de logs compartida
//Se usa una cola (Queue) thread-safe donde se encolan mensajes de log.
//Esto evita escribir en disco en medio de atender clientes (sería lento).
ConcurrentQueue<string> logQueue = new ConcurrentQueue<string>();
bool loggingActive = true;

// Tarea que escribe los logs en disco continuamente
Task.Run(async () =>
{
    while (loggingActive || !logQueue.IsEmpty)
    {
        if (logQueue.TryDequeue(out string logLine))
        {
            string date = DateTime.Now.ToString("yyyy-MM-dd");
            string logFile = Path.Combine(Directory.GetCurrentDirectory(), $"{date}.log");

            using (var stream = new FileStream(logFile, FileMode.Append, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream))
            {
                await writer.WriteLineAsync(logLine);
            }
        }
        else
        {
            await Task.Delay(50); // Pequeño descanso si la cola está vacía
        }
    }
});

// --- Función para registrar logs ---
void LogRequest(string clientIp, string method, string path, string body = "")
{
    string logLine = $"[{DateTime.Now}] {clientIp} - {method} {path}";
    if (!string.IsNullOrEmpty(body))
        logLine += $" - Error: {body}";

    logQueue.Enqueue(logLine); // Solo encolamos, no escribimos directo
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

string host = root.GetProperty("host").GetString() ?? "localhost";
int port = root.GetProperty("port").GetInt32();
string wwwroot = root.GetProperty("wwwroot").GetString() ?? "wwwroot";
string welcomeFile = root.GetProperty("welcomeFile").GetString() ?? "index.html";

// --- Resolver host a IP (soporta "localhost") ---
IPHostEntry entry = Dns.GetHostEntry(host);
IPAddress ip = entry.AddressList.First(a => a.AddressFamily == AddressFamily.InterNetwork);

// --- Servidor asincrónico y escuchar conexiones---
TcpListener server = new TcpListener(ip, port);
server.Start();
Console.WriteLine($"Servidor escuchando en http://{host}:{port}/ ...");

while (true)
{
    TcpClient client = await server.AcceptTcpClientAsync(); // aceptar cliente
    TcpClient capturedClient = client; 

    _ = Task.Run(async () =>
    {
        try
        {
            await HandleClientAsync(capturedClient); 
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error manejando cliente: " + ex.Message);
        }
    });
}


// --- Manejo de cliente asincrónico ---
async Task HandleClientAsync(TcpClient client)
{
    try
    {
        //para leer/escribir datos del cliente, A través de él llega la petición HTTP que hace el navegador.
        using NetworkStream stream = client.GetStream();
        //Se reserva un array de bytes para guardar temporalmente los datos que llegan.
        byte[] buffer = new byte[4096];
        //ReadAsync espera a que lleguen datos desde el navegador.
        //Guarda esos datos crudos (sin interpretar) dentro del buffer.
        //bytesRead indica cuántos bytes se recibieron realmente.
        int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
        string requestText = Encoding.UTF8.GetString(buffer, 0, bytesRead);

        //manejo del cliente
        string[] requestLines = requestText.Split("\r\n");
        string[] parts = requestLines[0].Split(' ');
        string method = parts[0];
        string path = parts[1];

        // ✅ Solo redirigir "/" a index.html si es GET
        if (method == "GET" && path == "/")
            path = "/" + welcomeFile;

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
            
        //Acá detectás si el navegador dijo que soporta GZIP.
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

            //navegador
            if (aceptaGzip && !descargaGzip)
            {
                Console.WriteLine($"ingresando al primer if");
                body = CompressGzip(body);
                string header = $"HTTP/1.1 200 OK\r\nContent-Type: {contentType}\r\nContent-Encoding: gzip\r\nContent-Length: {body.Length}\r\n\r\n";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(header));
                await stream.WriteAsync(body);
            }
            //usuario
            else if (descargaGzip)
            {
                Console.WriteLine($"ingresando al else if(descargar)");
                byte[] gz = CompressGzip(body);
                string header =
                    $"HTTP/1.1 200 OK\r\nContent-Type: application/gzip\r\n" +
                    $"Content-Disposition: attachment; filename=\"{Path.GetFileName(requestedFile)}.gz\"\r\n" +
                    $"Content-Length: {gz.Length}\r\n\r\n";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(header));
                await stream.WriteAsync(gz);
            }
            //navegador no acepta gzip
            else
            {
                Console.WriteLine($"no hace nada");
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
        LogRequest(clientIp, "Error", "");
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
        Console.WriteLine($"POST recibido desde {clientIp}, Path: {path}, Body: {body}");


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
