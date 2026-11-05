# Guía del código del backend Kiogloss

Esta guía sirve para ubicarte rápido en el código, aunque no seas programador. Cada archivo `.cs` también empieza con una
**"GUÍA RÁPIDA DE ESTE ARCHIVO"** que explica en pocas líneas qué hace.

## 1. La idea general en 30 segundos

El backend es una API: el frontend (tienda o panel admin) hace una **petición** a una dirección (ruta) y el backend responde con **JSON**.

```
Frontend ──petición──▶ [Revisiones de seguridad] ──▶ Controlador ──▶ Servicio ──▶ Base de datos
                                                   (recibe)      (decide)     (guarda/lee)
Frontend ◀──JSON────────────────────────────────── Controlador ◀── Servicio
```

| Capa                    | Pregunta que responde                                          | Carpeta                             |
| ----------------------- | -------------------------------------------------------------- | ----------------------------------- |
| Revisiones de seguridad | ¿Quién eres? ¿Puedes entrar? ¿Hubo un error?                   | `Compat/Middleware.cs`, `Security/` |
| Controlador             | ¿Qué dirección se pidió y con qué datos?                       | `Controllers/`                      |
| Servicio                | ¿Qué reglas del negocio hay que aplicar?                       | `Services/`                         |
| Modelo                  | ¿Cómo es cada tabla de la base de datos? (una clase por tabla) | `Models/`, `Data/`                  |
| Formatos de datos (DTO) | ¿Qué campos entran y salen por la API?                         | `Dtos/`                             |

**Regla de oro:** el controlador NO decide nada, solo recibe y devuelve. Las reglas del negocio van en el servicio.

## 2. Mapa de carpetas

Todo está en `src/Kiogloss.Api/`.

| Carpeta / archivo           | Qué contiene                                                                                  | Tócalo cuando…                                             |
| --------------------------- | --------------------------------------------------------------------------------------------- | ---------------------------------------------------------- |
| `Program.cs`                | Encendido del servidor, piezas del sistema y orden de las revisiones                          | sumas una pieza nueva o cambias el orden de las revisiones |
| `Controllers/`              | Las rutas (endpoints)                                                                         | agregas o cambias una ruta                                 |
| `Services/`                 | Reglas del negocio                                                                            | cambias cómo funciona algo (stock, pedidos, favoritos…)    |
| `Models/`                   | Una clase por tabla de la base de datos                                                       | agregas una columna o tabla                                |
| `Data/KioglossDbContext.cs` | Conexión con la base de datos y lista de tablas                                               | agregas una tabla nueva                                    |
| `Dtos/`                     | Formatos de los datos que entran y salen                                                      | una pantalla necesita un campo nuevo                       |
| `Mappers/`                  | Transforman los datos de la base al formato de la respuesta                                   | cambias cómo se arma una respuesta del admin               |
| `Security/`                 | Contraseñas, llaves de sesión (tokens) y permisos por ruta                                    | cambias quién puede entrar a qué                           |
| `Configuration/Options.cs`  | Lista de ajustes disponibles                                                                  | agregas un ajuste nuevo                                    |
| `Background/`               | Tareas automáticas (latido, datos iniciales)                                                  | cambias tareas que corren solas                            |
| `Compat/`                   | Compatibilidad con el sistema anterior (Java): errores, formato de datos, páginas, validación | **casi nunca**; ver aviso abajo                            |
| `../../db/schema.sql`       | Esquema de la base de datos                                                                   | montas una base nueva                                      |
| `../../tests/`              | Pruebas automáticas                                                                           | cambias el comportamiento de un endpoint                   |

> **Aviso sobre `Compat/`:** este backend es una migración de Java (Spring) a .NET y los frontends no se tocaron. Por eso hay código
> que reproduce al detalle formatos, orden de listas y mensajes de error del backend viejo. Modificarlo puede romper los frontends.

## 3. Guía de rutas (endpoints)

Todas las rutas empiezan con `/API/V1.0` (ver `Server:ContextPath`). Ejemplo: `/API/V1.0/products`.

**Quién puede entrar** (lo decide `UrlAuthorizationMiddleware` en `Security/Auth.cs`):
- 🟢 **Pública**: sin token.
- 🔵 **Usuario**: requiere token (cualquier usuario con sesión).
- 🔴 **Admin**: requiere token de un administrador. Si no, responde 403 vacío.

### Autenticación — `Controllers/PublicControllers.cs` → `AuthController` → `Services/UserServices.cs`
| Método | Ruta                    | Acceso | Qué hace                                     |
| ------ | ----------------------- | ------ | -------------------------------------------- |
| POST   | `/login`                | 🟢      | Email + contraseña → tokens                  |
| POST   | `/refresh`              | 🟢      | Renueva el token de acceso                   |
| POST   | `/auth/forgot-password` | 🟢      | Envía correo con enlace de recuperación      |
| POST   | `/auth/reset-password`  | 🟢      | Cambia la contraseña con el token del correo |

### Catálogo público — `PublicControllers.cs` → `ProductController` → `Services/CatalogServices.cs`
| Método | Ruta              | Acceso | Qué hace                                        |
| ------ | ----------------- | ------ | ----------------------------------------------- |
| GET    | `/products`       | 🟢      | Lista de productos (filtros, orden, paginación) |
| GET    | `/article/{slug}` | 🟢      | Detalle de un producto                          |
| GET    | `/tags`           | 🟢      | Etiquetas                                       |
| GET    | `/videos`         | 🟢      | Videos publicados                               |

### Usuarios — `PublicControllers.cs` → `UserController` → `UserService`
| Método | Ruta                       | Acceso                                                  | Qué hace                   |
| ------ | -------------------------- | ------------------------------------------------------- | -------------------------- |
| POST   | `/user/` (con barra final) | 🟢                                                       | Registrar un usuario nuevo |
| GET    | `/user/` (con barra final) | 🟢 según `PermitExact` (revisar si debería exigir token) | Listar usuarios            |
| GET    | `/user/{pk}`               | 🔵                                                       | Detalle del usuario        |
| PATCH  | `/user/update/{pk}`        | 🔵                                                       | Actualizar perfil          |

### Favoritos — `PublicControllers.cs` → `FavoriteController` → `FavoriteService`
| Método | Ruta                                   | Acceso | Qué hace                |
| ------ | -------------------------------------- | ------ | ----------------------- |
| POST   | `/user/favorite`                       | 🔵      | Agregar favorito        |
| DELETE | `/user/delete/{pk}`                    | 🔵      | Quitar favorito         |
| GET    | `/user/account/account_favorites/{id}` | 🔵      | Favoritos de una cuenta |

### Pedidos y carrito — `Controllers/CustomerControllers.cs`
| Método | Ruta              | Acceso | Qué hace                                                    |
| ------ | ----------------- | ------ | ----------------------------------------------------------- |
| POST   | `/user/order`     | 🔵      | Crear pedido (descuenta stock) → `OrderService.CreateOrder` |
| GET    | `/user/orders`    | 🔵      | Historial de pedidos del usuario                            |
| GET    | `/user/cart`      | 🔵      | Ver carrito                                                 |
| POST   | `/user/cart`      | 🔵      | Agregar al carrito                                          |
| PUT    | `/user/cart/{id}` | 🔵      | Cambiar cantidad                                            |
| DELETE | `/user/cart/{id}` | 🔵      | Quitar un producto                                          |
| DELETE | `/user/cart`      | 🔵      | Vaciar el carrito                                           |

### Notificaciones — `CustomerControllers.cs` → `NotificationController` → `NotificationService`
Existen dos juegos idénticos: `/user/notifications…` (🔵) y `/admin/notifications…` (🔴).
| Método | Sufijo          | Qué hace                                      |
| ------ | --------------- | --------------------------------------------- |
| GET    | `/stream`       | Canal en vivo (SSE). El token va en `?token=` |
| GET    | `` (vacío)      | Lista                                         |
| GET    | `/unread-count` | Cantidad sin leer                             |
| PATCH  | `/{id}/read`    | Marcar una como leída                         |
| PATCH  | `/read-all`     | Marcar todas                                  |
| DELETE | `/{id}`         | Borrar                                        |

### Panel admin — `Controllers/AdminControllers.cs` → `AdminController` → `Services/AdminService.cs` (todas 🔴)
| Recurso                       | Rutas (bajo `/admin`)                                                                                          |
| ----------------------------- | -------------------------------------------------------------------------------------------------------------- |
| Archivos                      | `POST /upload`                                                                                                 |
| Productos                     | `GET/POST /products`, `GET/PUT/DELETE /products/{id}`, `POST /products/images`, `DELETE /products/images/{id}` |
| Etiquetas                     | `GET/POST /tags`, `PUT/DELETE /tags/{id}`                                                                      |
| Tipos de variante             | `GET/POST /variants/types`, `PUT/DELETE /variants/types/{id}`                                                  |
| Usuarios                      | `GET /users`, `GET /users/{id}`, `PUT /users/{id}/roles`, `PATCH /users/{id}/toggle-status`                    |
| Pedidos                       | `GET /orders`, `GET /orders/{id}`, `GET /users/{userId}/orders`, `PATCH /orders/{id}/status`                   |
| Estadísticas                  | `GET /stats`                                                                                                   |
| Videos                        | `GET/POST /videos`, `PUT/DELETE /videos/{id}`                                                                  |
| Reportes (`ReportController`) | `GET /admin/reports/summary`, `/sales-by-day`, `/top-products`, `/orders-by-status`                            |

## 4. Recetas: "quiero hacer X, ¿dónde voy?"

| Quiero…                                                   | Pasos                                                                                                                                                                                            |
| --------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **Agregar una ruta nueva**                                | 1) Método con `[HttpGet("...")]` (o Post/Put/Delete/Patch) en el controlador. 2) Si es pública, agrégala a `PermitExact`/`PermitPrefixes` en `Security/Auth.cs`. 3) La lógica va en un servicio. |
| **Cambiar una regla de negocio**                          | Busca el método en `Services/` (la tabla de rutas te dice cuál).                                                                                                                                 |
| **Agregar un campo a un producto/usuario**                | 1) Columna en la base. 2) Propiedad en `Models/`. 3) Mapeo en `Data/KioglossDbContext.cs`. 4) Campo en el DTO.                                                                                   |
| **Cambiar un mensaje de error**                           | Donde se lanza (`throw …` en el servicio). Ojo: los frontends muestran estos textos.                                                                                                             |
| **Cambiar quién accede a una ruta**                       | `Security/Auth.cs` → `UrlAuthorizationMiddleware`.                                                                                                                                               |
| **Cambiar un ajuste (secretos, correo, umbral de stock)** | `appsettings.Local.json` o variables de entorno (ver `README.md`). Nunca en el código.                                                                                                           |
| **Entender un error 500**                                 | Lee el mensaje de la respuesta: casi siempre dice qué falló. Lo arma `GlobalExceptionMiddleware` (en `Compat/Middleware.cs`).                                                                    |

## 5. Buenas prácticas que se siguen (y conviene mantener)

1. **Cada parte hace una sola cosa:** el controlador recibe y responde, el servicio decide, el modelo representa los datos.
2. **Cada clase pide lo que necesita** y las piezas se registran en un solo lugar (`Program.cs`), en vez de crearse a mano en cualquier sitio.
3. **Nunca se muestra la base de datos tal cual:** las respuestas usan formatos propios (DTOs).
4. **Lo lento (base de datos, internet) se hace con `async/await`,** para que el servidor atienda a otros mientras espera.
5. **Los errores se manejan en un solo lugar:** el servicio avisa con un error claro (no encontrado, dato inválido...) y `GlobalExceptionMiddleware` arma la respuesta.
6. **Todo o nada:** las operaciones de varios pasos (hacer un pedido, registrar un usuario) se guardan completas o no se guarda nada.
7. **Lo secundario no rompe lo principal:** si falla un aviso, el pedido igual se crea.
8. **Los secretos no van en el código:** claves, correo y base de datos se leen de la configuración.
9. **Las contraseñas se guardan cifradas,** nunca en texto normal.
10. **Comentarios `///`** en clases y métodos importantes; comentarios `//` solo para explicar el *por qué* de algo que no es obvio.
11. **Nombres:** clases y métodos con la primera letra de cada palabra en mayúscula (`PedidoService`), variables empezando en minúscula; los controladores terminan en `Controller` y los servicios en `Service`.

## 6. Pruebas

```
cd Kiogloss-Backend-Net
dotnet test Kiogloss.sln
```
Las pruebas de `tests/Kiogloss.Api.Tests` comprueban que la API responde como se espera (rutas, códigos y formato de los datos).
Algunas necesitan Docker (levantan una base PostgreSQL temporal).
