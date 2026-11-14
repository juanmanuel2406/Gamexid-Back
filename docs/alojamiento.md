# Alojamiento de Gamexid

Verificado por SSH el 13 de noviembre de 2026.

| Parte | Ubicación | Función |
| --- | --- | --- |
| Frontend Angular | VPS de Goodly, Nginx, https://gamexid.goodlycomunicacion.com | Sirve HTML, JavaScript, estilos e imágenes compilados. |
| Backend .NET 9 | Mismo VPS, servicio `gamexid-api`, `127.0.0.1:5099` | Recibe las solicitudes `/api` enviadas por Nginx. |
| Acceso heredado | Servicio `gamexid-access`, `127.0.0.1:5098` | Conserva login/logout y las cookies compartidas. |
| MySQL | Servicio administrado Aiven | Guarda usuarios, sedes, pedidos, productos propios e inventario. Conexión TLS con certificado verificado. |
| Código fuente | GitHub: `juanmanuel2406/Gamexid-Back` y `Gamexid-Front` | Historial y colaboración; GitHub no ejecuta estos servicios. |
| Catálogo | API externa de Gaming City | Catálogo y stock del portal por sede. |
| Asistente | API de Gemini, Google | Interpretación de preguntas y llamada a herramientas de solo lectura. |

## Rutas de mantenimiento

- Raíz de Nginx: `/home/goodlyadmin/web/juanmi.goodlycomunicacion.com/public_html`.
  El nombre de esa carpeta es histórico; el dominio público es `gamexid.goodlycomunicacion.com`.
- El backend se publica en `/opt/gamexid/releases/<version>/api`.
  `systemctl show gamexid-api -p WorkingDirectory -p ExecStart` informa la versión activa.
- Configuración privada del backend: `/etc/gamexid-api/appsettings.Production.json`.
  Las claves y contraseñas no se incluyen en esta documentación ni en Git.
- En desarrollo se utilizan User Secrets, identificador `gamexid-local-development`.
- La carpeta local definitiva del backend es `Gamexid-Back`; se retiró la copia duplicada `back`.

## Recorrido

El navegador recibe Angular desde Nginx. Las solicitudes `/api` llegan al backend,
que valida la sesión y consulta MySQL, Gaming City o Gemini según la operación.
El navegador no recibe las credenciales de esos servicios.

La entrega de sucursales incluye backend y frontend: la interfaz por pasos utiliza
`/api/pc-builder/options` y el catálogo paginado utiliza `/api/catalog/products`.
Ambos reciben la sede local seleccionada. Nginx permite hasta 95 segundos para
consultas de armado que necesitan comprobar varios productos del proveedor.
