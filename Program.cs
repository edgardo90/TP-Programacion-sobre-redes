using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

// Leer el archivo de configuración config.json
string configText = File.ReadAllText("config.json");
using var json = JsonDocument.Parse(configText);
var root = json.RootElement;

// Obtener valores configurados desde config.json
string host = root.GetProperty("host").GetString()!;
int port = root.GetProperty("port").GetInt32();
string wwwroot = root.GetProperty("wwwroot").GetString()!;
string welcomeFile = root.GetProperty("welcomeFile").GetString()!;

// Crear el servidor en la IP y Puerto indicado
TcpListener server = new TcpListener(IPAddress.Parse("127.0.0.1"), port);
server.Start();
Console.WriteLine($"Servidor escuchando en http://{host}:{port}/ ...");

// Función para determinar el tipo MIME según la extensión del archivo
string GetContentType(string filePath)
{
    string extension = Path.GetExtension(filePath).ToLower();

    // Devuelve el MIME correcto según la extensión
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
        _ => "application/octet-stream", // Tipo genérico si no reconocemos la extensión
    };
}

// Bucle principal: atiende conexiones de clientes indefinidamente
while (true)
{
    // Espera que un cliente se conecte
    TcpClient client = server.AcceptTcpClient();
    Console.WriteLine(">> Cliente conectado");

    try // manejo los metodos async con try catch para manejar el error
    {
        // Se obtiene el stream para enviar y recibir datos
        using NetworkStream stream = client.GetStream();

        // Buffer para leer datos enviados por el navegador
        byte[] buffer = new byte[4096];
        int bytesRead = stream.Read(buffer, 0, buffer.Length);

        // Si no se leyó nada, continuar con el siguiente ciclo
        if (bytesRead == 0)
            continue;

        // Convertimos los bytes recibidos a texto (la solicitud HTTP)
        string requestText = Encoding.UTF8.GetString(buffer, 0, bytesRead);

        Console.WriteLine("=== Solicitud recibida ===");
        Console.WriteLine(requestText);
        Console.WriteLine("==========================");

        // La solicitud HTTP se divide por líneas
        string[] requestLines = requestText.Split("\r\n");
        if (requestLines.Length == 0)
            continue;

        // La primera línea contiene: método, ruta y versión HTTP
        string firstLine = requestLines[0];
        string[] parts = firstLine.Split(" ");

        if (parts.Length < 2)
            continue;

        string method = parts[0]; // Ej: GET
        string path = parts[1]; // Ej: /index.html

        // Si la ruta es solo "/", cargamos el archivo de bienvenida
        if (path == "/")
            path = "/" + welcomeFile;

        // Se construye la ruta completa al archivo dentro del wwwroot
        string filePath = Path.Combine(wwwroot, path.TrimStart('/'));

        // Si es un método GET (único soportado por ahora)
        if (method == "GET")
        {
            if (File.Exists(filePath))
            {
                // Leer el contenido del archivo
                byte[] body = File.ReadAllBytes(filePath);
                string contentType = GetContentType(filePath);

                // Construcción del header HTTP
                string header =
                    $"HTTP/1.1 200 OK\r\n"
                    + $"Content-Type: {contentType}\r\n"
                    + $"Content-Length: {body.Length}\r\n"
                    + $"Connection: close\r\n\r\n";

                // Enviar header y cuerpo al navegador
                stream.Write(Encoding.UTF8.GetBytes(header));
                stream.Write(body);
            }
            else
            {
                // Si no existe el archivo, devolvemos un 404
                string notFound = "<h1>404 - Archivo no encontrado</h1>";
                byte[] body = Encoding.UTF8.GetBytes(notFound);

                string header =
                    $"HTTP/1.1 404 Not Found\r\n"
                    + $"Content-Type: text/html; charset=utf-8\r\n"
                    + $"Content-Length: {body.Length}\r\n"
                    + $"Connection: close\r\n\r\n";

                stream.Write(Encoding.UTF8.GetBytes(header));
                stream.Write(body);
            }
        }
    }
    catch (Exception ex)
    {
        // Cualquier error al manejar el cliente se muestra aquí
        Console.WriteLine("Error manejando cliente: " + ex.Message);
    }
    finally
    {
        // Aseguramos que la conexión se cierre siempre
        client.Close();
    }
}
