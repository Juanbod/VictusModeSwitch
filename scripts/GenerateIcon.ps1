[CmdletBinding()]
param(
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $PSScriptRoot '..\assets\VictusModeSwitch.ico'
}
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class NativeIconHandle {
    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr handle);
}
'@

$directory = [System.IO.Path]::GetDirectoryName([System.IO.Path]::GetFullPath($OutputPath))
[System.IO.Directory]::CreateDirectory($directory) | Out-Null
$bitmap = [System.Drawing.Bitmap]::new(64, 64)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::Transparent)
$brush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(0, 103, 192))
$pen = [System.Drawing.Pen]::new([System.Drawing.Color]::White, 4)
$points = [System.Drawing.Point[]]@(
    [System.Drawing.Point]::new(32, 5),
    [System.Drawing.Point]::new(59, 32),
    [System.Drawing.Point]::new(32, 59),
    [System.Drawing.Point]::new(5, 32)
)
$graphics.FillPolygon($brush, $points)
$graphics.DrawPolygon($pen, $points)
$pngPath = [System.IO.Path]::ChangeExtension([System.IO.Path]::GetFullPath($OutputPath), '.png')
$bitmap.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
$handle = $bitmap.GetHicon()

try {
    $icon = [System.Drawing.Icon]::FromHandle($handle)
    $stream = [System.IO.File]::Create([System.IO.Path]::GetFullPath($OutputPath))
    try {
        $icon.Save($stream)
    } finally {
        $stream.Dispose()
        $icon.Dispose()
    }
} finally {
    [NativeIconHandle]::DestroyIcon($handle) | Out-Null
    $pen.Dispose()
    $brush.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}

Write-Output "Generated $([System.IO.Path]::GetFullPath($OutputPath))"
