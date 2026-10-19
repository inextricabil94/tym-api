param([string]$Python = 'python')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$models = Join-Path $root 'private-models'
$venv = Join-Path $models '.venv'
& $Python -m venv $venv
if ($LASTEXITCODE -ne 0) { throw 'POS Python environment creation failed.' }
$runtime = Join-Path $venv 'Scripts\python.exe'
if (-not (Test-Path -LiteralPath $runtime)) { $runtime = Join-Path $venv 'bin/python' }
& $runtime -m pip install -r (Join-Path $PSScriptRoot 'requirements-pos.txt')
if ($LASTEXITCODE -ne 0) { throw 'spaCy/model installation failed.' }
$settings = @{ TYM_POS_PYTHON = $runtime; TYM_POS_EN_MODEL = 'en_core_web_sm'; TYM_POS_RO_MODEL = 'ro_core_news_sm' }
[IO.File]::WriteAllText((Join-Path $models 'pos-settings.json'), ($settings | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
Write-Output 'Local spaCy POS models configured. Restart the TYM API from the repository root.'
