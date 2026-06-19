param(
    [Parameter(Mandatory = $true)]
    [string] $ProjectPath,

    [string] $ModelRoot,

    [string] $Prefix,

    [string[]] $Include = @(),

    [string[]] $Exclude = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Normalize-Include {
    param([string] $Value)

    $normalized = ($Value -replace '/', '\')

    if ($ModelRoot) {
        $root = (Resolve-Path -LiteralPath $ModelRoot).Path.TrimEnd('\')
        $full = $null

        if ([System.IO.Path]::IsPathRooted($normalized) -and (Test-Path -LiteralPath $normalized)) {
            $full = (Resolve-Path -LiteralPath $normalized).Path
        }

        if ($full -and $full.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
            $normalized = $full.Substring($root.Length).TrimStart('\')
        }
    }

    if ($normalized.EndsWith('.xml', [System.StringComparison]::OrdinalIgnoreCase)) {
        $normalized = $normalized.Substring(0, $normalized.Length - 4)
    }

    return $normalized
}

if (!(Test-Path -LiteralPath $ProjectPath)) {
    throw "ProjectPath not found: $ProjectPath"
}

[xml] $project = Get-Content -Raw -LiteralPath $ProjectPath
$namespaceManager = New-Object System.Xml.XmlNamespaceManager($project.NameTable)
$namespaceManager.AddNamespace('msb', 'http://schemas.microsoft.com/developer/msbuild/2003')

$actual = @(
    $project.SelectNodes('//msb:Content', $namespaceManager) |
        ForEach-Object { Normalize-Include $_.Include }
)

$expected = New-Object System.Collections.Generic.List[string]

if ($ModelRoot -and $Prefix) {
    $artifactFolders = @(
        'AxClass',
        'AxTable',
        'AxTableExtension',
        'AxEdt',
        'AxEnum',
        'AxEnumExtension',
        'AxForm',
        'AxFormExtension',
        'AxMenuItemDisplay',
        'AxMenuItemAction',
        'AxMenuItemOutput',
        'AxQuery',
        'AxView',
        'AxSecurityPrivilege',
        'AxSecurityDuty',
        'AxSecurityRole',
        'AxWorkflowTemplate',
        'AxWorkflowApproval',
        'AxWorkflowTask',
        'AxLabelFile'
    )

    foreach ($folder in $artifactFolders) {
        $path = Join-Path $ModelRoot $folder
        if (!(Test-Path -LiteralPath $path)) {
            continue
        }

        Get-ChildItem -LiteralPath $path -Filter "$Prefix*.xml" -File |
            ForEach-Object {
                $expected.Add((Normalize-Include $_.FullName))
            }
    }
}

foreach ($item in $Include) {
    if ($item) {
        $expected.Add((Normalize-Include $item))
    }
}

$excludeNormalized = @($Exclude | Where-Object { $_ } | ForEach-Object { Normalize-Include $_ })
$expectedDistinct = $expected | Sort-Object -Unique | Where-Object { $excludeNormalized -notcontains $_ }
$missing = @($expectedDistinct | Where-Object { $actual -notcontains $_ })

[pscustomobject]@{
    ProjectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
    ExpectedCount = $expectedDistinct.Count
    ContentEntryCount = $actual.Count
    MissingCount = $missing.Count
    Missing = $missing
}

if ($missing.Count -gt 0) {
    exit 1
}
