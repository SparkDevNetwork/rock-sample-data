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
    Checks the note text pools in this folder against README.md: one file per note type in
    configuration/notes.json plus shared.json, a section for exactly the audiences each type
    has a weight for, the required lists, entry lengths, tokens, plain characters, and no two
    entries in one list that start with the same five words. Prints a summary and exits with 1
    when anything fails.

    Usage: node content/notes/check-pools.js
*/

const fs = require( "fs" );
const path = require( "path" );

const poolDir = __dirname;
const configuration = JSON.parse( fs.readFileSync( path.join( poolDir, "..", "..", "configuration", "notes.json" ), "utf8" ) );

// Character lengths before tokens are filled, from the README's Lengths table.
const LENGTHS = {
    openers: { min: 30, max: 220 },
    details: { min: 30, max: 200 },
    alerts: { min: 20, max: 120 },
    replies: { min: 20, max: 160 },
    mentionLines: { min: 20, max: 100 }
};

// Openers should mostly fall in this range. It is reported, not enforced.
const OPENER_TYPICAL = { min: 60, max: 150 };

const PERSON_TOKENS = [ "{name}", "{he}", "{him}", "{his}", "{He}", "{His}", "{campus}" ];
const STAFF_TOKEN = "{staff}";

// The only type file with an alerts list (README: Type files).
const ALERT_TYPE = "personal";
const SHARED_FILE = "shared.json";
const FIRST_WORDS = 5;

const errors = [];
const summary = [];

/**
 * Reads a pool file, or records an error and returns null when it is not valid JSON.
 */
function readPool( file ) {
    try {
        return JSON.parse( fs.readFileSync( path.join( poolDir, file ), "utf8" ) );
    }
    catch ( e ) {
        errors.push( `${file}: is not valid JSON (${e.message}).` );
        return null;
    }
}

/**
 * The first few words of an entry, lowercased and without punctuation, so near-identical openings match.
 */
function firstWords( text ) {
    return text.toLowerCase().replace( /[^a-z0-9{}\s]/g, " " ).split( /\s+/ ).filter( w => w ).slice( 0, FIRST_WORDS ).join( " " );
}

/**
 * Checks one list of entries: its length rules, tokens, characters, and first words.
 */
function checkList( owner, poolName, entries ) {
    if ( !Array.isArray( entries ) || entries.length === 0 ) {
        errors.push( `${owner}: "${poolName}" must be a non-empty list.` );
        return 0;
    }

    const limits = LENGTHS[poolName];
    const openings = new Map();
    for ( const [ index, entry ] of entries.entries() ) {
        const at = `${owner} ${poolName}[${index}]`;
        if ( typeof entry !== "string" ) {
            errors.push( `${at}: is not a string.` );
            continue;
        }
        if ( entry !== entry.trim() ) {
            errors.push( `${at}: has leading or trailing spaces.` );
        }
        if ( entry.length < limits.min || entry.length > limits.max ) {
            errors.push( `${at}: is ${entry.length} characters; ${poolName} are ${limits.min} to ${limits.max}. "${entry}"` );
        }

        // Tokens: only the documented ones, and {staff} only in mention lines, where it is required.
        const tokens = entry.match( /\{[^}]*\}/g ) || [];
        for ( const token of tokens ) {
            const isStaff = token === STAFF_TOKEN;
            if ( isStaff && poolName !== "mentionLines" ) {
                errors.push( `${at}: uses ${STAFF_TOKEN}, which belongs only in mentionLines.` );
            }
            else if ( !isStaff && !PERSON_TOKENS.includes( token ) ) {
                errors.push( `${at}: uses unknown token ${token}.` );
            }
        }
        if ( /[{}]/.test( entry.replace( /\{[^{}]*\}/g, "" ) ) ) {
            errors.push( `${at}: has an unmatched brace.` );
        }
        if ( poolName === "mentionLines" && !tokens.includes( STAFF_TOKEN ) ) {
            errors.push( `${at}: a mention line must bring in another staff member with ${STAFF_TOKEN}.` );
        }

        // Plain characters: printable ASCII only, which rules out dashes, curly quotes, and emoji.
        const nonAscii = [ ...entry ].filter( c => c < " " || c > "~" );
        if ( nonAscii.length ) {
            const codes = [ ...new Set( nonAscii ) ].map( c => `U+${c.codePointAt( 0 ).toString( 16 ).toUpperCase().padStart( 4, "0" )}` );
            errors.push( `${at}: has characters that are not plain (${codes.join( ", " )}). Use straight quotes, commas, or periods.` );
        }
        if ( entry.includes( "--" ) ) {
            errors.push( `${at}: uses "--" as a dash. Use a comma or a period.` );
        }

        const opening = firstWords( entry );
        if ( openings.has( opening ) ) {
            errors.push( `${at}: starts with the same ${FIRST_WORDS} words as entry ${openings.get( opening )} ("${opening}").` );
        }
        else {
            openings.set( opening, index );
        }
    }
    return entries.length;
}

/**
 * Checks that an object has exactly the expected keys, ignoring $comment.
 */
function checkKeys( owner, value, required, optional = [] ) {
    if ( !value || typeof value !== "object" || Array.isArray( value ) ) {
        errors.push( `${owner}: must be an object.` );
        return false;
    }
    for ( const key of required ) {
        if ( !( key in value ) ) {
            errors.push( `${owner}: is missing "${key}".` );
        }
    }
    for ( const key of Object.keys( value ) ) {
        if ( key !== "$comment" && !required.includes( key ) && !optional.includes( key ) ) {
            errors.push( `${owner}: has unexpected "${key}".` );
        }
    }
    return true;
}

// Files: one per configured type plus shared.json, and nothing else.
const expectedFiles = configuration.types.map( t => `${t.key}.json` ).concat( SHARED_FILE );
const presentFiles = fs.readdirSync( poolDir ).filter( f => f.endsWith( ".json" ) );
for ( const file of expectedFiles.filter( f => !presentFiles.includes( f ) ) ) {
    errors.push( `${file}: is missing. Every note type in configuration/notes.json needs a pool file.` );
}
for ( const file of presentFiles.filter( f => !expectedFiles.includes( f ) ) ) {
    errors.push( `${file}: is not a note type in configuration/notes.json or ${SHARED_FILE}.` );
}

// Type files: a section for exactly the audiences the type has a weight for.
for ( const type of configuration.types ) {
    const file = `${type.key}.json`;
    if ( !presentFiles.includes( file ) ) {
        continue;
    }
    const pool = readPool( file );
    if ( !pool ) {
        continue;
    }
    const audiences = Object.keys( type.weights || {} ).filter( a => type.weights[a] > 0 );
    if ( !checkKeys( file, pool, audiences ) ) {
        continue;
    }

    const hasAlerts = type.key === ALERT_TYPE;
    const counts = [];
    for ( const audience of audiences.filter( a => pool[a] ) ) {
        const owner = `${file} ${audience}`;
        const lists = hasAlerts ? [ "openers", "details", "alerts" ] : [ "openers", "details" ];
        if ( !checkKeys( owner, pool[audience], lists ) ) {
            continue;
        }
        const sizes = lists.filter( list => list in pool[audience] ).map( list => `${checkList( owner, list, pool[audience][list] )} ${list}` );
        const openers = ( pool[audience].openers || [] ).filter( o => typeof o === "string" );
        const typical = openers.filter( o => o.length >= OPENER_TYPICAL.min && o.length <= OPENER_TYPICAL.max ).length;
        const share = openers.length ? Math.round( 100 * typical / openers.length ) : 0;
        counts.push( `${audience}: ${sizes.join( ", " )} (${share}% of openers are ${OPENER_TYPICAL.min} to ${OPENER_TYPICAL.max} characters)` );
    }
    summary.push( `${file}\n    ${counts.join( "\n    " )}` );
}

// The shared file: staff replies and mention lines.
if ( presentFiles.includes( SHARED_FILE ) ) {
    const shared = readPool( SHARED_FILE );
    if ( shared && checkKeys( SHARED_FILE, shared, [ "replies", "mentionLines" ] ) ) {
        const replies = checkList( SHARED_FILE, "replies", shared.replies );
        const mentionLines = checkList( SHARED_FILE, "mentionLines", shared.mentionLines );
        summary.push( `${SHARED_FILE}\n    ${replies} replies, ${mentionLines} mentionLines` );
    }
}

for ( const line of summary ) {
    console.log( line );
}
for ( const e of errors ) {
    console.log( `ERROR   ${e}` );
}
console.log( `\nNote pools: ${presentFiles.length} files checked. ${errors.length} errors.` );
process.exit( errors.length ? 1 : 0 );
