[CmdletBinding()]
param(
  [string]$OutputDirectory = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName PresentationCore, WindowsBase

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$brandDirectory = Join-Path $repositoryRoot "src\DictateAnywhere.App\Assets\Brand"
$writeCompatibilityIcon = [string]::IsNullOrWhiteSpace($OutputDirectory)
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = $brandDirectory }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$sourceSvg = Join-Path $brandDirectory "koncus-nai-mark.svg"
[xml]$source = [IO.File]::ReadAllText($sourceSvg)
$paths = @($source.svg.ChildNodes | Where-Object { $_.LocalName -eq 'path' })
if ($paths.Count -ne 2) { throw "The Koncus Nai master must contain its two ribbon paths." }

function Write-BrandPng {
  param([int]$Size, [string]$OutputPath, [string]$Monochrome = '')
  $visual = [System.Windows.Media.DrawingVisual]::new()
  $context = $visual.RenderOpen()
  try {
    # A square canvas with equal optical margins; SVG and WPF share path syntax.
    $scale = $Size / 280.0
    $context.PushTransform([System.Windows.Media.ScaleTransform]::new($scale, $scale))
    $context.PushTransform([System.Windows.Media.TranslateTransform]::new(0, 6))
    foreach ($path in $paths) {
      $color = if ($Monochrome) { $Monochrome } else { $path.GetAttribute('fill') }
      $brush = [System.Windows.Media.BrushConverter]::new().ConvertFromInvariantString($color)
      $geometry = [System.Windows.Media.Geometry]::Parse($path.GetAttribute('d'))
      $context.DrawGeometry($brush, $null, $geometry)
    }
    $context.Pop()
    $context.Pop()
  }
  finally { $context.Close() }
  $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($Size, $Size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
  $bitmap.Render($visual)
  $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
  $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
  $stream = [IO.File]::Create($OutputPath)
  try { $encoder.Save($stream) } finally { $stream.Dispose() }
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
foreach ($size in $sizes) {
  Write-BrandPng -Size $size -OutputPath (Join-Path $OutputDirectory "koncus-nai-$size.png")
}
Write-BrandPng -Size 1024 -OutputPath (Join-Path $OutputDirectory 'koncus-nai-master.png')
Write-BrandPng -Size 256 -Monochrome '#FFFFFF' -OutputPath (Join-Path $OutputDirectory 'koncus-nai-white-256.png')
Write-BrandPng -Size 256 -Monochrome '#20242B' -OutputPath (Join-Path $OutputDirectory 'koncus-nai-dark-256.png')

$images = @($sizes | ForEach-Object { [pscustomobject]@{ Size = $_; Bytes = [IO.File]::ReadAllBytes((Join-Path $OutputDirectory "koncus-nai-$_.png")) } })
$stream = [IO.MemoryStream]::new()
$writer = [IO.BinaryWriter]::new($stream)
try {
  $writer.Write([uint16]0)
  $writer.Write([uint16]1)
  $writer.Write([uint16]$images.Count)
  $offset = 6 + 16 * $images.Count
  foreach ($image in $images) {
    $dimension = if ($image.Size -eq 256) { 0 } else { $image.Size }
    $writer.Write([byte]$dimension)
    $writer.Write([byte]$dimension)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$image.Bytes.Length)
    $writer.Write([uint32]$offset)
    $offset += $image.Bytes.Length
  }
  foreach ($image in $images) { $writer.Write([byte[]]$image.Bytes) }
  $writer.Flush()
  [IO.File]::WriteAllBytes((Join-Path $OutputDirectory 'KoncusNai.ico'), $stream.ToArray())
}
finally { $writer.Dispose(); $stream.Dispose() }
if ($writeCompatibilityIcon) {
  # Existing user-pinned shortcuts may still point at this historical icon path.
  Copy-Item -LiteralPath (Join-Path $OutputDirectory 'KoncusNai.ico') -Destination (Join-Path $brandDirectory '..\Notype.ico') -Force
  Copy-Item -LiteralPath (Join-Path $OutputDirectory 'KoncusNai.ico') -Destination (Join-Path $brandDirectory 'Nilo.ico') -Force
}
[xml]$lettering = [IO.File]::ReadAllText((Join-Path $brandDirectory 'koncus-nai-lettering.svg'))
foreach ($variant in @(@{Name='light'; Ink='#20242B'}, @{Name='dark'; Ink='#F5F4F0'})) {
  $markXml = ($paths | ForEach-Object { $_.OuterXml }) -join "`n"
  $letterXml = (@($lettering.svg.ChildNodes | Where-Object { $_.LocalName -eq 'path' }) | ForEach-Object { $_.OuterXml.Replace('#20242B', $variant.Ink) }) -join "`n"
  $svg = "<svg xmlns=`"http://www.w3.org/2000/svg`" viewBox=`"0 0 1220 280`"><title>Koncus Nai</title>`n$markXml`n$letterXml`n</svg>"
  [IO.File]::WriteAllText((Join-Path $OutputDirectory "koncus-nai-wordmark-$($variant.Name).svg"), $svg, [Text.UTF8Encoding]::new($false))
}
foreach ($variant in @(@{Name='white'; Ink='#FFFFFF'}, @{Name='dark'; Ink='#20242B'})) {
  $svg = [IO.File]::ReadAllText($sourceSvg).Replace('#D85A2A', $variant.Ink).Replace('#A83C1C', $variant.Ink)
  [IO.File]::WriteAllText((Join-Path $OutputDirectory "koncus-nai-mark-$($variant.Name).svg"), $svg, [Text.UTF8Encoding]::new($false))
}
$drawings = foreach ($variant in @(@{Name='Color'; Contrast=$false}, @{Name='Contrast'; Contrast=$true})) {
  $geometries = foreach ($path in $paths) {
    $brush = if ($variant.Contrast) { '{DynamicResource Brush.Text.Primary}' } else { $path.GetAttribute('fill') }
    "<GeometryDrawing Brush=`"$brush`" Geometry=`"$($path.GetAttribute('d'))`" />"
  }
  "<DrawingImage x:Key=`"Brand.Mark.$($variant.Name)`"><DrawingImage.Drawing><DrawingGroup>$($geometries -join '')</DrawingGroup></DrawingImage.Drawing></DrawingImage>"
}
$dictionary = "<ResourceDictionary xmlns=`"http://schemas.microsoft.com/winfx/2006/xaml/presentation`" xmlns:x=`"http://schemas.microsoft.com/winfx/2006/xaml`">`n$($drawings -join "`n")`n</ResourceDictionary>"
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'BrandDrawing.xaml'), $dictionary, [Text.UTF8Encoding]::new($false))
Write-Host "Generated the Koncus Nai icon family from $sourceSvg."
