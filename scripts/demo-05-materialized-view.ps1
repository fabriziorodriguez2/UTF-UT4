param(
    [string]$GatewayUrl = $(if ($env:GW) { $env:GW } else { 'http://localhost:8080' }),
    [ValidateRange(1, 50)]
    [int]$Reads = 5
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$GatewayUrl = $GatewayUrl.TrimEnd('/')
$authHeaders = @{}
$restoreUsers = $false

function Invoke-Api {
    param([string]$Method, [string]$Path, [hashtable]$Body)

    $request = @{
        Method = $Method
        Uri = "$GatewayUrl$Path"
        Headers = $authHeaders
        TimeoutSec = 15
        ErrorAction = 'Stop'
    }
    if ($null -ne $Body) {
        $request.ContentType = 'application/json; charset=utf-8'
        $request.Body = [System.Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Compress))
    }
    Invoke-RestMethod @request
}

function Assert-Condition {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "FALLO: $Message" }
}

function Assert-Assignment {
    param($Task, [string]$UserId, [string]$Name)
    Assert-Condition ($Task.assignee -eq $UserId) 'El ID del responsable no coincide.'
    Assert-Condition ($Task.assigneeName -eq $Name) 'El nombre materializado no coincide.'
}

function Register-DemoUser {
    param([string]$Name)
    $registration = Invoke-Api POST '/auth/register' @{
        name = $Name
        email = "materialized+$([guid]::NewGuid().ToString('N'))@example.com"
        password = 'DemoMaterialized123!'
    }
    Assert-Condition (-not [string]::IsNullOrWhiteSpace($registration.token)) 'No se recibio un JWT.'
    # Leer el ID desde el JWT evita depender del orden del listado de usuarios.
    $payload = $registration.token.Split('.')[1].Replace('-', '+').Replace('_', '/')
    $payload = $payload.PadRight($payload.Length + ((4 - $payload.Length % 4) % 4), '=')
    $claims = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($payload)) | ConvertFrom-Json
    @{ Id = ([guid]$claims.sub).ToString(); Token = $registration.token; Name = $Name }
}

function Assert-Board {
    param([object[]]$Board, [string]$ProjectId, $Reassigned, $Combined, $Unassigned, $Ana, $Bruno)
    Assert-Condition ($Board.Count -eq 3) 'El tablero debe tener exactamente tres tareas.'
    foreach ($card in $Board) {
        Assert-Condition ($card.projectId -eq $ProjectId) 'Aparecio una tarea de otro proyecto.'
    }
    $reassignedCards = @($Board | Where-Object { $_.id -eq $Reassigned.id })
    $combinedCards = @($Board | Where-Object { $_.id -eq $Combined.id })
    $unassignedCards = @($Board | Where-Object { $_.id -eq $Unassigned.id })
    Assert-Condition ($reassignedCards.Count -eq 1) 'Falta la tarea reasignada.'
    Assert-Condition ($combinedCards.Count -eq 1) 'Falta la tarea creada y asignada.'
    Assert-Condition ($unassignedCards.Count -eq 1) 'Falta la tarea sin responsable.'
    Assert-Assignment $reassignedCards[0] $Bruno.Id $Bruno.Name
    Assert-Assignment $combinedCards[0] $Ana.Id $Ana.Name
    Assert-Condition ($unassignedCards[0].assignee -eq [guid]::Empty.ToString()) 'La tarea nueva no debe tener responsable.'
    Assert-Condition ($null -eq $unassignedCards[0].assigneeName) 'La tarea sin responsable debe tener nombre null.'
}

Push-Location (Join-Path $PSScriptRoot '..')
try {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw 'No se encontro Docker. Instala Docker Desktop y abri la aplicacion.'
    }
    try { & docker info --format '{{.ServerVersion}}' *> $null }
    catch { throw 'Docker no esta disponible. Abri Docker Desktop y espera a que arranque.' }
    if ($LASTEXITCODE -ne 0) { throw 'Docker no esta disponible. Abri Docker Desktop y espera a que arranque.' }
    $runningUsers = @(& docker compose ps --status running -q usermanager)
    if ($LASTEXITCODE -ne 0 -or $runningUsers.Count -eq 0) {
        throw 'Primero levanta los servicios desde UTF-UT4 con: docker compose up -d --build'
    }
    $null = Invoke-Api GET '/health'

    Write-Host '== 1. Registrar Ana y Bruno, y crear un proyecto =='
    $ana = Register-DemoUser 'Ana'
    $bruno = Register-DemoUser 'Bruno'
    $authHeaders = @{ Authorization = "Bearer $($ana.Token)" }
    $project = Invoke-Api POST '/projects' @{ ownerId = $ana.Id; name = 'Demo Materialized View' }
    Write-Host "Proyecto: $($project.id)"

    Write-Host '== 2. Asignar a Ana: se materializa el nombre =='
    $task = Invoke-Api POST '/tasks' @{ projectId = $project.id; title = 'Informe' }
    $assigned = Invoke-Api POST "/tasks/$($task.id)/assign" @{ userId = $ana.Id }
    Assert-Assignment $assigned $ana.Id $ana.Name
    Write-Host "OK: assigneeName = $($assigned.assigneeName)"

    Write-Host '== 3. Reasignar a Bruno: se actualizan ID y nombre =='
    $reassigned = Invoke-Api POST "/tasks/$($task.id)/assign" @{ userId = $bruno.Id }
    Assert-Assignment $reassigned $bruno.Id $bruno.Name
    Write-Host "OK: assigneeName = $($reassigned.assigneeName)"

    Write-Host '== 4. Crear y asignar en una transaccion, y crear una tarea sin responsable =='
    $combined = Invoke-Api POST '/tasks/create-and-assign' @{
        projectId = $project.id; title = 'Pruebas'; userId = $ana.Id
    }
    Assert-Assignment $combined $ana.Id $ana.Name
    $unassigned = Invoke-Api POST '/tasks' @{ projectId = $project.id; title = 'Sin responsable' }
    $board = @(Invoke-Api GET "/tasks?projectId=$($project.id)")
    Assert-Board $board $project.id $reassigned $combined $unassigned $ana $bruno
    $board | Format-Table title, status, assigneeName -AutoSize | Out-Host

    Write-Host '== 5. Detener UserManager y confirmar que no se pueden consultar usuarios =='
    # Marcar antes del stop para restaurar incluso si el comando falla parcialmente.
    $restoreUsers = $true
    & docker compose stop usermanager
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo detener UserManager.' }
    $userLookupFailed = $false
    try { $null = Invoke-Api GET "/users/$($ana.Id)" }
    catch {
        $response = $_.Exception.Response
        if ($null -eq $response -or [int]$response.StatusCode -notin @(502, 504)) { throw }
        $userLookupFailed = $true
    }
    Assert-Condition $userLookupFailed 'UserManager sigue respondiendo; no se puede demostrar la lectura con el servicio detenido.'
    Write-Host 'OK: consultar usuarios falla porque UserManager esta detenido.'

    Write-Host "== 6. Leer el tablero $Reads veces con UserManager detenido =="
    for ($i = 1; $i -le $Reads; $i++) {
        $board = @(Invoke-Api GET "/tasks?projectId=$($project.id)")
        Assert-Board $board $project.id $reassigned $combined $unassigned $ana $bruno
        Write-Host "Lectura $i/$Reads OK: Bruno, Ana y una tarea sin responsable."
    }
    $board | Format-Table title, status, assigneeName -AutoSize | Out-Host
    Write-Host 'DEMO OK: el tablero devuelve los nombres guardados aunque UserManager no este disponible.' -ForegroundColor Green
}
finally {
    try {
        if ($restoreUsers) {
            Write-Host 'Restaurando UserManager...'
            & docker compose start usermanager
            if ($LASTEXITCODE -ne 0) { throw 'No se pudo restaurar UserManager. Ejecuta: docker compose start usermanager' }
        }
    }
    finally { Pop-Location }
}
