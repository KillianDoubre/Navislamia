#Requires -Version 5.1
<#
.SYNOPSIS
    Démarre l'ensemble du serveur Navislamia en une seule exécution.

.DESCRIPTION
    Le Game Server (DevConsole) est un CLIENT de l'AuthServer/UploadServer : il se
    connecte à 127.0.0.1:4502 (auth) et 127.0.0.1:4616 (upload) au démarrage et
    crashe si personne n'écoute. Les deux serveurs ont également besoin de
    PostgreSQL (Arcadia / Telecaster / auth) sur 127.0.0.1:5432. Ce script :

      1. Vérifie que PostgreSQL écoute et démarre le service si besoin.
      2. Vérifie qu'aucune instance précédente ne squatte les ports du serveur.
      3. Compile AuthServer puis DevConsole (les deux « dotnet run » sont ensuite
         lancés avec --no-build : deux compilations simultanées se battraient sur
         les dossiers obj/bin partagés du projet Game).
      4. Lance l'AuthServer (stub Auth 4502 + Upload 4616) dans sa propre fenêtre.
      5. Attend qu'il écoute réellement sur les deux ports.
      6. Lance le Game Server (DevConsole) dans la fenêtre courante.
      7. À la fermeture de DevConsole, arrête l'AuthServer (et ses processus enfants).

.PARAMETER Configuration
    Configuration de build (Debug par défaut, ou Release).

.PARAMETER Force
    Arrête les processus qui occupent déjà les ports du serveur au lieu d'abandonner.

.PARAMETER SkipBuild
    Réutilise la compilation existante sans relancer « dotnet build ».

.EXAMPLE
    .\start-server.ps1
    .\start-server.ps1 -Configuration Release
    .\start-server.ps1 -Force
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [switch]$Force,

    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# Ports du serveur : auth (4502), client auth (4601), upload (4616) et jeu (4515).
$ServerPorts = 4502, 4601, 4616, 4515
$DatabasePort = 5432

function Test-Port {
    param(
        [string]$TargetHost,
        [int]$Port
    )
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $client.Connect($TargetHost, $Port)
        return $true
    }
    catch {
        return $false
    }
    finally {
        $client.Close()
    }
}

function Wait-ForPort {
    param(
        [string]$TargetHost,
        [int]$Port,
        [int]$TimeoutSeconds = 90
    )
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Port -TargetHost $TargetHost -Port $Port) {
            return $true
        }
        Start-Sleep -Milliseconds 500
    }
    return $false
}

function Stop-ProcessTree {
    # /T arrête aussi le process applicatif enfant lancé par « dotnet run ».
    # Rediriger stderr d'un exécutable natif transforme chaque ligne en ErrorRecord
    # sous PowerShell 5.1, ce qui devient une erreur terminante avec
    # $ErrorActionPreference = 'Stop'. On neutralise la préférence le temps de l'appel.
    param([int]$ProcessId)
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { taskkill /PID $ProcessId /T /F 2>$null | Out-Null } catch { }
    finally { $ErrorActionPreference = $previous }
}

function Start-DatabaseService {
    # Le service PostgreSQL est en démarrage manuel : après un redémarrage de
    # Windows il ne tourne pas et les deux serveurs partent avec une base
    # injoignable (l'AuthServer refuse alors tous les logins).
    $services = @(Get-Service -Name 'postgresql*' -ErrorAction SilentlyContinue |
        Where-Object { $_.StartType -ne 'Disabled' })
    if ($services.Count -eq 0) {
        throw "PostgreSQL n'écoute pas sur $DatabasePort et aucun service « postgresql* » activé n'a été trouvé. Démarrez PostgreSQL manuellement."
    }

    # La machine peut héberger plusieurs versions : on prend la plus récente active.
    $service = $services |
        Sort-Object { [int][regex]::Match($_.Name, '(\d+)$').Groups[1].Value } -Descending |
        Select-Object -First 1

    Write-Host "==> Démarrage du service PostgreSQL « $($service.Name) »..." -ForegroundColor Cyan
    try {
        Start-Service -Name $service.Name
    }
    catch {
        # Start-Service échoue sans droits administrateur : on relance la commande élevée.
        Write-Host '    Droits administrateur requis, élévation (UAC)...' -ForegroundColor Yellow
        $elevated = Start-Process -FilePath 'powershell' `
            -ArgumentList @('-NoProfile', '-Command', "Start-Service -Name '$($service.Name)'") `
            -Verb RunAs -PassThru -Wait
        if ($elevated.ExitCode -ne 0) {
            throw "Impossible de démarrer le service « $($service.Name) » (code $($elevated.ExitCode))."
        }
    }

    if (-not (Wait-ForPort -TargetHost '127.0.0.1' -Port $DatabasePort -TimeoutSeconds 60)) {
        throw "Le service « $($service.Name) » a démarré mais rien n'écoute sur $DatabasePort."
    }
}

function Assert-PortsFree {
    $busy = @()
    foreach ($port in $ServerPorts) {
        if (Test-Port -TargetHost '127.0.0.1' -Port $port) {
            $busy += $port
        }
    }
    if ($busy.Count -eq 0) {
        return
    }

    $owners = @(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
        Where-Object { $_.LocalPort -in $busy } |
        Select-Object -ExpandProperty OwningProcess -Unique)

    if (-not $Force) {
        $details = foreach ($owner in $owners) {
            $name = (Get-Process -Id $owner -ErrorAction SilentlyContinue).ProcessName
            "PID $owner ($name)"
        }
        throw ("Les ports $($busy -join ', ') sont déjà utilisés par $($details -join ', ') " +
            '— une instance précédente tourne encore. Fermez-la, ou relancez avec -Force.')
    }

    Write-Host "==> Arrêt de l'instance précédente sur les ports $($busy -join ', ')..." -ForegroundColor Yellow
    foreach ($owner in $owners) {
        Stop-ProcessTree -ProcessId $owner
    }
    foreach ($port in $busy) {
        $deadline = (Get-Date).AddSeconds(15)
        while ((Test-Port -TargetHost '127.0.0.1' -Port $port) -and (Get-Date) -lt $deadline) {
            Start-Sleep -Milliseconds 250
        }
        if (Test-Port -TargetHost '127.0.0.1' -Port $port) {
            throw "Le port $port est toujours occupé après l'arrêt de l'instance précédente."
        }
    }
}

Write-Host '==> Vérification de PostgreSQL...' -ForegroundColor Cyan
if (Test-Port -TargetHost '127.0.0.1' -Port $DatabasePort) {
    Write-Host "    PostgreSQL écoute déjà sur $DatabasePort." -ForegroundColor DarkGray
}
else {
    Start-DatabaseService
}

Assert-PortsFree

if (-not $SkipBuild) {
    Write-Host '==> Compilation (AuthServer puis DevConsole)...' -ForegroundColor Cyan
    foreach ($project in @("$root\AuthServer\AuthServer.csproj", "$root\DevConsole\DevConsole.csproj")) {
        # /clp:ErrorsOnly : les projets produisent ~160 avertissements connus qui
        # noieraient une vraie erreur de compilation au démarrage.
        dotnet build $project -c $Configuration --nologo -v quiet /clp:ErrorsOnly
        if ($LASTEXITCODE -ne 0) {
            throw "La compilation de « $project » a échoué (code $LASTEXITCODE)."
        }
    }
}

$authProcess = $null
try {
    Write-Host '==> Démarrage de l''AuthServer (stub Auth 4502 / Upload 4616)...' -ForegroundColor Cyan
    # Start-Process ne cite pas les arguments : le chemin du dépôt contient un espace
    # (« Rappelz Kiff »), donc sans guillemets explicites dotnet reçoit « A:\Rappelz »
    # et répond « n'est pas un fichier projet valide » — l'AuthServer ne démarrait jamais.
    $authProcess = Start-Process -FilePath 'dotnet' `
        -ArgumentList "run --project `"$root\AuthServer\AuthServer.csproj`" -c $Configuration --no-build" `
        -PassThru

    Write-Host '==> Attente que l''AuthServer écoute sur 4502 et 4616...' -ForegroundColor Cyan
    foreach ($port in 4502, 4616) {
        if (-not (Wait-ForPort -TargetHost '127.0.0.1' -Port $port)) {
            if ($authProcess.HasExited) {
                throw "L'AuthServer s'est arrêté (code $($authProcess.ExitCode)) sans écouter sur $port. Vérifiez la fenêtre AuthServer."
            }
            throw "L'AuthServer n'écoute pas sur $port (timeout). Vérifiez la fenêtre AuthServer."
        }
    }

    Write-Host '==> AuthServer prêt. Démarrage du Game Server (DevConsole)...' -ForegroundColor Green
    Push-Location "$root\DevConsole"
    try {
        dotnet run -c $Configuration --no-build
    }
    finally {
        Pop-Location
    }
}
finally {
    if ($authProcess -and -not $authProcess.HasExited) {
        Write-Host '==> Arrêt de l''AuthServer...' -ForegroundColor Cyan
        Stop-ProcessTree -ProcessId $authProcess.Id
    }
}
