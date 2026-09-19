# Acceso del piloto Gamexid

Servicio separado de acceso para el frontend piloto, mediante autenticación de cookies y PasswordHasher de Microsoft. No sustituye ni conecta la API de inventario existente. El inventario sigue siendo local al navegador; no cargar datos empresariales sensibles hasta implementar y verificar la base compartida.

## Publicación

Compilar con .NET SDK 9: `dotnet publish Gamexid.Access/Gamexid.Access.csproj -c Release -r linux-x64 --self-contained true -o artifacts/access-linux`.
El runtime está fijado en 9.0.20. Mantenerlo actualizado y migrar a una versión soportada antes del fin de soporte de .NET 9.

Instalar el resultado en /opt/gamexid-access con usuario de sistema gamexid-access. Crear /etc/gamexid-access y /var/lib/gamexid-access/keys con permisos restringidos. Ejecutar una sola vez `Gamexid.Access --provision /etc/gamexid-access/password.hash` y escribir la contraseña por entrada estándar; nunca incluirla en argumentos, repositorio o frontend. El comando rechaza sobrescribir credenciales existentes.

El archivo de hash debe ser root:gamexid-access, modo 640. La carpeta de claves debe pertenecer al usuario del servicio, modo 700. Instalar deploy/gamexid-access.service y el fragmento Nginx de deploy/ en el dominio, validar Nginx y activar el servicio antes de publicar el frontend.

## Controles y límites

- Cuenta piloto admin@gamexid.com; contraseña configurada solamente en el servidor.
- Cookie Secure, HttpOnly y SameSite Strict, duración 8 horas.
- Peticiones POST requieren X-Gamexid: 1; no se permite CORS.
- Diez intentos de login por minuto, límite global del piloto.
- Escucha solamente en 127.0.0.1:5098 detrás de HTTPS.
- GET /api/access/health público; GET /api/access/me requiere sesión.
- No tiene alta, recuperación ni cambio de contraseña desde interfaz. Antes de ampliar a varios usuarios, integrar el proveedor de identidad y las autorizaciones de la API de inventario.
- No registrar contraseñas ni cookies. Respaldar las claves de protección y el hash fuera de Git.

La prueba scripts/verify-access.cjs del frontend verifica el servicio real y recibe la contraseña por entrada estándar.
