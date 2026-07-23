[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root 'SynToolkit\Assets'
$sourcePath = Join-Path $assets 'Logo\SynToolkit-Master.png'
$installerAssets = Join-Path $root 'Installer\Synergy\Assets'
New-Item -ItemType Directory -Path $installerAssets -Force | Out-Null

function Export-SquareImage {
    param(
        [System.Drawing.Image]$Source,
        [int]$Size,
        [string]$OutputPath
    )

    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.DrawImage($Source, 0, 0, $Size, $Size)
        $bitmap.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Get-SquarePngBytes {
    param(
        [System.Drawing.Image]$Source,
        [int]$Size
    )

    $tempPath = Join-Path ([System.IO.Path]::GetTempPath()) ("syntoolkit-icon-{0}-{1}.png" -f $Size, [System.Guid]::NewGuid().ToString('N'))
    try {
        Export-SquareImage $Source $Size $tempPath
        return ,([System.IO.File]::ReadAllBytes($tempPath))
    }
    finally {
        Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue
    }
}

$source = [System.Drawing.Image]::FromFile($sourcePath)
try {
    Export-SquareImage $source 300 (Join-Path $assets 'Square150x150Logo.scale-200.png')
    Export-SquareImage $source 88 (Join-Path $assets 'Square44x44Logo.scale-200.png')
    Export-SquareImage $source 48 (Join-Path $assets 'LockScreenLogo.scale-200.png')

    $iconPng = Join-Path $assets 'Logo\SynToolkit-Icon-256.png'
    Export-SquareImage $source 256 $iconPng

    $splash = [System.Drawing.Bitmap]::new(1240, 600, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $splashGraphics = [System.Drawing.Graphics]::FromImage($splash)
    try {
        $splashGraphics.Clear([System.Drawing.Color]::FromArgb(255, 10, 11, 13))
        $splashGraphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $splashGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $splashGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $splashGraphics.DrawImage($source, 440, 120, 360, 360)
        $splash.Save((Join-Path $assets 'SplashScreen.scale-200.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $splashGraphics.Dispose()
        $splash.Dispose()
    }

    $wizardImage = [System.Drawing.Bitmap]::new(430, 824, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $wizardGraphics = [System.Drawing.Graphics]::FromImage($wizardImage)
    try {
        $wizardGraphics.Clear([System.Drawing.Color]::FromArgb(10, 11, 13))
        $wizardGraphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $wizardGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $wizardGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $wizardGraphics.DrawImage($source, 50, 247, 330, 330)
        $wizardImage.Save((Join-Path $installerAssets 'WizardImage.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)
    }
    finally {
        $wizardGraphics.Dispose()
        $wizardImage.Dispose()
    }

    $wizardSmallImage = [System.Drawing.Bitmap]::new(116, 116, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $wizardSmallGraphics = [System.Drawing.Graphics]::FromImage($wizardSmallImage)
    try {
        $wizardSmallGraphics.Clear([System.Drawing.Color]::White)
        $wizardSmallGraphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $wizardSmallGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $wizardSmallGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $wizardSmallGraphics.DrawImage($source, 8, 8, 100, 100)
        $wizardSmallImage.Save((Join-Path $installerAssets 'WizardSmallImage.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)
    }
    finally {
        $wizardSmallGraphics.Dispose()
        $wizardSmallImage.Dispose()
    }
}
finally {
    $source.Dispose()
}

# Store common Windows icon sizes in a standards-compliant ICO container.
$iconSource = [System.Drawing.Image]::FromFile($sourcePath)
try {
    $iconEntries = @(
        16, 20, 24, 32, 40, 48, 64, 128, 256 | ForEach-Object {
            [pscustomobject]@{
                Size = $_
                Bytes = Get-SquarePngBytes $iconSource $_
            }
        }
    )
}
finally {
    $iconSource.Dispose()
}

$iconPath = Join-Path $assets 'Logo\SynToolkit.ico'
$stream = [System.IO.File]::Create($iconPath)
$writer = [System.IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$iconEntries.Count)

    $dataOffset = 6 + (16 * $iconEntries.Count)
    foreach ($entry in $iconEntries) {
        $dimension = if ($entry.Size -eq 256) { [byte]0 } else { [byte]$entry.Size }
        $writer.Write($dimension)
        $writer.Write($dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$entry.Bytes.Length)
        $writer.Write([uint32]$dataOffset)
        $dataOffset += $entry.Bytes.Length
    }

    foreach ($entry in $iconEntries) {
        $writer.Write([byte[]]$entry.Bytes)
    }
}
finally {
    $writer.Dispose()
    $stream.Dispose()
}

Copy-Item -LiteralPath $iconPath -Destination (Join-Path $installerAssets 'SynToolkit.ico') -Force

$blankPngPath = Join-Path ([System.IO.Path]::GetTempPath()) 'syntoolkit-blank-icon.png'
$blankBitmap = [System.Drawing.Bitmap]::new(32, 32, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
try {
    $blankBitmap.Save($blankPngPath, [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $blankBitmap.Dispose()
}

$blankBytes = [System.IO.File]::ReadAllBytes($blankPngPath)
$blankIconPath = Join-Path $installerAssets 'Blank.ico'
$blankStream = [System.IO.File]::Create($blankIconPath)
$blankWriter = [System.IO.BinaryWriter]::new($blankStream)
try {
    $blankWriter.Write([uint16]0)
    $blankWriter.Write([uint16]1)
    $blankWriter.Write([uint16]1)
    $blankWriter.Write([byte]32)
    $blankWriter.Write([byte]32)
    $blankWriter.Write([byte]0)
    $blankWriter.Write([byte]0)
    $blankWriter.Write([uint16]1)
    $blankWriter.Write([uint16]32)
    $blankWriter.Write([uint32]$blankBytes.Length)
    $blankWriter.Write([uint32]22)
    $blankWriter.Write($blankBytes)
}
finally {
    $blankWriter.Dispose()
    $blankStream.Dispose()
    Remove-Item -LiteralPath $blankPngPath -Force -ErrorAction SilentlyContinue
}

Write-Host 'SynToolkit branding assets regenerated successfully.'
