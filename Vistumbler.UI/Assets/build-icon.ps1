param(
    [Parameter(Mandatory = $true)][string]$SourcePng,
    [Parameter(Mandatory = $true)][string]$OutputIco,
    [int[]]$Sizes = @(16, 24, 32, 48, 64, 128, 256)
)

Add-Type -AssemblyName System.Drawing

# Load source
$src = [System.Drawing.Image]::FromFile((Resolve-Path $SourcePng).Path)
try {
    $entries = New-Object 'System.Collections.Generic.List[byte[]]'

    foreach ($size in $Sizes) {
        # Render the icon at this size with high-quality resampling
        $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        try {
            $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $g.CompositingQuality= [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $g.Clear([System.Drawing.Color]::Transparent)
            $g.DrawImage($src, (New-Object System.Drawing.Rectangle 0, 0, $size, $size))
        } finally {
            $g.Dispose()
        }

        # Encode as PNG into memory (ICO supports embedded PNG frames)
        $ms = New-Object System.IO.MemoryStream
        try {
            $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
            $entries.Add($ms.ToArray())
        } finally {
            $ms.Dispose()
            $bmp.Dispose()
        }
    }

    # Build ICO container
    $iconDir   = New-Object System.IO.MemoryStream
    $writer    = New-Object System.IO.BinaryWriter $iconDir
    try {
        $count = $entries.Count
        # ICONDIR
        $writer.Write([uint16]0)      # reserved
        $writer.Write([uint16]1)      # type = 1 (icon)
        $writer.Write([uint16]$count) # number of images

        $headerSize = 6 + 16 * $count
        $offset = $headerSize

        for ($i = 0; $i -lt $count; $i++) {
            $size  = $Sizes[$i]
            $data  = $entries[$i]
            $bytes = $data.Length

            # ICONDIRENTRY (16 bytes)
            $w = if ($size -ge 256) { 0 } else { [byte]$size }
            $h = if ($size -ge 256) { 0 } else { [byte]$size }
            $writer.Write([byte]$w)              # width  (0 = 256)
            $writer.Write([byte]$h)              # height (0 = 256)
            $writer.Write([byte]0)               # color count
            $writer.Write([byte]0)               # reserved
            $writer.Write([uint16]1)             # color planes
            $writer.Write([uint16]32)            # bits per pixel
            $writer.Write([uint32]$bytes)        # image data size
            $writer.Write([uint32]$offset)       # image data offset

            $offset += $bytes
        }

        # Append PNG payloads in the same order
        foreach ($data in $entries) {
            $writer.Write($data)
        }

        $writer.Flush()
        [System.IO.File]::WriteAllBytes((Resolve-Path -LiteralPath (Split-Path -Parent $OutputIco)).Path + '\' + (Split-Path -Leaf $OutputIco), $iconDir.ToArray())
        Write-Host ("Wrote {0} ({1} frames)" -f $OutputIco, $count)
    } finally {
        $writer.Dispose()
        $iconDir.Dispose()
    }
} finally {
    $src.Dispose()
}
