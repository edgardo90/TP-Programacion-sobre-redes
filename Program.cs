using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Text.Json;

// 1. Leer el archivo de configuración
string configText = File.ReadAllText("config.json");
using var json = JsonDocument.Parse(configText);
var root = json.RootElement;

//Obtener valores configurables
string host = root.GetProperty("host").GetString()!;
int port = root.GetProperty("port").GetInt32();
string wwwroot = root.GetProperty("wwwroot").GetString()!;
string welcomeFile = root.GetProperty("welcomeFile").GetString()!;

// 2. Crear el socket servidor (TcpListener)
TcpListener server = new TcpListener(IPAddress.Parse("127.0.0.1"), port);
server.Start();
Console.WriteLine($"Servidor escuchando en http://{host}:{port}/ ...");

// Función para obtener el tipo MIME según la extensión del archivo
string GetContentType(string filePath)
{
    string extension = Path.GetExtension(filePath).ToLower();

    return extension switch
    {
        ".html" => "text/html; charset=utf-8",
        ".htm"  => "text/html; charset=utf-8",
        ".css"  => "text/css",
        ".js"   => "application/javascript",
        ".json" => "application/json",
        ".png"  => "image/png",
        ".jpg"  => "image/jpeg",
        ".jpeg" => "image/jpeg",
        ".gif"  => "image/gif",
        ".svg"  => "image/svg+xml",
        ".ico"  => "image/x-icon",
        _       => "application/octet-stream" // por defecto
    };
}

// 3. Bucle infinito para aceptar clientes
while (true)
{
    TcpClient client = server.AcceptTcpClient();
    Console.WriteLine(">> Cliente conectado");

    // 4. Leer solicitud
    using NetworkStream stream = client.GetStream();
    byte[] buffer = new byte[4096];
    int bytesRead = stream.Read(buffer, 0, buffer.Length);
    string requestText = Encoding.UTF8.GetString(buffer, 0, bytesRead);

    Console.WriteLine("=== Solicitud recibida ===");
    Console.WriteLine(requestText);
    Console.WriteLine("==========================");

    // 5. Procesar la solicitud HTTP
    string[] requestLines = requestText.Split("\r\n");
    string firstLine = requestLines[0]; // Ej: "GET /index.html HTTP/1.1"
    string[] parts = firstLine.Split(" ");

    string method = parts[0];   // GET o POST
    string path = parts[1];     // / o /archivo.html

    // Si no se pidió archivo, usar el archivo de bienvenida
    if (path == "/")
        path = "/" + welcomeFile;

    // Crear ruta absoluta al archivo solicitado
    string filePath = Path.Combine(wwwroot, path.TrimStart('/'));

    if (method == "GET")
    {
        if (File.Exists(filePath))
        {
            byte[] body = File.ReadAllBytes(filePath);

            // Obtener tipo MIME correcto
            string contentType = GetContentType(filePath);

            string header = $"HTTP/1.1 200 OK\r\n" +
                            $"Content-Type: {contentType}\r\n" +
                            $"Content-Length: {body.Length}\r\n\r\n";

            stream.Write(Encoding.UTF8.GetBytes(header));
            stream.Write(body);
        }
        else
        {
            //mirar lo que hay arriba para replicar qie lea un archivo de 404 en html
            string notFound = "<h1>404 - Archivo no encontrado</h1>";
            byte[] body = Encoding.UTF8.GetBytes(notFound);

            string header = $"HTTP/1.1 404 Not Found\r\n" +
                            $"Content-Type: text/html; charset=utf-8\r\n" +
                            $"Content-Length: {body.Length}\r\n\r\n";

            stream.Write(Encoding.UTF8.GetBytes(header));
            stream.Write(body);
        }
    }
    client.Close(); // por ahora cerramos, sin responder nada
}