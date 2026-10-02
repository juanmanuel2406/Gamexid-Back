# Conector de GamingCity

Contrato recibido: 30/09/2026. Sin credenciales en el repositorio.

Configurar `GamingCity__AccessToken` en el entorno privado del servicio. Reiniciar únicamente gamexid-access después de configurar. No habilitar logs de URLs salientes: la operación GET de catálogo requiere access_token en la query según el contrato del proveedor.

Endpoints protegidos por la sesión existente:
- GET /api/access/gc/status: configuración, no certifica conectividad.
- GET /api/access/gc/branches: POST sucursales, action GetSucursales.
- GET /api/access/gc/products?search=...: GET saas, action productos, cantidad 20.

Respuestas externas: envoltorio response=ok y data. Campos de negocio pendientes de confirmación. No se importan registros automáticamente. Operaciones de escritura excluidas, incluso las marcadas como Consulta por el inventario documental cuando el nombre indica modificación.

Pruebas: dotnet run --project Gamexid.Access.Checks. Usan transporte simulado, sin credenciales reales.
