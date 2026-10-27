param([string]$OutputDirectory=(Join-Path $PSScriptRoot 'publish'))
$ErrorActionPreference='Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
dotnet publish (Join-Path $PSScriptRoot 'src/RuaCLI.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugSymbols=false -o $OutputDirectory
if($LASTEXITCODE -ne 0){throw 'RuaCLI build failed'}
