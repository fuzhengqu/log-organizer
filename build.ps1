[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [ValidateSet('win-x64', 'win-arm64', 'win-x86')]
    [string]$Runtime = 'win-x64',
    [switch]$FrameworkDependent,
    [string]$Output = (Join-Path $PSScriptRoot 'publish')
)

# Publishes LogOrganizer.exe.
#
# Default: self-contained single file. The .NET runtime and the Windows App SDK
# runtime are both bundled, so the target machine needs neither installed. On
# first start the bundle is unpacked to %TEMP% (about 90 MB on disk).
#
# -FrameworkDependent: multi-file output for machines that already have the .NET 10
# Desktop Runtime and the Windows App Runtime. WinUI 3 cannot combine "framework
# dependent" with single file: that combination builds but crashes at startup, so
# this mode always writes a directory.

$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'src/LogOrganizer.WinUI/LogOrganizer.WinUI.csproj'
if (-not (Test-Path $project)) {
    throw "找不到项目文件：$project"
}

$arguments = @(
    'publish', $project,
    '--configuration', $Configuration,
    '--runtime', $Runtime,
    '--output', $Output
)

if ($FrameworkDependent) {
    $arguments += '--self-contained', 'false'
    $arguments += '-p:WindowsAppSDKSelfContained=false'
    $arguments += '-p:PublishSingleFile=false'
    Write-Host "正在发布框架依赖版本（目标机需安装 .NET 10 Desktop Runtime 与 Windows App Runtime）..."
}
else {
    $arguments += '--self-contained', 'true'
    $arguments += '-p:PublishSingleFile=true'
    $arguments += '-p:EnableCompressionInSingleFile=true'
    # Windows App SDK content (WinUI, DWrite, bootstrapper DLLs) has to live next to
    # the single file before extraction, which only works in this compatibility mode.
    $arguments += '-p:IncludeAllContentForSelfExtract=true'
    $arguments += '-p:WindowsAppSDKSelfContained=true'
    $arguments += '-p:WindowsAppSDKBootstrapInitialize=false'
    $arguments += '-p:DebugType=none'
    $arguments += '-p:NoWarn=NETSDK1244'
    Write-Host "正在发布自包含单文件（目标机无需安装 .NET，首次启动会解压运行时）..."
}

dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish 失败，退出码 $LASTEXITCODE"
}

$exe = Join-Path $Output 'LogOrganizer.exe'
if (-not (Test-Path $exe)) {
    throw "输出目录中未找到 LogOrganizer.exe：$Output"
}

$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "发布完成：$exe（$sizeMb MB）"

if ($FrameworkDependent) {
    Write-Host "提示：当前为框架依赖版本，目标机需要 .NET 10 Desktop Runtime 与 Windows App Runtime。"
}
else {
    Write-Host "提示：可直接分发 $exe；大量分发时建议同时提供微软官方 VC++ 运行库安装包（见 tools/make-release.ps1）。"
}