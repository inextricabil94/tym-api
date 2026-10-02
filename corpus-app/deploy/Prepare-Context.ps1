<#
.SYNOPSIS
Stages explicit source/model allowlists for remote Docker builds.
.DESCRIPTION
Raw books, imported JSONL, private cluster weights, tests, Git metadata and browser screenshots are excluded.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ModelsPath,
    [Parameter(Mandatory)][string]$OutPath,
    [string]$PdfPath,
    [string]$PptxPath
)
$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$contextRoot = [System.IO.Path]::GetFullPath($OutPath)
if (Test-Path -LiteralPath $contextRoot) {
    if (@(Get-ChildItem -LiteralPath $contextRoot -Force).Count -gt 0) {
        throw 'Choose a new empty build-context directory so stale files cannot enter the Docker image.'
    }
}
New-Item -ItemType Directory -Path $contextRoot -Force | Out-Null
function Copy-ContextFile([string]$Source, [string]$Target) {
    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) { throw "Required file is missing: $Source" }
    New-Item -ItemType Directory -Path ([System.IO.Path]::GetDirectoryName($Target)) -Force | Out-Null
    Copy-Item -LiteralPath $Source -Destination $Target
}
foreach ($service in @('Api','Ui')) {
    $serviceRoot = Join-Path $projectRoot "src/Tym.Corpus.$service"
    $destination = Join-Path $contextRoot $service.ToLowerInvariant()
    foreach ($file in Get-ChildItem -LiteralPath $serviceRoot -File -Force) {
        if ($file.Extension -in @('.cs','.csproj') -or $file.Name -in @('Dockerfile','.dockerignore','package.json','package-lock.json','tsconfig.json')) {
            $targetName = if ($service -eq 'Api' -and $file.Extension -in @('.cs','.csproj')) { 'src/Tym.Corpus.Api/' + $file.Name } else { $file.Name }
            Copy-ContextFile $file.FullName (Join-Path $destination $targetName)
        }
    }
    if ($service -eq 'Ui') {
        foreach ($folder in @('wwwroot','client')) {
            $contentRoot = Join-Path $serviceRoot $folder
            if (Test-Path -LiteralPath $contentRoot) {
                foreach ($file in Get-ChildItem -LiteralPath $contentRoot -File -Recurse) {
                    $relativePath = [System.IO.Path]::GetRelativePath($serviceRoot, $file.FullName)
                    Copy-ContextFile $file.FullName (Join-Path $destination $relativePath)
                }
            }
        }
    }
}
$coreRoot = Join-Path $projectRoot 'src/Tym.Corpus.Core'
foreach ($file in Get-ChildItem -LiteralPath $coreRoot -File) {
    if ($file.Extension -in @('.cs','.csproj')) {
        Copy-ContextFile $file.FullName (Join-Path $contextRoot ('api/src/Tym.Corpus.Core/' + $file.Name))
    }
}
$modelIds = @('segment_type_en','segment_type_ro','temporal_relation_en','temporal_relation_ro',
    'timebank_event_class_ro','timebank_event_tense_ro','timebank_timex_type_ro',
    'timebank_tlink_ro','timebank_slink_ro','timebank_alink_ro',
    'timebank_event_class_ro_structured','timebank_event_tense_ro_structured','timebank_timex_type_ro_structured',
    'timebank_tlink_ro_structured','timebank_slink_ro_structured','timebank_alink_ro_structured')
foreach ($modelId in $modelIds) {
    $artifact = Join-Path $ModelsPath ($modelId + '_sdca.zip')
    $manifestPath = $artifact + '.manifest.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $actualHash = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($manifest.model_sha256 -cne $actualHash) { throw "Model checksum does not match its manifest: $modelId" }
    Copy-ContextFile $artifact (Join-Path $contextRoot ('api/models/' + [System.IO.Path]::GetFileName($artifact)))
    Copy-ContextFile $manifestPath (Join-Path $contextRoot ('api/models/' + [System.IO.Path]::GetFileName($manifestPath)))
}
foreach ($artifactPath in @($PdfPath,$PptxPath)) {
    if ($artifactPath) {
        Copy-ContextFile $artifactPath (Join-Path $contextRoot ('ui/wwwroot/results/' + [System.IO.Path]::GetFileName($artifactPath)))
    }
}
$inventory = Get-ChildItem -LiteralPath $contextRoot -File -Recurse | ForEach-Object {
    [pscustomobject]@{ path=[System.IO.Path]::GetRelativePath($contextRoot,$_.FullName); bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
}
$inventory | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $contextRoot 'context-inventory.json') -Encoding utf8
Write-Output "Staged two explicit build contexts with sixteen checksum-verified ML.NET classifiers: $contextRoot"
