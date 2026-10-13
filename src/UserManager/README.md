# UserManager: guía de implementación

Este componente registra usuarios, verifica sus credenciales y permite consultar
el equipo. Se implementó sin modificar ProjectManager, TaskManager ni ApiGateway.

## Cómo se conectan las clases

Una petición sigue este recorrido:

```text
Cliente HTTP -> Controller -> UserService -> UserRepository -> UsersDbContext -> PostgreSQL
                                  |
                                  +-> JwtTokenIssuer (emite el token)
```

- **Domain/User.cs** representa un usuario: `Id`, `Name`, `Email` y `PasswordHash`.
  `Guid` es el tipo usado para identificadores; `Guid.NewGuid()` genera uno nuevo.
- **Interfaces/IUsuarios.cs** y **IAutenticacion.cs** definen las operaciones
  disponibles. Una interfaz es un contrato: dice qué métodos debe implementar
  una clase. `UserService` implementa ambas.
- **Services/UserService.cs** contiene las reglas de negocio: valida datos,
  rechaza emails repetidos, comprueba contraseñas y solicita tokens.
- **Repositories/UserRepository.cs** concentra las consultas y escrituras.
  `Save` inserta si el identificador no existe y actualiza si ya existe.
  `FindById` y `FindByEmail` devuelven `null` cuando no encuentran al usuario.
  Se agregó `GetAll` para poder implementar el listado del servicio.
- **Persistence/UsersDbContext.cs** conecta Entity Framework Core con la base.
  `Users` representa la tabla. Se conserva la clave primaria por `Id` y el índice
  único por `Email` que ya tenía el esqueleto.
- **Controllers/** transforma JSON en objetos de entrada y resultados del
  servicio en respuestas HTTP. Los DTO `UserResponse` tienen solamente los tres
  datos públicos, de modo que el hash no aparece en las respuestas.
- **Auth/JwtTokenIssuer.cs** genera tokens firmados, válidos durante una hora.
- **Program.cs** configura y conecta todas estas piezas al iniciar la aplicación.

## Registro paso a paso

1. `POST /auth/register` recibe `name`, `email` y `password`.
2. Se comprueba que el nombre no esté vacío y que el email tenga un formato válido.
3. El email se convierte a minúsculas y se quitan espacios de sus extremos. Así,
   ` ANA@Example.com ` y `ana@example.com` corresponden a la misma cuenta.
4. Se exige una contraseña de al menos 8 caracteres, no formada solamente por
   espacios y de hasta 72 bytes UTF-8. Este último límite evita el truncamiento
   de BCrypt; una letra acentuada puede ocupar más de un byte.
5. Se busca el email para impedir un registro duplicado.
6. Se crea el usuario con un identificador nuevo y un hash BCrypt. El hash permite
   comprobar la contraseña posteriormente sin almacenar el texto original.
7. `SaveChanges()` guarda el usuario. El índice único también impide duplicados
   si dos peticiones intentan registrar el mismo email al mismo tiempo; el
   repositorio traduce ese error de PostgreSQL en un conflicto.
8. Se devuelve `{ "token": "..." }`.

**Diferencia con el diagrama:** en la imagen, `Register` devuelve `User` y aparece
`Exists`. El esqueleto C# ya tenía `Register : string` y `Exist`. Conservé esas
firmas para respetar los contratos del repositorio. El endpoint HTTP mantiene
`/users/{id}/exists`. Si la entrega exige firmas idénticas al UML, será necesario
acordar ese cambio con el equipo; esta implementación sigue el contrato C# actual.

## Login y JWT

`POST /auth/login` normaliza el email, busca al usuario y llama a
`BCrypt.Verify` para comparar la contraseña recibida con el hash guardado. Si
no existe la cuenta o la contraseña es incorrecta, devuelve el mismo mensaje.
Si son correctos, emite otro JWT.

El token contiene `sub` (identificador), `email`, emisor, destinatario y vencimiento.
Se firma con HMAC-SHA256 y `Jwt:Secret`. Un JWT firmado **no está cifrado**:
su contenido puede leerse; no contiene la contraseña ni su hash.

Las rutas `/users` tienen `[Authorize]`. El middleware comprueba firma, emisor,
destinatario y vencimiento antes de ejecutar el controller. No se guarda una
sesión en memoria. Las réplicas deben compartir la configuración JWT y la base.
En este alcance, cualquier usuario autenticado puede consultar el equipo.

## Respuestas HTTP

| Operación | Resultado |
| --- | --- |
| `POST /auth/register` | 200 con token; 400 por datos inválidos; 409 por email repetido |
| `POST /auth/login` | 200 con token; 401 por credenciales incorrectas |
| `GET /users` | 200 con lista de usuarios, sin hashes |
| `GET /users/{id}` | 200 con usuario; 404 si no existe |
| `GET /users/{id}/exists` | 200 con `true` o `false` |
| Consulta protegida sin token válido | 401 |
| `GET /health` | 200 con `ok` si la aplicación responde |

`[ApiController]` también rechaza cuerpos inválidos o campos obligatorios ausentes
con 400 antes de llamar al método. Un identificador que no tenga formato GUID no
coincide con la ruta `{id:guid}`.

## Conceptos de C# usados

- `private readonly` marca una dependencia interna que se asigna en el constructor.
- La **inyección de dependencias** permite que ASP.NET cree las clases y entregue
  automáticamente sus dependencias. `AddScoped` crea una instancia por petición;
  `AddSingleton` comparte una durante la vida de la aplicación.
- `User?` permite devolver un usuario o `null`. `!= null` comprueba si existe.
- `FirstOrDefault(user => user.Email == email)` busca la primera coincidencia;
  la expresión con `=>` es una función corta que indica la condición de búsqueda.
- `ToList()` ejecuta la consulta y obtiene una lista. `AsNoTracking()` evita que EF
  siga cambios en los objetos de una consulta que solamente sirve para leer.
- `throw` comunica un error; `try/catch` lo captura. Los controllers convierten
  los errores esperados en códigos HTTP para el cliente.
- Los DTO son `record` que ya existían: objetos sencillos para transportar datos.

## Arranque y configuración

`Program.cs` exige `Jwt:Secret` de al menos 32 bytes UTF-8 y un emisor y destinatario
no vacíos. En Docker las variables `Jwt__Secret`, `Jwt__Issuer` y `Jwt__Audience`
se traducen a esas claves de configuración. Deben coincidir con las del gateway.

Se activaron los reintentos de PostgreSQL para errores transitorios. Al arrancar,
`EnsureCreated()` crea la base y su esquema si hacen falta; no actualiza esquemas
existentes como lo haría una migración. Es el mecanismo sencillo previsto para
esta demo. `/health` comprueba que el proceso responde, no el estado de la base.

## Probar solamente UserManager con Docker

Desde la raíz del repositorio, con Docker en ejecución y las variables del `.env`
configuradas:

```powershell
docker compose up -d users-db
docker compose exec users-db sh -c 'pg_isready -U "$POSTGRES_USER" -d "$POSTGRES_DB"'
# Una vez que PostgreSQL indique que acepta conexiones:
docker compose run --build --rm -p 8081:8080 usermanager
```

Esto publica UserManager en `http://localhost:8081` sin arrancar los otros módulos.
Swagger está en `http://localhost:8081/swagger`. En otra terminal PowerShell:

```powershell
$baseUrl = 'http://localhost:8081'
$registration = @{
    name = 'Ana'
    email = 'ana@example.com'
    password = 'ClaveDemo123!'
} | ConvertTo-Json

$result = Invoke-RestMethod "$baseUrl/auth/register" -Method Post `
    -ContentType 'application/json' -Body $registration
$headers = @{ Authorization = "Bearer $($result.token)" }

$users = Invoke-RestMethod "$baseUrl/users" -Headers $headers
$users
$id = ($users | Where-Object { $_.email -eq 'ana@example.com' }).id
Invoke-RestMethod "$baseUrl/users/$id" -Headers $headers
Invoke-RestMethod "$baseUrl/users/$id/exists" -Headers $headers

$login = @{ email = 'ana@example.com'; password = 'ClaveDemo123!' } | ConvertTo-Json
Invoke-RestMethod "$baseUrl/auth/login" -Method Post `
    -ContentType 'application/json' -Body $login
```

Si ya registraste ese email, usá login o elegí otro email para repetir el registro.
Probá también contraseña incorrecta (401), registro repetido (409), nombre vacío
(400), GUID inexistente (404 en consulta y `false` en existencia) y listar sin
Authorization (401). Reiniciar el servicio debe conservar los usuarios.

Los otros módulos siguen pendientes de implementación. Sus clientes HTTP deberán
enviar `Authorization: Bearer <token>` al consultar UserManager. No se modificaron
esos clientes ni el gateway.

## Pruebas automatizadas

```powershell
dotnet build src/UserManager/UserManager.csproj
dotnet test tests/UserManager.Tests/UserManager.Tests.csproj
```

Las pruebas usan una base SQLite temporal en memoria que se elimina al terminar.
Cubren registro, hash, firma y contenido del JWT, validaciones, emails repetidos,
login, consultas, actualización y respuestas de los controllers. No requieren
cambiar la base PostgreSQL de la aplicación ni ejecutar los otros componentes.

Estas pruebas no ejercitan PostgreSQL, la carrera entre registros simultáneos ni
el middleware HTTP de autorización; esos casos requieren una prueba de integración
con el servicio ejecutándose. No se agregó el proyecto de pruebas a la solución
compartida: se ejecuta directamente con el comando indicado.
