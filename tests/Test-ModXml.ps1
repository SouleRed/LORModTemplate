#requires -Version 5.1
# Run with Windows PowerShell (.NET Framework), not pwsh.
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $repo '必要程序集\游戏原版DLL\Assembly-CSharp.dll'))
$count = 0
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $repo 'MOD根目录') -Recurse -Filter '*.xml' -File) {
    [xml]$xml = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
    $rootName = $xml.DocumentElement.LocalName
    if ($rootName -eq 'localize') { $count++; continue }
    $type = $null
    foreach ($prefix in @('', 'Workshop.', 'LOR_XML.', 'LOR_DiceSystem.')) {
        $type = $assembly.GetType($prefix + $rootName)
        if ($type) { break }
    }
    if (!$type) { throw "Game XML root type not found: $rootName" }
    $serializer = New-Object System.Xml.Serialization.XmlSerializer($type)
    $reader = New-Object System.IO.StreamReader($file.FullName)
    try { $model = $serializer.Deserialize($reader) }
    finally { $reader.Dispose() }
    # Empty template lists must deserialize as empty collections, not null.
    foreach ($field in $type.GetFields()) {
        if ([Collections.IList].IsAssignableFrom($field.FieldType) -and $null -eq $field.GetValue($model)) {
            throw "Null collection: $($file.FullName) / $($field.Name)"
        }
    }
    $count++
}
Write-Host "PASS: $count template XML files validated using actual game types."
