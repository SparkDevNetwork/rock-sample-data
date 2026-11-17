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
    Checks the content standards (docs/content-standards.md) that a script can check, across the
    whole repository. cast/validate-cast.js checks the Cast's own structure; this checks the rules
    that span folders. Each problem names the rule it breaks. Exits with 1 when any rule fails.

    Usage: node validation/check-standards.js
*/

const fs = require( "fs" );
const path = require( "path" );
const { execFileSync } = require( "child_process" );

const root = path.join( __dirname, ".." );
const EMAIL = /[A-Za-z0-9._%+-]+@([A-Za-z0-9.-]+\.[A-Za-z]{2,})/g;
const EMAIL_DOMAINS = [ "example.org", "rocksolidchurchdemo.com" ];
const DATA_FOLDERS = [ "cast", "configuration", "datasets", "content", "calibration/profiles", "photos" ];

const problems = [];
let checked = 0;

/**
 * Every JSON file under a folder, with its parsed data.
 */
function readJson( folder ) {
    const dir = path.join( root, folder );
    if ( !fs.existsSync( dir ) ) {
        return [];
    }
    return fs.readdirSync( dir, { recursive: true } )
        .filter( f => f.endsWith( ".json" ) )
        .map( f => ( { file: path.join( folder, f ).replace( /\\/g, "/" ), data: JSON.parse( fs.readFileSync( path.join( dir, f ), "utf8" ) ) } ) );
}

/**
 * Every string value in a JSON value, with its path.
 */
function* strings( value, at = "" ) {
    if ( typeof value === "string" ) {
        yield [ at, value ];
    }
    else if ( value && typeof value === "object" ) {
        for ( const [ key, child ] of Object.entries( value ) ) {
            yield* strings( child, at ? `${at}.${key}` : key );
        }
    }
}

// Rule 2 for the area codes in configuration/phones.json: each is a US geographic code, listed once, under a state
// or Washington, D.C. Geographic codes are NXX with a middle digit from 0 to 8. NOT_GEOGRAPHIC holds the rest: N11
// service codes, N00 codes, toll-free codes, personal communications codes, codes held for expansion, and special uses.
const phonesFile = "configuration/phones.json";
const US_STATES = [ "AK", "AL", "AR", "AZ", "CA", "CO", "CT", "DC", "DE", "FL", "GA", "HI", "IA", "ID", "IL", "IN", "KS", "KY", "LA", "MA", "MD", "ME", "MI", "MN", "MO", "MS",
    "MT", "NC", "ND", "NE", "NH", "NJ", "NM", "NV", "NY", "OH", "OK", "OR", "PA", "RI", "SC", "SD", "TN", "TX", "UT", "VA", "VT", "WA", "WI", "WV", "WY" ];
const GEOGRAPHIC = /^[2-9][0-8]\d$/;
const NOT_GEOGRAPHIC = /^(\d11|[2-9]00|8(22|33|44|55|66|77|88)|88\d|5(2[1-9]|33|44|55|66|77|88)|37\d|96\d|456|622|710|950)$/;
const areaCodes = JSON.parse( fs.readFileSync( path.join( root, phonesFile ), "utf8" ) );
const listedAreaCodes = new Map();
function checkAreaCode( code, at ) {
    checked++;
    if ( !GEOGRAPHIC.test( code ) || NOT_GEOGRAPHIC.test( code ) ) {
        problems.push( `Rule 2: ${phonesFile} ${at} lists ${code}, which is not a geographic area code.` );
    }
    if ( listedAreaCodes.has( code ) ) {
        problems.push( `Rule 2: ${phonesFile} lists ${code} under both ${listedAreaCodes.get( code )} and ${at}.` );
    }
    listedAreaCodes.set( code, at );
}
const LOCAL_AREA_CODES = areaCodes.localAreaCodes || [];
if ( !LOCAL_AREA_CODES.length ) {
    problems.push( `Rule 2: ${phonesFile} has no localAreaCodes, which the Cast and the church use.` );
}
for ( const code of LOCAL_AREA_CODES ) {
    checkAreaCode( code, "localAreaCodes" );
}
for ( const [ state, codes ] of Object.entries( areaCodes.otherAreaCodes || {} ) ) {
    if ( !US_STATES.includes( state ) ) {
        problems.push( `Rule 2: ${phonesFile} otherAreaCodes has ${state}, which is not a US state or DC.` );
    }
    for ( const code of codes ) {
        checkAreaCode( code, `otherAreaCodes.${state}` );
    }
}

// The Cast and the church keep the local codes. Only generated people and businesses use the others (decision D-bugs18-1).
const PHONE = new RegExp( `^(${LOCAL_AREA_CODES.join( "|" )})55501\\d\\d$` );
const LOCAL_TEXT = LOCAL_AREA_CODES.length > 1
    ? `${LOCAL_AREA_CODES.slice( 0, -1 ).join( ", " )}, or ${LOCAL_AREA_CODES[ LOCAL_AREA_CODES.length - 1 ]}`
    : LOCAL_AREA_CODES.join( "" );

// Rule 2: phones use the reserved range, and a number is shared only when the story says so:
// within one household, as an office's main line (work phones only), or between duplicate records.
const households = readJson( "cast/households" ).filter( h => h.data.key );
// A duplicate record points at the original with duplicateOf, so records of one person share the original's key.
const original = new Map( households.map( h => [ h.data.key, h.data.duplicateOf || h.data.key ] ) );
const phones = new Map();
for ( const { file, data: h } of households ) {
    for ( const p of h.members || [] ) {
        for ( const [ type, number ] of Object.entries( p.phones || {} ) ) {
            checked++;
            if ( !PHONE.test( number ) ) {
                problems.push( `Rule 2: ${file} (${p.key}) ${type} phone ${number} is not 555-0100 to 555-0199 in ${LOCAL_TEXT}.` );
            }
            if ( !phones.has( number ) ) {
                phones.set( number, [] );
            }
            phones.get( number ).push( { household: h.key, person: p.key, type } );
        }
    }
}
for ( const [ number, uses ] of phones ) {
    const others = uses.filter( u => u.household !== uses[0].household );
    const isOfficeLine = uses.every( u => u.type === "work" );
    const isDuplicateStory = uses.every( u => original.get( u.household ) === original.get( uses[0].household ) );
    if ( others.length && !isOfficeLine && !isDuplicateStory ) {
        problems.push( `Rule 2: ${number} is used by ${uses.map( u => `${u.household}/${u.person} (${u.type})` ).join( ", " )}. Share a number only within a household, as an office line, or between duplicate records.` );
    }
}

// Rule 2 for the church's own texting numbers: each is reserved, unique, and never a Cast person's number,
// so a text in SMS Conversations never seems to come from a church member.
const communicationsFile = "configuration/communications.json";
if ( fs.existsSync( path.join( root, communicationsFile ) ) ) {
    const numbers = JSON.parse( fs.readFileSync( path.join( root, communicationsFile ), "utf8" ) ).phoneNumbers || {};
    const systemNumbers = ( numbers.numbers || [] ).map( n => [ n.key, n.number ] )
        .concat( Object.entries( numbers.campusPastors?.numbers || {} ).map( ( [ campus, n ] ) => [ `campus pastor ${campus}`, n.number ] ) );
    const seen = new Map();
    for ( const [ key, e164 ] of systemNumbers ) {
        checked++;
        const number = String( e164 ).replace( /^\+1/, "" );
        if ( !/^\+1\d{10}$/.test( e164 ) || !PHONE.test( number ) ) {
            problems.push( `Rule 2: ${communicationsFile} ${key} number ${e164} is not +1 and 555-0100 to 555-0199 in ${LOCAL_TEXT}.` );
        }
        if ( phones.has( number ) ) {
            problems.push( `Rule 2: ${communicationsFile} ${key} number ${e164} is a Cast person's number (${phones.get( number ).map( u => `${u.household}/${u.person}` ).join( ", " )}).` );
        }
        if ( seen.has( number ) ) {
            problems.push( `Rule 2: ${communicationsFile} ${key} number ${e164} is also ${seen.get( number )}.` );
        }
        seen.set( number, key );
    }
}

// Rule 3: every email address anywhere in the data uses a reserved domain.
for ( const folder of DATA_FOLDERS ) {
    for ( const { file, data } of readJson( folder ) ) {
        for ( const [ at, text ] of strings( data ) ) {
            for ( const match of text.matchAll( EMAIL ) ) {
                checked++;
                const domain = match[1].toLowerCase();
                if ( !EMAIL_DOMAINS.includes( domain ) ) {
                    problems.push( `Rule 3: ${file} ${at} uses ${match[0]}. Emails use example.org, or rocksolidchurchdemo.com for church staff.` );
                }
            }
        }
    }
}

// Rule 5: raw data never enters git. The raw folders hold only their .gitignore and README.
const tracked = execFileSync( "git", [ "ls-files", "calibration/raw", "addresses/raw" ], { cwd: root, encoding: "utf8" } ).split( "\n" ).filter( f => f );
for ( const file of tracked.filter( f => ![ ".gitignore", "README.md" ].includes( path.basename( f ) ) ) ) {
    problems.push( `Rule 5: ${file} is raw data and must not be committed.` );
}
checked += tracked.length;

// Rule 6: calibration sources are labeled source-1, source-2, and so on, and never described.
const SOURCE = /^source-\d+$/;
for ( const { file, data } of readJson( "calibration/profiles/sources" ) ) {
    checked++;
    const label = path.basename( file, ".json" );
    if ( !SOURCE.test( label ) || data.source !== label ) {
        problems.push( `Rule 6: ${file} must be named source-N.json and labeled with the same source-N.` );
    }
    if ( data.description !== "Calibration source" ) {
        // Never echo the text: the output lands in logs and receipts, and would repeat the description.
        problems.push( `Rule 6: ${file} describes its source. The description is always "Calibration source".` );
    }
}
for ( const { file, data } of readJson( "calibration/profiles" ).filter( p => Array.isArray( p.data.blend ) ) ) {
    for ( const part of data.blend ) {
        checked++;
        if ( !SOURCE.test( part.source ) ) {
            problems.push( `Rule 6: ${file} blends "${part.source}". Sources are referred to only as source-N.` );
        }
    }
}

for ( const problem of problems ) {
    console.log( `ERROR   ${problem}` );
}
console.log( `\nContent standards: ${checked} values checked against rules 2, 3, 5, and 6. ${problems.length} problems.` );
process.exit( problems.length ? 1 : 0 );
