[CmdletBinding()]
param(
    [string]$Version,
    [string]$RedistDir = (Join-Path $PSScriptRoot '..\artifacts\vcredist'),
    [switch]$SkipRedist,
    [switch]$SkipX64,
    [switch]$SkipArm64
)

# Builds the two shipping packages:
#   LogOrganizer-<version>-win-x64.zip    exe + Visual C++ runtime installer + readme
#   LogOrganizer-<version>-win-arm64.zip  same for Windows on ARM
# plus the bare exes and SHA256SUMS.txt. Nothing here is committed: everything lands in artifacts/.

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$artifacts = Join-Path $root 'artifacts'

if (-not $Version) {
    [xml]$props = Get-Content (Join-Path $root 'Directory.Build.props') -Encoding UTF8
    $Version = ($props.Project.PropertyGroup.Version | Where-Object { $_ }) | Select-Object -First 1
}

if (-not $Version) {
    throw '无法从 Directory.Build.props 读取版本号，请用 -Version 指定。'
}

Write-Host "打包版本：$Version"

$redist = @{
    x64   = 'VC_redist.x64.exe'
    arm64 = 'VC_redist.arm64.exe'
}

function Get-LocalRedist([string]$name) {
    if ($SkipRedist) { return $null }

    New-Item -ItemType Directory -Force -Path $RedistDir | Out-Null
    $target = Join-Path $RedistDir $name
    if (Test-Path $target) {
        Write-Host "使用已缓存的运行库：$name"
        return $target
    }

    # Official Microsoft permalink for the current VC++ 2015-2022 redistributable.
    $url = "https://aka.ms/vs/17/release/$name"
    try {
        Write-Host "下载 $name ..."
        Invoke-WebRequest -Uri $url -OutFile $target -UseBasicParsing -TimeoutSec 300
        Write-Host "已下载：$name（$([math]::Round((Get-Item $target).Length / 1MB, 1)) MB）"
        return $target
    }
    catch {
        Write-Warning "无法下载 $name：$($_.Exception.Message)。压缩包将不包含运行库安装程序。"
        return $null
    }
}

$architectures = @()
if (-not $SkipX64) { $architectures += @{ Rid = 'win-x64'; Arch = 'x64' } }
if (-not $SkipArm64) { $architectures += @{ Rid = 'win-arm64'; Arch = 'arm64' } }

$produced = @()

foreach ($target in $architectures) {
    $rid = $target.Rid
    $arch = $target.Arch
    $publishDir = Join-Path $artifacts "publish-$rid"
    $stageDir = Join-Path $artifacts "package-$rid"

    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
    if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $stageDir | Out-Null

    & (Join-Path $root 'build.ps1') -Configuration Release -Runtime $rid -Output $publishDir
    if ($LASTEXITCODE -ne 0) {
        throw "$rid 发布失败，退出码 $LASTEXITCODE"
    }

    $exe = Join-Path $publishDir 'LogOrganizer.exe'
    Copy-Item $exe (Join-Path $stageDir 'LogOrganizer.exe')

    $redistFile = Get-LocalRedist $redist[$arch]
    if ($redistFile) {
        Copy-Item $redistFile (Join-Path $stageDir $redist[$arch])
    }

    $readme = @"
日志归档助手 $Version（$arch）
========================================

1. 把整个文件夹解压到普通可写目录，例如 D:\Apps\LogOrganizer
2. 双击 LogOrganizer.exe

如果启动时提示缺少系统运行库，先运行本文件夹内的 $($redist[$arch])，
安装完成后重新打开 LogOrganizer.exe。安装包来自微软官方，可离线安装。

本程序已内置 .NET 与 Windows App Runtime，无需另外安装。
系统要求：Windows 10 版本 1809（内部版本 17763）或更高。

许可证：GNU GPL v3.0（GPL-3.0-only）
项目主页：https://github.com/fuzhengqu/log-organizer
SHA-256 校验值见发布页面的 SHA256SUMS.txt
"@
    [System.IO.File]::WriteAllText(
        (Join-Path $stageDir '使用说明.txt'),
        $readme,
        (New-Object System.Text.UTF8Encoding($true)))

    $zip = Join-Path $artifacts "LogOrganizer-$Version-$rid.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $stageDir '*') -DestinationPath $zip -CompressionLevel Optimal
    Write-Host "已生成：$zip"

    $standalone = Join-Path $artifacts "LogOrganizer-$Version-$rid.exe"
    Copy-Item $exe $standalone -Force

    $produced += $zip
    $produced += $standalone
}

# SHA256SUMS.txt covers the packages and the bare executables.
$sumFile = Join-Path $artifacts 'SHA256SUMS.txt'
$lines = foreach ($file in $produced) {
    $hash = (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $(Split-Path $file -Leaf)"
}
[System.IO.File]::WriteAllLines($sumFile, $lines, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "已生成：$sumFile"
Get-Content $sumFile | ForEach-Object { Write-Host "  $_" }