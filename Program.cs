using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Text.Json;
//compresion gzip
using System.IO.Compression;



// --- Función de logging ---
void LogRequest(string clientIp, string method, string path, string body = "")
{
    string date = DateTime.Now.ToString("yyyy-MM-dd"); // para el nombre del archivo
    string logFile = Path.Combine(Directory.GetCurrentDirectory(), $"{date}.log");

    string logLine = $"[{DateTime.Now}] {clientIp} - {method} {path}";
    if (!string.IsNullOrEmpty(body))
        logLine += $" - Body: {body}";
    logLine += "\n";

    File.AppendAllText(logFile, logLine);
}

//Detectar si el navegador soporta compresión
byte[] CompressGzip(byte[] data)
{
    using (var output = new MemoryStream())
    {
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }
}

//funcion para crear zip 
byte[] CreateZip(string filePath)
{
    using (var mem = new MemoryStream())
    {
        using (var zip = new ZipArchive(mem, ZipArchiveMode.Create, true))
        {
            zip.CreateEntryFromFile(filePath, Path.GetFileName(filePath));
        }
        return mem.ToArray();
    }
}



// --- Código principal ---
// 1. Leer el archivo de configuración
string configText = File.ReadAllText("config.json");
using var json = JsonDocument.Parse(configText);
var root = json.RootElement;

// Obtener valores configurables
string host = root.GetProperty("host").GetString()!;
int port = root.GetProperty("port").GetInt32();
string wwwroot = root.GetProperty("wwwroot").GetString()!;
string welcomeFile = root.GetProperty("welcomeFile").GetString()!;

// 2. Crear el socket servidor (TcpListener)
TcpListener server = new TcpListener(Dns.GetHostAddresses(host)[0], port);
server.Start();
Console.WriteLine($"Servidor escuchando en http://{host}:{port}/ ...");

// Función para obtener el tipo MIME según la extensión del archivo
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

// 3. Bucle infinito para aceptar clientes
while (true)
{
    TcpClient client = server.AcceptTcpClient();
    Console.WriteLine(">> Cliente conectado");

    using NetworkStream stream = client.GetStream();
    byte[] buffer = new byte[4096];
    int bytesRead = stream.Read(buffer, 0, buffer.Length);
    string requestText = Encoding.UTF8.GetString(buffer, 0, bytesRead);

    Console.WriteLine("=== Solicitud recibida ===");
    Console.WriteLine(requestText);
    Console.WriteLine("==========================");

    // Procesar solicitud HTTP
    string[] requestLines = requestText.Split("\r\n");
    string firstLine = requestLines[0];
    string[] parts = firstLine.Split(" ");

    string method = parts[0];
    string path = parts[1];

    if (path == "/")
        path = "/" + welcomeFile;

    string filePath = Path.Combine(wwwroot, path.TrimStart('/'));
    string clientIp = ((IPEndPoint)client.Client.RemoteEndPoint!).Address.ToString();


    if (method == "GET")
    {
        // Detectar si el navegador acepta gzip
        bool aceptaGzip = requestText.Contains("Accept-Encoding: gzip");

        // Separar path y query string
        string pathOnly = path;
        string query = "";
        int qIndex = path.IndexOf('?');
        if (qIndex >= 0)
        {
            pathOnly = path.Substring(0, qIndex);
            query = path.Substring(qIndex + 1);
        }

        // Si no se pidió archivo, usar el archivo de bienvenida
        if (pathOnly == "/")
            pathOnly = "/" + welcomeFile;

        // Crear ruta absoluta al archivo solicitado
        string requestedFile = Path.Combine(wwwroot, pathOnly.TrimStart('/'));

        // Log de GET básico
        LogRequest(clientIp, method, pathOnly);

        // Log de query si existe
        if (!string.IsNullOrEmpty(query))
            LogRequest(clientIp, method, pathOnly, "Query: " + query);
        // 👉 Si piden descargar TODO el sitio en ZIP
        if (query.Contains("download=sitezip", StringComparison.OrdinalIgnoreCase))
        {
            string folderToZip = wwwroot; // ✅ Solo la carpeta wwwroot
            string zipPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot.zip");

            // Si ya existe, lo borramos para generar uno nuevo
            if (File.Exists(zipPath))
                File.Delete(zipPath);

            // Crear ZIP (requiere using System.IO.Compression)
            ZipFile.CreateFromDirectory(folderToZip, zipPath);

            byte[] zipBytes = File.ReadAllBytes(zipPath);

            string header =
                $"HTTP/1.1 200 OK\r\n" +
                $"Content-Type: application/zip\r\n" +
                $"Content-Disposition: attachment; filename=\"wwwroot.zip\"\r\n" +
                $"Content-Length: {zipBytes.Length}\r\n\r\n";


            stream.Write(Encoding.UTF8.GetBytes(header));
            stream.Write(zipBytes);

            stream.Flush();
            client.Close();
            continue; // ✅ IMPORTANTE
        }

        if (File.Exists(requestedFile))
        {
            byte[] body = File.ReadAllBytes(requestedFile);
            string contentType = GetContentType(requestedFile);

            bool descargaZip = query.Contains("download=zip", StringComparison.OrdinalIgnoreCase);
            bool descargaGzip = query.Contains("download=gzip", StringComparison.OrdinalIgnoreCase);


            // Si el cliente acepta GZIP → comprimimos
            if (aceptaGzip && !descargaGzip)
            {
                // Modo normal → Comprimir solo si el navegador lo acepta
                body = CompressGzip(body);
                string header = $"HTTP/1.1 200 OK\r\nContent-Type: {contentType}\r\nContent-Encoding: gzip\r\nContent-Length: {body.Length}\r\n\r\n";
                stream.Write(Encoding.UTF8.GetBytes(header));
                stream.Write(body);
            }
            else if (descargaGzip)
            {
                // Modo descarga forzada en GZIP
                byte[] gz = CompressGzip(body);

               
                string header =
                    $"HTTP/1.1 200 OK\r\n" +
                    $"Content-Type: application/gzip\r\n" +
                    $"Content-Disposition: attachment; filename=\"{Path.GetFileName(requestedFile)}.gz\"\r\n" +
                    $"Content-Length: {gz.Length}\r\n\r\n";


                stream.Write(Encoding.UTF8.GetBytes(header));
                stream.Write(gz);

                stream.Flush();
                client.Close();
                continue;   // 👈 MUY IMPORTANTE
            }
            else
            {
                // Normal sin compresión
                string header = $"HTTP/1.1 200 OK\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\n\r\n";
                stream.Write(Encoding.UTF8.GetBytes(header));
                stream.Write(body);
            }
        }
        else
        {
            // Log de GET fallido
            LogRequest(clientIp, method, pathOnly, "Archivo no encontrado");

            string notFoundPath = Path.Combine(wwwroot, "404.html");
            byte[] body;

            if (File.Exists(notFoundPath))
                body = File.ReadAllBytes(notFoundPath);
            else
                body = Encoding.UTF8.GetBytes("<h1>404 - Archivo no encontrado</h1>");

            string contentType = GetContentType("404.html");
            string header = $"HTTP/1.1 404 Not Found\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\n\r\n";
            stream.Write(Encoding.UTF8.GetBytes(header));
            stream.Write(body);
        }
    } 


    else if (method == "POST")
    {
        // Obtener Content-Length
        int contentLength = 0;
        foreach (var line in requestLines)
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                contentLength = int.Parse(line.Split(':')[1].Trim());
                break;
            }
        }

        // Leer body ya recibido
        int headerEndIndex = requestText.IndexOf("\r\n\r\n") + 4;
        string body = "";
        if (bytesRead > headerEndIndex)
            body = Encoding.UTF8.GetString(buffer, headerEndIndex, bytesRead - headerEndIndex);

        // Leer el resto si es necesario
        if (body.Length < contentLength)
        {
            int remaining = contentLength - body.Length;
            byte[] restBuffer = new byte[remaining];
            int read = stream.Read(restBuffer, 0, remaining);
            body += Encoding.UTF8.GetString(restBuffer, 0, read);
        }

        // Logear POST
        LogRequest(clientIp, method, path, body);

        // Responder 200 OK
        string response = "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n";
        byte[] responseBytes = Encoding.UTF8.GetBytes(response);
        stream.Write(responseBytes, 0, responseBytes.Length);
    }

    client.Close();
}
