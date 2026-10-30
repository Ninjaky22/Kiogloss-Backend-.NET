# Kiogloss Backend (.NET)

Migración 1:1 de [`Kiogloss-Backend`](https://github.com/Ninjaky22/Kiogloss-Backend) (Spring Boot 3.5 / Java 17) a
ASP.NET Core sobre .NET 10. El objetivo es que **los frontends no noten el cambio**:

- [FrontendKioglossUser](https://github.com/Ninjaky22/FrontendKioglossUser)
- [FrontendKioglossAdmin](https://github.com/Ninjaky22/FrontendKioglossAdmin)

## Qué se conserva

Se mantiene igual que en Spring:

- **Rutas y servidor:** las mismas rutas bajo `/API/V1.0`, en el puerto 8000.
- **Contrato HTTP:** los mismos métodos, parámetros (`page_size`, `rangeDate_after`…) y nombres de campos JSON, con el **mismo orden de claves**.
- **Códigos de estado y errores:** iguales, incluidos los raros:
  - 403 vacío sin token. El panel admin refresca el token justamente ante ese 403.
  - 500 con credenciales incorrectas.
  - 401 solo en `GET /user/{pk}`.
- **Textos de error:** iguales, como `Error interno del servidor: java.lang.RuntimeException: Stock insuficiente…`. Los frontends los muestran tal cual.
- **Tokens JWT intercambiables:** HS512, claims `user_id`/`sub`/`iat`/`exp`. Un token emitido por Spring sirve en .NET y al revés, así que las sesiones abiertas sobreviven al cambio.
- **Contraseñas:** BCrypt `$2a$10$`; los hashes ya guardados siguen funcionando.
- **Base de datos:** la misma PostgreSQL y las mismas tablas, **sin cambios de esquema** (EF Core no ejecuta migraciones).
- **SSE de notificaciones:** las mismas tramas (`event:connected`, `event:notification`, `:heartbeat` cada 25 s).

La equivalencia está verificada con un harness que ejecuta los mismos ~180 pasos contra Spring y contra .NET y compara las respuestas (ver *Pruebas*).

## Estructura

| .NET | Equivalente Spring |
|---|---|
| `src/Kiogloss.Api/Controllers` | `controller/*` (mismos endpoints, uno a uno) |
| `src/Kiogloss.Api/Services` | `service/*` (mismos métodos y lógica) |
| `src/Kiogloss.Api/Models`, `Data/KioglossDbContext.cs` | `model/*` + mapeo JPA/Hibernate |
| `src/Kiogloss.Api/Dtos` | `dto/*`, `dto/admin/*` |
| `src/Kiogloss.Api/Mappers` | `mapper/*` |
| `src/Kiogloss.Api/Security` | `security/*`, `config/SecurityConfig.java` |
| `src/Kiogloss.Api/Compat` | Lo que Spring/Jackson/Hibernate hacían "gratis": formato de errores, `Page`, JSON, orden de `HashMap`, validación |
| `src/Kiogloss.Api/Background` | `@Scheduled` heartbeat y `DatabaseSeeder` |
| `db/schema.sql` | Esquema que crea Hibernate (`ddl-auto=update`), para entornos nuevos |

## Configuración

Las claves equivalen a las de `application.properties`. Se pueden poner en `appsettings.json`, en `appsettings.Local.json` (ignorado por git) o en variables de entorno (`Seccion__Clave`).

| Spring | .NET | Variable de entorno |
|---|---|---|
| `spring.datasource.*` | `ConnectionStrings:Default` | `ConnectionStrings__Default="Host=…;Port=5432;Database=store;Username=…;Password=…"` |
| `jwt.secret` | `Jwt:Secret` | `Jwt__Secret` (**debe ser el mismo secreto** para que los tokens sigan siendo válidos) |
| `jwt.access-token-expiration` / `refresh-…` | `Jwt:AccessTokenExpiration` / `Jwt:RefreshTokenExpiration` (ms) | |
| `cloudinary.*` | `Cloudinary:CloudName`, `ApiKey`, `ApiSecret` | `Cloudinary__ApiSecret`… |
| `spring.mail.*` | `Mail:Host`, `Port`, `Username`, `Password`, `StartTls` | `Mail__Password`… |
| `app.upload.dir` | `App:UploadDir` | `App__UploadDir` (apuntar a la **misma** carpeta `uploads/`) |
| `app.frontend-url` | `App:FrontendUrl` | |
| `notifications.low-stock-threshold` | `Notifications:LowStockThreshold` | |
| locale de la JVM | `App:Culture` (por defecto `es-CO`) | formato del monto en las notificaciones (`COP 90.000`) |
| zona horaria de la JVM | variable `TZ` del proceso | `TZ=America/Bogota` (fechas de órdenes, "hoy" en reportes, notificaciones) |
| `server.port` / `context-path` | `Urls` / `Server:ContextPath` | `Urls=http://0.0.0.0:8000` |

> Los secretos del `application.properties` original están commiteados en el repo de Spring. Hay que **rotarlos**
> (JWT, Cloudinary, contraseña de aplicación de Gmail, BD). Ver `docs/SECURITY-FINDINGS.md`.

## Ejecutar

```bash
# con el SDK de .NET 10
cd src/Kiogloss.Api && dotnet run

# o con Docker
docker build -t kiogloss-api .
docker run -p 8000:8000 \
  -e ConnectionStrings__Default="Host=host.docker.internal;Port=5432;Database=store;Username=postgres;Password=…" \
  -e Jwt__Secret="…" -e Cloudinary__ApiKey="…" -e Cloudinary__ApiSecret="…" \
  -e Mail__Username="…" -e Mail__Password="…" \
  -v /ruta/a/uploads:/app/uploads kiogloss-api
```

**Base de datos nueva:** aplicar `db/schema.sql` con `psql -f db/schema.sql`. Al arrancar, el seeder carga el catálogo de ejemplo si `store_product` está vacía; se desactiva con `App:SeedDatabase=false`.

## Probar con los frontends

**1. Backend + base de datos.** Elige una opción:

| Opción | Cuándo usarla | Comandos |
|---|---|---|
| **Docker Compose** (recomendada) | Probar o desplegar sin instalar .NET | `cp .env.example .env` (rellenar secretos) → `docker compose up -d --build` |
| **Dev Container (VS Code)** | Desarrollar y depurar el código C# | `docker compose up -d db`, luego en VS Code: *Dev Containers: Reopen in Container* → `cd src/Kiogloss.Api && dotnet run` |
| **SDK local** | Si instalas .NET 10 en tu máquina | `docker compose up -d db` → `cd src/Kiogloss.Api && dotnet run` (usa `appsettings.Local.json`) |

En las tres opciones la API queda en `http://localhost:8000/API/V1.0` y PostgreSQL en `localhost:5432`
(base `store`, usuario `postgres`). En una base nueva se aplica `db/schema.sql` y se carga el catálogo de ejemplo.

**2. Frontends.** Requieren Node 20+.

```bash
git clone https://github.com/Ninjaky22/FrontendKioglossUser.git
git clone https://github.com/Ninjaky22/FrontendKioglossAdmin.git
(cd FrontendKioglossUser  && npm ci && npx vite --port 5173)   # tienda  -> http://localhost:5173
(cd FrontendKioglossAdmin && npm ci && npx vite --port 5174)   # admin   -> http://localhost:5174/#/log-in
```

- La tienda lee `VITE_API_URL` de su `.env` (ya apunta a `localhost:8000`). El admin tiene `http://localhost:8000/API/V1.0` escrito a mano.
- La tienda debe quedar en el puerto **5173**, porque `App:FrontendUrl` lo usa para el enlace del correo de recuperación de contraseña.
- Las advertencias `npm warn allow-scripts … esbuild` de npm 11 son inofensivas.

**3. Cuenta de administrador.** El panel no puede crearla: su registro no envía `isSuperuser`. Se crea con la API, usando el mismo atajo que tenía Spring:

```bash
curl -X POST http://localhost:8000/API/V1.0/user/ -H 'Content-Type: application/json' \
  -d '{"email":"admin@kiogloss.com","name":"Admin","password":"Admin123!","phoneNumber":"3000000001","isSuperuser":true}'
```

**Recorrido E2E automático.** `tests/e2e/e2e.mjs` usa Playwright y Chromium headless y prueba:
- registro, catálogo, carrito, favoritos y órdenes en la tienda;
- en el admin: login, pedidos, cambio de estado y notificación en vivo al cliente por SSE;
- productos, usuarios y reportes;
- que ambos frontends renueven la sesión al expirar el token.

```bash
JWT_ACCESS_EXPIRATION_MS=70000 docker compose up -d --build   # tokens de 70 s para que el paso de refresh dure ~1 min
cd tests/e2e && npm install && npx playwright install chromium && node e2e.mjs
```

La creación de la orden se hace por la API con el mismo payload de `Checkout.jsx`, porque en la tienda la orden solo se crea después de pagar en el sandbox de PayPal.

## Pruebas

```bash
dotnet test                                                   # unitarias + integración (Testcontainers, requiere Docker)
tests/parity/run-parity.sh /ruta/a/Kiogloss-Backend           # paridad Spring vs .NET (requiere Docker, Java 17, python3)
```

**Pruebas unitarias e integración**
- Los valores esperados salen del lado Java: tokens generados con jjwt 0.12.3, hashes del `BCryptPasswordEncoder` real y respuestas capturadas del backend Spring.

**Harness de paridad (`tests/parity/run-parity.sh`)**
1. Levanta un PostgreSQL desechable.
2. Arranca Spring, que crea el esquema y carga la semilla.
3. Clona esa BD para .NET y arranca .NET.
4. Ejecuta `tests/parity/parity.py`.

El escenario reproduce las llamadas de los dos frontends y los errores importantes, y compara:
- código HTTP;
- `Content-Type`;
- JSON (valores, texto exacto de los números y orden de claves).

**Lo que el harness normaliza.** Solo lo que varía entre dos ejecuciones o lo que en Java es aleatorio:
- valores JWT;
- marcas de tiempo (se compara su forma);
- host de las URLs de subida;
- listas que Java itera desde un `HashSet` con hash de identidad (ver `docs/SECURITY-FINDINGS.md`).

**Lo que no ejercita (servicios externos reales).** Estos caminos quedan cubiertos solo por revisión de código:
- subida real a Cloudinary (`POST /admin/products/images`, `profileImage` en base64);
- envío real de correo (`/auth/forgot-password` con un email existente).

## Cutover y rollback

1. Configurar .NET con la **misma** BD, el **mismo** `Jwt:Secret`, la misma carpeta `uploads/` y `TZ=America/Bogota`.
2. Detener Spring y arrancar .NET en el puerto 8000. No hay cambios de esquema y los tokens son compatibles, así que las sesiones abiertas siguen funcionando.
3. Rollback: detener .NET y volver a arrancar Spring.

No hay que tener los dos backends a la vez detrás de un balanceador: las conexiones SSE viven en la memoria de cada proceso.
