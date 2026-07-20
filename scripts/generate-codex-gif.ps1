param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\source\codex.gif')
)

Add-Type -AssemblyName System.Drawing

function New-PetFrame {
    param([int]$FrameIndex)
    $bitmap = [System.Drawing.Bitmap]::new(32, 32, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.Clear([System.Drawing.Color]::Black)
    $white = [System.Drawing.Brushes]::White
    $gray = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(112, 112, 112))
    $black = [System.Drawing.Brushes]::Black
    $offsets = @(1, 0, 0, 0, 0, 1)
    $y = $offsets[$FrameIndex]

    $graphics.FillRectangle($white, 9, 8 + $y, 14, 2)
    $graphics.FillRectangle($white, 7, 10 + $y, 18, 11)
    $graphics.FillRectangle($white, 5, 12 + $y, 2, 6)
    $graphics.FillRectangle($white, 25, 12 + $y, 2, 6)
    $graphics.FillRectangle($white, 9, 21 + $y, 5, 3)
    $graphics.FillRectangle($white, 18, 21 + $y, 5, 3)
    $graphics.FillRectangle($gray, 7, 24 + $y, 18, 2)
    if ($FrameIndex -eq 3) {
        $graphics.FillRectangle($black, 10, 14 + $y, 4, 1)
        $graphics.FillRectangle($black, 18, 14 + $y, 4, 1)
    }
    else {
        $graphics.FillRectangle($black, 11, 13 + $y, 2, 3)
        $graphics.FillRectangle($black, 19, 13 + $y, 2, 3)
    }
    $graphics.FillRectangle($black, 14, 18 + $y, 4, 1)
    $gray.Dispose()
    $graphics.Dispose()
    return $bitmap
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
$frames = @(for ($i = 0; $i -lt 6; $i++) { New-PetFrame $i })
try {
    $encodedFrames = foreach ($frame in $frames) {
        $memory = [System.IO.MemoryStream]::new()
        try {
            $frame.Save($memory, [System.Drawing.Imaging.ImageFormat]::Gif)
            ,$memory.ToArray()
        }
        finally { $memory.Dispose() }
    }

    $first = $encodedFrames[0]
    $colorTableLength = 3 * [math]::Pow(2, (($first[10] -band 7) + 1))
    $prefixLength = 13 + $colorTableLength
    $output = [System.Collections.Generic.List[byte]]::new()
    $output.AddRange([byte[]]$first[0..($prefixLength - 1)])
    $output.AddRange([byte[]](0x21,0xFF,0x0B,0x4E,0x45,0x54,0x53,0x43,0x41,0x50,0x45,0x32,0x2E,0x30,0x03,0x01,0x00,0x00,0x00))

    foreach ($encoded in $encodedFrames) {
        $imageStart = [Array]::IndexOf($encoded, [byte]0x2C, $prefixLength)
        if ($imageStart -lt 0) { throw 'Encoded GIF frame has no image descriptor.' }
        $output.AddRange([byte[]](0x21,0xF9,0x04,0x04,0x0C,0x00,0x00,0x00))
        $output.AddRange([byte[]]$encoded[$imageStart..($encoded.Length - 2)])
    }
    $output.Add(0x3B)
    [System.IO.File]::WriteAllBytes($resolvedOutput, $output.ToArray())
}
finally {
    foreach ($frame in $frames) { $frame.Dispose() }
}

Write-Output "Generated $resolvedOutput (32x32, 6 frames)"
