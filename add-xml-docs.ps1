# Auto-generate XML documentation for C# files
# This script adds basic <summary> tags to public classes, properties, and methods

param(
    [string]$Path = "src/LakskongenSim",
    [switch]$DryRun
)

$files = Get-ChildItem -Path $Path -Filter "*.cs" -Recurse

foreach ($file in $files) {
    $content = Get-Content $file.FullName -Raw
    $modified = $content
    
    # Add <summary> for public enums
    $modified = $modified -replace '(?m)^(\s*)(public enum \w+)\s*$', '$1/// <summary>$2</summary>`r`n$1$2'
    
    # Add <summary> for public enum members
    $modified = $modified -replace '(?m)^(\s*)(\w+),?\s*$', '$1/// <summary>$2</summary>`r`n$1$2'
    
    # Add <summary> for public classes
    $modified = $modified -replace '(?m)^(\s*)(public (?:class|struct|interface|record) \w+)', '$1/// <summary>$2</summary>`r`n$1$2'
    
    # Add <summary> for public properties
    $modified = $modified -replace '(?m)^(\s*)(public \w+\s+\w+\s*\{)', '$1/// <summary>$2</summary>`r`n$1$2'
    
    # Add <summary> for public methods
    $modified = $modified -replace '(?m)^(\s*)(public \w+\s+\w+\s*\()', '$1/// <summary>$2</summary>`r`n$1$2'
    
    # Add <summary> for public delegates
    $modified = $modified -replace '(?m)^(\s*)(public delegate)', '$1/// <summary>$2</summary>`r`n$1$2'
    
    # Add <summary> for public events
    $modified = $modified -replace '(?m)^(\s*)(public event)', '$1/// <summary>$2</summary>`r`n$1$2'
    
    if ($modified -ne $content) {
        if ($DryRun) {
            Write-Host "Would modify: $($file.FullName)"
        } else {
            Set-Content -Path $file.FullName -Value $modified -NoNewline
            Write-Host "Modified: $($file.FullName)"
        }
    }
}
