# Armado de PC y asistente por sucursal

Actualizado el 14 de noviembre de 2026. Backend y frontend comparten la sucursal
seleccionada. El armado utiliza el diseño por pasos integrado en esta entrega.

## Identificación y permisos

`branchId` en `/api/pc-builder/*` es el ID de la sucursal de Gamexid, obtenido de
`/api/branches` y del contexto de trabajo. No es el ID del portal ni del depósito.
El servidor relaciona `Branch.Code` con el ID del portal. El mapeo predeterminado
cubre las 16 sucursales iniciales y puede ajustarse en configuración privada con
`GamingCitySaaS:BranchMap:<codigo>`. Una sede sin vínculo o sin credencial devuelve
un error: nunca se reemplaza silenciosamente por el depósito.

Operadores y encargados solo usan su sucursal asignada. El administrador puede
seleccionar otra sucursal activa. Si falta `branchId`, se utiliza la asignación
del usuario por compatibilidad; si tampoco existe, devuelve 400. El frontend debe
enviarlo siempre para reflejar exactamente la selección de pantalla.

## Endpoints

Todos requieren sesión; los POST requieren `X-Gamexid: 1`.

- `POST /api/pc-builder/options`: productos del paso solicitado, con stock en la sede.
- `GET /api/pc-builder/prices/{id}?branchId=13`: precios disponibles para el producto.
- `GET /api/pc-builder/mercadolibre/{id}?branchId=13`: publicaciones; necesita autorización externa.
- `POST /api/pc-builder/confirm`: `{ "branchId": 13, "productId": "123", "offerId": "..." }`.
- `POST /api/pc-builder/quote`: `{ "branchId": 13, "items": [...] }`.

Los números de los ejemplos son ilustrativos: tomar IDs reales de la API.
No se añadieron tablas ni migraciones; los presupuestos no reservan stock.

## Selección por pasos

Orden sugerido: `cpu`, `mother`, `ram`, `gpu`, `storage`, `psu`, `case`, `cooler`.
GPU y cooler requieren revisión según gráficos integrados y refrigeración incluida;
no deben descartarse automáticamente por no ser obligatorios en el formulario.

Primera consulta:

```json
{ "branchId": 13, "slot": "cpu", "items": [] }
```

Después de elegir un procesador:

```json
{
  "branchId": 13,
  "slot": "mother",
  "items": [{ "slot": "cpu", "productId": "123", "quantity": 1 }],
  "includePending": true
}
```

`search` es opcional (3 a 80 caracteres cuando se envía); sin texto se busca la
categoría del paso. `quantity` sirve para exigir stock para la cantidad solicitada.
`items` debe contener la selección actual completa; al reemplazar un componente,
el servidor excluye su versión anterior y vuelve a comparar el resto.

- `missingSteps`: pasos necesarios antes de consultar. Mother requiere CPU; RAM
  requiere CPU y mother; los demás requieren CPU, mother y RAM.
- `products`: candidatos de la categoría, con stock suficiente. Los componentes
  con incompatibilidades conocidas quedan fuera.
- `checks`: evidencia y verificaciones pendientes por candidato. Socket coincidente
  no garantiza BIOS, potencia, conectores o espacio físico.
- `status`: `checked` solo si los controles devueltos no tienen pendientes;
  normalmente será `pending` porque aún hay verificaciones manuales.
- `includePending: false`: oculta los candidatos pendientes; puede devolver vacío.
- `selectionChecks`: revisión de los componentes actuales.
- `truncated`: el proveedor superó el límite de resultados; refinar la búsqueda.
- `branchId`, `branchName`, `checkedUtc`: identifican sede y momento de consulta.

No inferir “compatible al 100 %” a partir de una coincidencia parcial. No mezclar
ARS con USD. El presupuesto conserva los importes separados por moneda.

## Cambio de sucursal

El frontend debe cancelar las solicitudes anteriores, limpiar candidatos, precios,
publicaciones elegidas y presupuesto, y volver a validar los componentes en la
nueva sede. Los precios temporales se vinculan al usuario, producto y sucursal;
reutilizar un `offerId` de otra sede se rechaza.

El frontend cancela las consultas anteriores y reinicia el armado al cambiar la
sucursal. Productos, Pedidos, Sedes y el inicio comparten el selector global.
Los registros de pedidos e inventario propios permanecen separados del catálogo
externo; mostrar productos del portal no los ingresa automáticamente a MySQL.

## Catálogo por sucursal

`GET /api/catalog/products?branchId=13&page=1&pageSize=50` permite recorrer el
catálogo sin escribir una búsqueda. Acepta `search`, `onlyInStock` y `hideCombos`.
Devuelve `data`, `branchId` y `branchName`; `data.total` informa la cantidad de
productos y `data.products` contiene la página solicitada. `pageSize` admite
hasta 100 productos. La interfaz utiliza páginas de 50 y permite recorrerlas todas.
`GET /api/catalog/products/{id}?branchId=13` obtiene la ficha en la misma sede.
Ambos endpoints validan la sucursal autorizada y utilizan IDs locales.
El endpoint heredado `/api/access/gc/products` conserva sus IDs del portal.

## Asistente

El frontend ya envía `sucursalId` con el mensaje. BuscarProductos, VerFichaProducto
y ConsultarArmado ahora resuelven esa sucursal, identifican la fuente y devuelven
stock del portal. Un texto que pida otra sede no modifica la autorización.
El stock del portal no equivale a las unidades registradas en MySQL.
Las preguntas generales siguen disponibles sin consultar el catálogo.

Si el portal devuelve una ficha individual sin identidad, se busca el ID exacto
en el catálogo disponible de la misma sucursal. No se toma una ficha de otra sede;
si no existe allí, la consulta informa que el producto no está disponible.
