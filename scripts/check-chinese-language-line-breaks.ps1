[CmdletBinding()]
param(
    [string]$LanguageDirectory,
    [switch]$Fix
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($LanguageDirectory)) {
    $LanguageDirectory = Join-Path $PSScriptRoot '..\release-package\optional-languages\zho'
}

$resolvedLanguageDirectory = (Resolve-Path -LiteralPath $LanguageDirectory).Path
$files = @(Get-ChildItem -LiteralPath $resolvedLanguageDirectory -Filter '*.loctable.xml' -File | Sort-Object Name)
if ($files.Count -eq 0) {
    throw "No localization tables found in: $resolvedLanguageDirectory"
}

$singleline = [System.Text.RegularExpressions.RegexOptions]::Singleline
$rowRegex = New-Object System.Text.RegularExpressions.Regex(
    '<Row\b[^>]*>.*?</Row>',
    $singleline)
$cellRegex = New-Object System.Text.RegularExpressions.Regex(
    '<Cell\b[^>]*(?:/>|>.*?</Cell>)',
    $singleline)
$dataRegex = New-Object System.Text.RegularExpressions.Regex(
    '<Data\b(?![^>]*\/>)[^>]*>(?<value>.*?)</Data>',
    $singleline)
$lineBreakRegex = New-Object System.Text.RegularExpressions.Regex('\r\n|\r|\n')
$lineFeedReferenceRegex = New-Object System.Text.RegularExpressions.Regex('&#10;')

$totalChangedFiles = 0
$totalChangedValues = 0
$totalPhysicalBreaks = 0
$totalLineFeedReferences = 0
$invalidFiles = New-Object System.Collections.Generic.List[string]

foreach ($file in $files) {
    $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
    $hasUtf8Bom = $bytes.Length -ge 3 -and
        $bytes[0] -eq 0xEF -and
        $bytes[1] -eq 0xBB -and
        $bytes[2] -eq 0xBF
    $utf8 = New-Object System.Text.UTF8Encoding($hasUtf8Bom, $true)
    $originalText = [System.IO.File]::ReadAllText($file.FullName, $utf8)

    $originalDocument = New-Object System.Xml.XmlDocument
    $originalDocument.PreserveWhitespace = $false
    $originalDocument.LoadXml($originalText)

    $counts = [pscustomobject]@{
        ChangedValues = 0
        PhysicalBreaks = 0
    }

    $normalizedText = $rowRegex.Replace(
        $originalText,
        [System.Text.RegularExpressions.MatchEvaluator]{
            param($rowMatch)

            $rowText = $rowMatch.Value
            $cells = $cellRegex.Matches($rowText)
            if ($cells.Count -lt 2) {
                return $rowText
            }

            $activeCell = $cells[1]
            $data = $dataRegex.Match($activeCell.Value)
            if (-not $data.Success) {
                return $rowText
            }

            $value = $data.Groups['value'].Value
            $breakCount = $lineBreakRegex.Matches($value).Count
            if ($breakCount -eq 0) {
                return $rowText
            }

            $counts.ChangedValues++
            $counts.PhysicalBreaks += $breakCount
            $normalizedValue = $lineBreakRegex.Replace($value, '&#10;')
            $normalizedCell = $activeCell.Value.Substring(0, $data.Groups['value'].Index) +
                $normalizedValue +
                $activeCell.Value.Substring($data.Groups['value'].Index + $data.Groups['value'].Length)

            return $rowText.Substring(0, $activeCell.Index) +
                $normalizedCell +
                $rowText.Substring($activeCell.Index + $activeCell.Length)
        })

    $normalizedDocument = New-Object System.Xml.XmlDocument
    $normalizedDocument.PreserveWhitespace = $false
    $normalizedDocument.LoadXml($normalizedText)
    $expectedDecodedXml = $lineBreakRegex.Replace($originalDocument.OuterXml, "`n")
    $actualDecodedXml = $lineBreakRegex.Replace($normalizedDocument.OuterXml, "`n")
    if ($expectedDecodedXml -cne $actualDecodedXml) {
        throw "Decoded XML content changed while normalizing: $($file.Name)"
    }

    if ($counts.PhysicalBreaks -gt 0) {
        $totalPhysicalBreaks += $counts.PhysicalBreaks
        $totalChangedValues += $counts.ChangedValues
        if ($Fix) {
            [System.IO.File]::WriteAllText($file.FullName, $normalizedText, $utf8)
            $totalChangedFiles++
        }
        else {
            $invalidFiles.Add("$($file.Name): $($counts.ChangedValues) values, $($counts.PhysicalBreaks) line breaks")
        }
    }

    $textToCheck = if ($Fix) { $normalizedText } else { $originalText }
    foreach ($row in $rowRegex.Matches($textToCheck)) {
        $cells = $cellRegex.Matches($row.Value)
        if ($cells.Count -lt 2) {
            continue
        }
        $data = $dataRegex.Match($cells[1].Value)
        if ($data.Success) {
            $totalLineFeedReferences += $lineFeedReferenceRegex.Matches($data.Groups['value'].Value).Count
        }
    }
}

if ($invalidFiles.Count -gt 0) {
    throw "Physical line breaks found in active Simplified Chinese values. Use &#10; instead.`n$($invalidFiles -join "`n")"
}

Write-Host (
    "Simplified Chinese line breaks verified: {0} tables, {1} &#10; references" -f
    $files.Count,
    $totalLineFeedReferences) -ForegroundColor DarkGray

if ($Fix) {
    Write-Host (
        "Normalized {0} physical line breaks in {1} active values across {2} files" -f
        $totalPhysicalBreaks,
        $totalChangedValues,
        $totalChangedFiles) -ForegroundColor DarkGray
}
