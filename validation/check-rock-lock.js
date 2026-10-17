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
    Rock version guard. The builder ports some of Rock's code (giving automation math, alert rules, value
    formats). This script reads those Rock files from GitHub, at a branch or release tag, and checks that
    they still match the fingerprints in rock-lock.json. A change means the port may be out of date.

    It reads Rock's public repository on GitHub, so it works the same on every machine and needs no local
    Rock checkout.

    Usage:
      node validation/check-rock-lock.js                 check against Rock's develop branch
      node validation/check-rock-lock.js --ref 19.1      check against a release tag or any branch or commit
      node validation/check-rock-lock.js --update        after reviewing a change and updating the port,
                                                         record the new fingerprints

    Set GITHUB_TOKEN to raise GitHub's rate limit (the script makes one API call to name the commit).
    Exits with 1 when any file changed or could not be read.
*/

const fs = require( "fs" );
const path = require( "path" );
const crypto = require( "crypto" );

const REPOSITORY = "SparkDevNetwork/Rock";
const lockPath = path.join( __dirname, "rock-lock.json" );

const args = process.argv.slice( 2 );
const update = args.includes( "--update" );
const refIndex = args.indexOf( "--ref" );
const ref = refIndex >= 0 ? args[ refIndex + 1 ] : "develop";

// #region Functions

/**
 * The fingerprint of a file's text, or of the part between two markers. Line endings are normalized,
 * so a checkout on Windows and one on Linux agree.
 */
function fingerprint( text, entry ) {
    let part = text.replace( /\r\n/g, "\n" );
    if ( entry.from ) {
        const start = part.indexOf( entry.from );
        const end = start < 0 ? -1 : part.indexOf( entry.to, start + entry.from.length );
        if ( start < 0 || end < 0 ) {
            return null;
        }

        part = part.substring( start, end );
    }

    return crypto.createHash( "sha256" ).update( part, "utf8" ).digest( "hex" );
}

async function fetchText( url, headers ) {
    const response = await fetch( url, { headers } );
    if ( !response.ok ) {
        throw new Error( `${response.status} ${response.statusText}` );
    }

    return response.text();
}

// #endregion Functions

async function main() {
    const lock = JSON.parse( fs.readFileSync( lockPath, "utf8" ) );
    const headers = process.env.GITHUB_TOKEN ? { Authorization: `Bearer ${process.env.GITHUB_TOKEN}` } : {};

    // Name the exact commit, so the lock records what was reviewed.
    const commit = JSON.parse( await fetchText( `https://api.github.com/repos/${REPOSITORY}/commits/${encodeURIComponent( ref )}`, { ...headers, Accept: "application/vnd.github+json" } ) ).sha;
    console.log( `Checking ${lock.files.length} Rock files at ${ref} (${commit.substring( 0, 10 )}) against ${lock.checkedRef} (${( lock.checkedCommit || "" ).substring( 0, 10 )})` );

    let problems = 0;
    let unreadable = 0;
    for ( const entry of lock.files ) {
        const label = entry.from ? `${entry.path} (${entry.section})` : entry.path;
        let current;
        try {
            const text = await fetchText( `https://raw.githubusercontent.com/${REPOSITORY}/${commit}/${entry.path.split( "/" ).map( encodeURIComponent ).join( "/" )}`, headers );
            current = fingerprint( text, entry );
        }
        catch ( e ) {
            console.log( `  MISSING  ${label}: ${e.message}` );
            problems++;
            unreadable++;
            continue;
        }

        if ( current === null ) {
            console.log( `  CHANGED  ${label}: the section markers were not found` );
            problems++;
            unreadable++;
            continue;
        }

        if ( current === entry.sha256 ) {
            console.log( `  ok       ${label}` );
            continue;
        }

        console.log( `  CHANGED  ${label}` );
        console.log( `           review: https://github.com/${REPOSITORY}/commits/${ref}/${entry.path.split( "/" ).map( encodeURIComponent ).join( "/" )}` );
        console.log( `           then update: ${entry.mirroredBy.join( ", " )}` );
        problems++;
        if ( update ) {
            entry.sha256 = current;
        }
    }

    if ( update && unreadable > 0 ) {
        console.log( `Not updated: ${unreadable} file(s) could not be read or their section markers moved. Fix rock-lock.json first.` );
        return 1;
    }

    if ( update ) {
        lock.checkedRef = ref;
        lock.checkedCommit = commit;
        lock.checkedDate = new Date().toISOString().substring( 0, 10 );
        fs.writeFileSync( lockPath, JSON.stringify( lock, null, 2 ) + "\n" );
        console.log( `Recorded ${ref} (${commit.substring( 0, 10 )}) in rock-lock.json.` );
        return 0;
    }

    console.log( problems === 0
        ? "Rock has not changed any code the builder ports."
        : `${problems} Rock file(s) changed. Review each change, update the port and the builder files listed, run the parity test (validation/README.md), then run with --update.` );
    return problems === 0 ? 0 : 1;
}

main().then( code => process.exit( code ) ).catch( e => {
    console.error( `Could not check Rock: ${e.message}` );
    process.exit( 1 );
} );
