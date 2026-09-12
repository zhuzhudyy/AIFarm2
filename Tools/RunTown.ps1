[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $PlayerPath,
    [string] $PythonPath = '',
    [switch] $CheckOnly
)

$ErrorActionPreference = 'Stop'
$townRepository = Split-Path -Parent $PSScriptRoot
$townPlayer = (Resolve-Path -LiteralPath $PlayerPath).Path
$townBuildDirectory = Split-Path -Parent $townPlayer
$townServerDirectory = Join-Path $townBuildDirectory 'Server'
if (-not (Test-Path -LiteralPath (Join-Path $townServerDirectory 'app/main.py') -PathType Leaf)) {
    throw 'The build is missing Server/app/main.py. Build through AIFarm > Build Windows Town to package the gateway sidecar.'
}

if ([string]::IsNullOrWhiteSpace($PythonPath)) {
    $townRepositoryPython = Join-Path $townRepository 'Server/.venv/Scripts/python.exe'
    $townBuildPython = Join-Path $townServerDirectory '.venv/Scripts/python.exe'
    if (Test-Path -LiteralPath $townBuildPython -PathType Leaf) { $PythonPath = $townBuildPython }
    elseif (Test-Path -LiteralPath $townRepositoryPython -PathType Leaf) { $PythonPath = $townRepositoryPython }
    else { $PythonPath = (Get-Command python -ErrorAction Stop).Source }
}
$townPython = (Resolve-Path -LiteralPath $PythonPath).Path
& $townPython -c "import fastapi,httpx,pydantic,uvicorn; from openai import OpenAI; print('AIFarm gateway Python dependencies: ready')"
if ($LASTEXITCODE -ne 0) {
    throw "Install the gateway dependencies using this interpreter: $townPython -m pip install -r `"$townServerDirectory/requirements.txt`""
}
Write-Host "Player: $townPlayer"
Write-Host "Gateway sidecar: $townServerDirectory"
Write-Host 'Credentials remain in the local user configuration; no credential is copied into the build.'
if ($CheckOnly) { return }

$townPreviousPython = $env:AIFARM_GATEWAY_PYTHON
$townPreviousServer = $env:AIFARM_GATEWAY_SERVER_DIR
try {
    $env:AIFARM_GATEWAY_PYTHON = $townPython
    $env:AIFARM_GATEWAY_SERVER_DIR = $townServerDirectory
    # The player is the requested visible application; its gateway child is hidden.
    Start-Process -FilePath $townPlayer -WorkingDirectory $townBuildDirectory
}
finally {
    $env:AIFARM_GATEWAY_PYTHON = $townPreviousPython
    $env:AIFARM_GATEWAY_SERVER_DIR = $townPreviousServer
}
