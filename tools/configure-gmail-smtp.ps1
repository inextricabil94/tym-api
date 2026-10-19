[CmdletBinding()]
param([string]$Email)

$ErrorActionPreference = 'Stop'
$project = Join-Path (Split-Path $PSScriptRoot -Parent) 'Tym.Api.csproj'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET SDK is required.' }
if (-not $Email) {
    $privateSettings = Join-Path (Split-Path $PSScriptRoot -Parent) 'private-models/mail-provider-settings.json'
    if (Test-Path -LiteralPath $privateSettings) {
        $Email = (Get-Content -LiteralPath $privateSettings -Raw | ConvertFrom-Json).verification_contact
    }
}
if (-not $Email) { $Email = Read-Host 'Gmail sender address' }
$Email = $Email.Trim()
if ($Email -notmatch '^[^@\s]+@gmail\.com$') { throw 'Use the Gmail mailbox that created the App Password.' }

Write-Host 'Enter a dedicated Google App Password, not your regular Gmail password.'
Write-Host 'Create it at https://myaccount.google.com/apppasswords with 2-Step Verification enabled.'
Write-Host 'Use the 16-character App Password, not a six-digit verification code. Copied spacing is ignored.'
Write-Host 'This stores development settings outside the repository using .NET User Secrets.'
Write-Host 'User Secrets are not encrypted. Use Azure secrets for production.'
$plain = $null; $settings = $null; $json = $null
try {
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        $password = $null
        $pointer = [IntPtr]::Zero
        try {
            $password = Read-Host 'Google App Password (hidden)' -AsSecureString
            $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($password)
            $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
            $plain = [regex]::Replace($plain, '[\s\u200B\u2060\uFEFF]', '')
            if ($plain.Length -ge 2 -and (($plain[0] -eq [char]34 -and $plain[-1] -eq [char]34) -or
                ($plain[0] -eq [char]39 -and $plain[-1] -eq [char]39))) {
                $plain = $plain.Substring(1, $plain.Length - 2)
            }
        }
        finally {
            if ($pointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
            if ($password) { $password.Dispose() }
        }
        if ($plain -cmatch '^[A-Za-z0-9]{16}$') { break }
        $length = $plain.Length
        $plain = $null
        $message = "Input has $length characters after removing copy formatting; a Google App Password must have exactly 16 letters/numbers. Do not use a normal password or verification code."
        if ($attempt -eq 3) { throw "$message Setup cancelled; no settings were changed." }
        Write-Warning "$message Please try again."
    }
    $settings = @{
        TYM_SMTP_HOST = 'smtp.gmail.com'
        TYM_SMTP_PORT = '587'
        TYM_SMTP_USER = $Email
        TYM_SMTP_FROM = $Email
        TYM_SMTP_PASSWORD = $plain
        TYM_MAIL_PICKUP_PATH = ''
    }
    $json = $settings | ConvertTo-Json -Compress
    # Pass credentials through stdin, not shell history or process arguments.
    $null = $json | & dotnet user-secrets set --project $project 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Unable to store development SMTP settings.' }
}
finally {
    $plain = $null; $settings = $null; $json = $null
}
Write-Host 'Gmail SMTP settings saved. No test email has been sent yet.'
Write-Host 'Restart the Development API without TYM_MAIL_PICKUP_PATH in its environment, then resend confirmation.'
