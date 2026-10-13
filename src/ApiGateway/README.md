# ApiGateway

Punto de entrada HTTP del sistema. Valida localmente los JWT emitidos por
UserManager y reenvía las solicitudes sin guardar sesiones.

| Ruta | Destino | Acceso |
| --- | --- | --- |
| `GET /health` | Gateway | Público |
| `/auth/login`, `/auth/register` | UserManager | Público |
| Resto de `/auth`, `/users` | UserManager | JWT obligatorio |
| `/projects` | ProjectManager | JWT obligatorio |
| `/tasks` | TaskManager | JWT obligatorio |

Los prefijos incluyen sus subrutas. Las rutas desconocidas devuelven `404`;
las protegidas sin un token válido devuelven `401` y `WWW-Authenticate: Bearer`.
El servicio destino conserva la responsabilidad de validar métodos y datos.

## Configuración

`Jwt:Secret` debe tener al menos 32 bytes UTF-8. `Jwt:Issuer` y `Jwt:Audience`
son obligatorios; los tres valores deben coincidir con UserManager. El gateway
comprueba esta configuración al iniciar. Solo acepta tokens firmados con HS256,
con firma, emisor, audiencia y vencimiento válidos, sin tolerancia adicional de reloj.

`Services:Users`, `Services:Projects` y `Services:Tasks` definen las URL base.
Docker Compose ya proporciona estos valores y las variables `Jwt__*` desde `.env`.

## Reenvío

Conserva método, ruta, query, cuerpo, autorización y cabeceras de extremo a extremo,
así como el estado y contenido de la respuesta del backend. Excluye cabeceras
propias de la conexión. No sigue redirecciones ni comparte cookies entre usuarios.
Las respuestas se transmiten como un flujo; un fallo de conexión devuelve `502`
y un tiempo de espera superior a 100 segundos devuelve `504`. Si la respuesta ya
comenzó, se aborta la conexión porque no se puede reemplazar su estado HTTP.

Los destinos usan nombres de servicio Docker. Las conexiones se renuevan cada
30 segundos para volver a resolver DNS; el gateway no implementa un algoritmo
propio de reparto por petición ni garantiza una distribución uniforme entre réplicas.
Está orientado a las API REST del proyecto; no implementa túneles WebSocket.

## Verificación

Desde la raíz del repositorio:

```sh
dotnet test tests/ApiGateway.Tests/ApiGateway.Tests.csproj
docker compose up --build
```

Las pruebas automatizadas validan JWT, rutas, reenvío y errores con un backend
simulado, sin necesitar bases de datos.
