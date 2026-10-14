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
   ./scripts/demo-05-cache-aside.sh
   ```
2. Escala TaskManager a 3 réplicas, crea un proyecto y le agrega 6 tareas.
   Cada tarea valida que el proyecto esté abierto (`IsOpen`).
3. En los logs se ve un solo `Cache MISS` (la única llamada HTTP a
   ProjectManager) y el resto `Cache HIT`, aunque las tareas las atiendan
   réplicas distintas: el caché en Redis es compartido.
4. Al final muestra el TTL restante de la entrada en Redis (30 s por defecto),
   que acota cuánto puede estar desactualizado el dato.