[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $SourcePath,
    [string] $OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($SourcePath)) {
    $SourcePath = Join-Path $repositoryRoot "logo.png"
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repositoryRoot "CDSI.Agent.WinForms\Beacon.ico"
}

$resolvedSourcePath = [System.IO.Path]::GetFullPath($SourcePath)
$resolvedOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
if (-not [System.IO.File]::Exists($resolvedSourcePath)) {
    throw "Icon source image does not exist: $resolvedSourcePath"
}

Add-Type -AssemblyName System.Drawing

$sizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
$frames = [System.Collections.Generic.List[byte[]]]::new()
$source = [System.Drawing.Image]::FromFile($resolvedSourcePath)
try {
    if ($source.Width -ne $source.Height) {
        throw "Icon source image must be square: $($source.Width)x$($source.Height)"
    }

    # The source contains a wordmark below the symbol; Windows icons use the mark only.
    $cropSize = [int] [System.Math]::Round($source.Width * 0.50)
    $cropLeft = [int] [System.Math]::Round(($source.Width - $cropSize) / 2)
    $cropTop = [int] [System.Math]::Round($source.Height * 0.15)
    $sourceRectangle = [System.Drawing.Rectangle]::new(
        $cropLeft,
        $cropTop,
        $cropSize,
        $cropSize)

    foreach ($size in $sizes) {
        $bitmap = [System.Drawing.Bitmap]::new(
            $size,
            $size,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([System.Drawing.Color]::Transparent)
                $graphics.CompositingMode =
                    [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                $graphics.CompositingQuality =
                    [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                $graphics.InterpolationMode =
                    [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.PixelOffsetMode =
                    [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.SmoothingMode =
                    [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $graphics.DrawImage(
                    $source,
                    [System.Drawing.Rectangle]::new(0, 0, $size, $size),
                    $sourceRectangle,
                    [System.Drawing.GraphicsUnit]::Pixel)
            }
            finally {
                $graphics.Dispose()
            }

            $stream = [System.IO.MemoryStream]::new()
            try {
                $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
                $frames.Add($stream.ToArray())
            }
            finally {
                $stream.Dispose()
            }
        }
        finally {
            $bitmap.Dispose()
        }
    }
}
finally {
    $source.Dispose()
}

if (-not $PSCmdlet.ShouldProcess(
        $resolvedOutputPath,
        "Generate a multi-resolution Windows icon")) {
    return
}

$outputDirectory = [System.IO.Path]::GetDirectoryName($resolvedOutputPath)
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$temporaryPath = $resolvedOutputPath + ".tmp"

try {
    $fileStream = [System.IO.File]::Create($temporaryPath)
    try {
        $writer = [System.IO.BinaryWriter]::new($fileStream)
        try {
            $writer.Write([uint16] 0)
            $writer.Write([uint16] 1)
            $writer.Write([uint16] $frames.Count)

            $imageOffset = 6 + (16 * $frames.Count)
            for ($index = 0; $index -lt $frames.Count; $index++) {
                $size = $sizes[$index]
                $frame = $frames[$index]
                $writer.Write([byte] $(if ($size -eq 256) { 0 } else { $size }))
                $writer.Write([byte] $(if ($size -eq 256) { 0 } else { $size }))
                $writer.Write([byte] 0)
                $writer.Write([byte] 0)
                $writer.Write([uint16] 1)
                $writer.Write([uint16] 32)
                $writer.Write([uint32] $frame.Length)
                $writer.Write([uint32] $imageOffset)
                $imageOffset += $frame.Length
            }

            foreach ($frame in $frames) {
                $writer.Write($frame)
            }
        }
        finally {
            $writer.Dispose()
        }
    }
    finally {
        $fileStream.Dispose()
    }

    Move-Item -LiteralPath $temporaryPath -Destination $resolvedOutputPath -Force
}
finally {
    if ([System.IO.File]::Exists($temporaryPath)) {
        [System.IO.File]::Delete($temporaryPath)
    }
}

$resolvedOutputPath
