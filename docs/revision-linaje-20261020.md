# Revisión de Linaje

El módulo `features/audit` del frontend consulta productos y unidades de la API, pero guarda asociaciones de gabinetes en `gx_lineage_mysql_v1` y verificaciones de devoluciones en `gx_audit_mysql_v1`, ambos en `localStorage`.

No hay tablas, claves foráneas ni endpoints de Linaje en el backend. No interviene en pedidos, recepción ni cálculo de stock. Sus registros son específicos del navegador, se pueden borrar o modificar localmente y no constituyen una auditoría compartida.

Recomendación: deprecar las asociaciones y verificaciones locales. Si se necesita trazabilidad de gabinetes, reemplazarlas por registros persistentes con usuario, fecha, permisos e historial en la base de datos. No importar automáticamente los datos locales.

Esta actualización conserva la pantalla y los datos existentes: la solicitud pide evaluar la función, no eliminar registros o incorporar un módulo nuevo.
