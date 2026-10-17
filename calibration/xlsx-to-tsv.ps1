# <copyright>
# Copyright by the Spark Development Network
#
# Licensed under the Rock Community License (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
# http://www.rockrms.com/license
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.
# </copyright>
#

# Converts the Excel export of a calibration grid (Rock's SQL editor) into "Section<TAB>Json"
# lines that normalize.js can read. Uses no Excel install and no extra modules.
#
# Usage: .\xlsx-to-tsv.ps1 -Path .\raw\source-2-part-1.xlsx
# Output: the same path with a .tsv extension.

param( [Parameter( Mandatory = $true )][string]$Path )

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead( ( Resolve-Path $Path ) )
try {
    # Shared strings hold the cell text.
    $strings = @()
    $entry = $zip.Entries | Where-Object { $_.FullName -eq 'xl/sharedStrings.xml' }
    if ( $entry ) {
        $reader = New-Object IO.StreamReader( $entry.Open() )
        [xml]$xml = $reader.ReadToEnd()
        $reader.Close()
        $strings = @( $xml.sst.si | ForEach-Object { $_.InnerText } )
    }

    $lines = @()
    foreach ( $sheet in ( $zip.Entries | Where-Object { $_.FullName -like 'xl/worksheets/sheet*.xml' } ) ) {
        $reader = New-Object IO.StreamReader( $sheet.Open() )
        [xml]$xml = $reader.ReadToEnd()
        $reader.Close()
        $ns = New-Object Xml.XmlNamespaceManager( $xml.NameTable )
        $ns.AddNamespace( 'm', $xml.DocumentElement.NamespaceURI )
        foreach ( $row in $xml.SelectNodes( '//m:sheetData/m:row', $ns ) ) {
            $cells = foreach ( $cell in $row.SelectNodes( 'm:c', $ns ) ) {
                $value = $cell.SelectSingleNode( 'm:v', $ns )
                $inline = $cell.SelectSingleNode( 'm:is', $ns )
                if ( $cell.t -eq 's' -and $value ) { $strings[ [int]$value.InnerText ] }
                elseif ( $inline ) { $inline.InnerText }
                elseif ( $value ) { $value.InnerText }
                else { '' }
            }
            $lines += ( $cells -join "`t" )
        }
    }
}
finally {
    $zip.Dispose()
}

$out = [IO.Path]::ChangeExtension( $Path, '.tsv' )
$lines | Set-Content -Path $out -Encoding UTF8
"Wrote $out"
