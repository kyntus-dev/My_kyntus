<#
My Kyntus - activation de la recette sur Docker Desktop Windows.
Base analysee : 59b2cc9b5afdb650955607db35d8e24e6f674b9f (08/09/2026).
Conserve les donnees Docker locales existantes. Ne cree pas de jeu de test.
-VerifierSeulement : prepare les fichiers locaux et verifie la configuration,
sans demarrer/recreer de conteneur.
Le script ne fait ni build, ni pull, ni push, ni deploiement distant.
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = (Get-Location).Path,
    [string]$DockerContext = 'desktop-linux',
    [switch]$VerifierSeulement
)

$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding $false
$savedEnv = @{}
$envNames = @('KYNTUS_JWT_SECRET', 'KYNTUS_INTERNAL_SERVICE_API_KEY',
    'KYNTUS_LOCAL_PACK_DIR', 'KYNTUS_LOCAL_NETWORK_NAME')
foreach ($name in $envNames) {
    $savedEnv[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

function Read-DockerJson {
    param([string[]]$DockerArgs)
    $raw = & docker @DockerArgs
    if ($LASTEXITCODE -ne 0) { throw 'Commande Docker echouee. Voir le message ci-dessus.' }
    return (($raw -join "`n") | ConvertFrom-Json)
}

function New-LocalSecret {
    $bytes = New-Object byte[] 32
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    return 'LOCAL_RECETTE_' + ([BitConverter]::ToString($bytes).Replace('-', '').ToLowerInvariant())
}

function Get-EnvValue {
    param($Service, [string]$Key)
    $prop = $Service.environment.PSObject.Properties[$Key]
    if ($null -eq $prop) { return '' }
    return [string]$prop.Value
}

try {
    if ($env:OS -ne 'Windows_NT') {
        throw 'Ce script est prevu pour le PC Windows avec Docker Desktop, pas pour le serveur.'
    }
    if ($env:SSH_CONNECTION -or $env:SSH_CLIENT) {
        throw 'Executer depuis PowerShell sur votre PC, hors session SSH.'
    }
    $ips = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction Stop)
    if ($ips.IPAddress -contains '10.10.10.25') {
        throw 'Cette machine porte l adresse du serveur deploye. Activation refusee.'
    }
    $RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path
    if ($RepoRoot.StartsWith('\\') -or $PSScriptRoot.StartsWith('\\')) {
        throw 'Utiliser une copie sur le disque du PC, pas un partage reseau.'
    }
    foreach ($rel in @('docker-compose.yml', '.env', 'init\ocelot.gateway.json',
        'config\kyntus-public-urls.local.json')) {
        if (-not (Test-Path -LiteralPath (Join-Path $RepoRoot $rel) -PathType Leaf)) {
            throw "Fichier requis absent : $rel. Voir LISEZ-MOI.md."
        }
    }
    $null = Get-Command docker -ErrorAction Stop
    $contextInfo = @(Read-DockerJson -DockerArgs @('context', 'inspect', $DockerContext))[0]
    $endpoint = [string]$contextInfo.Endpoints.docker.Host
    # Accepte uniquement les pipes Windows locaux connus de Docker Desktop.
    if ($endpoint -notmatch '^npipe:////\./pipe/(dockerDesktopLinuxEngine|docker_engine)$') {
        throw "Le contexte '$DockerContext' ne cible pas le pipe local Docker Desktop. Aucun conteneur modifie."
    }
    $info = Read-DockerJson -DockerArgs @('--context', $DockerContext, 'info', '--format', '{{json .}}')
    if ($info.OSType -ne 'linux' -or $info.OperatingSystem -notmatch 'Docker Desktop') {
        throw 'Docker Desktop doit etre demarre en mode conteneurs Linux sur ce PC.'
    }
    $composeVersion = (& docker --context $DockerContext compose version --short).Trim().TrimStart('v')
    if ($LASTEXITCODE -ne 0 -or [version]($composeVersion.Split('-')[0]) -lt [version]'2.24.4') {
        throw 'Docker Compose 2.24.4 ou plus recent est requis.'
    }
    Write-Host "Contexte local confirme : $DockerContext (Docker Desktop Linux)."

    # Cle JWT propre a la recette : les nouveaux jetons ne reutilisent pas la cle du .env.
    $secretFile = Join-Path $PSScriptRoot 'secrets-locaux.json'
    if (-not (Test-Path -LiteralPath $secretFile)) {
        $secrets = @{ Jwt = (New-LocalSecret); Internal = (New-LocalSecret) }
        [IO.File]::WriteAllText($secretFile, ($secrets | ConvertTo-Json), $utf8)
    }
    $secrets = Get-Content -Raw -LiteralPath $secretFile | ConvertFrom-Json
    if ($secrets.Jwt -notmatch '^LOCAL_RECETTE_[a-f0-9]{64}$' -or
        $secrets.Internal -notmatch '^LOCAL_RECETTE_[a-f0-9]{64}$') {
        throw 'Format de secrets-locaux.json inattendu. Ne pas y copier de secret du serveur.'
    }
    $env:KYNTUS_JWT_SECRET = $secrets.Jwt
    $env:KYNTUS_INTERNAL_SERVICE_API_KEY = $secrets.Internal
    $env:KYNTUS_LOCAL_PACK_DIR = $PSScriptRoot.Replace('\', '/')
    $hasher = [System.Security.Cryptography.SHA256]::Create()
    try { $digest = $hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($RepoRoot.ToLowerInvariant())) }
    finally { $hasher.Dispose() }
    $suffix = [BitConverter]::ToString($digest).Replace('-', '').Substring(0, 12).ToLowerInvariant()
    $env:KYNTUS_LOCAL_NETWORK_NAME = "kyntus_recette_locale_$suffix"
    $templateDir = Join-Path $PSScriptRoot 'documentation-templates'
    $null = New-Item -ItemType Directory -Path $templateDir -Force

    $publicUrls = Get-Content -Raw -LiteralPath (Join-Path $RepoRoot 'config\kyntus-public-urls.local.json') | ConvertFrom-Json
    $expectedUrls = @{
        gateway = 'http://localhost:8500'; planningSpa = 'http://localhost:8200';
        authFrontend = 'http://localhost:8201'; authLogin = 'http://localhost:8201/login';
        planningAuthCallback = 'http://localhost:8200/auth-callback'
    }
    foreach ($key in $expectedUrls.Keys) {
        if ($publicUrls.$key -ne $expectedUrls[$key]) { throw "Profil local inattendu : $key." }
    }
    $expectedUrls.host = 'localhost'
    $js = '// Recette locale uniquement.' + "`nwindow.__KYNTUS_PUBLIC_URLS__ = " +
        ($expectedUrls | ConvertTo-Json) + ";`n"
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'kyntus-public-urls.local.js'), $js, $utf8)
    $gateway = Get-Content -Raw -LiteralPath (Join-Path $RepoRoot 'init\ocelot.gateway.json') | ConvertFrom-Json
    $gateway.GlobalConfiguration.BaseUrl = 'http://localhost:8500'
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'ocelot.local.json'), ($gateway | ConvertTo-Json -Depth 100), $utf8)

    $composeArgs = @('--context', $DockerContext, 'compose', '--project-directory', $RepoRoot,
        '--env-file', (Join-Path $RepoRoot '.env'), '-f', (Join-Path $RepoRoot 'docker-compose.yml'))
    $existingOverride = Join-Path $RepoRoot 'docker-compose.override.yml'
    if (Test-Path -LiteralPath $existingOverride) {
        $composeArgs += @('-f', $existingOverride)
        Write-Host 'Complement Docker local existant inclus avant les restrictions de recette.'
    }
    $composeArgs += @('-f', (Join-Path $PSScriptRoot 'compose.recette-locale.yml'))
    # La configuration complete reste en memoire et ne sera jamais affichee (secrets).
    $model = Read-DockerJson -DockerArgs ($composeArgs + @('config', '--format', 'json'))
    $project = [string]$model.name
    if (-not $project) { throw 'Nom du projet Compose introuvable.' }
    $composeArgs += @('--project-name', $project)
    $issues = New-Object System.Collections.Generic.List[string]
    $allowedHosts = @('localhost', '127.0.0.1', '::1', 'api-gateway', 'kyntus_gateway',
        'auth-backend', 'kyntus_auth_backend', 'planning-backend', 'formation-backend',
        'documentation-backend', 'conge-backend', 'prime-backend', 'parrainage-backend',
        'employee-directory-backend', 'kcq-backend', 'kcq-frontend', 'postgres', 'redis', 'rabbitmq', 'kcq-mongo')
    foreach ($route in $gateway.Routes) {
        foreach ($down in $route.DownstreamHostAndPorts) {
            if ($allowedHosts -notcontains $down.Host) { $issues.Add('Gateway : destination non locale dans une route.') }
        }
    }
    foreach ($p in $model.services.PSObject.Properties) {
        $name = $p.Name; $service = $p.Value
        $nets = @($service.networks.PSObject.Properties.Name)
        if ($nets.Count -ne 1 -or $nets[0] -ne 'microservices-network' -or $service.network_mode) {
            $issues.Add("${name} : reseau supplementaire ou mode reseau non admis.")
        }
        if ($service.privileged -or $service.cap_add -contains 'NET_ADMIN' -or $service.devices) {
            $issues.Add("${name} : droits systeme particuliers a examiner.")
        }
        foreach ($property in $service.environment.PSObject.Properties) {
            $key = $property.Name; $value = [string]$property.Value
            if ($key -like 'ConnectionStrings__*' -and $key -ne 'ConnectionStrings__Redis') {
                $hostMatch = [regex]::Match($value, '(?i)(?:^|;)\s*(?:Host|Server)\s*=\s*([^;]+)')
                if (-not $hostMatch.Success -or $hostMatch.Groups[1].Value.Trim() -ne 'postgres') {
                    $issues.Add("${name} : $key doit pointer sur Host=postgres (base du Docker local).")
                }
            }
            if ($key -eq 'ConnectionStrings__Redis' -and $value -notmatch '^redis:6379(?:,|$)') {
                $issues.Add("${name} : Redis doit pointer sur redis:6379.")
            }
            if ($key -eq 'RabbitMQ__Host' -and $value -ne 'rabbitmq') { $issues.Add("${name} : RabbitMQ non local.") }
            if ($key -in @('JwtSettings__Secret', 'JWT_SECRET') -and $value -ne $secrets.Jwt) {
                $issues.Add("${name} : la cle JWT effective doit etre celle de la recette.")
            }
            if ($key -eq 'InternalServices__ApiKey' -and $value -ne $secrets.Internal) {
                $issues.Add("${name} : la cle interne effective doit etre celle de la recette.")
            }
            if ($value -match '^https?://' -and $key -ne 'ASPNETCORE_URLS') {
                try { $hostValue = ([Uri]$value).DnsSafeHost } catch { $hostValue = '' }
                if ($allowedHosts -notcontains $hostValue) { $issues.Add("${name} : $key contient une destination externe.") }
            }
            if ($key -match '^(HTTP_PROXY|HTTPS_PROXY|ALL_PROXY)$' -and $value) {
                $issues.Add("${name} : proxy sortant configure.")
            }
        }
        foreach ($port in $service.ports) {
            if ($port.host_ip -and $port.host_ip -notin @('127.0.0.1', '::1')) {
                $issues.Add("${name} : port publie explicitement hors loopback.")
            }
        }
        foreach ($mount in $service.volumes) {
            if ($mount.type -eq 'bind') {
                $mountPath = [string]$mount.source
                $full = [IO.Path]::GetFullPath($mountPath)
                $inRepo = $full.StartsWith($RepoRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)
                $inPack = $full.StartsWith($PSScriptRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)
                if (-not ($inRepo -or $inPack)) { $issues.Add("${name} : montage situe hors du projet/dossier de recette.") }
            }
        }
    }
    if ((Get-EnvValue $model.services.'kcq-backend' 'MONGO_URI') -ne 'mongodb://kcq-mongo:27017/kcq') {
        $issues.Add('Qualite : MongoDB doit pointer sur le service kcq-mongo local.')
    }
    $network = $model.networks.'microservices-network'
    if (-not $network.internal -or $network.external -or $network.driver -ne 'bridge') {
        $issues.Add('Le reseau de recette doit etre un bridge interne gere par Compose.')
    }
    $volumeNames = @(& docker --context $DockerContext volume ls --format '{{.Name}}')
    if ($LASTEXITCODE -ne 0) { throw 'Impossible de verifier les volumes locaux.' }
    foreach ($p in $model.volumes.PSObject.Properties) {
        $v = $p.Value
        if ($v.external -or ($v.driver -and $v.driver -ne 'local') -or $v.driver_opts) {
            $issues.Add("Volume $($p.Name) : stockage externe ou options de montage a examiner.")
        }
        if ($volumeNames -contains $v.name) {
            $actualVolume = @(Read-DockerJson -DockerArgs @('--context', $DockerContext, 'volume', 'inspect', $v.name))[0]
            if ($actualVolume.Driver -ne 'local' -or ($actualVolume.Options -and @($actualVolume.Options.PSObject.Properties).Count -gt 0)) {
                $issues.Add("Volume $($p.Name) : le volume existant utilise un stockage particulier.")
            }
        }
    }
    $containerNames = @(& docker --context $DockerContext ps -a --format '{{.Names}}')
    if ($LASTEXITCODE -ne 0) { throw 'Impossible de verifier les conteneurs locaux.' }
    foreach ($p in $model.services.PSObject.Properties) {
        if ($p.Value.container_name -and $containerNames -contains $p.Value.container_name) {
            $c = @(Read-DockerJson -DockerArgs @('--context', $DockerContext, 'inspect', $p.Value.container_name))[0]
            if ($c.Config.Labels.'com.docker.compose.project' -ne $project) {
                $issues.Add("$($p.Name) : nom de conteneur utilise par un autre projet Compose.")
            }
        }
    }
    if ($issues.Count -gt 0) {
        Write-Host 'Activation arretee AVANT toute recreation de conteneur :' -ForegroundColor Yellow
        $issues | Sort-Object -Unique | ForEach-Object { Write-Host " - $_" }
        throw 'Corriger les points signales dans la configuration locale. Ne pas transmettre le .env complet.'
    }
    Write-Host 'Configuration verifiee : SQL/Mongo/Redis/RabbitMQ locaux ; reseau interne ; URLs localhost.'
    if ($VerifierSeulement) {
        Write-Host 'Verification terminee. Aucun conteneur demarre ou recree. Relancer sans -VerifierSeulement pour appliquer.'
        return
    }
    # Le depot stocke certains modeles DOCX dans le conteneur sans volume.
    # Les recuperer avant de recreer ce conteneur, sans ecraser un stock deja prepare.
    $docName = $model.services.'documentation-backend'.container_name
    if ($docName -and $containerNames -contains $docName) {
        $docContainer = @(Read-DockerJson -DockerArgs @('--context', $DockerContext, 'inspect', $docName))[0]
        $sameMount = @($docContainer.Mounts | Where-Object {
            $_.Destination -eq '/app/local-template-storage' -and
            ([string]$_.Source).Replace('\', '/').ToLowerInvariant().EndsWith(
                $templateDir.Replace('\', '/').Substring(2).ToLowerInvariant())
        }).Count -gt 0
        if (-not $sameMount) {
            if (@(Get-ChildItem -LiteralPath $templateDir -Force).Count -gt 0) {
                throw 'Le stock documentaire de destination est deja rempli. Verifier sa correspondance avant recreation ; aucun fichier ecrase.'
            }
            $previousPreference = $ErrorActionPreference
            try {
                $ErrorActionPreference = 'Continue'
                $copyOutput = @(& docker --context $DockerContext cp "${docName}:/app/local-template-storage/." $templateDir 2>&1)
                $copyCode = $LASTEXITCODE
            }
            finally { $ErrorActionPreference = $previousPreference }
            if ($copyCode -ne 0 -and ($copyOutput -join ' ') -notmatch '(?i)could not find|no such file|does not exist') {
                throw 'Impossible de conserver les modeles documentaires locaux. Recreation annulee.'
            }
        }
    }
    Write-Host 'Recreation de la stack locale avec ses volumes existants. Les sessions locales seront a reconnecter.'
    & docker @composeArgs up -d --force-recreate --no-build --pull never
    if ($LASTEXITCODE -ne 0) {
        throw 'Demarrage local incomplet. Certains conteneurs peuvent deja etre recrees. Voir LISEZ-MOI.md ; ne pas retirer les restrictions pour contourner une erreur.'
    }
    $actualNetwork = @(Read-DockerJson -DockerArgs @('--context', $DockerContext, 'network', 'inspect', $network.name))[0]
    if (-not $actualNetwork.Internal) { throw 'Le reseau actif ne confirme pas Internal=true. Ne pas commencer la recette.' }
    $ids = @(& docker @composeArgs ps -q)
    if ($LASTEXITCODE -ne 0 -or $ids.Count -eq 0) { throw 'Impossible de confirmer les conteneurs actifs.' }
    foreach ($id in $ids) {
        $running = @(Read-DockerJson -DockerArgs @('--context', $DockerContext, 'inspect', $id))[0]
        $runningNets = @($running.NetworkSettings.Networks.PSObject.Properties.Name)
        if ($runningNets.Count -ne 1 -or $runningNets[0] -ne $network.name) {
            throw 'Un conteneur actif a une connexion reseau supplementaire. Ne pas commencer la recette.'
        }
    }
    foreach ($port in @(8200, 8201)) {
        $response = Invoke-WebRequest -UseBasicParsing -Uri "http://localhost:$port/kyntus-public-urls.js" -TimeoutSec 20
        $text = [string]$response.Content
        if ($text -match '10\.10\.10\.25' -or $text -notmatch 'http://localhost:8200/auth-callback') {
            throw "Le frontend $port ne sert pas le profil attendu. Ne pas commencer la recette."
        }
    }
    Write-Host 'Profil LOCAL servi par les deux interfaces ; reseau interne confirme.' -ForegroundColor Green
    Write-Host 'Ouvrir http://localhost:8201/login puis verifier que la connexion reste sur http://localhost:8200.'
    Write-Host 'HTEL, mails, push externe, IA distante et audio distant restent a tester avec des doubles/sandboxes.'
    Write-Host 'La connexion avec vos comptes et les tests fonctionnels restent a executer dans le navigateur.'
}
finally {
    foreach ($name in $envNames) {
        [Environment]::SetEnvironmentVariable($name, $savedEnv[$name], 'Process')
    }
}
