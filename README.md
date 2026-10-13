# Mini Gestor de Proyectos — TFU UT3 (Grupo 3)

Aplicación estilo Trello/Jira donde varios usuarios comparten un dashboard y
gestionan las tareas de cada proyecto, con su cumplimiento y asignaciones.

Este repositorio contiene el **esqueleto** (estructura de carpetas + firmas de
funciones a implementar) del sistema, derivado del **Diagrama de Componentes UML**
de la TFU. El código está pensado para levantarse con `docker compose` y probarse
como una **API REST** con `curl` o Postman.

## Conceptos de la UT3 que demuestra la arquitectura

| Concepto (letra TFU)         | Dónde se ve en esta solución                                                                                                                                                                                        |
| ---------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Componentes e interfaces** | 4 componentes desplegables (`ApiGateway`, `UserManager`, `ProjectManager`, `TaskManager`), cada uno expone una interfaz (`IApiRest`, `IUsuarios`/`IAutenticacion`, `IProyectos`, `ITareas`) y consume las de otros. |
| **Escalabilidad horizontal** | Cada componente es un contenedor liviano; `docker compose up --scale taskmanager=3` levanta réplicas y el `ApiGateway` balancea entre ellas.                                                                        |
| **Contenedores (Docker)**    | Un `Dockerfile` por componente + `docker-compose.yml` que orquesta servicios y bases de datos.                                                                                                                      |
| **ACID (con transacciones)** | Cada componente de negocio tiene su propia base **PostgreSQL**; las operaciones que tocan varias filas (crear tarea + asignar) se ejecutan dentro de una transacción.                                               |
| **Servicios sin estado**     | La autenticación se resuelve con **JWT**; los servicios no guardan sesión en memoria, cualquier réplica puede atender cualquier request.                                                                            |

## Componentes (según el diagrama)

```
                 ┌─────────────────┐
   cliente  ───► │   ApiGateway    │   enruta / balancea + valida token (JWT)
   (curl)        └───────┬─────────┘
                         │
        ┌────────────────┼─────────────────┐
        ▼                ▼                  ▼
┌───────────────┐ ┌───────────────┐ ┌───────────────┐
│  UserManager  │ │ProjectManager │ │  TaskManager  │
│  IUsuarios    │ │  IProyectos   │ │   ITareas     │
│ IAutenticacion│ │               │ │ (usa IProyectos
│               │ │ (usa IUsuarios│ │  e IUsuarios) │
└──────┬────────┘ └──────┬────────┘ └──────┬────────┘
       ▼                 ▼                 ▼
   users-db          projects-db        tasks-db      (PostgreSQL, 1 por componente)
```

Dependencias entre componentes (aristas del diagrama):

- `ApiGateway` → **valida token** contra `UserManager` (`IAutenticacion`) y **enruta/balancea** hacia los 3 servicios.
- `TaskManager` → **valida proyecto abierto** (`IProyectos`) y **valida asignado** (`IUsuarios`).
- `ProjectManager` → **valida dueño** (`IUsuarios`).

## Estructura del repositorio

```
UT3-TFUGrupo3/
├── MiniGestorProyectos.sln          # Solución .NET que agrupa los 4 proyectos
├── docker-compose.yml               # Orquesta gateway + servicios + 3 bases
├── .env                             # Variables (puertos, credenciales, JWT secret)
├── src/
│   ├── ApiGateway/                  # Componente ApiGateway.dll  (IApiRest)
│   ├── UserManager/                 # Componente UserManager.dll (IUsuarios, IAutenticacion)
│   ├── ProjectManager/              # Componente ProjectManager.dll (IProyectos)
│   └── TaskManager/                 # Componente TaskManager.dll (ITareas)
└── scripts/                         # Scripts para demostrar cada concepto
    ├── demo-01-componentes.sh       #   API REST end-to-end (crear user/proyecto/tarea)
    ├── demo-02-escalabilidad.sh     #   levantar réplicas y ver el balanceo
    ├── demo-03-acid.sh              #   transacción atómica (crear+asignar)
    └── demo-04-sin-estado.sh        #   mismo JWT servido por cualquier réplica
```

Cada componente de negocio sigue la misma organización interna:

```
src/<Componente>/
├── <Componente>.csproj
├── Program.cs                # Composición: DI, EF Core, JWT, controllers
├── Dockerfile
├── appsettings.json
├── Domain/                   # Entidades del diagrama (User / Project / Task)
├── Interfaces/               # Interfaz EXPUESTA por el componente
├── Contracts/                # Interfaces CONSUMIDas de otros componentes (+ clientes HTTP)
├── Services/                 # Lógica de negocio (implementa la interfaz expuesta)
├── Repositories/            # Persistencia (Save / FindById / ...)
├── Persistence/            # DbContext de EF Core (mapeo a PostgreSQL)
└── Controllers/           # Endpoints REST que publican la interfaz
```

## Cómo levantar (una vez implementado)

```bash
docker compose up --build
# API disponible a través del gateway en http://localhost:8080
```

Escalar un componente:

```bash
docker compose up --scale taskmanager=3
```
