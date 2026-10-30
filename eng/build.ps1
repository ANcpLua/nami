$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    & bash ./build.sh @args
    $buildExitCode = $LASTEXITCODE
}
finally {
    Pop-Location
}
exit $buildExitCode
