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
    Resolves a calibration profile into the effective target values the builder uses.

    Usage: node resolve-profile.js profiles/sample-church.json [output.json]

    Steps:
      1. Blend the sources by weight. Numbers are weighted averages; other values come from the first source.
         A profile's "sectionBlend" can give one section (for example "distance") its own sources.
      2. Remove excluded paths. A map that loses a key is renormalized to 100 when it was a percentage mix.
      3. Apply overrides.
*/

const fs = require( "fs" );
const path = require( "path" );

const [ , , profilePath, outputPath ] = process.argv;
if ( !profilePath ) {
    console.error( "Usage: node resolve-profile.js <profile.json> [output.json]" );
    process.exit( 1 );
}

const profile = JSON.parse( fs.readFileSync( profilePath, "utf8" ) );
const sourcesDir = path.join( path.dirname( profilePath ), "sources" );
const SKIP = new Set( [ "source", "description", "collected", "period", "notes" ] );

/**
 * Blends values from several sources into one, recursively.
 */
function blend( values, weights ) {
    const present = values.map( ( v, i ) => ( { v, w: weights[ i ] } ) ).filter( x => x.v !== undefined && x.v !== null );
    if ( !present.length ) {
        return null;
    }
    if ( present.every( x => typeof x.v === "number" ) ) {
        const total = present.reduce( ( t, x ) => t + x.w, 0 );
        return Math.round( present.reduce( ( t, x ) => t + x.v * x.w, 0 ) / total * 100 ) / 100;
    }
    if ( present.every( x => typeof x.v === "object" && !Array.isArray( x.v ) ) ) {
        const keys = [ ...new Set( present.flatMap( x => Object.keys( x.v ) ) ) ];
        return Object.fromEntries( keys.map( k => [ k, blend( present.map( x => x.v[ k ] ), present.map( x => x.w ) ) ] ) );
    }
    return present[ 0 ].v;
}

/**
 * Finds the parent object and final key for a dotted path.
 */
function locate( root, dotted ) {
    const parts = dotted.split( "." );
    let node = root;
    for ( const p of parts.slice( 0, -1 ) ) {
        node = node ? node[ p ] : undefined;
    }
    return { parent: node, key: parts[ parts.length - 1 ] };
}

const sectionBlend = profile.sectionBlend || {};
const sourceIds = [ ...new Set( [ ...profile.blend, ...Object.values( sectionBlend ).flat() ].map( b => b.source ) ) ];
const sources = Object.fromEntries( sourceIds.map( id => [ id, JSON.parse( fs.readFileSync( path.join( sourcesDir, `${id}.json` ), "utf8" ) ) ] ) );

// Sections come from the default blend, plus any section given its own sources.
const defaultSections = profile.blend.flatMap( b => Object.keys( sources[ b.source ] ) );
const sections = [ ...new Set( [ ...defaultSections, ...Object.keys( sectionBlend ) ] ) ].filter( k => !SKIP.has( k ) );
const effective = Object.fromEntries( sections.map( k => {
    const recipe = sectionBlend[ k ] || profile.blend;
    return [ k, blend( recipe.map( b => sources[ b.source ][ k ] ), recipe.map( b => b.weight ) ) ];
} ) );

for ( const ex of profile.excluded || [] ) {
    const { parent, key } = locate( effective, ex.path );
    if ( !parent || !( key in parent ) ) {
        console.warn( `Excluded path not found: ${ex.path}` );
        continue;
    }
    delete parent[ key ];

    // Renormalize a percentage mix (an object of numbers or of { pctX } objects) that lost a key.
    const values = Object.values( parent );
    if ( values.length && values.every( v => typeof v === "object" && v !== null ) ) {
        const fields = Object.keys( values[ 0 ] ).filter( f => f.startsWith( "pct" ) );
        for ( const f of fields ) {
            const total = values.reduce( ( t, v ) => t + ( v[ f ] || 0 ), 0 );
            if ( total > 0 ) {
                values.forEach( v => { v[ f ] = Math.round( v[ f ] * 1000 / total ) / 10; } );
            }
        }
    }
}

// Overrides may add sections no source provides, so missing parents are created.
for ( const ov of profile.overrides || [] ) {
    const parts = ov.path.split( "." );
    let node = effective;
    for ( const p of parts.slice( 0, -1 ) ) {
        if ( typeof node[ p ] !== "object" || node[ p ] === null ) {
            node[ p ] = {};
        }
        node = node[ p ];
    }
    node[ parts[ parts.length - 1 ] ] = ov.value;
}

const result = { profile: profile.profile, version: profile.version, sources: profile.blend, sectionSources: sectionBlend, ...effective };
const text = JSON.stringify( result, null, 2 ) + "\n";
if ( outputPath ) {
    fs.writeFileSync( outputPath, text );
    console.log( `Wrote ${outputPath}` );
}
else {
    process.stdout.write( text );
}
