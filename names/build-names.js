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
    Builds the Crowd's name tables from public-domain name data.

    Usage: node build-names.js [--check]

    Inputs (downloaded into the git-ignored raw/ folder, see README.md):
      raw/ssa/yob1930.txt to yob2024.txt   US Social Security Administration national baby names, one file per birth year.
      raw/census/Names_2010Census.csv      US Census Bureau 2010 surnames.

    Outputs:
      first-names.json   The top 200 first names for each gender and birth decade, weighted by births.
      surnames.json      The top 2,000 surnames, weighted by people.

    It also checks nicknames.json, which is written by hand: every name it lists must be in first-names.json.

    --check builds the tables in memory and fails when the committed files differ, without writing them.
    The tables have no random choices, so a rebuild from the same source files gives the same tables.
*/

const fs = require( "fs" );
const path = require( "path" );

const rawDir = path.join( __dirname, "raw" );
const FIRST_YEAR = 1930;
const LAST_YEAR = 2024;
const NAMES_PER_DECADE = 200;
const SURNAMES = 2000;

/*
    The Census file is all capitals, with apostrophes and spaces taken out. Most names read right as a capital
    and lower case letters, and "MC" names read as "Mc" and a capital. These are the top 2,000 names that need
    their apostrophe, space, or inner capital back.
*/
const SURNAME_SPELLINGS = {
    DASILVA: "Da Silva",
    DEJESUS: "De Jesus",
    DELACRUZ: "De La Cruz",
    DELAROSA: "De La Rosa",
    DELATORRE: "De La Torre",
    DELEON: "De Leon",
    DELOSSANTOS: "De Los Santos",
    DEWITT: "DeWitt",
    LEBLANC: "LeBlanc",
    MACDONALD: "MacDonald",
    OBRIEN: "O'Brien",
    OCONNELL: "O'Connell",
    OCONNOR: "O'Connor",
    ODONNELL: "O'Donnell",
    OHARA: "O'Hara",
    OLEARY: "O'Leary",
    ONEAL: "O'Neal",
    ONEIL: "O'Neil",
    ONEILL: "O'Neill"
};

function main()
{
    const isCheck = process.argv.includes( "--check" );
    const firstNames = buildFirstNames();
    const surnames = buildSurnames();
    checkNicknames( firstNames );

    const outputs = [
        [ "first-names.json", firstNamesJson( firstNames ) ],
        [ "surnames.json", surnamesJson( surnames ) ]
    ];

    let differs = 0;
    for ( const [ file, text ] of outputs )
    {
        const target = path.join( __dirname, file );
        if ( isCheck )
        {
            const committed = fs.existsSync( target ) ? fs.readFileSync( target, "utf8" ).replace( /\r\n/g, "\n" ) : "";
            if ( committed !== text )
            {
                console.error( `${file} differs from what the source data gives. Run node names/build-names.js.` );
                differs++;
            }
        }
        else
        {
            fs.writeFileSync( target, text );
            console.log( `Wrote ${file}` );
        }
    }

    if ( differs > 0 )
    {
        process.exit( 1 );
    }

    if ( isCheck )
    {
        console.log( "first-names.json and surnames.json match the source data." );
    }
}

// #region First names

/**
 * The top first names for each decade and gender, with births summed over the decade's years.
 * @returns {Map<number, {female: [string, number][], male: [string, number][]}>}
 */
function buildFirstNames()
{
    const decades = new Map();
    for ( let year = FIRST_YEAR; year <= LAST_YEAR; year++ )
    {
        const file = path.join( rawDir, "ssa", `yob${year}.txt` );
        if ( !fs.existsSync( file ) )
        {
            throw new Error( `Missing ${path.relative( __dirname, file )}. See names/README.md for the source data.` );
        }

        const decade = Math.floor( year / 10 ) * 10;
        if ( !decades.has( decade ) )
        {
            decades.set( decade, { F: new Map(), M: new Map() } );
        }

        const counts = decades.get( decade );
        for ( const line of fs.readFileSync( file, "utf8" ).split( /\r?\n/ ) )
        {
            if ( !line )
            {
                continue;
            }

            const [ name, sex, births ] = line.split( "," );
            if ( !/^[A-Z][a-z]+$/.test( name ) || ( sex !== "F" && sex !== "M" ) )
            {
                throw new Error( `yob${year}.txt has an unexpected line: ${line}` );
            }

            counts[sex].set( name, ( counts[sex].get( name ) || 0 ) + Number( births ) );
        }
    }

    const top = counts => [ ...counts.entries() ]
        .sort( ( a, b ) => b[1] - a[1] || ( a[0] < b[0] ? -1 : 1 ) )
        .slice( 0, NAMES_PER_DECADE );

    const tables = new Map();
    for ( const [ decade, counts ] of decades )
    {
        tables.set( decade, { female: top( counts.F ), male: top( counts.M ) } );
    }

    return tables;
}

function firstNamesJson( tables )
{
    const lines = [
        "{",
        `  "$comment": "Built by names/build-names.js. Never edit it by hand. The top ${NAMES_PER_DECADE} first names for each gender and birth decade, each with its births in that decade. Source: US Social Security Administration national baby names, births ${FIRST_YEAR} to ${LAST_YEAR}, public domain.",`,
        "  \"decades\": {"
    ];

    const decades = [ ...tables.keys() ].sort();
    decades.forEach( ( decade, d ) =>
    {
        const table = tables.get( decade );
        lines.push( `    "${decade}": {` );
        [ "female", "male" ].forEach( ( gender, g ) =>
        {
            lines.push( `      "${gender}": [` );
            table[gender].forEach( ( [ name, births ], i ) => lines.push( `        [ "${name}", ${births} ]${i < table[gender].length - 1 ? "," : ""}` ) );
            lines.push( `      ]${g === 0 ? "," : ""}` );
        } );
        lines.push( `    }${d < decades.length - 1 ? "," : ""}` );
    } );

    lines.push( "  }", "}" );
    return lines.join( "\n" ) + "\n";
}

// #endregion

// #region Surnames

/**
 * The top surnames by rank, spelled the way people write them.
 * @returns {[string, number][]}
 */
function buildSurnames()
{
    const file = path.join( rawDir, "census", "Names_2010Census.csv" );
    if ( !fs.existsSync( file ) )
    {
        throw new Error( `Missing ${path.relative( __dirname, file )}. See names/README.md for the source data.` );
    }

    const [ header, ...rows ] = fs.readFileSync( file, "utf8" ).split( /\r?\n/ ).filter( Boolean );
    if ( !header.startsWith( "name,rank,count," ) )
    {
        throw new Error( `Names_2010Census.csv has an unexpected header: ${header}` );
    }

    // The last row, ALL OTHER NAMES, has rank 0.
    const ranked = rows.map( r => r.split( "," ) )
        .filter( r => Number( r[1] ) > 0 )
        .sort( ( a, b ) => Number( a[1] ) - Number( b[1] ) )
        .slice( 0, SURNAMES );

    const unused = new Set( Object.keys( SURNAME_SPELLINGS ) );
    const surnames = ranked.map( ( [ name, , count ] ) =>
    {
        if ( !/^[A-Z]+$/.test( name ) )
        {
            throw new Error( `Names_2010Census.csv has an unexpected name: ${name}` );
        }

        unused.delete( name );
        return [ spell( name ), Number( count ) ];
    } );

    if ( unused.size > 0 )
    {
        throw new Error( `SURNAME_SPELLINGS lists names outside the top ${SURNAMES}: ${[ ...unused ].join( ", " )}` );
    }

    return surnames;
}

function spell( name )
{
    if ( SURNAME_SPELLINGS[name] )
    {
        return SURNAME_SPELLINGS[name];
    }

    if ( /^MC[A-Z]/.test( name ) )
    {
        return "Mc" + name[2] + name.slice( 3 ).toLowerCase();
    }

    return name[0] + name.slice( 1 ).toLowerCase();
}

function surnamesJson( surnames )
{
    const lines = [
        "{",
        `  "$comment": "Built by names/build-names.js. Never edit it by hand. The top ${SURNAMES.toLocaleString( "en-US" )} surnames, each with how many people had it. Source: US Census Bureau 2010 surnames, public domain.",`,
        "  \"surnames\": ["
    ];

    surnames.forEach( ( [ name, count ], i ) => lines.push( `    [ "${name}", ${count} ]${i < surnames.length - 1 ? "," : ""}` ) );
    lines.push( "  ]", "}" );
    return lines.join( "\n" ) + "\n";
}

// #endregion

// #region Nicknames

/**
 * Every formal name in nicknames.json is a first name in the tables, and every nickname is a different name.
 */
function checkNicknames( tables )
{
    const file = path.join( __dirname, "nicknames.json" );
    const nicknames = JSON.parse( fs.readFileSync( file, "utf8" ) ).nicknames;
    const known = new Set();
    for ( const table of tables.values() )
    {
        table.female.concat( table.male ).forEach( ( [ name ] ) => known.add( name ) );
    }

    const problems = [];
    for ( const [ name, shorts ] of Object.entries( nicknames ) )
    {
        if ( !known.has( name ) )
        {
            problems.push( `${name} is not a first name in any decade's top ${NAMES_PER_DECADE}` );
        }

        if ( !Array.isArray( shorts ) || shorts.length === 0 || new Set( shorts ).size !== shorts.length )
        {
            problems.push( `${name} needs a list of different nicknames` );
            continue;
        }

        for ( const short of shorts )
        {
            if ( !/^[A-Z][a-z]+$/.test( short ) || short === name )
            {
                problems.push( `${name}: ${short} must be a different name in letters only` );
            }
        }
    }

    if ( problems.length > 0 )
    {
        throw new Error( `nicknames.json:\n  ${problems.join( "\n  " )}` );
    }
}

// #endregion

main();
