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
    Computes distance-to-campus statistics from the grid-cell output of
    Report_SampleDataCalibration_4_DistanceToCampus.sql. All distance math
    happens here, on the developer's machine, not on the church's server.

    Usage: node distance-from-cells.js <church-label> <cells.tsv> <output.tsv>

    Input: the part 4 grid exported to Excel and converted with xlsx-to-tsv.ps1
    (or copied from SSMS with headers). Output: "Section<TAB>Json" lines that
    normalize.js reads like any other part.

    Only summary statistics are printed. Cell coordinates never leave this script.
*/

const fs = require( "fs" );

const [ , , churchLabel, inputPath, outputPath ] = process.argv;
if ( !churchLabel || !inputPath || !outputPath ) {
    console.error( "Usage: node distance-from-cells.js <church-label> <cells.tsv> <output.tsv>" );
    process.exit( 1 );
}

// #region Input

/**
 * Reads the grid into objects keyed by normalized column name (for example "rowtype").
 */
function readRows( file ) {
    const lines = fs.readFileSync( file, "utf8" ).replace( /^﻿/, "" ).split( /\r?\n/ );
    const headerIndex = lines.findIndex( l => /latitude/i.test( l ) && /families/i.test( l ) );
    if ( headerIndex < 0 ) {
        throw new Error( "Could not find the header row (expected Latitude and Families columns)." );
    }
    const headers = lines[ headerIndex ].split( "\t" ).map( h => h.replace( /\s+/g, "" ).toLowerCase() );
    return lines.slice( headerIndex + 1 ).filter( l => l.trim() ).map( l => {
        const cells = l.split( "\t" );
        return Object.fromEntries( headers.map( ( h, i ) => [ h, cells[ i ] === undefined ? "" : cells[ i ].trim() ] ) );
    } );
}

const rows = readRows( inputPath );
const num = v => v === "" || v === undefined ? null : Number( v );
const campuses = rows.filter( r => r.rowtype === "campus" ).map( r => ( { number: num( r.campusnumber ), lat: num( r.latitude ), lng: num( r.longitude ) } ) );
const cells = rows.filter( r => r.rowtype === "cell" ).map( r => ( {
    campus: num( r.campusnumber ), lat: num( r.latitude ), lng: num( r.longitude ),
    all: num( r.families ) || 0, attending: num( r.attendingfamilies ) || 0, giving: num( r.givingfamilies ) || 0
} ) );
const coverageRow = rows.find( r => r.rowtype === "coverage" );
const coverage = coverageRow && coverageRow.detail ? JSON.parse( coverageRow.detail ) : {};
const suppressedRow = rows.find( r => r.rowtype === "suppressed" );
const suppressed = suppressedRow ? num( suppressedRow.families ) || 0 : 0;

if ( !campuses.length || !cells.length ) {
    throw new Error( `Found ${campuses.length} campuses and ${cells.length} cells. Both are required.` );
}

// #endregion Input

// #region Math

/**
 * Great-circle distance in miles (haversine).
 */
function miles( aLat, aLng, bLat, bLng ) {
    const r = 3958.8;
    const rad = d => d * Math.PI / 180;
    const dLat = rad( bLat - aLat ), dLng = rad( bLng - aLng );
    const h = Math.sin( dLat / 2 ) ** 2 + Math.cos( rad( aLat ) ) * Math.cos( rad( bLat ) ) * Math.sin( dLng / 2 ) ** 2;
    return 2 * r * Math.asin( Math.sqrt( h ) );
}

const round = ( n, d = 1 ) => n == null ? null : Math.round( n * 10 ** d ) / 10 ** d;

/**
 * Nearest-rank percentile over weighted values.
 */
function weightedPercentile( items, p ) {
    const sorted = items.filter( i => i.w > 0 ).sort( ( a, b ) => a.v - b.v );
    const total = sorted.reduce( ( t, i ) => t + i.w, 0 );
    if ( !total ) {
        return null;
    }
    let running = 0;
    for ( const i of sorted ) {
        running += i.w;
        if ( running >= p * total ) {
            return round( i.v );
        }
    }
    return round( sorted[ sorted.length - 1 ].v );
}

const BANDS = [ [ 1, "00-01" ], [ 2, "01-02" ], [ 3, "02-03" ], [ 5, "03-05" ], [ 7, "05-07" ], [ 10, "07-10" ], [ 15, "10-15" ], [ 20, "15-20" ], [ 30, "20-30" ], [ 50, "30-50" ], [ Infinity, "50+" ] ];
const band = m => BANDS.find( b => m < b[ 0 ] )[ 1 ];

// #endregion Math

// Distance from each cell to its own campus and to every campus.
const measured = cells.map( c => {
    const all = campuses.map( k => ( { number: k.number, d: miles( c.lat, c.lng, k.lat, k.lng ) } ) ).sort( ( a, b ) => a.d - b.d );
    const own = all.find( k => k.number === c.campus );
    return { ...c, miles: own.d, nearest: all[ 0 ].d, rank: all.findIndex( k => k.number === c.campus ) + 1 };
} );

const SEGMENTS = { All: "all", Attending: "attending", Giving: "giving" };
const sections = {};

sections.meta = { church: churchLabel, part: 4, periodStart: coverage.periodStart, periodEndExclusive: coverage.periodEndExclusive };

const pctOf = ( part, whole ) => whole ? round( part * 100 / whole ) : null;
sections.distanceCoverage = {
    geocodedCampusRange: campuses.length <= 1 ? "1" : campuses.length <= 4 ? "2-4" : campuses.length <= 9 ? "5-9" : "10+",
    pctFamiliesWithCampus: pctOf( coverage.withCampus, coverage.activeFamilies ),
    pctFamiliesWithGeocodedHome: pctOf( coverage.withGeocodedHome, coverage.activeFamilies ),
    pctFamiliesMeasured: pctOf( measured.reduce( ( t, c ) => t + c.all, 0 ), coverage.activeFamilies ),
    pctMeasurableFamiliesSuppressed: pctOf( suppressed, coverage.measurable )
};

sections.distancePercentiles = Object.entries( SEGMENTS ).map( ( [ name, key ] ) => {
    const items = measured.map( c => ( { v: c.miles, w: c[ key ] } ) );
    return { segment: name, p10: weightedPercentile( items, 0.10 ), p25: weightedPercentile( items, 0.25 ), p50: weightedPercentile( items, 0.50 ), p75: weightedPercentile( items, 0.75 ), p90: weightedPercentile( items, 0.90 ), p95: weightedPercentile( items, 0.95 ) };
} );

sections.distanceBands = Object.entries( SEGMENTS ).flatMap( ( [ name, key ] ) => {
    const total = measured.reduce( ( t, c ) => t + c[ key ], 0 );
    const byBand = {};
    measured.forEach( c => { byBand[ band( c.miles ) ] = ( byBand[ band( c.miles ) ] || 0 ) + c[ key ]; } );
    return Object.keys( byBand ).sort().map( b => ( { segment: name, miles: b, pctFamilies: pctOf( byBand[ b ], total ) } ) );
} );

sections.nearestCampus = Object.entries( SEGMENTS ).map( ( [ name, key ] ) => {
    const total = measured.reduce( ( t, c ) => t + c[ key ], 0 );
    const share = f => pctOf( measured.filter( f ).reduce( ( t, c ) => t + c[ key ], 0 ), total );
    const extra = measured.filter( c => c.rank > 1 ).map( c => ( { v: c.miles - c.nearest, w: c[ key ] } ) );
    return {
        segment: name,
        pctAtNearestCampus: share( c => c.rank === 1 ), pctAtSecondNearest: share( c => c.rank === 2 ), pctAtThirdOrFarther: share( c => c.rank >= 3 ),
        extraMilesWhenNotNearestP50: weightedPercentile( extra, 0.5 ), extraMilesWhenNotNearestP90: weightedPercentile( extra, 0.9 )
    };
} );

// Campus 1 is the largest campus by families.
const attendingTotal = measured.reduce( ( t, c ) => t + c.attending, 0 );
sections.distanceByCampusKind = [ [ "Largest campus", c => c.campus === 1 ], [ "Other campuses", c => c.campus !== 1 ] ].map( ( [ kind, f ] ) => {
    const items = measured.filter( f ).map( c => ( { v: c.miles, w: c.attending } ) );
    return { campusKind: kind, pctOfFamilies: pctOf( items.reduce( ( t, i ) => t + i.w, 0 ), attendingTotal ), p50: weightedPercentile( items, 0.5 ), p75: weightedPercentile( items, 0.75 ), p90: weightedPercentile( items, 0.9 ) };
} ).filter( k => k.pctOfFamilies );

const spacing = campuses.length > 1 ? campuses.map( a => Math.min( ...campuses.filter( b => b !== a ).map( b => miles( a.lat, a.lng, b.lat, b.lng ) ) ) ).sort( ( a, b ) => a - b ) : [];
sections.campusSpacing = spacing.length ? { minMiles: round( spacing[ 0 ] ), medianMiles: round( spacing[ Math.ceil( spacing.length * 0.5 ) - 1 ] ), maxMiles: round( spacing[ spacing.length - 1 ] ) } : {};

fs.writeFileSync( outputPath, Object.entries( sections ).map( ( [ k, v ] ) => `${k}\t${JSON.stringify( v )}` ).join( "\n" ) + "\n" );
console.log( `Wrote ${outputPath}` );
console.log( `Campuses: ${campuses.length}. Cells: ${cells.length}. Measured families: ${sections.distanceCoverage.pctFamiliesMeasured}% of active.` );
console.log( `Median distance (attending): ${sections.distancePercentiles[ 1 ].p50} miles. At nearest campus (attending): ${sections.nearestCampus[ 1 ].pctAtNearestCampus}%.` );
