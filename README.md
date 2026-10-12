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

## Pruebas

```powershell
dotnet test Gamexid.Tests
```

Las pruebas ordinarias usan SQLite temporal, no Aiven. La prueba real de MySQL
requiere habilitar explícitamente `GAMEXID_TEST_AIVEN=1`; crea registros con un
identificador QA único y los elimina al terminar, incluso si una comprobación falla.

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
