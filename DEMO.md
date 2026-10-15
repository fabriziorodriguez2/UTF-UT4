# Demo TFU UT3 (Grupo 3) — paso a paso

## Antes de arrancar

1. Tener Docker Desktop instalado y abierto.
2. Abrir una terminal y pararse en la carpeta del repo:
   ```bash
   cd UT3-TFUGrupo3
   ```
3. Correr y dejar esta terminal abierta (tarda unos minutos la primera vez):
   ```bash
   docker compose up --build
   ```
   > Si el puerto **8080** está ocupado en tu máquina, levantá el gateway en otro
   > puerto y usalo en los scripts:
   >
   > ```bash
   > GATEWAY_PORT=8090 docker compose up --build      # terminal 1
   > export GW=http://localhost:8090                  # terminal 2 (antes de los scripts)
   > ```
4. Abrir una **segunda terminal** y pararse en la misma carpeta. Todos los
   comandos de acá en adelante van en esta segunda terminal.
   Los scripts necesitan `curl` y `jq` instalados.

## 1. Componentes e interfaces

1. Correr:
   ```bash
   ./scripts/demo-01-componentes.sh
   ```
2. Va a registrar un usuario, crear un proyecto, crear una tarea, asignarla y
   listar las tareas del proyecto — todo pasando por el ApiGateway.
3. Correr también:
   ```bash
   docker compose ps
   ```
   Tiene que mostrar 4 componentes (`apigateway`, `usermanager`,
   `projectmanager`, `taskmanager`) más 3 bases, todos en estado `Up`.
4. Cada paso del script habla con un componente distinto: crear proyecto hace
   que ProjectManager le pregunte a UserManager si el dueño existe; crear
   tarea hace que TaskManager le pregunte a ProjectManager y a UserManager.

## 2. Escalabilidad horizontal

1. Correr:
   ```bash
   ./scripts/demo-02-escalabilidad.sh
   ```
2. Escala TaskManager a 3 réplicas y hace 30 pedidos seguidos al mismo
   endpoint (registra su propio usuario y proyecto).
3. Al final imprime una lista agrupada por hostname (header `X-Served-By`).
   Tienen que aparecer los **3 hostnames** distintos: el gateway abre una
   conexión nueva por pedido y Docker reparte entre las réplicas.

## 3. Contenedores

1. Correr:
   ```bash
   docker compose ps
   ```
   y mostrar que cada componente corre en su propio contenedor.
2. Abrir el archivo `docker-compose.yml` y mostrar que cada componente tiene
   su propio contenedor y su propia base Postgres.
3. Abrir cualquier `Dockerfile` (por ejemplo `src/TaskManager/Dockerfile`) y
   mostrar las dos etapas: build (con el SDK) y runtime (liviano, solo
   ASP.NET).

## 4. ACID

1. Correr:
   ```bash
   ./scripts/demo-03-acid.sh
   ```
2. Intenta crear una tarea y asignarla a un usuario que no existe, en una
   sola transacción.
3. La operación falla con error 409, y el script confirma que la cantidad de
   tareas del proyecto no cambió — no quedó nada a medio crear (rollback).

## 5. Servicios sin estado

1. Correr:
   ```bash
   ./scripts/demo-04-sin-estado.sh
   ```
2. Registra un usuario, escala ProjectManager a 3 réplicas y hace 12 pedidos
   con el mismo token (emitido una sola vez).
3. Todos los pedidos responden 200, y el script muestra que distintas réplicas
   los atendieron aunque ninguna vio el login original.
4. Al final, un pedido sin token da 401 en cualquier réplica.

## 6. Materialized View (UT4)

La vista del tablero se guarda en la propia tabla `Tasks`: cada tarea tiene
`Assignee` (ID) y `AssigneeName` (nombre del responsable). Al asignar se ejecuta
una sola llamada `GET /users/{id}` que valida el usuario y obtiene su nombre;
ambos valores se persisten juntos. `POST /tasks/create-and-assign` hace lo mismo
dentro de su transacción. Reasignar reemplaza el ID y el nombre.

`GET /tasks?projectId=...` hace una sola consulta a `tasks-db`, filtrada por
proyecto, y devuelve el nombre ya resuelto. No consulta UserManager ni
ProjectManager. Por ejemplo:

```json
{
  "id": "11111111-1111-1111-1111-111111111111",
  "projectId": "22222222-2222-2222-2222-222222222222",
  "assignee": "33333333-3333-3333-3333-333333333333",
  "title": "Informe",
  "status": "Todo",
  "assigneeName": "Euge"
}
```

Para probarlo desde **PowerShell en Windows**, abrir Docker Desktop y ejecutar
desde la carpeta `UTF-UT4`:

```powershell
docker compose up -d --build
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\demo-05-materialized-view.ps1
```

Este script no requiere `curl` ni `jq`. Registra dos usuarios, asigna una tarea
a Ana, la reasigna a Bruno, crea y asigna otra a Ana y agrega una sin responsable.
Verifica los IDs y nombres, detiene UserManager y confirma que consultar un
usuario falla. Después lee el tablero cinco veces y comprueba que los nombres
siguen presentes. Restaura UserManager en un bloque `finally`, incluso si falla
una comprobación. Una falla termina el script con error; si todo pasa, muestra
`DEMO OK`.

Si usás otro puerto o querés más lecturas:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\demo-05-materialized-view.ps1 -GatewayUrl http://localhost:8090 -Reads 10
```

También está disponible la versión Bash (con `curl` y `jq`, igual que las demos
anteriores):

```bash
docker compose up -d --build
bash scripts/demo-05-materialized-view.sh
```

El script crea un proyecto, asigna una tarea, crea y asigna otra en una
transacción y agrega una sin responsable. Luego detiene UserManager y comprueba
que el tablero responde con los nombres guardados. Al salir vuelve a iniciar
UserManager, incluso si falla una comprobación. El JWT se valida localmente,
por lo que el token emitido antes de detener UserManager sigue funcionando.

La columna se agrega automáticamente al arrancar TaskManager, conservando los
datos existentes. Las tareas sin asignar y las asignadas antes de esta versión
devuelven `assigneeName: null`; volver a asignar estas últimas materializa su
nombre. Si el usuario cambia de nombre, la vista conserva el nombre guardado
hasta la próxima asignación: la actualización ocurre durante la escritura.
Si UserManager falla o el usuario no existe, no se modifica la asignación ni
se crea parcialmente una tarea en `create-and-assign`.

Los puntos de implementación están en `TaskItem.AssigneeName`,
`TaskService.AssignTask`, `TaskService.CreateAndAssign` y el mapeo a
`TaskResponse`. Para verificar la persistencia, la consulta única y la lectura
sin servicios externos con SQLite:

```bash
dotnet test tests/TaskManager.Tests/TaskManager.Tests.csproj
```

## 7. Cache-Aside (Redis)

1. Correr:
   ```bash
   ./scripts/demo-06-cache-aside.sh
   ```
2. Escala TaskManager a 3 réplicas, crea un proyecto y le agrega 6 tareas.
   Cada tarea valida que el proyecto esté abierto (`IsOpen`).
3. En los logs se ve un solo `Cache MISS` (la única llamada HTTP a
   ProjectManager) y el resto `Cache HIT`, aunque las tareas las atiendan
   réplicas distintas: el caché en Redis es compartido.
4. Al final muestra el TTL restante de la entrada en Redis (30 s por defecto),
   que acota cuánto puede estar desactualizado el dato.

## 8. Circuit Breaker (UT4)

Con Docker Desktop abierto, desde la raíz del repositorio, en Bash (Git Bash,
WSL o Linux/macOS) con `curl` y `jq` instalados:

```bash
docker compose up -d --build --scale taskmanager=1
bash scripts/demo-07-circuit-breaker.sh
```

Esperar que los servicios terminen de arrancar antes de ejecutar el script.
Para otro puerto, usar `GW=http://localhost:8090 bash scripts/demo-07-circuit-breaker.sh`.
La demo requiere los valores predeterminados de `CircuitBreaker` y una sola
réplica de TaskManager; no cambia la cantidad de réplicas automáticamente.
Crea datos propios y los conserva para inspeccionarlos. Detiene temporalmente
UserManager y siempre intenta restaurarlo con un `trap` de salida, incluso ante
un error o una interrupción con Ctrl+C.

La demo comprueba:

1. Una tarea se crea y asigna normalmente, guardando el nombre del responsable.
2. Con UserManager detenido, tres llamadas reales fallan con HTTP 503 y
   `code: dependency_unavailable`.
3. La siguiente devuelve HTTP 503 y `code: circuit_open`. El número de mensajes
   `Dependency HTTP attempt UserManager` no aumenta: no se intentó la llamada.
4. El tablero sigue disponible y conserva la asignación. Crear una tarea en otro
   proyecto consulta ProjectManager y funciona: sus circuitos son independientes.
5. Al volver UserManager y transcurrir el período de apertura, una solicitud
   prueba la recuperación. Los logs muestran `Open`, `HalfOpen` y `Closed`.

### Implementación y configuración

`src/Shared/DependencyResilience.cs` contiene la integración de Polly mediante
`Microsoft.Extensions.Http.Resilience`. Se compila en ProjectManager y
TaskManager, y ambos Dockerfiles incluyen ese archivo compartido.
Los registros `AddHttpClient` de cada `Program.cs` agregan esta protección:

- TaskManager → UserManager.
- TaskManager → ProjectManager.
- ProjectManager → UserManager.

Cada cliente tiene su propio circuito reutilizado entre solicitudes, local al
proceso. Las réplicas no comparten el estado del breaker. La caché queda antes
del cliente HTTP: un hit no necesita atravesar el circuito.

En `appsettings.json` de ambos servicios:

| Propiedad de CircuitBreaker | Valor | Significado                                                |
| --------------------------- | ----- | ---------------------------------------------------------- |
| FailureRatio                | 0.5   | Abre si al menos la mitad de las llamadas observadas falla |
| MinimumThroughput           | 3     | Requiere al menos tres llamadas dentro de la ventana       |
| SamplingSeconds             | 10    | Ventana de observación                                     |
| BreakSeconds                | 15    | Tiempo abierto antes de permitir una prueba                |
| TimeoutSeconds              | 2     | Límite de espera del envío HTTP hasta recibir cabeceras    |

Son valores didácticos. Se cuentan errores de conexión, timeouts de la política
HTTP y respuestas 5xx. Los 4xx y las cancelaciones del llamador no abren el
circuito. No se agregan reintentos. El timeout está dentro del breaker para que
sus fallos se contabilicen. La lectura posterior del cuerpo no queda cubierta
por ese timeout de la pipeline; estos clientes consumen respuestas JSON pequeñas.

El middleware devuelve 503 para dependencia no disponible o circuito abierto,
sin confundirlos con usuario inexistente (409 en la regla de negocio). Los
clientes liberan las respuestas HTTP después de leerlas. Las asignaciones y
la creación transaccional conservan su comportamiento ante fallos.

Pruebas sin Docker:

```powershell
dotnet test MiniGestorProyectos.sln
```

Cubren apertura, rechazo sin nuevas llamadas, aislamiento entre dependencias,
recuperación, reapertura si la prueba falla, errores de conexión, timeout,
exclusión de 4xx/cancelaciones, respuesta 503 y conservación de datos.

Referencias: [resiliencia HTTP en .NET](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience)
y [circuit breaker de Polly](https://www.pollydocs.org/strategies/circuit-breaker.html).

## 9. Retry (UT4)

Patrón: [Retry](https://learn.microsoft.com/es-es/azure/architecture/patterns/retry).
Una llamada entre servicios que falla por un motivo **transitorio** (red, timeout,
5xx) se reintenta automáticamente en lugar de fallarle de inmediato al usuario.

Con Docker Desktop abierto, desde la raíz del repositorio, en Bash:

```bash
docker compose up -d --build --scale taskmanager=1
bash scripts/demo-08-retry.sh
```

La demo requiere los valores predeterminados de `Retry` y `CircuitBreaker` y una
sola réplica de TaskManager. Detiene UserManager y lo restaura al terminar (también
ante un error o Ctrl+C). Comprueba que:

1. Con UserManager detenido, **una sola** solicitud produce 3 intentos HTTP
   (`Dependency HTTP attempt UserManager`) y 2 avisos `Retry UserManager: intento N/3`.
2. Tras agotar los intentos responde HTTP 503 `dependency_unavailable`, con una
   demora que incluye el backoff.
3. Al restaurar UserManager la misma solicitud funciona.

### Implementación y configuración

Vive en `src/Shared/DependencyResilience.cs`, junto al Circuit Breaker, por lo que
se aplica a las tres llamadas entre servicios (TaskManager → UserManager,
TaskManager → ProjectManager, ProjectManager → UserManager). Orden de la pipeline,
de afuera hacia adentro:

```
CircuitBreaker → timeout total → Retry → timeout por intento → HTTP
```

- **Circuito abierto:** el breaker rechaza antes de llegar al Retry, así que nunca
  se reintenta contra un circuito abierto (lo que pide el patrón).
- **Timeout por intento** dentro del Retry: un intento colgado se corta y se reintenta.
- **Timeout total** acota intentos + esperas, para no bloquear al llamador (las
  llamadas de los clientes son síncronas).

En `appsettings.json` de TaskManager y ProjectManager:

| Propiedad de Retry  | Valor | Significado                                                                      |
| ------------------- | ----- | -------------------------------------------------------------------------------- |
| MaxRetryAttempts    | 2     | Reintentos tras el intento inicial (3 intentos en total). `0` desactiva el Retry |
| DelayMilliseconds   | 200   | Espera base; crece exponencialmente (≈200 ms, ≈400 ms) con jitter                |
| TotalTimeoutSeconds | 8     | Tiempo máximo de la operación completa                                           |

Qué se reintenta: errores de conexión, timeout de un intento, HTTP 408 y 5xx.
**No** se reintentan los 4xx (400/401/403/404/409: reintentar daría lo mismo) ni las
cancelaciones del llamador. Este pipeline se usa solo con llamadas idempotentes
(hoy, todas `GET`: `Exist`, `IsOpen`, `GetById`), por lo que reintentar es seguro.
El jitter evita que varias réplicas reintenten al mismo instante.

> Como el Retry está dentro del breaker, un fallo que el reintento resuelve no
> cuenta como fallo del circuito: el breaker solo abre ante caídas sostenidas.

Pruebas sin Docker:

```bash
dotnet test tests/TaskManager.Tests/TaskManager.Tests.csproj --filter RetryTests
```

Cubren recuperación tras 503 / error de conexión / intento colgado, agotamiento de
intentos, 4xx sin reintento, Retry desactivado, circuito abierto sin nuevos
intentos y cancelación del llamador.

> Los reintentos de PostgreSQL (`EnableRetryOnFailure`) siguen activos solo en
> UserManager. No se habilitaron en TaskManager/ProjectManager porque
> `TaskService` abre transacciones manuales (`BeginTransaction`), incompatibles con
> esa opción salvo que se envuelvan en una `CreateExecutionStrategy()`.
