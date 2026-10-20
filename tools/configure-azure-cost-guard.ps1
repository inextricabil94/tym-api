[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SubscriptionId,
    [string]$ResourceGroup = 'tym-llm-prod-rg',
    [string]$Location = 'eastus',
    [string]$BudgetName = 'all-hosted-apps-monthly-eur10',
    [string]$WorkflowName = 'all-hosted-apps-budget-stop',
    [string]$ActionGroupName = 'all-hosted-apps-budget-stop',
    [Parameter(Mandatory)][string]$NodePath,
    [string]$AzurePython = 'C:/Program Files/Microsoft SDKs/Azure/CLI2/python.exe',
    [switch]$Test
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$subscription = "/subscriptions/$SubscriptionId"
$arm = 'https://management.azure.com'
$token = & $AzurePython -X utf8 -m azure.cli account get-access-token --resource "$arm/" --subscription $SubscriptionId --query accessToken -o tsv
if ($LASTEXITCODE -ne 0) { throw 'Could not obtain Azure credentials.' }
$headers = @{ Authorization = "Bearer $token" }

function Invoke-Arm([string]$Method, [string]$Path, $Body = $null) {
    $arguments = @{ Method = $Method; Uri = "$arm$Path"; Headers = $headers; TimeoutSec = 180 }
    if ($null -ne $Body) {
        $arguments.ContentType = 'application/json'
        $arguments.Body = $Body | ConvertTo-Json -Depth 100 -Compress
    }
    Invoke-RestMethod @arguments
}

function Get-StableGuid([string]$Name) {
    $hash = [System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes("$SubscriptionId/$Name"))
    ([guid]::new([byte[]]$hash[0..15])).ToString()
}

$budgetPath = "$subscription/providers/Microsoft.Consumption/budgets/${BudgetName}?api-version=2024-08-01"
$budget = Invoke-Arm GET $budgetPath
if ($budget.properties.amount -ne 20 -or $budget.properties.timeGrain -ne 'Monthly' -or $budget.properties.currentSpend.unit -ne 'EUR') {
    throw 'This tool requires the existing EUR 20 monthly budget.'
}
if (@($budget.properties.filter.PSObject.Properties).Count -ne 0) { throw 'Budget must cover the whole subscription.' }

$generated = & $NodePath (Join-Path $PSScriptRoot '../deploy/azure-cost-guard.mjs')
if ($LASTEXITCODE -ne 0) { throw 'Workflow generation failed.' }
$template = $generated | ConvertFrom-Json -AsHashtable -Depth 100
$workflowId = "$subscription/resourceGroups/$ResourceGroup/providers/Microsoft.Logic/workflows/$WorkflowName"
$workflowPath = "${workflowId}?api-version=2019-05-01"

foreach ($namespace in @('Microsoft.Logic', 'Microsoft.Insights')) {
    $provider = Invoke-Arm GET "$subscription/providers/${namespace}?api-version=2021-04-01"
    if ($provider.registrationState -ne 'Registered') {
        $null = Invoke-Arm POST "$subscription/providers/$namespace/register?api-version=2021-04-01" @{}
        $deadline = [DateTime]::UtcNow.AddMinutes(5)
        do {
            Start-Sleep -Seconds 10
            $provider = Invoke-Arm GET "$subscription/providers/${namespace}?api-version=2021-04-01"
        } until ($provider.registrationState -eq 'Registered' -or [DateTime]::UtcNow -gt $deadline)
        if ($provider.registrationState -ne 'Registered') { throw "$namespace provider registration is still pending." }
    }
}

$workflowBody = @{
    location = $Location
    identity = @{ type = 'SystemAssigned' }
    tags = @{ purpose = 'Approved EUR 20 monthly all-app shutdown'; managedBy = 'TYM cost guard' }
    properties = @{
        state = 'Disabled'
        definition = $template.definition
        parameters = @{
            subscriptionId = @{ value = $SubscriptionId }; budgetName = @{ value = $BudgetName }
            currency = @{ value = 'EUR' }; monthlyLimit = @{ value = 20 }
        }
    }
}
$workflow = Invoke-Arm PUT $workflowPath $workflowBody
$principalId = $workflow.identity.principalId
if (-not $principalId) { throw 'Workflow managed identity was not created.' }
Write-Output 'Workflow created disabled; configuring restricted permissions.'

$roleId = "$subscription/providers/Microsoft.Authorization/roleDefinitions/$(Get-StableGuid 'all-hosted-apps-budget-stop-role')"
$role = @{
    properties = @{
        roleName = 'All Hosted Apps Budget Stop'
        description = 'Query costs, list and stop hosted apps, and disable static websites. No delete, start, keys, data access, or role administration.'
        type = 'CustomRole'; assignableScopes = @($subscription)
        permissions = @(@{ actions = $template.permissions; notActions = @(); dataActions = @(); notDataActions = @() })
    }
}
$null = Invoke-Arm PUT "${roleId}?api-version=2022-04-01" $role
$assignmentId = Get-StableGuid "all-hosted-apps-budget-stop-assignment/$principalId"
$null = Invoke-Arm PUT "$subscription/providers/Microsoft.Authorization/roleAssignments/${assignmentId}?api-version=2022-04-01" @{
    properties = @{ roleDefinitionId = $roleId; principalId = $principalId; principalType = 'ServicePrincipal' }
}

$workflowBody.properties.state = 'Enabled'
$null = Invoke-Arm PUT $workflowPath $workflowBody
$callback = Invoke-Arm POST "$workflowId/triggers/manual/listCallbackUrl?api-version=2019-05-01" @{}
if (-not $callback.value) { throw 'No workflow callback was returned.' }

# The signed callback stays in memory and Azure; never print or export it.
$actionGroupId = "$subscription/resourceGroups/$ResourceGroup/providers/Microsoft.Insights/actionGroups/$ActionGroupName"
$null = Invoke-Arm PUT "${actionGroupId}?api-version=2023-01-01" @{
    location = 'global'
    properties = @{
        groupShortName = 'BudgetStop20'; enabled = $true
        logicAppReceivers = @(@{ name = 'StopHostedApps'; resourceId = $workflowId; callbackUrl = $callback.value; useCommonAlertSchema = $true })
    }
}

$notification = $budget.properties.notifications.PSObject.Properties | Where-Object { $_.Value.thresholdType -eq 'Actual' -and $_.Value.threshold -eq 100 }
if (@($notification).Count -ne 1) { throw 'Expected exactly one actual 100% budget alert.' }
$notification.Value.contactGroups = @(@($notification.Value.contactGroups) + $actionGroupId | Select-Object -Unique)
$budgetBody = @{
    eTag = $budget.eTag
    properties = @{
        amount = $budget.properties.amount; category = $budget.properties.category
        timeGrain = $budget.properties.timeGrain; timePeriod = $budget.properties.timePeriod
        filter = $budget.properties.filter; notifications = $budget.properties.notifications
    }
}
$null = Invoke-Arm PUT $budgetPath $budgetBody
$verified = Invoke-Arm GET $budgetPath
$linked = @($verified.properties.notifications.PSObject.Properties | Where-Object { $_.Value.thresholdType -eq 'Actual' -and $_.Value.threshold -eq 100 -and $_.Value.contactGroups -contains $actionGroupId })
if ($linked.Count -ne 1) { throw 'Budget/action-group link was not saved.' }
Write-Output 'Verified: EUR 20 monthly actual-cost alert linked to the enabled shutdown workflow.'

if ($Test) {
    $payload = @{
        schemaId = 'azureMonitorCommonAlertSchema'
        data = @{ alertContext = @{ AlertData = @{
            BudgetId = "$subscription/providers/Microsoft.Consumption/budgets/$BudgetName"
            ThresholdType = 'Actual'; Unit = 'EUR'
        } } }
    } | ConvertTo-Json -Depth 8
    try {
        $response = Invoke-WebRequest -Method Post -Uri $callback.value -ContentType application/json -Body $payload -TimeoutSec 120
    } catch {
        throw 'Workflow test submission failed; signed callback details suppressed.'
    }
    $runId = @($response.Headers['x-ms-workflow-run-id'])[0]
    if (-not $runId) { throw 'Test accepted but no run ID returned; inspect Azure workflow history.' }
    $deadline = [DateTime]::UtcNow.AddMinutes(5)
    do {
        Start-Sleep -Seconds 5
        $run = Invoke-Arm GET "$workflowId/runs/${runId}?api-version=2019-05-01"
    } until ($run.properties.status -notin @('Running', 'Waiting') -or [DateTime]::UtcNow -gt $deadline)
    if ($run.properties.status -ne 'Succeeded') { throw "Workflow test did not succeed: $($run.properties.status). Inspect Azure run history." }
    Write-Output 'Live workflow test succeeded using real current-month cost; no cost override was supplied.'
}

[pscustomobject]@{ workflow = $workflowId; actionGroup = $actionGroupId; role = $roleId; monthlyLimitEUR = 20; automaticRestart = $false } | ConvertTo-Json
