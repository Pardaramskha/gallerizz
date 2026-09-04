# Retire proprement tout ce que register.ps1 a inscrit (HKCU uniquement).
$ErrorActionPreference = 'SilentlyContinue'

$extensions = @('.jpg', '.jpeg', '.jfif', '.png', '.gif', '.bmp', '.webp', '.svg', '.tif', '.tiff', '.ico', '.avif', '.heic', '.heif')
$progId = 'Gallerizz.Image'
$classes = 'HKCU:\Software\Classes'

foreach ($ext in $extensions) {
    Remove-ItemProperty -Path "$classes\$ext\OpenWithProgids" -Name $progId
}
Remove-Item -Path "$classes\$progId" -Recurse -Force
Remove-Item -Path 'HKCU:\Software\Gallerizz' -Recurse -Force
Remove-ItemProperty -Path 'HKCU:\Software\RegisteredApplications' -Name 'Gallerizz'

Write-Host 'Gallerizz est desinscrit.'
