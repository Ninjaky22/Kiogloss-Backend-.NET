# Hallazgos heredados del backend Spring

La migración replica el comportamiento de Spring **1:1**, a propósito, para que los frontends no noten el cambio.
Este documento lista lo que se copió tal cual aunque no sea correcto, para corregirlo después de la migración.
Para cada caso indica qué tocar y si el cambio afecta a los frontends.

## 1. Seguridad

| # | Hallazgo | Dónde (.NET) | Para corregir |
|---|---|---|---|
| S1 | `GET /user/` es **público** (`permitAll("/user/")`) y devuelve todos los usuarios con email, teléfono y dirección. | `UrlAuthorizationMiddleware`, `UserController.FindAllUsers` | Exigir ROLE_ADMIN o eliminar el endpoint. Ningún frontend lo usa. |
| S2 | `POST /user/` acepta `isSuperuser`: **cualquiera puede registrarse como administrador**. | `UserService.CreateUser` | Ignorar `isSuperuser` en el registro público. Los frontends no lo envían. |
| S3 | Faltan controles de propiedad (IDOR). Cualquier usuario autenticado puede actuar sobre datos ajenos en: `PATCH /user/update/{pk}`, `PUT/DELETE /user/cart/{id}`, `POST /user/favorite` (`account` en el body), `DELETE /user/delete/{pk}`, `GET /user/account/account_favorites/{id}` y `POST /user/order` (`account` en el body). | controladores / servicios correspondientes | Comparar con el usuario del token. Los frontends ya envían sus propios ids, así que no se rompen. |
| S4 | Un usuario **desactivado** (`is_active=false`) conserva el acceso mientras su token sea válido; solo se bloquea el login. | `SpringJwtAuthenticationHandler` | Rechazar el token si `!IsEnabled()`. |
| S5 | El refresh token es un JWT idéntico al access token (solo cambia `exp`), sirve como access token y no se puede revocar. | `JwtUtil` | Añadir un claim `typ` y revocación. Hay que coordinarlo con los frontends. |
| S6 | Secretos commiteados en `application.properties` del repo Spring: `jwt.secret`, `cloudinary.api-secret`, contraseña de aplicación de Gmail, contraseña de la BD. | — | **Rotarlos** y pasarlos por variables de entorno. Rotar `jwt.secret` cierra todas las sesiones. |
| S7 | `DELETE /admin/videos/{id}` borra el archivo local indicado por la URL guardada sin normalizar la ruta (`…/uploads/../../x` permite path traversal). | `FileStorageService.DeleteIfLocal` | Verificar que la ruta final quede dentro de `UploadDir`. |
| S8 | Los errores 500 exponen nombres de clases y excepciones internas (`Error interno del servidor: org.springframework…`), incluido SQL. | `GlobalExceptionMiddleware` | Los frontends muestran `message` (p. ej. "Stock insuficiente…", "Este enlace ya fue utilizado"), así que conviene introducir códigos de error estables y ajustar los textos que muestran. |
| S9 | Una petición sin autenticar (o con token expirado) recibe **403**, no 401. | `UrlAuthorizationMiddleware` | **No cambiar solo en el backend:** `FrontendKioglossAdmin/src/services/httpClient.ts` refresca el token únicamente ante un 403. Hay que cambiar ambos a la vez. |
| S10 | La búsqueda de productos no escapa `%` ni `_` en `LIKE`. | `ProductService.SearchByWords`, `AdminService.GetAllProducts` | Escapar los comodines. |
| S11 | CORS abierto (`*`) para todos los orígenes y métodos. | `SpringCorsMiddleware` | Restringir a los dominios de los frontends. |

## 2. Bugs heredados

| # | Comportamiento | Detalle |
|---|---|---|
| B1 | `GET /user/orders` con `rangeDate_after` o `rangeDate_before` **siempre responde 500**. | El JPQL `(:startDate IS NULL OR o.date >= :startDate)` hace que Hibernate envíe el parámetro sin tipo y PostgreSQL lo rechaza (`could not determine data type of parameter $4`). Se reproduce en `OrderService.GetUserOrders` con el mismo mensaje. Ningún frontend envía fechas. Para habilitar el filtro basta con quitar ese `throw`. |
| B2 | Credenciales incorrectas en `/login` responden **500**, no 401. | `BadCredentialsException` cae en el handler genérico. El frontend de usuario lo trata como error genérico. |
| B3 | Errores de validación (`@Valid`), JSON mal formado y parámetros obligatorios ausentes responden **500**. | `@ExceptionHandler(Exception.class)` los captura antes que el manejo estándar de Spring. |
| B4 | `PATCH /admin/users/{id}/roles` no existe (solo `PUT`). | Responde 500 `HttpRequestMethodNotSupportedException`. El admin actual usa `PUT`. |
| B5 | `POST /user/order` con un producto sin stock deja creada la notificación "Nueva orden recibida" a los admins aunque la orden se revierta. | Las notificaciones se guardan en una transacción `REQUIRES_NEW` (replicada con un `DbContext` aparte). |
| B6 | `DashboardStatsDTO` solo rellena `totalUsers` y `activeUsers`; el resto sale `null`. | Igual que en Spring. |
| B7 | `GET /user/notifications/stream` sin `Accept: text/event-stream` responde 500 `HttpMediaTypeNotAcceptableException`. | `EventSource` envía ese header, así que no afecta a los frontends. |
| B8 | Las rutas con barra final (salvo `/user/`) y las rutas inexistentes responden 500 `NoResourceFoundException` si el usuario tiene permiso, y 403 si no. | Semántica de Spring 6 replicada en `SpringRoutingMiddleware`. |

## 3. Comportamientos aleatorios en Java (y qué hace .NET)

En Spring, varias colecciones de entidades son `HashSet` de clases **sin `equals/hashCode`**, que usan el hash de identidad. Su orden de iteración cambia en cada ejecución de la JVM. Como ningún frontend puede depender de algo aleatorio, .NET usa **orden por id**:

- **Opciones de una variante:** `options`, y el texto `variantDetails`/`size`, que es su unión con `", "`.
- **Variantes de un producto** en las respuestas de admin, y también el orden en que se insertan al crear varias a la vez.
- **Items de una orden** (`GET /admin/orders/{id}`).
- **`defaultVariantId` en favoritos:** se toma con `findFirst()` sobre las variantes, así que en productos con varias variantes la elección es aleatoria.
- **`recentOrders`:** se ordenan por fecha descendente; los empates conservan el orden aleatorio del `HashSet`.

Las colecciones con `hashCode` determinista sí se replican **exactamente**, simulando `java.util.HashMap`. Según los casos medidos contra Spring:

- **Atributos de producto** (`Map`): mismo hash de `String` y misma tabla que dimensiona Hibernate para las filas cargadas.
- **Tags y primera imagen del producto** (`Set` con `hashCode` de Lombok).
- **Claves del item del carrito** (`new HashMap<>()`).

Por último, Jackson ordena las propiedades de `PageImpl` (`first` / `numberOfElements`) por reflexión, y ese orden cambia entre ejecuciones de la JVM. .NET usa un orden fijo.
