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
    Checks the prayer text pools against the rules in README.md.

    Usage: node content/prayer/check-pools.js [--names]

    Errors (exit code 1): a category with weight above 0 has no pool, a pool file has no
    category, an entry is outside its length range, an entry uses a forbidden character
    or word, a verse is cited without WEB or KJV, or two entries in a pool share their
    first five words.

    --names lists capitalized words that are not on the allowed list, for a person to check
    that no entry names a person.
*/

const fs = require( "fs" );
const path = require( "path" );

const root = path.join( __dirname, "..", ".." );
const showNames = process.argv.includes( "--names" );

const LENGTHS = {
    requests: [ 40, 450 ],
    details: [ 50, 260 ],
    answers: [ 30, 220 ],
    team: [ 15, 200 ],
    requesterReplies: [ 20, 220 ],
    closings: [ 10, 80 ]
};

// Dashes, curly quotes, the section sign, tabs, and emoji.
const FORBIDDEN_CHARACTERS = /[–—‘’“”§\t]|\p{Extended_Pictographic}/u;

// Politics, self-harm, and words that suggest a real place or organization.
const FORBIDDEN_WORDS = /\b(democrats?|republicans?|liberals?|conservatives?|election|elections|ballot|vote|voting|candidates?|congress|senate|senators?|president|governor|mayor|abortion|immigration|suicid\w*|self-harm|kill (myself|himself|herself|themselves)|overdos\w*)\b/i;

// Capitalized words that are fine anywhere in a sentence.
const ALLOWED_CAPITALS = new Set( ( "I I'm I've I'll I'd God God's Jesus Jesus' Christ Lord Lord's Holy Spirit Father Savior Heavenly Bible Scripture Psalm Psalms Proverbs Isaiah Jeremiah Philippians Romans Matthew Mark Luke John Corinthians Hebrews James Peter Ephesians Lamentations Deuteronomy Joshua Nehemiah Timothy Colossians Thessalonians Galatians Exodus Genesis Revelation WEB KJV " +
    "Phoenix Arizona Tucson Flagstaff Mesa Tempe Chandler Gilbert Peoria Scottsdale Goodyear Glendale Valley Ohio Texas California Colorado Michigan Minnesota Oregon Washington Utah Nevada Mexico Guatemala Honduras Kenya Uganda Haiti Philippines India Thailand Peru Ecuador Nicaragua Africa Asia Europe America American " +
    "Monday Tuesday Wednesday Thursday Friday Saturday Sunday January February March April May June July August September October November December Christmas Easter Thanksgiving " +
    "ICU NICU ER MRI CT OK PT AA Mom Dad Grandma Grandpa Mother's Father's Day Dr" ).split( " " ) );

// #region Functions

/**
 * Reads a JSON file relative to the repository root.
 */
function readJson( relative ) {
    return JSON.parse( fs.readFileSync( path.join( root, relative ), "utf8" ) );
}

/**
 * The first five words of an entry, lowercased, for spotting entries that open the same way.
 */
function opening( text ) {
    return text.toLowerCase().replace( /[^a-z0-9' ]/g, " " ).split( /\s+/ ).filter( Boolean ).slice( 0, 5 ).join( " " );
}

/**
 * Capitalized words that do not start a sentence and are not on the allowed list.
 */
function unexpectedCapitals( text ) {
    const found = [];
    const words = text.split( /\s+/ );
    for ( let i = 1; i < words.length; i++ ) {
        const startsSentence = /[.!?:"(]$/.test( words[ i - 1 ] );
        const word = words[ i ].replace( /^[("']+|[.,!?;:)"']+$/g, "" );
        if ( !startsSentence && /^[A-Z]/.test( word ) && !ALLOWED_CAPITALS.has( word ) ) {
            found.push( word );
        }
    }
    return found;
}

/**
 * The median of a list of numbers.
 */
function median( values ) {
    const sorted = [ ...values ].sort( ( a, b ) => a - b );
    const middle = Math.floor( sorted.length / 2 );
    return sorted.length % 2 ? sorted[ middle ] : Math.round( ( sorted[ middle - 1 ] + sorted[ middle ] ) / 2 );
}

// #endregion Functions

const errors = [];
const capitals = new Map();
const rows = [];

/**
 * Checks one pool (an array of strings) and records its statistics.
 */
function checkPool( file, poolName, entries ) {
    if ( !Array.isArray( entries ) || entries.length === 0 ) {
        errors.push( `${file}: "${poolName}" is missing or empty.` );
        return;
    }

    const [ min, max ] = LENGTHS[ poolName ];
    const openings = new Map();
    entries.forEach( ( text, index ) => {
        const where = `${file} ${poolName}[${index}]`;
        if ( typeof text !== "string" ) {
            errors.push( `${where}: not a string.` );
            return;
        }
        if ( text.length < min || text.length > max ) {
            errors.push( `${where}: ${text.length} characters, outside ${min} to ${max}.` );
        }
        if ( text !== text.trim() || /  /.test( text ) ) {
            errors.push( `${where}: extra spaces.` );
        }
        if ( FORBIDDEN_CHARACTERS.test( text ) ) {
            errors.push( `${where}: forbidden character.` );
        }
        const word = text.match( FORBIDDEN_WORDS );
        if ( word ) {
            errors.push( `${where}: forbidden word "${word[ 0 ]}".` );
        }
        if ( /\b\d+:\d+\b/.test( text ) && !/\b\d+:\d+(-\d+)?,? (WEB|KJV)\)/.test( text ) ) {
            errors.push( `${where}: a verse must be cited with WEB or KJV, like "(Psalm 46:1, WEB)".` );
        }
        const key = opening( text );
        if ( openings.has( key ) ) {
            errors.push( `${where}: opens like entry ${openings.get( key )} ("${key}").` );
        }
        else {
            openings.set( key, index );
        }
        for ( const capital of unexpectedCapitals( text ) ) {
            capitals.set( capital, ( capitals.get( capital ) || 0 ) + 1 );
        }
    } );

    const lengths = entries.filter( e => typeof e === "string" ).map( e => e.length );
    rows.push( { file, pool: poolName, count: entries.length, min: Math.min( ...lengths ), median: median( lengths ), max: Math.max( ...lengths ) } );
}

const configuration = readJson( "configuration/prayer.json" );
const categoryDir = path.join( __dirname, "categories" );
const poolFiles = fs.existsSync( categoryDir ) ? fs.readdirSync( categoryDir ).filter( f => f.endsWith( ".json" ) ) : [];
const keysWithWeight = configuration.categories.filter( c => c.weight > 0 ).map( c => c.key );

for ( const key of keysWithWeight ) {
    if ( !poolFiles.includes( `${key}.json` ) ) {
        errors.push( `categories/${key}.json is missing for a category with weight above 0.` );
    }
}

for ( const file of poolFiles ) {
    const key = file.replace( /\.json$/, "" );
    if ( !keysWithWeight.includes( key ) ) {
        errors.push( `categories/${file} has no category with weight above 0 in configuration/prayer.json.` );
        continue;
    }
    const pool = readJson( `content/prayer/categories/${file}` );
    for ( const poolName of [ "requests", "details", "answers" ] ) {
        checkPool( `categories/${file}`, poolName, pool[ poolName ] );
    }
}

for ( const [ file, pools ] of [ [ "comments.json", [ "team", "requesterReplies" ] ], [ "shared.json", [ "closings" ] ] ] ) {
    if ( !fs.existsSync( path.join( __dirname, file ) ) ) {
        errors.push( `${file} is missing.` );
        continue;
    }
    const pool = readJson( `content/prayer/${file}` );
    for ( const poolName of pools ) {
        checkPool( file, poolName, pool[ poolName ] );
    }
}

console.log( "File".padEnd( 44 ) + "Pool".padEnd( 18 ) + "Count".padStart( 6 ) + "Min".padStart( 6 ) + "Median".padStart( 8 ) + "Max".padStart( 6 ) );
for ( const r of rows ) {
    console.log( r.file.padEnd( 44 ) + r.pool.padEnd( 18 ) + String( r.count ).padStart( 6 ) + String( r.min ).padStart( 6 ) + String( r.median ).padStart( 8 ) + String( r.max ).padStart( 6 ) );
}
console.log( `\n${rows.reduce( ( t, r ) => t + r.count, 0 )} entries in ${rows.length} pools.` );

if ( showNames ) {
    const sorted = [ ...capitals.entries() ].sort( ( a, b ) => b[ 1 ] - a[ 1 ] );
    console.log( `\nCapitalized words to check for names (${sorted.length}): ${sorted.map( ( [ w, n ] ) => `${w} (${n})` ).join( ", " ) || "none"}` );
}

if ( errors.length ) {
    console.log( `\n${errors.length} problem(s):` );
    errors.forEach( e => console.log( `  ${e}` ) );
    process.exit( 1 );
}
console.log( "All pools pass." );
