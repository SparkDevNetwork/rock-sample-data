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
    Merges one release's packages into the hosted package index, index.json. The format is in the loader
    README, under "The package index". The release workflow runs it after every package passed the
    publishing gate, and uploads the result last (decision 128).

    Usage:
      node release/merge-index.js --packages <folder> --version 1.2.0 --out <new index.json>
                                  [--index <current index.json>] [--commit <full hash>]

    --packages  The release's packages: one rock-{version} folder per Rock release, each holding
                {dataset}.zip and its receipt, {dataset}.verify.json.
    --version   The package release, from the tag, without the "v".
    --index     The index now hosted. Leave it out, or name a missing file, for the first release.
    --commit    Refuse a receipt for a package built from any other commit.
    --out       Where to write the merged index.

    Each package becomes one entry. Its checksum, dataset, and Rock come from its receipt, its size from
    the file, and its name, description, people, and campuses from the dataset's manifest in datasets/.
    A new entry replaces the hosted entry for the same dataset and Rock version, so the index lists a
    dataset once per Rock. Entries for other Rock versions stay. Packages already published stay at
    their versioned paths; only the index stops listing them.

    Exits with 1, writing nothing, when a receipt did not pass, a field is missing, or the hosted index
    already lists this version of a package with different bytes.
*/

const fs = require( "fs" );
const path = require( "path" );

const FORMAT_VERSION = 1;
const root = path.join( __dirname, ".." );

// #region Functions

/**
 * Reads --name value pairs from the command line.
 */
function readArguments( args ) {
    const options = {};
    for ( let i = 0; i < args.length; i++ ) {
        if ( args[ i ].startsWith( "--" ) ) {
            options[ args[ i ].substring( 2 ) ] = args[ i + 1 ];
            i++;
        }
    }

    return options;
}

/**
 * Compares two Rock versions such as "21.0.2" by number, for sorting newest first.
 */
function compareVersions( a, b ) {
    const left = a.split( "." ).map( Number );
    const right = b.split( "." ).map( Number );
    for ( let i = 0; i < Math.max( left.length, right.length ); i++ ) {
        const difference = ( right[ i ] || 0 ) - ( left[ i ] || 0 );
        if ( difference !== 0 ) {
            return difference;
        }
    }

    return 0;
}

/**
 * Reads the hosted index, or an empty one when there is none yet.
 */
function readIndex( file ) {
    if ( !file || !fs.existsSync( file ) ) {
        return { formatVersion: FORMAT_VERSION, packages: [] };
    }

    const index = JSON.parse( fs.readFileSync( file, "utf8" ) );
    if ( index.formatVersion !== FORMAT_VERSION ) {
        throw new Error( `${file} uses index format ${index.formatVersion}; this script writes format ${FORMAT_VERSION}.` );
    }

    return { formatVersion: FORMAT_VERSION, packages: index.packages || [] };
}

/**
 * Makes the index entry for one verified package.
 */
function toEntry( folder, rockFolder, receiptFile, version, commit ) {
    const receipt = JSON.parse( fs.readFileSync( path.join( folder, receiptFile ), "utf8" ) );
    const label = `${rockFolder}/${receiptFile}`;
    if ( receipt.passed !== true ) {
        throw new Error( `${label} did not pass.` );
    }

    if ( commit && receipt.package.source.commit !== commit ) {
        throw new Error( `${label} is for a package built from ${receipt.package.source.commit}, not ${commit}.` );
    }

    const dataset = receipt.package.dataset;
    const manifest = JSON.parse( fs.readFileSync( path.join( root, "datasets", `${dataset}.json` ), "utf8" ) );
    const entry = {
        dataset,
        name: manifest.name,
        description: manifest.summary || manifest.description,
        people: manifest.people,
        campuses: manifest.campuses,
        version,
        url: `v${version}/${rockFolder}/${receipt.package.file}`,
        sha256: receipt.package.sha256,
        size: fs.statSync( path.join( folder, receipt.package.file ) ).size,
        rockVersion: receipt.rock.version,
        rockMigration: receipt.rock.migration
    };

    // Leave out what the manifest does not give, rather than write a wrong zero.
    for ( const key of Object.keys( entry ) ) {
        if ( entry[ key ] === undefined || entry[ key ] === null ) {
            delete entry[ key ];
        }
    }

    for ( const required of [ "dataset", "name", "version", "url", "sha256", "rockVersion", "rockMigration" ] ) {
        if ( !entry[ required ] ) {
            throw new Error( `${label} gives no ${required}.` );
        }
    }

    if ( !/^[0-9a-f]{64}$/.test( entry.sha256 ) ) {
        throw new Error( `${label} has a sha256 that is not 64 lowercase hex digits.` );
    }

    if ( `rock-${entry.rockVersion}` !== rockFolder ) {
        throw new Error( `${label} is for Rock ${entry.rockVersion}, but it is in ${rockFolder}.` );
    }

    return entry;
}

// #endregion Functions

function main() {
    const options = readArguments( process.argv.slice( 2 ) );
    for ( const required of [ "packages", "version", "out" ] ) {
        if ( !options[ required ] ) {
            throw new Error( `--${required} is required.` );
        }
    }

    const version = options.version.replace( /^v/, "" );
    const added = [];
    const rockFolders = fs.readdirSync( options.packages ).filter( name => name.startsWith( "rock-" ) ).sort();
    for ( const rockFolder of rockFolders ) {
        const folder = path.join( options.packages, rockFolder );
        for ( const receiptFile of fs.readdirSync( folder ).filter( name => name.endsWith( ".verify.json" ) ).sort() ) {
            added.push( toEntry( folder, rockFolder, receiptFile, version, options.commit ) );
        }
    }

    if ( added.length === 0 ) {
        throw new Error( `${options.packages} has no receipts.` );
    }

    const index = readIndex( options.index );
    const key = entry => `${entry.dataset} ${entry.rockVersion}`;
    const replaced = new Set( added.map( key ) );
    for ( const hosted of index.packages ) {
        const same = added.find( entry => key( entry ) === key( hosted ) && entry.version === hosted.version );
        if ( same && same.sha256 !== hosted.sha256 ) {
            throw new Error( `The index already lists ${hosted.dataset} ${hosted.version} for Rock ${hosted.rockVersion} with other bytes. Tag a new version instead.` );
        }
    }

    // One entry per dataset and Rock: in dataset order, newest Rock first.
    const packages = index.packages.filter( hosted => !replaced.has( key( hosted ) ) ).concat( added );
    packages.sort( ( a, b ) => a.dataset.localeCompare( b.dataset, "en" ) || compareVersions( a.rockVersion, b.rockVersion ) );

    fs.mkdirSync( path.dirname( path.resolve( options.out ) ), { recursive: true } );
    fs.writeFileSync( options.out, JSON.stringify( { formatVersion: FORMAT_VERSION, packages }, null, 2 ) + "\n" );

    console.log( `Wrote ${options.out}: ${packages.length} packages, ${added.length} from v${version}.` );
    for ( const entry of added ) {
        console.log( `  ${entry.url}  ${entry.sha256.substring( 0, 12 )}  ${entry.size} bytes` );
    }
}

try {
    main();
}
catch ( e ) {
    console.error( e.message );
    process.exit( 1 );
}
