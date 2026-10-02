<#
.SYNOPSIS
Builds the separate application with ACR's remote Docker builder and deploys two Azure Container Apps.
.DESCRIPTION
Uses the existing Azure environment/registry selected by the caller. Registry credentials remain in memory and are not written to a result file.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SubscriptionId,
    [Parameter(Mandatory)][string]$ResourceGroup,
    [Parameter(Mandatory)][string]$EnvironmentName,
    [Parameter(Mandatory)][string]$RegistryName,
    [Parameter(Mandatory)][string]$ContextPath,
    [string]$ApiName = 'tym-corpus-api-serban',
    [string]$UiName = 'tym-corpus-ui-serban',
    [string]$ImageTag = ('research-' + (Get-Date -Format 'yyyyMMdd-HHmmss')),
    [string]$ReportPath
)
$ErrorActionPreference = 'Stop'
function Az-Json([string[]]$Arguments) {
    $result = & az @Arguments --subscription $SubscriptionId --only-show-errors --output json
    if ($LASTEXITCODE -ne 0) { throw 'Azure CLI operation failed; deployment has not been confirmed.' }
    if ($result) { return ($result -join "`n" | ConvertFrom-Json) }
}
$registry = Az-Json @('acr','show','--name',$RegistryName)
$environment = Az-Json @('containerapp','env','show','--name',$EnvironmentName,'--resource-group',$ResourceGroup)
foreach ($service in @('api','ui')) {
    $context = Join-Path $ContextPath $service
    if (-not (Test-Path -LiteralPath (Join-Path $context 'Dockerfile'))) { throw 'Build context is incomplete.' }
    & az acr build --subscription $SubscriptionId --registry $RegistryName --image "tym-corpus-${service}:$ImageTag" --file (Join-Path $context 'Dockerfile') $context --only-show-errors
    if ($LASTEXITCODE -ne 0) { throw "Remote Docker build failed for $service." }
}
$credentials = Az-Json @('acr','credential','show','--name',$RegistryName)
$registryPassword = $credentials.passwords[0].value
try {
    $apiFqdn = $null
    $deployed = @()
    foreach ($service in @('api','ui')) {
        $name = if ($service -eq 'api') { $ApiName } else { $UiName }
        $existing = @(Az-Json @('containerapp','list','--resource-group',$ResourceGroup)) | Where-Object name -EQ $name
        $image = "$($registry.loginServer)/tym-corpus-${service}:$ImageTag"
        $envVars = if ($service -eq 'api') {
            @('TYM_CORPUS_MODEL_DIR=/app/models')
        } else { @("TYM_API_BASE_URL=https://$apiFqdn") }
        if ($existing) {
            $app = Az-Json (@('containerapp','update','--name',$name,'--resource-group',$ResourceGroup,'--image',$image,'--set-env-vars') + $envVars)
        } else {
            $app = Az-Json (@('containerapp','create','--name',$name,'--resource-group',$ResourceGroup,'--environment',$environment.id,
                '--image',$image,'--ingress','external','--target-port','8080','--cpu','0.5','--memory','1Gi',
                '--min-replicas','0','--max-replicas','3','--registry-server',$registry.loginServer,
                '--registry-username',$credentials.username,'--registry-password',$registryPassword,'--env-vars') + $envVars)
        }
        $app = Az-Json @('containerapp','show','--name',$name,'--resource-group',$ResourceGroup)
        if ($service -eq 'api') { $apiFqdn = $app.properties.configuration.ingress.fqdn }
        $deployed += [pscustomobject]@{ name=$name; url=('https://' + $app.properties.configuration.ingress.fqdn); revision=$app.properties.latestRevisionName; image=$image; provisioning_state=$app.properties.provisioningState; running_status=$app.properties.runningStatus }
    }
    $report = [pscustomobject]@{ timestamp_utc=[DateTimeOffset]::UtcNow; subscription_id=$SubscriptionId; resource_group=$ResourceGroup; environment=$EnvironmentName; services=$deployed; external_prediction_verification='pending browser/HTTP confirmation' }
    if ($ReportPath) { $report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $ReportPath -Encoding utf8 }
    $report | ConvertTo-Json -Depth 10
} finally {
    $registryPassword = $null
    $credentials = $null
}
