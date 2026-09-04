# Inscrit Gallerizz comme candidat "Ouvrir avec" pour les formats d'image (HKCU, aucun droit admin).
# Windows 10/11 ne permet plus a une application de se forcer par defaut : apres ce script,
# choisir Gallerizz une fois via "Ouvrir avec > Toujours", ou dans Parametres > Applications par defaut.
# Reversible avec unregister.ps1.

$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot 'Gallerizz.exe'
if (-not (Test-Path $exe)) { Write-Error "Gallerizz.exe introuvable a cote du script. Compilez d'abord (build.bat)." }

$extensions = @('.jpg', '.jpeg', '.jfif', '.png', '.gif', '.bmp', '.webp', '.svg', '.tif', '.tiff', '.ico', '.avif', '.heic', '.heif')
$progId = 'Gallerizz.Image'
$classes = 'HKCU:\Software\Classes'

# Le ProgID : commande d'ouverture + icone.
New-Item -Path "$classes\$progId" -Force | Out-Null
Set-ItemProperty -Path "$classes\$progId" -Name '(default)' -Value 'Image (Gallerizz)'
New-Item -Path "$classes\$progId\DefaultIcon" -Force | Out-Null
Set-ItemProperty -Path "$classes\$progId\DefaultIcon" -Name '(default)' -Value "`"$exe`",0"
New-Item -Path "$classes\$progId\shell\open\command" -Force | Out-Null
Set-ItemProperty -Path "$classes\$progId\shell\open\command" -Name '(default)' -Value "`"$exe`" `"%1`""

# Chaque extension propose Gallerizz dans "Ouvrir avec".
foreach ($ext in $extensions) {
    New-Item -Path "$classes\$ext\OpenWithProgids" -Force | Out-Null
    New-ItemProperty -Path "$classes\$ext\OpenWithProgids" -Name $progId -PropertyType String -Value '' -Force | Out-Null
}

# Declaration d'application : fait apparaitre Gallerizz dans Parametres > Applications par defaut.
$caps = 'HKCU:\Software\Gallerizz\Capabilities'
New-Item -Path $caps -Force | Out-Null
Set-ItemProperty -Path $caps -Name 'ApplicationName' -Value 'Gallerizz'
Set-ItemProperty -Path $caps -Name 'ApplicationDescription' -Value 'Visualiseur d''images (JPG, PNG, GIF anime, WebP, SVG, TIFF, AVIF, HEIC...)'
New-Item -Path "$caps\FileAssociations" -Force | Out-Null
foreach ($ext in $extensions) {
    Set-ItemProperty -Path "$caps\FileAssociations" -Name $ext -Value $progId
}
New-Item -Path 'HKCU:\Software\RegisteredApplications' -Force | Out-Null
Set-ItemProperty -Path 'HKCU:\Software\RegisteredApplications' -Name 'Gallerizz' -Value 'Software\Gallerizz\Capabilities'

Write-Host 'Gallerizz est inscrit.'
Write-Host 'Derniere etape (un clic) : clic droit sur une image > Ouvrir avec > Choisir une autre application > Gallerizz > Toujours.'
