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
    Gives placeholder decisions their real numbers. See AGENTS.md and docs/maintaining.md.

    On a branch, a new decision in docs/decisions.md gets a placeholder in its # column, such as
    D-example-1, and is cited by that placeholder everywhere ("decision D-example-1"). Branches never take
    real numbers, so two branches cannot claim the same one. When a branch merges into main, run this
    script: it numbers the placeholders from the next free number, in the order they appear in the
    decision log, and replaces every mention in the repository's tracked text files.

    Usage:
      node docs/number-decisions.js            Numbers every placeholder and rewrites the files.
      node docs/number-decisions.js --dry-run  Shows what it would do, and changes nothing.
      node docs/number-decisions.js --check    Exits with 1 if any placeholder is left (for main).
*/

const fs = require( "fs" );
const path = require( "path" );
const { execFileSync } = require( "child_process" );

const root = path.join( __dirname, ".." );
const decisionsPath = path.join( __dirname, "decisions.md" );
const isDryRun = process.argv.includes( "--dry-run" );
const isCheck = process.argv.includes( "--check" );

// A placeholder: "D-", a lowercase slug, "-", and a number, such as D-example-1 or D-example-teams-12.
const PLACEHOLDER = /D-[a-z][a-z0-9]*(?:-[a-z][a-z0-9]*)*-\d+/;
const ROW = new RegExp( `^\\| (\\d+|${PLACEHOLDER.source}) \\|`, "gm" );
const TEXT_EXTENSIONS = new Set( [ ".md", ".json", ".js", ".cs", ".sql", ".txt", ".ps1" ] );

const decisions = fs.readFileSync( decisionsPath, "utf8" );
const numbers = [];
const placeholders = [];
for ( const match of decisions.matchAll( ROW ) ) {
    if ( /^\d+$/.test( match[ 1 ] ) ) {
        numbers.push( Number( match[ 1 ] ) );
    }
    else if ( !placeholders.includes( match[ 1 ] ) ) {
        placeholders.push( match[ 1 ] );
    }
}

if ( isCheck ) {
    if ( placeholders.length ) {
        console.error( `Decisions still have placeholders: ${placeholders.join( ", " )}. Run node docs/number-decisions.js.` );
        process.exit( 1 );
    }
    console.log( "Every decision has a number." );
    process.exit( 0 );
}

if ( !placeholders.length ) {
    console.log( "No placeholder decisions to number." );
    process.exit( 0 );
}

const duplicates = numbers.filter( ( n, i ) => numbers.indexOf( n ) !== i );
if ( duplicates.length ) {
    console.error( `docs/decisions.md uses these numbers twice: ${[ ...new Set( duplicates ) ].join( ", " )}. Fix them by hand first.` );
    process.exit( 1 );
}

let next = Math.max( 0, ...numbers ) + 1;
const assigned = new Map( placeholders.map( p => [ p, String( next++ ) ] ) );
for ( const [ placeholder, number ] of assigned ) {
    console.log( `${placeholder} -> ${number}` );
}

// Longest first, so D-example-12 is never rewritten as D-example-1 followed by "2".
const pattern = new RegExp( `(?<![A-Za-z0-9-])(${[ ...assigned.keys() ].sort( ( a, b ) => b.length - a.length ).join( "|" )})(?![A-Za-z0-9])`, "g" );
const files = execFileSync( "git", [ "ls-files" ], { cwd: root, encoding: "utf8" } )
    .split( /\r?\n/ )
    .filter( f => f && TEXT_EXTENSIONS.has( path.extname( f ).toLowerCase() ) );

let changedFiles = 0;
for ( const file of files ) {
    const full = path.join( root, file );
    if ( !fs.existsSync( full ) ) {
        continue;
    }
    const text = fs.readFileSync( full, "utf8" );
    const updated = text.replace( pattern, p => assigned.get( p ) );
    if ( updated !== text ) {
        changedFiles++;
        console.log( `  ${isDryRun ? "would update" : "updated"} ${file}` );
        if ( !isDryRun ) {
            fs.writeFileSync( full, updated );
        }
    }
}

console.log( `${assigned.size} decision${assigned.size === 1 ? "" : "s"} numbered in ${changedFiles} file${changedFiles === 1 ? "" : "s"}${isDryRun ? " (dry run, nothing changed)" : ""}.` );
