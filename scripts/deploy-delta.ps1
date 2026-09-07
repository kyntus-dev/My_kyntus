<#
.SYNOPSIS
  Suivi des fichiers à déployer (delta) vers le serveur Kyntus.

.DESCRIPTION
  - status  : liste les fichiers en attente (pending + optionnellement git depuis le mark)
  - add     : ajoute un ou plusieurs chemins à deploy/pending-files.txt
  - export  : génère un tar Windows + 3 commandes (scp / extract / rebuild)
  - mark    : après déploiement réussi, vide pending et met à jour last-deploy.json

.EXAMPLE
  .\scripts\deploy-delta.ps1 -Action status
  .\scripts\deploy-delta.ps1 -Action add -Path 'PlanningService\Planning.Infrastructure\Persistence\PlanningSchemaPatches.cs'
  .\scripts\deploy-delta.ps1 -Action export
  .\scripts\deploy-delta.ps1 -Action mark -Note 'fix exceptional requests + schema patch'
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('status', 'add', 'export', 'mark')]
    [string]$Action,

    [string[]]$Path = @(),

    [string]$Note = '',

    [switch]$IncludeGitDiff,

    [string]$OutDir = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$deployDir = Join-Path $root 'deploy'
$pendingFile = Join-Path $deployDir 'pending-files.txt'
$lastDeployFile = Join-Path $deployDir 'last-deploy.json'
$historyFile = Join-Path $deployDir 'history.jsonl'

if (-not (Test-Path $deployDir)) {
    New-Item -ItemType Directory -Path $deployDir | Out-Null
}

function Normalize-RelPath([string]$p) {
    $p = $p.Trim().TrimStart('.', '\', '/')
    return ($p -replace '/', '\').Trim()
}

function Read-Pending {
    $list = [System.Collections.Generic.List[string]]::new()
    if (-not (Test-Path $pendingFile)) { return ,$list }
    Get-Content $pendingFile | ForEach-Object {
        $line = $_.Trim()
        if (-not $line -or $line.StartsWith('#')) { return }
        $n = Normalize-RelPath $line
        if ($n -and -not $list.Contains($n)) { [void]$list.Add($n) }
    }
    return ,$list
}

function Write-Pending([System.Collections.Generic.List[string]]$items) {
    if ($null -eq $items) { $items = [System.Collections.Generic.List[string]]::new() }
    $header = @(
        '# Fichiers a deployer depuis le dernier mark (un chemin relatif par ligne).',
        '# Mis a jour par: scripts\deploy-delta.ps1 -Action add -Path ...',
        '# Ou manuellement / par l''agent Cursor apres chaque correctif.',
        '# Ignorer les lignes vides et celles qui commencent par #.',
        '#'
    )
    $body = @($items | Sort-Object -Unique)
    Set-Content -Path $pendingFile -Value ($header + $body) -Encoding UTF8
}

function Test-IgnoredPath([string]$rel) {
    $lower = $rel.ToLowerInvariant()
    $parts = $lower -split '[\\/]'
    $skipDirs = @('bin', 'obj', 'node_modules', '.angular', 'dist', '.git', 'TestResults', 'coverage')
    foreach ($d in $skipDirs) {
        if ($parts -contains $d) { return $true }
    }
    if ($lower -match '\.(dll|pdb|cache|user)$') { return $true }
    return $false
}

function Get-GitChangedSinceMark {
    $files = [System.Collections.Generic.List[string]]::new()
    if (-not (Test-Path $lastDeployFile)) { return ,$files }
    $mark = Get-Content -Raw $lastDeployFile | ConvertFrom-Json
    $commit = $mark.gitCommit
    if (-not $commit) { return ,$files }

    Push-Location $root
    try {
        $diff = git diff --name-only $commit -- 2>$null
        $untracked = git ls-files --others --exclude-standard 2>$null
        foreach ($f in @($diff) + @($untracked)) {
            if (-not $f) { continue }
            $n = Normalize-RelPath $f
            if (Test-IgnoredPath $n) { continue }
            if ($n -and -not $files.Contains($n)) { [void]$files.Add($n) }
        }
    } finally {
        Pop-Location
    }
    return ,$files
}

function Get-DeploySet {
    $set = Read-Pending
    if ($null -eq $set) { $set = [System.Collections.Generic.List[string]]::new() }
    if ($IncludeGitDiff) {
        foreach ($f in (Get-GitChangedSinceMark)) {
            if (-not $set.Contains($f)) { [void]$set.Add($f) }
        }
    }
    $existing = [System.Collections.Generic.List[string]]::new()
    foreach ($f in $set) {
        $full = Join-Path $root $f
        if (Test-Path -LiteralPath $full -PathType Leaf) {
            [void]$existing.Add($f)
        } elseif (Test-Path -LiteralPath $full -PathType Container) {
            Get-ChildItem -LiteralPath $full -Recurse -File | ForEach-Object {
                $rel = Normalize-RelPath $_.FullName.Substring($root.Length)
                if (-not (Test-IgnoredPath $rel) -and -not $existing.Contains($rel)) {
                    [void]$existing.Add($rel)
                }
            }
        } else {
            Write-Warning "Introuvable (ignore): $f"
        }
    }
    return ,$existing
}

function Get-RebuildServices([string[]]$files) {
    $order = @(
        'auth-backend',
        'employee-directory-backend',
        'planning-backend',
        'formation-backend',
        'conge-backend',
        'prime-backend',
        'parrainage-backend',
        'kcq-backend',
        'kcq-frontend',
        'api-gateway',
        'auth-frontend',
        'planning-frontend'
    )
    $hit = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($f in $files) {
        $n = ($f -replace '/', '\').ToLowerInvariant()
        $parts = $n -split '\\'
        if ($parts -contains 'unittests' -or $parts -contains 'tests') { continue }

        if ($n.StartsWith('authservice\')) { [void]$hit.Add('auth-backend') }
        elseif ($n.StartsWith('employeedirectory\')) { [void]$hit.Add('employee-directory-backend') }
        elseif ($n.StartsWith('planningservice\')) { [void]$hit.Add('planning-backend') }
        elseif ($n.StartsWith('formationservice\conge.') -or $n.StartsWith('formationservice\conge\')) { [void]$hit.Add('conge-backend') }
        elseif ($n.StartsWith('formationservice\')) { [void]$hit.Add('formation-backend') }
        elseif ($n.StartsWith('primebackend\')) { [void]$hit.Add('prime-backend') }
        elseif ($n.StartsWith('parrainage_service_backend\')) { [void]$hit.Add('parrainage-backend') }
        elseif ($n.StartsWith('qualiteservice\frontend\')) { [void]$hit.Add('kcq-frontend') }
        elseif ($n.StartsWith('qualiteservice\')) { [void]$hit.Add('kcq-backend') }
        elseif ($n.StartsWith('projet_kyntus_service_planning-frontend\')) { [void]$hit.Add('planning-frontend') }
        elseif ($n.StartsWith('auth-frontend\')) { [void]$hit.Add('auth-frontend') }
        elseif ($n.StartsWith('apigateway.api\') -or $n.StartsWith('init\ocelot')) { [void]$hit.Add('api-gateway') }
        elseif ($n.StartsWith('kyntus.iam\')) {
            [void]$hit.Add('employee-directory-backend')
            [void]$hit.Add('planning-backend')
            [void]$hit.Add('conge-backend')
            [void]$hit.Add('prime-backend')
        }
    }
    $services = @($order | Where-Object { $hit.Contains($_) })
    if ($services.Count -eq 0) { $services = @('planning-backend') }
    return ,$services
}

function Read-LastDeploy {
    if (-not (Test-Path $lastDeployFile)) {
        return [pscustomobject]@{
            markedAt = $null
            gitCommit = $null
            serverHost = 'o@10.10.10.25'
            serverPath = '/home/o/projets_mykyntus-'
        }
    }
    return Get-Content -Raw $lastDeployFile | ConvertFrom-Json
}

switch ($Action) {
    'status' {
        $mark = Read-LastDeploy
        Write-Host "Dernier mark: $($mark.markedAt)  commit=$($mark.gitCommit)"
        Write-Host "Serveur: $($mark.serverHost):$($mark.serverPath)"
        Write-Host ''
        $pending = Read-Pending
        Write-Host "Pending ($($pending.Count)):"
        $pending | ForEach-Object { Write-Host "  $_" }
        if ($IncludeGitDiff) {
            Write-Host ''
            $git = Get-GitChangedSinceMark
            Write-Host "Git depuis mark ($($git.Count), hors bin/obj):"
            $git | Select-Object -First 80 | ForEach-Object { Write-Host "  $_" }
            if ($git.Count -gt 80) { Write-Host "  ... +$($git.Count - 80) autres" }
        }
    }

    'add' {
        if (-not $Path -or $Path.Count -eq 0) {
            throw 'Utiliser -Path avec un ou plusieurs chemins relatifs au repo.'
        }
        $list = Read-Pending
        $expanded = [System.Collections.Generic.List[string]]::new()
        foreach ($raw in $Path) {
            foreach ($piece in ($raw -split ',')) {
                $piece = $piece.Trim().Trim("'").Trim('"')
                if ($piece) { [void]$expanded.Add($piece) }
            }
        }
        foreach ($p in $expanded) {
            $n = Normalize-RelPath $p
            $full = Join-Path $root $n
            if (-not (Test-Path -LiteralPath $full)) {
                Write-Warning "Chemin introuvable: $n"
                continue
            }
            if (-not $list.Contains($n)) {
                [void]$list.Add($n)
                Write-Host "Ajoute: $n"
            } else {
                Write-Host "Deja liste: $n"
            }
        }
        Write-Pending $list
    }

    'export' {
        $mark = Read-LastDeploy
        $files = Get-DeploySet
        if ($files.Count -eq 0) {
            Write-Host 'Aucun fichier à exporter. Ajoutez des chemins (add) ou -IncludeGitDiff.'
            return
        }

        if (-not $OutDir) {
            $OutDir = Join-Path $deployDir 'out'
        }
        if (-not (Test-Path $OutDir)) {
            New-Item -ItemType Directory -Path $OutDir | Out-Null
        }

        $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
        $tarName = "kyntus-delta-$stamp.tar"
        $tarPath = Join-Path $OutDir $tarName
        $listPath = Join-Path $OutDir "kyntus-delta-$stamp.files.txt"

        $unixPaths = @($files | ForEach-Object { ($_ -replace '\\', '/') })
        $unixPaths | Set-Content -Path $listPath -Encoding ascii

        Push-Location $root
        try {
            Remove-Item $tarPath -ErrorAction SilentlyContinue
            & tar -acf $tarPath --files-from=$listPath
            if ($LASTEXITCODE -ne 0 -or -not (Test-Path $tarPath)) {
                Remove-Item $tarPath -ErrorAction SilentlyContinue
                & tar -acf $tarPath @unixPaths
            }
        } finally {
            Pop-Location
        }

        if (-not (Test-Path $tarPath)) {
            throw "Échec création archive: $tarPath"
        }

        $hostUser = $mark.serverHost
        if (-not $hostUser) { $hostUser = 'o@10.10.10.25' }
        $remoteRoot = $mark.serverPath
        if (-not $remoteRoot) { $remoteRoot = '/home/o/projets_mykyntus-' }

        $remoteTar = "/tmp/$tarName"
        $sizeMb = [math]::Round((Get-Item $tarPath).Length / 1MB, 2)

        $services = Get-RebuildServices @($files)
        $svcList = ($services -join ' ')
        $svcCount = $services.Count

        Write-Host ''
        Write-Host ("Archive: {0} ({1} Mo) - {2} fichier(s)" -f $tarPath, $sizeMb, $files.Count)
        Write-Host ("Liste:   {0}" -f $listPath)
        Write-Host ''
        Write-Host '1. Copier'
        Write-Host ''
        Write-Host ('scp "{0}" {1}:{2}' -f $tarPath, $hostUser, $remoteTar)
        Write-Host ''
        Write-Host '2. Extraire sur le serveur'
        Write-Host ''
        $extractCmd = 'cd {0}; tar -xf {1}; rm -f {1}' -f $remoteRoot, $remoteTar
        Write-Host ('ssh {0} "{1}"' -f $hostUser, $extractCmd)
        Write-Host ''
        Write-Host ("3. Rebuild des {0} services" -f $svcCount)
        Write-Host ''
        $rebuildCmd = 'cd {0}; docker compose up -d --build {1}' -f $remoteRoot, $svcList
        Write-Host ('ssh {0} "{1}"' -f $hostUser, $rebuildCmd)
        Write-Host ''
        Write-Host ("Quand les {0} sont Up, dis C FAIT (on marquera le delta ensuite). Ne copie pas le .env." -f $svcCount)
    }

    'mark' {
        Push-Location $root
        try {
            $commit = (git rev-parse HEAD).Trim()
        } finally {
            Pop-Location
        }
        $mark = Read-LastDeploy
        $pending = Read-Pending
        $now = (Get-Date).ToString('yyyy-MM-ddTHH:mm:sszzz')
        $newMark = [ordered]@{
            markedAt            = $now
            note                = if ($Note) { $Note } else { 'Deploiement delta applique' }
            gitCommit           = $commit
            serverHost          = if ($mark.serverHost) { $mark.serverHost } else { 'o@10.10.10.25' }
            serverPath          = if ($mark.serverPath) { $mark.serverPath } else { '/home/o/projets_mykyntus-' }
            lastServicesRebuilt = @()
            fileCount           = $pending.Count
        }
        $utf8NoBom = New-Object System.Text.UTF8Encoding $false
        [System.IO.File]::WriteAllText(
            $lastDeployFile,
            (($newMark | ConvertTo-Json -Depth 5) + [Environment]::NewLine),
            $utf8NoBom
        )

        $hist = [ordered]@{
            at        = $now
            event     = 'deployed'
            gitCommit = $commit
            note      = $newMark.note
            files     = @($pending)
        }
        [System.IO.File]::AppendAllText(
            $historyFile,
            (($hist | ConvertTo-Json -Compress -Depth 5) + [Environment]::NewLine),
            $utf8NoBom
        )

        Write-Pending ([System.Collections.Generic.List[string]]::new())
        Write-Host ("Mark enregistre ({0}). Pending vide. Historique: deploy/history.jsonl" -f $now)
    }
}
