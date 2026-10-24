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
    Refuses a release whose loader would be older than the packages it publishes (decision 158).
    The builder writes SampleDataPackage.MinimumLoaderVersion into every package, and the release packs the
    loader with the tag's version. A tag below that minimum would ship a loader that refuses its own packages.

    Usage:
      node release/check-minimum-loader.js --version 1.0.0

    Exits with 1 when the version is below the minimum, or either is not a version like 1.0.0 or 1.1.0-beta.1.
*/

const fs = require( "fs" );
const path = require( "path" );

const sourcePath = path.join( __dirname, "..", "loader", "packages", "SampleDataPackage.cs" );

// #region Functions

/**
 * Splits a semantic version into its three numbers and its prerelease parts. Build metadata is ignored.
 */
function parse( version ) {
    const match = /^(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z.-]+))?(?:\+.*)?$/.exec( version || "" );
    if ( !match ) {
        throw new Error( `"${version}" is not a version like 1.0.0 or 1.1.0-beta.1.` );
    }

    return { numbers: match.slice( 1, 4 ).map( Number ), labels: match[ 4 ] ? match[ 4 ].split( "." ) : [] };
}

/**
 * Orders two semantic versions as SemVer 2.0 does, the same order as the loader's LoaderVersion.Compare.
 */
function compare( a, b ) {
    const left = parse( a );
    const right = parse( b );
    for ( let i = 0; i < 3; i++ ) {
        if ( left.numbers[ i ] !== right.numbers[ i ] ) {
            return Math.sign( left.numbers[ i ] - right.numbers[ i ] );
        }
    }

    if ( left.labels.length === 0 || right.labels.length === 0 ) {
        return Math.sign( right.labels.length - left.labels.length );
    }

    for ( let i = 0; i < Math.min( left.labels.length, right.labels.length ); i++ ) {
        const isLeftNumber = /^\d+$/.test( left.labels[ i ] );
        const isRightNumber = /^\d+$/.test( right.labels[ i ] );
        const order = isLeftNumber && isRightNumber ? Math.sign( Number( left.labels[ i ] ) - Number( right.labels[ i ] ) )
            : isLeftNumber !== isRightNumber ? ( isLeftNumber ? -1 : 1 )
            : left.labels[ i ] < right.labels[ i ] ? -1 : left.labels[ i ] > right.labels[ i ] ? 1 : 0;
        if ( order !== 0 ) {
            return order;
        }
    }

    return Math.sign( left.labels.length - right.labels.length );
}

// #endregion Functions

function main() {
    const versionIndex = process.argv.indexOf( "--version" );
    const version = versionIndex >= 0 ? String( process.argv[ versionIndex + 1 ] || "" ).replace( /^v/, "" ) : "";
    if ( !version ) {
        throw new Error( "--version is required." );
    }

    const minimum = ( /MinimumLoaderVersion\s*=\s*"([^"]+)"/.exec( fs.readFileSync( sourcePath, "utf8" ) ) || [] )[ 1 ];
    if ( !minimum ) {
        throw new Error( `${sourcePath} has no MinimumLoaderVersion.` );
    }

    if ( compare( version, minimum ) < 0 ) {
        throw new Error( `Version ${version} is below SampleDataPackage.MinimumLoaderVersion, ${minimum}. Its loader would refuse its own packages. Tag ${minimum} or later.` );
    }

    console.log( `Version ${version} is at least the minimum loader version, ${minimum}.` );
}

try {
    main();
}
catch ( e ) {
    console.error( e.message );
    process.exit( 1 );
}
