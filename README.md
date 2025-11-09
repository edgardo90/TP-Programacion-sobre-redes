# Servidor Web en C# – Descargas y Front Interactivo

Este proyecto es un **servidor web simple en C#** que permite:

- Servir archivos desde la carpeta `wwwroot`.  
- Descargar archivos individuales en **gzip**
- Descargar la carpeta completa `wwwroot` como zip (`sitezip`).  
- Mostrar en el front la **query actual** y el **historial de queries**.  

---

## 🗂 Estructura del proyecto
TP-Programacion-sobre-redes/
│
├─ wwwroot/ # Carpeta con todos los archivos estáticos
│ ├─ index.html
│ ├─ style.css
│ └─ imágenes y otros archivos
│
├─ config.json # Configuración de host, puerto, wwwroot y welcomeFile
├─ Program.cs # Código del servidor
└─ README.md
---

## Configuración

Ejemplo de `config.json`:

{
  "host": "localhost",
  "port": 8080,
  "wwwroot": "wwwroot",
  "welcomeFile": "index.html"
}

---

## Comandos Principales
Ejecutar el servidor
dotnet run

## Acceder al front

Abrir en el navegador:

http://localhost:8080/

## Descargar archivos

Descarga el archivo seleccionado en formato .gz (por ejemplo, index.html):

http://localhost:8080/index.html?download=gzip


Zip de toda la carpeta wwwroot:

http://localhost:8080/?download=sitezip

---