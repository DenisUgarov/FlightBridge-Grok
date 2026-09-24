# Generates docs/README.<Code>.md from the <Guide> text of every language in src/SetupLanguages.xml.
# The installer help and these README files therefore always share one source of truth.
# tests/InstallerTests.ps1 checks that each README contains its language's Guide text.
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$xml = New-Object System.Xml.XmlDocument
$xml.Load((Join-Path $project 'src/SetupLanguages.xml'))
$docs = Join-Path $project 'docs'
New-Item -ItemType Directory -Force -Path $docs | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding($false)
$count = 0
foreach ($language in $xml.SelectNodes('/Languages/Language')) {
 $code = $language.GetAttribute('Code')
 $name = $language.GetAttribute('Name')
 $guideNode = $language.SelectSingleNode('Guide')
 if (-not $code -or -not $guideNode) { throw "Language without Code or Guide in SetupLanguages.xml" }
 $guide = $guideNode.InnerText.Replace("`r`n","`n")
 $text = "# Flight Bridge ($name)`n`n> EXPERIMENTAL / WORK IN PROGRESS`n`n$guide`n"
 [System.IO.File]::WriteAllText((Join-Path $docs "README.$code.md"), $text, $utf8)
 $count++
}
Write-Output "Generated $count language guides in docs/"
