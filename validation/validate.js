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
    Grades generated sample data against a calibration profile.

    Usage: node validate.js <generated.json> [--profile sample-church] [--all]

    <generated.json> is normalize.js output for a freshly loaded sample database:
      dotnet run -- --connection "<conn>" --calibrate raw.tsv          (in ../Builder)
      node ../calibration/normalize.js generated "Generated" raw.tsv --out generated.json

    Every numeric value in the resolved profile that tolerances.json checks is compared
    with the same path in the generated data. A value passes when it is within the rule's
    absolute points (for percentages) or relative share of the target. The worst misses
    are listed first. --all lists every comparison.

    Exit code: 0 when everything checked passes, 1 otherwise.
*/

const fs = require( "fs" );
const path = require( "path" );
const { execFileSync } = require( "child_process" );

const args = process.argv.slice( 2 );
const option = name => {
    const index = args.indexOf( `--${name}` );
    return index >= 0 ? args.splice( index, 2 )[ 1 ] : null;
};
const showAll = args.includes( "--all" ) ? ( args.splice( args.indexOf( "--all" ), 1 ), true ) : false;
const profileName = option( "profile" ) || "sample-church";
const [ generatedPath ] = args;
if ( !generatedPath ) {
    console.error( "Usage: node validate.js <generated.json> [--profile sample-church] [--all]" );
    process.exit( 1 );
}

const calibration = path.join( __dirname, "..", "calibration" );
const resolvedPath = path.join( require( "os" ).tmpdir(), `resolved-${profileName}.json` );
execFileSync( process.execPath, [ path.join( calibration, "resolve-profile.js" ), path.join( calibration, "profiles", `${profileName}.json` ), resolvedPath ], { stdio: "ignore" } );

const profile = JSON.parse( fs.readFileSync( resolvedPath, "utf8" ) );
const generated = JSON.parse( fs.readFileSync( generatedPath, "utf8" ) );
const tolerances = JSON.parse( fs.readFileSync( path.join( __dirname, "tolerances.json" ), "utf8" ) );

// #region Functions

/**
 * True when a dot path matches a pattern, where "*" matches one segment and a pattern matches its own descendants.
 */
function matches( pattern, dotPath ) {
    const p = pattern.split( "." );
    const d = dotPath.split( "." );
    if ( p.length > d.length ) {
        return false;
    }
    return p.every( ( part, i ) => part === "*" || part === d[ i ] );
}

/**
 * The tolerance for a path: the rule with the longest matching path, merged over the default.
 */
function ruleFor( dotPath ) {
    const best = Object.keys( tolerances.rules )
        .filter( key => key !== "default" && matches( key, dotPath ) )
        .sort( ( a, b ) => b.length - a.length )[ 0 ];
    return { ...tolerances.rules.default, ...( best ? tolerances.rules[ best ] : {} ) };
}

/**
 * Yields every numeric leaf of an object as [dotPath, value].
 */
function* leaves( node, prefix ) {
    if ( typeof node === "number" ) {
        yield [ prefix, node ];
        return;
    }
    if ( node && typeof node === "object" && !Array.isArray( node ) ) {
        for ( const [ key, value ] of Object.entries( node ) ) {
            yield* leaves( value, prefix ? `${prefix}.${key}` : key );
        }
    }
}

/**
 * Reads a dot path from an object; undefined when any segment is missing.
 */
function read( node, dotPath ) {
    return dotPath.split( "." ).reduce( ( current, key ) => current == null ? undefined : current[ key ], node );
}

/**
 * Percent-like values are compared in points; everything else relative to the target.
 * A value is a percent when any segment starts with "pct", or when it sits under a "...Mix"
 * and is not an average (cadenceMix.Weekly.avgAnnualTotal is dollars, not a percent).
 */
function isPercent( dotPath ) {
    const segments = dotPath.split( "." );
    const last = segments[ segments.length - 1 ];
    return segments.some( s => /^pct/i.test( s ) ) || ( segments.some( s => /Mix$/.test( s ) ) && !/^avg/i.test( last ) );
}

// #endregion Functions

const results = [];
for ( const [ dotPath, target ] of leaves( profile, "" ) ) {
    if ( !tolerances.checked.some( c => matches( c, dotPath ) ) || tolerances.skipped.some( s => matches( s, dotPath ) ) ) {
        continue;
    }

    const actual = read( generated, dotPath );
    if ( typeof actual !== "number" ) {
        // A small category the generator never produces (for example a rare payment type) is close enough.
        const isSmall = Math.abs( target ) < ( tolerances.missingPassesBelow || 0 );
        results.push( { dotPath, target, actual: null, pass: isSmall, miss: isSmall ? 0 : Infinity, note: isSmall ? "missing, small" : "missing" } );
        continue;
    }

    const rule = ruleFor( dotPath );
    const difference = actual - target;
    const percent = isPercent( dotPath );
    const allowed = percent ? rule.points : Math.abs( target ) * rule.relative;
    const pass = Math.abs( difference ) <= Math.max( allowed, 0.0001 );
    results.push( { dotPath, target, actual, pass, miss: Math.abs( difference ) / Math.max( allowed, 0.0001 ), note: percent ? `±${rule.points} pts` : `±${Math.round( rule.relative * 100 )}%` } );
}

// Summary by top-level section and second level.
const groups = {};
for ( const r of results ) {
    const key = r.dotPath.split( "." ).slice( 0, 2 ).join( "." );
    ( groups[ key ] ||= { pass: 0, fail: 0 } )[ r.pass ? "pass" : "fail" ]++;
}

const format = n => n == null ? "-" : Math.abs( n ) >= 1000 ? Math.round( n ).toLocaleString( "en-US" ) : ( Math.round( n * 100 ) / 100 ).toString();
const passed = results.filter( r => r.pass ).length;
console.log( `Validation of ${path.basename( generatedPath )} against ${profileName}: ${passed} of ${results.length} values within tolerance\n` );
console.log( "Area".padEnd( 42 ) + "Pass  Fail" );
for ( const [ key, g ] of Object.entries( groups ) ) {
    console.log( key.padEnd( 42 ) + String( g.pass ).padStart( 4 ) + String( g.fail ).padStart( 6 ) );
}

const listed = results.filter( r => showAll || !r.pass ).sort( ( a, b ) => b.miss - a.miss );
if ( listed.length ) {
    console.log( `\n${showAll ? "All values" : "Out of tolerance"} (worst first)\n` );
    console.log( "Path".padEnd( 64 ) + "Target".padStart( 12 ) + "Actual".padStart( 12 ) + "  Rule" );
    for ( const r of listed ) {
        console.log( r.dotPath.padEnd( 64 ) + format( r.target ).padStart( 12 ) + format( r.actual ).padStart( 12 ) + `  ${r.note}${r.pass ? "" : " FAIL"}` );
    }
}

process.exit( passed === results.length ? 0 : 1 );
