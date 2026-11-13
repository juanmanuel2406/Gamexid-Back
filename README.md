# Gamexid Backend

Solución: `Gamexid.sln`. Proyecto de inicio en Visual Studio: **Gamexid.Api**.

## Ejecutar localmente

```powershell
dotnet run --project Gamexid.Api
```

La API escucha en `http://localhost:5000`. Angular se inicia desde la carpeta
`Gamexid-Front` con `npm start`; su proxy envía `/api` al backend.

La conexión de Aiven está en los secretos locales de .NET, identificador
`gamexid-local-development`, fuera de los repositorios. El certificado CA tiene
una copia privada junto a esos secretos; no depende del archivo de Descargas.
No poner contraseñas en appsettings, el frontend, Git ni comandos compartidos.

Configuración admitida:

- `ConnectionStrings:GamexidDatabase`: cadena MySQL; o `MYSQL_URL`: URI de Aiven.
- `Database:ConnectionFile`: alternativa para leer la conexión de un archivo privado.
- `Database:CaCertificate`: ruta del certificado CA.
- `Database:ServerVersion`: versión de MySQL, actualmente 8.4.8.

Siempre se verifica el certificado y el nombre del servidor (`VerifyFull`).
Una conexión faltante no derriba el proceso: las operaciones devuelven 503 y
el estado informa el problema, sin fingir una base disponible.

## Base de datos

```powershell
dotnet run --project Gamexid.Api -- --check-db
dotnet run --project Gamexid.Api -- --migrate
```

Las migraciones originales se conservaron con sus identificadores. La nueva
migración agrega relaciones y restricciones; no borra tablas ni registros.
`GamexidDbContextFactory` sirve a las herramientas de migración; no es otra base
de datos ni un segundo contexto independiente.

El comando de migración también incorpora las sucursales oficiales que falten,
sin sobrescribir las existentes. La base nueva conserva el ID 2 para el depósito.
El administrador se crea explícitamente mediante `--create-admin`; ese comando
se niega a sobrescribir usuarios existentes.

## Scanner y PDFs

- `GET /api/products/lookup?code=...`: EAN, SKU o serial registrado. Las
  coincidencias ambiguas se rechazan. Un serial desconocido no permite deducir
  un producto; hay que vincularlo mediante EAN/SKU o un catálogo autorizado.
- Los ingresos se guardan en una transacción y relacionan movimiento, producto,
  unidad, sucursal y usuario. No se acepta un serial ya ingresado.
- `POST /api/documents/extract`: PdfPig, sesión autenticada, hasta 10 MB y 50
  páginas. El PDF no se envía a Google ni se conserva automáticamente.
- La extracción reconoce tablas con encabezados y líneas etiquetadas, devuelve
  cantidades y precios cuando son identificables y exige revisión. Un PDF que
  contiene solamente imágenes necesita OCR. Impuestos y descuentos no se infieren.

## Alcance y publicación

Inventario, usuarios, productos, sucursales, unidades e ingresos usan MySQL.
Los pedidos y controles de linaje/auditoría del frontend todavía usan el navegador.
Los datos históricos locales se conservaron, pero no se importaron automáticamente.
Los pedidos y auditorías del piloto anterior permanecen en sus almacenes originales.
El modo MySQL usa almacenes locales separados para esos dos módulos: así un ID
antiguo no vincula por error una sucursal o un serial diferente de la nueva base.

Estos cambios no se publicaron en Ubuntu ni se subieron a GitHub. El servicio
`Gamexid.Access` se conserva como acceso del piloto anterior; para el entorno nuevo
se inicia `Gamexid.Api`, que incluye autenticación mediante cookies ASP.NET Core.
Antes de publicar, configurar secretos y CA en Ubuntu, revisar el proxy de `/api`
y permitir cargas PDF de hasta 11 MB. No reutilizar el límite anterior de 4 KB.

## Catálogo Gaming City

Las consultas se realizan desde el servidor con `GamingCitySaaS:Iue` (variable de
entorno `GamingCitySaaS__Iue`). Guardar su valor en el almacén de secretos .NET para
desarrollo y en un archivo privado del servicio para producción. Nunca ponerlo en
Git, Angular, argumentos de URL del navegador ni documentación.

- `GET /api/access/gc/status`: configuración presente, sin afirmar conexión.
- `GET /api/access/gc/products?search=...`: búsqueda de 3 a 80 caracteres.
- `GET /api/access/gc/products/{id}`: detalle para depósito 1.
- Requieren sesión y limitación de consultas. Solo lectura; no sincronizan inventario.
- El proveedor devuelve `estado` y `resultados`; `solicitud` contiene la credencial
  y se descarta. Solo se publican ID, nombre, código, código de proveedor, SKU,
  precio final y stock. No se publican costos ni otros campos del proveedor.
- Máximo 8 MB por respuesta y 100 resultados visibles; la respuesta indica truncación.
- Redirecciones y logs HTTP del conector desactivados.
- `dotnet run --project Gamexid.Api -- --check-gc` comprueba catálogo y detalle
  reales usando la configuración privada, sin mostrar secretos ni modificar datos.

El frontend actual requiere Gamexid.Api y su conexión MySQL; Gamexid.Access aislado
no implementa los endpoints de inventario. Al publicar, verificar todas las rutas
`/api/`, HTTPS, sesión y configuración privada antes de cambiar el frontend.

## Publicación del 12 de octubre de 2026

Producción: https://gamexid.goodlycomunicacion.com.

- Gamexid.Api escucha solo en 127.0.0.1:5099; servicio gamexid-api.
- Se conservan login/logout del servicio gamexid-access en 127.0.0.1:5098 para
  mantener la contraseña existente. /api/access/me y los datos usan la API nueva.
- La identidad del administrador del piloto (ID 1) se verificó contra Aiven;
  ambos servicios usan el mismo nombre de aplicación y directorio de claves de sesión.
- El resto de /api/ se dirige a Gamexid.Api. La extracción PDF tiene su límite propio.
- Configuración privada en /etc/gamexid-api/appsettings.Production.json, permisos
  root:gamexid-access 640; enlace desde la carpeta de versión. CA en el mismo directorio.
- El conector identifica las consultas con User-Agent Gamexid/1.0 y Accept application/json.
  El proveedor rechazaba consultas desde el VPS sin estos encabezados. HTTP 429 se
  muestra como límite temporal del proveedor, sin reintentos automáticos agresivos.
- No se hicieron migraciones ni escrituras de inventario; la comprobación autenticada
  fue de solo lectura con una sesión temporal. Se conservó el dominio antiguo retirado.

Los archivos deploy/gamexid-api.service y deploy/nginx-gamexid-api.conf reflejan
esta instalación. Revisar las rutas de versión antes de reutilizarlos en otra máquina.

## Asistente de tecnología

`POST /api/chat/ask` conserva su contrato y requiere sesión y `X-Gamexid: 1`.
Funciona en Productos, Pedidos y Armá tu PC. Solo consulta datos; los pedidos
se filtran por la sucursal autorizada en el servidor, nunca por una indicación del modelo.

Puede explicar conceptos con conocimiento general, consultar el catálogo y buscar
información pública de tecnología aunque el modelo no esté en Gaming City.
Distingue catálogo, pedidos, evidencia web y conocimiento general pendiente de verificar.
Los precios, stock y compatibilidades específicas no se completan de memoria.

- `InvestigarWeb`: consulta técnica pública de 3 a 200 caracteres.
- `InvestigarProductoWeb`: investigación de un producto consultado en el catálogo.
- Ambas comparten tres búsquedas por mensaje. Se admiten seis pasos del modelo
  y hasta doce llamadas a herramientas, dentro del límite de 90 segundos.
- La búsqueda libre bloquea patrones de datos privados y claves configuradas;
  no se habilita después de consultar pedidos en el mismo mensaje.
- Solo se publican fragmentos web con referencias verificadas en los metadatos de Google.
  Las referencias se numeran y agrupan para conservar las tres investigaciones en el frontend existente.
- Si falta cuota web, se muestra el aviso y se puede responder con conocimiento general,
  sin presentarlo como información investigada.

`Gemini:Model` configura el modelo principal y `Gemini:SearchModel` el de búsqueda;
vacío reutiliza el principal. `Gemini:ThinkingLevel` vacío conserva el comportamiento
predeterminado; acepta `minimal`, `low`, `medium` o `high` para modelos Gemini 3 que
admitan ese nivel. Verificar soporte del modelo antes de cambiarlo.
`Gemini:Enabled` y `Gemini:ApiKey` permanecen en secretos locales o configuración
privada del VPS. No se incluyen claves en el repositorio ni en Angular.

`GET /api/chat/status` informa si está configurado. Hay ocho preguntas por minuto
por usuario y tres consultas simultáneas. El proxy debe permitir 95 segundos.
Las sugerencias de Google se muestran en el iframe aislado ya existente.

Verificación:

```powershell
dotnet build Gamexid.sln -c Release
dotnet test Gamexid.Tests/Gamexid.Tests.csproj -c Release
```
