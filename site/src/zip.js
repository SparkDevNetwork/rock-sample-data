// <copyright>
// Copyright by the Spark Development Network
//
// Licensed under the Rock Community License (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.rockrms.com/license
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// </copyright>
//

/*
    Writes a standard zip file with Node's built-in zlib, so the site needs no packages.
    Images are stored as-is (they are already compressed); text files are deflated.
*/

const fs = require( "fs" );
const zlib = require( "zlib" );

const compressible = /\.(svg|txt|md|json|css|html)$/i;

// A fixed timestamp keeps the zip bytes the same on every build of the same files.
const dosTime = 0;
const dosDate = ( ( 2026 - 1980 ) << 9 ) | ( 1 << 5 ) | 1;

/**
 * Writes entries ({ name, data }) to a zip file at the given path.
 */
function writeZip( zipPath, entries ) {
    const locals = [];
    const centrals = [];
    let offset = 0;

    for ( const entry of entries ) {
        const name = Buffer.from( entry.name, "utf8" );
        const deflate = compressible.test( entry.name );
        const body = deflate ? zlib.deflateRawSync( entry.data, { level: 9 } ) : entry.data;
        const crc = zlib.crc32( entry.data ) >>> 0;

        const local = Buffer.alloc( 30 );
        local.writeUInt32LE( 0x04034b50, 0 );
        local.writeUInt16LE( 20, 4 );
        local.writeUInt16LE( 0x0800, 6 );
        local.writeUInt16LE( deflate ? 8 : 0, 8 );
        local.writeUInt16LE( dosTime, 10 );
        local.writeUInt16LE( dosDate, 12 );
        local.writeUInt32LE( crc, 14 );
        local.writeUInt32LE( body.length, 18 );
        local.writeUInt32LE( entry.data.length, 22 );
        local.writeUInt16LE( name.length, 26 );
        locals.push( local, name, body );

        const central = Buffer.alloc( 46 );
        central.writeUInt32LE( 0x02014b50, 0 );
        central.writeUInt16LE( 20, 4 );
        central.writeUInt16LE( 20, 6 );
        central.writeUInt16LE( 0x0800, 8 );
        central.writeUInt16LE( deflate ? 8 : 0, 10 );
        central.writeUInt16LE( dosTime, 12 );
        central.writeUInt16LE( dosDate, 14 );
        central.writeUInt32LE( crc, 16 );
        central.writeUInt32LE( body.length, 20 );
        central.writeUInt32LE( entry.data.length, 24 );
        central.writeUInt16LE( name.length, 28 );
        central.writeUInt32LE( offset, 42 );
        centrals.push( central, name );

        offset += local.length + name.length + body.length;
    }

    const centralSize = centrals.reduce( ( sum, b ) => sum + b.length, 0 );
    const end = Buffer.alloc( 22 );
    end.writeUInt32LE( 0x06054b50, 0 );
    end.writeUInt16LE( entries.length, 8 );
    end.writeUInt16LE( entries.length, 10 );
    end.writeUInt32LE( centralSize, 12 );
    end.writeUInt32LE( offset, 16 );

    fs.writeFileSync( zipPath, Buffer.concat( [ ...locals, ...centrals, end ] ) );
}

module.exports = { writeZip };
