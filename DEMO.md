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
