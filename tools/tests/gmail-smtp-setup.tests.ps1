$ErrorActionPreference = 'Stop'
$global:TYMTestCapturedSettings = $null
$global:TYMTestInputs = @()
$global:TYMTestPromptCount = 0
$global:TYMTestStoreCount = 0
function Read-Host {
    param([string]$Prompt, [switch]$AsSecureString)
    if (-not $AsSecureString) { throw 'The password prompt must use SecureString.' }
    if ($global:TYMTestStoreCount -ne 0) { throw 'Settings were stored before input validation finished.' }
    if ($global:TYMTestPromptCount -ge $global:TYMTestInputs.Count) { throw 'Unexpected additional prompt.' }
    $value = $global:TYMTestInputs[$global:TYMTestPromptCount++]
    if ($value.Length -eq 0) { return [Security.SecureString]::new() }
    ConvertTo-SecureString $value -AsPlainText -Force
}
function dotnet {
    process {
        if ($args -contains 'abcdefghijklmnop') { throw 'A credential was passed as a command argument.' }
        $global:TYMTestCapturedSettings = $_ | ConvertFrom-Json
        $global:TYMTestStoreCount++
        $global:LASTEXITCODE = 0
    }
}
function Check($condition, $message) { if (-not $condition) { throw $message } }
try {
    $nbsp = [char]0x00a0
    $zeroWidth = [char]0x200b
    $bom = [char]0xfeff
    $wordJoiner = [char]0x2060
    $cases = @(
        @{ Name = 'plain'; Inputs = @('abcdefghijklmnop') },
        @{ Name = 'grouped'; Inputs = @('abcd efgh ijkl mnop') },
        @{ Name = 'tabs-newlines'; Inputs = @("`tabcd`tefgh`r`nijkl`nmnop`r`n") },
        @{ Name = 'nonbreaking-spaces'; Inputs = @("${nbsp}abcd${nbsp}efgh${nbsp}ijkl${nbsp}mnop${nbsp}") },
        @{ Name = 'invisible-copy-marks'; Inputs = @("${bom}abcd${zeroWidth}efgh${wordJoiner}ijkl${zeroWidth}mnop${bom}") },
        @{ Name = 'double-quoted-18'; Inputs = @('"abcdefghijklmnop"') },
        @{ Name = 'single-quoted-18'; Inputs = @("'abcdefghijklmnop'") },
        @{ Name = 'retry-code'; Inputs = @('123456', 'abcd efgh ijkl mnop') },
        @{ Name = 'retry-empty'; Inputs = @('', 'abcdefghijklmnop') },
        @{ Name = 'reject-punctuation'; Inputs = @('abcd!fghijklmnop', 'abcdefghijklmnop') },
        @{ Name = 'cancel-invalid'; Inputs = @('123456', 'abcdefghijklmnopqr', 'abcd!fghijklmnop'); Reject = $true }
    )
    foreach ($case in $cases) {
        $global:TYMTestInputs = $case.Inputs
        $global:TYMTestCapturedSettings = $null
        $global:TYMTestPromptCount = 0
        $global:TYMTestStoreCount = 0
        $warnings = @()
        $failed = $null
        try {
            & (Join-Path $PSScriptRoot '../configure-gmail-smtp.ps1') -Email 'authored-fixture@gmail.com' -WarningVariable warnings -WarningAction SilentlyContinue 6>$null
        }
        catch { $failed = $_ }
        Check ($global:TYMTestPromptCount -eq $case.Inputs.Count) "Incorrect prompt count: $($case.Name)"
        if ($case.Reject) {
            Check ($null -ne $failed -and $failed.Exception.Message -match 'no settings were changed') 'Invalid input was not safely rejected.'
            Check ($global:TYMTestStoreCount -eq 0 -and $null -eq $global:TYMTestCapturedSettings) 'Invalid input changed settings.'
        }
        else {
            Check ($null -eq $failed) "Valid input failed: $($case.Name)"
            Check ($global:TYMTestStoreCount -eq 1) 'Settings should be stored once after successful validation.'
            $settings = $global:TYMTestCapturedSettings
            Check ($settings.TYM_SMTP_HOST -eq 'smtp.gmail.com' -and $settings.TYM_SMTP_PORT -eq '587') 'Incorrect SMTP transport.'
            Check ($settings.TYM_SMTP_USER -eq 'authored-fixture@gmail.com' -and $settings.TYM_SMTP_FROM -eq $settings.TYM_SMTP_USER) 'Incorrect sender.'
            Check ($settings.TYM_SMTP_PASSWORD -ceq 'abcdefghijklmnop' -and $settings.TYM_MAIL_PICKUP_PATH -eq '') 'Incorrect normalization or pickup configuration.'
        }
        $diagnostics = ($warnings | Out-String) + $(if ($failed) { $failed.Exception.Message } else { '' })
        foreach ($inputValue in $case.Inputs) {
            if ($inputValue.Length -gt 0) { Check (-not $diagnostics.Contains($inputValue)) 'Input leaked into diagnostic output.' }
        }
        Write-Host "PASS $($case.Name)"
    }
    Write-Host "$($cases.Count) Gmail setup tests passed with mocked input/storage. No credentials saved; no network calls."
}
finally {
    Remove-Item Function:Read-Host,Function:dotnet
    Remove-Variable TYMTestCapturedSettings,TYMTestInputs,TYMTestPromptCount,TYMTestStoreCount -Scope Global -ErrorAction SilentlyContinue
}
