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
    Builds the address pool: real Phoenix-area streets with house numbers that do not exist.

    Usage: node build-pool.js [--cast]

    Inputs (downloaded into the git-ignored raw/ folder, see README.md):
      raw/tiger/tl_2025_04013_addrfeat.*   Census TIGER/Line address ranges for Maricopa County.
      raw/county/                          Cached county address point queries (filled on demand).

    Output:
      pool.json    Addresses by campus, placed with the profile's distance-to-campus pattern.

    --cast also gives addresses to Cast households: legacy addresses (real homes) move to an
    unused number on the same street, and new households get a pool address near the main campus.

    Every random choice is seeded, so a rerun gives the same pool.
*/

const fs = require( "fs" );
const path = require( "path" );
const https = require( "https" );
const { execFileSync } = require( "child_process" );

const root = path.join( __dirname, ".." );
const rawDir = path.join( __dirname, "raw" );
const countyDir = path.join( rawDir, "county" );
const tigerBase = path.join( rawDir, "tiger", "tl_2025_04013_addrfeat" );
const COUNTY_URL = "https://services6.arcgis.com/clPWQMwZfdWn4MQZ/ArcGIS/rest/services/Maricopa_County_Address_Points/FeatureServer/0/query";

const POOL_PER_CAMPUS = { main: 3600, other: 2400 };
const OVERSAMPLE = 1.7;
// How many made-up numbers one block side may hold: 3 in the first pass, 5 in the top-up pass.
let maxPicksPerSide = 3;
const TOPUP_STREET_BUDGET = 50;
const STREET_BUDGET = 1800;
const MILES_PER_DEGREE_LAT = 69.0;

// #region Seeded random

let seedState = 4012026;
const rnd = () => ( seedState = ( seedState * 1103515245 + 12345 ) % 2147483648 ) / 2147483648;
const between = ( a, b ) => a + Math.floor( rnd() * ( b - a + 1 ) );

// #endregion Seeded random

// #region Shapefile reading

/**
 * Reads the chosen columns of every DBF record.
 */
function readDbf( file, columns ) {
    const buf = fs.readFileSync( file );
    const count = buf.readUInt32LE( 4 );
    const headerLength = buf.readUInt16LE( 8 );
    const recordLength = buf.readUInt16LE( 10 );
    const fields = [];
    let offset = 1;
    for ( let o = 32; buf[ o ] !== 0x0D; o += 32 ) {
        const name = buf.toString( "latin1", o, o + 11 ).replace( /\0.*$/, "" );
        const length = buf[ o + 16 ];
        fields.push( { name, start: offset, length } );
        offset += length;
    }
    const wanted = fields.filter( f => columns.includes( f.name ) );
    const rows = new Array( count );
    for ( let i = 0; i < count; i++ ) {
        const base = headerLength + ( i * recordLength );
        const row = {};
        for ( const f of wanted ) {
            row[ f.name ] = buf.toString( "latin1", base + f.start, base + f.start + f.length ).trim();
        }
        rows[ i ] = row;
    }
    return rows;
}

/**
 * Reads every polyline in a SHP file as an array of [longitude, latitude] points (first part only).
 */
function readShp( file ) {
    const buf = fs.readFileSync( file );
    const lines = [];
    let o = 100;
    while ( o < buf.length ) {
        const contentLength = buf.readInt32BE( o + 4 ) * 2;
        const c = o + 8;
        const shapeType = buf.readInt32LE( c );
        if ( shapeType === 0 ) {
            lines.push( [] );
        }
        else {
            const numParts = buf.readInt32LE( c + 36 );
            const numPoints = buf.readInt32LE( c + 40 );
            const pointsStart = c + 44 + ( numParts * 4 );
            const firstPartEnd = numParts > 1 ? buf.readInt32LE( c + 48 ) : numPoints;
            const points = [];
            for ( let i = 0; i < firstPartEnd; i++ ) {
                points.push( [ buf.readDoubleLE( pointsStart + ( i * 16 ) ), buf.readDoubleLE( pointsStart + ( i * 16 ) + 8 ) ] );
            }
            lines.push( points );
        }
        o = c + contentLength;
    }
    return lines;
}

// #endregion Shapefile reading

// #region Geometry

const milesBetween = ( a, b ) => {
    const dLat = ( a.lat - b.lat ) * MILES_PER_DEGREE_LAT;
    const dLon = ( a.lon - b.lon ) * MILES_PER_DEGREE_LAT * Math.cos( a.lat * Math.PI / 180 );
    return Math.sqrt( ( dLat * dLat ) + ( dLon * dLon ) );
};

/**
 * The point a share of the way along a polyline, moved about 12 meters to the given side.
 */
function pointAlong( points, share, side ) {
    const lengths = [];
    let total = 0;
    for ( let i = 1; i < points.length; i++ ) {
        const d = Math.hypot( points[ i ][ 0 ] - points[ i - 1 ][ 0 ], points[ i ][ 1 ] - points[ i - 1 ][ 1 ] );
        lengths.push( d );
        total += d;
    }
    let target = Math.min( Math.max( share, 0.02 ), 0.98 ) * total;
    for ( let i = 1; i < points.length; i++ ) {
        if ( target <= lengths[ i - 1 ] || i === points.length - 1 ) {
            const t = lengths[ i - 1 ] === 0 ? 0 : Math.min( 1, target / lengths[ i - 1 ] );
            const [ x0, y0 ] = points[ i - 1 ];
            const [ x1, y1 ] = points[ i ];
            const dx = x1 - x0;
            const dy = y1 - y0;
            const len = Math.hypot( dx, dy ) || 1;

            // Left of the direction of travel is (-dy, dx); about 12 meters in degrees.
            const offset = 0.00011 * ( side === "L" ? 1 : -1 );
            return { lon: x0 + ( t * dx ) + ( -dy / len * offset ), lat: y0 + ( t * dy ) + ( dx / len * offset ) };
        }
        target -= lengths[ i - 1 ];
    }
    return { lon: points[ 0 ][ 0 ], lat: points[ 0 ][ 1 ] };
}

// #endregion Geometry

// #region Street names

const DIRECTIONS = new Set( [ "N", "S", "E", "W", "NE", "NW", "SE", "SW" ] );
const TYPES = {
    AVE: "AVE", ST: "ST", RD: "RD", DR: "DR", LN: "LN", WAY: "WAY", CT: "CT", PL: "PL", CIR: "CIR", BLVD: "BLVD", PKWY: "PKWY",
    TRL: "TRL", TER: "TER", LP: "LOOP", LOOP: "LOOP", PASS: "PASS", PATH: "PATH", RUN: "RUN", ROW: "ROW", XING: "XING", HWY: "HWY",
    CV: "CV", PT: "PT", TRCE: "TRCE", VW: "VW", WALK: "WALK", SQ: "SQ", GLN: "GLN", HL: "HL", HLS: "HLS", PLZ: "PLZ", ALY: "ALY",

    // Spelled-out forms, as legacy Cast addresses sometimes use them.
    AVENUE: "AVE", STREET: "ST", ROAD: "RD", DRIVE: "DR", LANE: "LN", COURT: "CT", PLACE: "PL", CIRCLE: "CIR", BOULEVARD: "BLVD",
    PARKWAY: "PKWY", TRAIL: "TRL", TERRACE: "TER", HIGHWAY: "HWY"
};

/**
 * Splits a TIGER full name ("N 31st Ln") into direction, name, and type, uppercased like the county data.
 */
function parseStreet( fullName ) {
    const tokens = fullName.toUpperCase().split( /\s+/ );
    const dir = DIRECTIONS.has( tokens[ 0 ] ) && tokens.length > 1 ? tokens.shift() : "";
    const type = tokens.length > 1 && TYPES[ tokens[ tokens.length - 1 ] ] ? TYPES[ tokens.pop() ] : "";
    return { dir, name: tokens.join( " " ), type };
}

const titleCase = s => s.toLowerCase().replace( /\b([a-z])/g, m => m.toUpperCase() ).replace( /\b(\d+)(St|Nd|Rd|Th)\b/g, ( m, n, sfx ) => n + sfx.toLowerCase() );

// #endregion Street names

// #region County address points

/**
 * Fetches a URL as JSON, with retries.
 */
function getJson( url, attempt = 1 ) {
    return new Promise( ( resolve, reject ) => {
        https.get( url, { headers: { "User-Agent": "Rock RMS sample data builder" } }, res => {
            let body = "";
            res.on( "data", d => body += d );
            res.on( "end", () => {
                try {
                    const json = JSON.parse( body );
                    if ( json.error ) {
                        throw new Error( JSON.stringify( json.error ) );
                    }
                    resolve( json );
                }
                catch ( e ) {
                    if ( attempt < 4 ) {
                        setTimeout( () => getJson( url, attempt + 1 ).then( resolve, reject ), 1500 * attempt );
                    }
                    else {
                        reject( e );
                    }
                }
            } );
        } ).on( "error", e => attempt < 4 ? setTimeout( () => getJson( url, attempt + 1 ).then( resolve, reject ), 1500 * attempt ) : reject( e ) );
    } );
}

/**
 * Every county address point on a street name within a ZIP code, cached in raw/county/.
 */
async function countyPoints( name, zip ) {
    const file = path.join( countyDir, `${zip}-${name.replace( /[^A-Z0-9]+/g, "_" )}.json` );
    if ( fs.existsSync( file ) ) {
        return JSON.parse( fs.readFileSync( file, "utf8" ) );
    }
    const points = [];
    for ( let offset = 0; ; offset += 2000 ) {
        const params = new URLSearchParams( {
            where: `StName='${name.replace( /'/g, "''" )}' AND Zip='${zip}'`,
            outFields: "HseNo,StDir,StType,USPSCityAlias,AddressType",
            returnGeometry: "false",
            resultOffset: String( offset ),
            resultRecordCount: "2000",
            f: "json"
        } );
        const json = await getJson( `${COUNTY_URL}?${params}` );
        for ( const f of json.features || [] ) {
            const a = f.attributes;
            points.push( [ parseInt( a.HseNo, 10 ), a.StDir || "", a.StType || "", a.USPSCityAlias || "", a.AddressType || "" ] );
        }
        if ( !json.exceededTransferLimit ) {
            break;
        }
    }
    fs.writeFileSync( file, JSON.stringify( points ) );
    return points;
}

/**
 * Runs county lookups a few at a time, so the public service is not hammered.
 */
async function fetchAll( keys, concurrency ) {
    let next = 0;
    let done = 0;
    const results = new Map();
    const worker = async () => {
        while ( next < keys.length ) {
            const key = keys[ next++ ];
            const [ zip, name ] = key.split( "|" );
            results.set( key, await countyPoints( name, zip ) );
            if ( ++done % 250 === 0 ) {
                console.log( `  county lookups: ${done} of ${keys.length}` );
            }
        }
    };
    await Promise.all( Array.from( { length: concurrency }, worker ) );
    return results;
}

// #endregion County address points

// #region Segments

/**
 * Loads street segment sides that have a house number range.
 */
function loadSides() {
    console.log( "Reading Census street ranges..." );
    const rows = readDbf( `${tigerBase}.dbf`, [ "FULLNAME", "LFROMHN", "LTOHN", "RFROMHN", "RTOHN", "ZIPL", "ZIPR", "ROAD_MTFCC" ] );
    const lines = readShp( `${tigerBase}.shp` );
    const sides = [];
    for ( let i = 0; i < rows.length; i++ ) {
        const r = rows[ i ];
        if ( !r.FULLNAME || lines[ i ].length < 2 ) {
            continue;
        }
        for ( const s of [ "L", "R" ] ) {
            const from = parseInt( r[ `${s}FROMHN` ], 10 );
            const to = parseInt( r[ `${s}TOHN` ], 10 );
            const zip = r[ `ZIP${s}` ];
            if ( !Number.isFinite( from ) || !Number.isFinite( to ) || !zip || from === to ) {
                continue;
            }
            const mid = lines[ i ][ Math.floor( lines[ i ].length / 2 ) ];
            sides.push( { id: sides.length, fullName: r.FULLNAME, street: parseStreet( r.FULLNAME ), from, to, zip, side: s, mtfcc: r.ROAD_MTFCC, line: lines[ i ], lat: mid[ 1 ], lon: mid[ 0 ] } );
        }
    }
    return sides;
}

/**
 * A coarse grid of residential segment sides, for nearest-street lookups.
 */
function gridIndex( sides ) {
    const grid = new Map();
    const add = list => {
        for ( const s of list ) {
            const k = `${Math.floor( s.lat * 100 )}|${Math.floor( s.lon * 100 )}`;
            if ( !grid.has( k ) ) {
                grid.set( k, [] );
            }
            grid.get( k ).push( s );
        }
    };
    add( sides );
    return {
        add,

        /**
         * The nearest side to a point, optionally only among sides the filter accepts.
         */
        nearest( lat, lon, accept ) {
            let best = null;
            let bestDistance = Infinity;
            const gy = Math.floor( lat * 100 );
            const gx = Math.floor( lon * 100 );
            for ( let dy = -1; dy <= 1; dy++ ) {
                for ( let dx = -1; dx <= 1; dx++ ) {
                    for ( const s of grid.get( `${gy + dy}|${gx + dx}` ) || [] ) {
                        if ( accept && !accept( s ) ) {
                            continue;
                        }
                        const d = ( ( s.lat - lat ) ** 2 ) + ( ( ( s.lon - lon ) * Math.cos( lat * Math.PI / 180 ) ) ** 2 );
                        if ( d < bestDistance ) {
                            bestDistance = d;
                            best = s;
                        }
                    }
                }
            }
            return best;
        }
    };
}

// #endregion Segments

// #region Placement

/**
 * A distance in miles from the profile's percentile curve.
 */
function sampleMiles( percentiles ) {
    const curve = [ [ 0, 0.3 ], [ 0.10, percentiles.p10 ], [ 0.25, percentiles.p25 ], [ 0.50, percentiles.p50 ], [ 0.75, percentiles.p75 ], [ 0.90, percentiles.p90 ], [ 0.95, percentiles.p95 ], [ 1, percentiles.max ] ];
    const u = rnd();
    for ( let i = 1; i < curve.length; i++ ) {
        if ( u <= curve[ i ][ 0 ] ) {
            const share = ( u - curve[ i - 1 ][ 0 ] ) / ( curve[ i ][ 0 ] - curve[ i - 1 ][ 0 ] );
            return curve[ i - 1 ][ 1 ] + ( share * ( curve[ i ][ 1 ] - curve[ i - 1 ][ 1 ] ) );
        }
    }
    return percentiles.max;
}

/*
    10/15/26 - CLAUDE

    Each new street costs one query to the county's public address service. To keep the
    number of queries small, a pick first looks for a block on a street already chosen
    within half a mile, and only opens a new street while the campus is under its street
    budget. The distance pattern holds, because every pick still starts from a point drawn
    from the profile's distance curve.

    Reason: Be polite to a public service; about 1,800 lookups instead of 8,600.
*/

/**
 * Tracks the streets (street name plus ZIP) chosen so far, so later picks can reuse them.
 */
function streetTracker( sides ) {
    const byKey = new Map();
    for ( const s of sides ) {
        const key = `${s.zip}|${s.street.name}`;
        if ( !byKey.has( key ) ) {
            byKey.set( key, [] );
        }
        byKey.get( key ).push( s );
    }
    return { byKey, chosen: new Set(), grid: gridIndex( [] ) };
}

/**
 * Picks candidate street sides for one campus, following the distance pattern and the
 * share of families who live closer to another campus.
 */
function pickSides( campus, campuses, count, index, distance, usage, streets, streetBudget, acceptSide ) {
    const multiplier = campus.isMain ? distance.largestCampusDistanceMultiplier || 1 : 1;
    const allowedElsewhere = 1 - ( distance.nearestCampus.pctAtNearest / 100 );
    const picks = [];
    let elsewhere = 0;
    let opened = 0;
    for ( let tries = 0; picks.length < count && tries < count * 40; tries++ ) {
        const miles = sampleMiles( distance.milesPercentiles ) * multiplier;
        const angle = rnd() * Math.PI * 2;
        const lat = campus.lat + ( miles / MILES_PER_DEGREE_LAT * Math.sin( angle ) );
        const lon = campus.lon + ( miles / ( MILES_PER_DEGREE_LAT * Math.cos( campus.lat * Math.PI / 180 ) ) * Math.cos( angle ) );

        let side = streets ? streets.grid.nearest( lat, lon, s => ( usage.get( s.id ) || 0 ) < maxPicksPerSide && ( !acceptSide || acceptSide( s ) ) ) : null;
        if ( !side || milesBetween( side, { lat, lon } ) > 0.5 ) {
            side = index.nearest( lat, lon );
            if ( side && streets ) {
                const key = `${side.zip}|${side.street.name}`;
                if ( !streets.chosen.has( key ) ) {
                    if ( opened >= streetBudget ) {
                        continue;
                    }
                    streets.chosen.add( key );
                    streets.grid.add( streets.byKey.get( key ) );
                    opened++;
                }
            }
        }
        if ( !side || milesBetween( side, { lat, lon } ) > 0.75 || ( usage.get( side.id ) || 0 ) >= maxPicksPerSide ) {
            continue;
        }
        const nearest = campuses.reduce( ( a, b ) => milesBetween( side, a ) <= milesBetween( side, b ) ? a : b );
        const isElsewhere = nearest.key !== campus.key;
        if ( isElsewhere && elsewhere >= allowedElsewhere * count ) {
            continue;
        }
        elsewhere += isElsewhere ? 1 : 0;
        usage.set( side.id, ( usage.get( side.id ) || 0 ) + 1 );
        picks.push( side );
    }
    return picks;
}

/**
 * The county points for one street side: those on the same street, in its range.
 */
function existingOnSide( side, points ) {
    const lo = Math.min( side.from, side.to );
    const hi = Math.max( side.from, side.to );
    return points.filter( p => p[ 0 ] >= lo && p[ 0 ] <= hi && ( !side.street.dir || p[ 1 ] === side.street.dir ) && ( !side.street.type || p[ 2 ] === side.street.type ) );
}

/**
 * Chooses an unused house number with the side's parity, away from every real number.
 * Returns null when the side has no room.
 */
function unusedNumber( side, streetPoints, taken, near ) {
    const lo = Math.min( side.from, side.to );
    const hi = Math.max( side.from, side.to );

    // Skip every real number on any street with this name in this ZIP, whatever its direction or type,
    // so "2512 E Riviera Dr" is never made up next to a real "2512 W Riviera Dr".
    const real = new Set( streetPoints.map( p => p[ 0 ] ) );
    const candidates = [];
    for ( let n = lo; n <= hi; n++ ) {
        if ( n % 2 !== lo % 2 || real.has( n ) || taken.has( `${side.zip}|${side.fullName}|${n}` ) ) {
            continue;
        }
        candidates.push( n );
    }
    if ( !candidates.length ) {
        return null;
    }
    if ( near != null ) {
        candidates.sort( ( a, b ) => Math.abs( a - near ) - Math.abs( b - near ) );
        return candidates[ 0 ];
    }
    return candidates[ Math.floor( rnd() * candidates.length ) ];
}

/**
 * Turns a side and a house number into an address record.
 */
function toAddress( side, number, existing ) {
    const cities = {};
    for ( const p of existing ) {
        cities[ p[ 3 ] ] = ( cities[ p[ 3 ] ] || 0 ) + 1;
    }
    const city = Object.entries( cities ).sort( ( a, b ) => b[ 1 ] - a[ 1 ] )[ 0 ][ 0 ];
    const share = ( number - side.from ) / ( side.to - side.from );
    const point = pointAlong( side.line, share, side.side );
    return { street1: `${number} ${side.fullName}`, city: titleCase( city ), state: "AZ", postalCode: side.zip, latitude: +point.lat.toFixed( 6 ), longitude: +point.lon.toFixed( 6 ) };
}

// #endregion Placement

// #region Cast

/**
 * Gives Cast households addresses: legacy real homes move to an unused number on the same street,
 * and households without an address get one near the main campus.
 */
async function assignCast( sides, index, mainCampus, distance, taken, streets ) {
    const castUsage = new Map();
    const castDir = path.join( root, "cast", "households" );
    const files = fs.readdirSync( castDir ).filter( f => f.endsWith( ".json" ) );
    const moved = new Map();
    const report = [];

    // Shared on purpose: the three duplicate Jon Smith records. Every other shared legacy address is split.
    const keepShared = new Set( [ "smith-jon", "smith-john-duplicate-a", "smith-john-duplicate-b" ] );

    for ( const f of files ) {
        const file = path.join( castDir, f );
        const text = fs.readFileSync( file, "utf8" );
        const h = JSON.parse( text );
        if ( h.members.every( m => m.isDeceased ) ) {
            continue;
        }

        let address = null;
        if ( h.address ) {
            const original = `${h.address.street1}|${h.address.postalCode}`;
            const match = h.address.street1.match( /^(\d+)\s+(.+)$/ );
            if ( !match ) {
                continue;
            }
            const number = parseInt( match[ 1 ], 10 );
            const street = parseStreet( match[ 2 ] );
            const points = await countyPoints( street.name, h.address.postalCode );
            const isReal = points.some( p => p[ 0 ] === number && ( !street.dir || p[ 1 ] === street.dir ) && ( !street.type || p[ 2 ] === street.type ) );
            if ( !isReal ) {
                report.push( `${h.key}: ${h.address.street1} is not a real address point; left unchanged.` );
                continue;
            }
            if ( keepShared.has( h.key ) && moved.has( original ) ) {
                address = moved.get( original );
            }
            else {
                // Same street means same name, direction, and type: N 31st Dr is not N 31st Ave.
                const sameStreet = s => s.zip === h.address.postalCode && s.street.name === street.name && s.street.dir === street.dir && ( !street.type || s.street.type === street.type ) && s.from % 2 === number % 2;
                const side = sides.find( s => sameStreet( s ) && number >= Math.min( s.from, s.to ) && number <= Math.max( s.from, s.to ) )
                    || sides.filter( sameStreet ).sort( ( a, b ) => Math.abs( Math.min( a.from, a.to ) - number ) - Math.abs( Math.min( b.from, b.to ) - number ) )[ 0 ];
                if ( !side ) {
                    report.push( `${h.key}: no Census street found for ${h.address.street1}; left unchanged.` );
                    continue;
                }
                const existing = existingOnSide( side, points );
                const fake = unusedNumber( side, points, taken, number );
                if ( fake == null ) {
                    report.push( `${h.key}: no unused number on ${side.fullName}; left unchanged.` );
                    continue;
                }
                taken.add( `${side.zip}|${side.fullName}|${fake}` );
                address = { type: h.address.type, ...toAddress( side, fake, existing.length ? existing : points ) };
                if ( keepShared.has( h.key ) ) {
                    moved.set( original, address );
                }
            }
            report.push( `${h.key}: ${h.address.street1} -> ${address.street1}` );
        }
        else {
            // A new household: a pool-style address near the main campus.
            for ( let tries = 0; !address && tries < 200; tries++ ) {
                // Reuse streets already looked up for the pool, so Cast placement adds few new queries.
                const [ side ] = pickSides( mainCampus, [ mainCampus ], 1, index, distance, castUsage, streets, 2 );
                if ( !side ) {
                    continue;
                }
                const points = await countyPoints( side.street.name, side.zip );
                const existing = existingOnSide( side, points );
                if ( existing.filter( p => p[ 4 ] === "RES" ).length < 3 ) {
                    continue;
                }
                const fake = unusedNumber( side, points, taken, null );
                if ( fake == null ) {
                    continue;
                }
                taken.add( `${side.zip}|${side.fullName}|${fake}` );
                address = { type: "home", ...toAddress( side, fake, existing ) };
            }
            report.push( `${h.key}: new address ${address ? address.street1 : "(none found)"}` );
        }

        if ( address ) {
            // Keep the property order of the file: address goes right after testPurposes / reviewNotes.
            const ordered = {};
            for ( const [ k, v ] of Object.entries( h ) ) {
                if ( k === "address" ) {
                    continue;
                }
                if ( k === "attendance" || ( k === "members" && !ordered.address ) ) {
                    ordered.address = address;
                }
                ordered[ k ] = v;
            }
            let out = JSON.stringify( ordered, null, 2 ) + "\n";
            if ( text.includes( "\r\n" ) ) {
                out = out.replace( /\n/g, "\r\n" );
            }
            fs.writeFileSync( file, out );
        }
    }
    return report;
}

// #endregion Cast

// #region Main

( async () => {
    fs.mkdirSync( countyDir, { recursive: true } );
    const doCast = process.argv.includes( "--cast" );

    const resolved = path.join( rawDir, "resolved-profile.json" );
    execFileSync( process.execPath, [ path.join( root, "calibration", "resolve-profile.js" ), path.join( root, "calibration", "profiles", "sample-church.json" ), resolved ], { stdio: "ignore" } );
    const distance = JSON.parse( fs.readFileSync( resolved, "utf8" ) ).distance;

    const campuses = JSON.parse( fs.readFileSync( path.join( root, "configuration", "campuses.json" ), "utf8" ) ).campuses
        .map( c => ( { key: c.key, isMain: !!c.rockDefault, lat: c.location.latitude, lon: c.location.longitude } ) );
    const mainCampus = campuses.find( c => c.isMain );

    const sides = loadSides();
    const residential = sides.filter( s => s.mtfcc === "S1400" );
    const index = gridIndex( residential );
    console.log( `  ${sides.length} street sides, ${residential.length} on local streets` );

    // Pick candidates for every campus first, then look up only the streets they use.
    const usage = new Map();
    const streets = streetTracker( residential );
    const totalTarget = campuses.reduce( ( sum, c ) => sum + ( c.isMain ? POOL_PER_CAMPUS.main : POOL_PER_CAMPUS.other ), 0 );
    const candidates = new Map();
    for ( const campus of campuses ) {
        const target = campus.isMain ? POOL_PER_CAMPUS.main : POOL_PER_CAMPUS.other;
        const budget = Math.round( STREET_BUDGET * target / totalTarget );
        candidates.set( campus.key, pickSides( campus, campuses, Math.ceil( target * OVERSAMPLE ), index, distance, usage, streets, budget ) );
    }
    const keys = [ ...new Set( [ ...candidates.values() ].flat().map( s => `${s.zip}|${s.street.name}` ) ) ];
    if ( process.argv.includes( "--count" ) ) {
        console.log( `Would look up ${keys.length} streets.` );
        return;
    }
    console.log( `Looking up ${keys.length} streets in the county address points (cached in raw/county)...` );
    const lookups = await fetchAll( keys, 4 );

    const taken = new Set();
    const pool = {};
    const targetFor = campus => campus.isMain ? POOL_PER_CAMPUS.main : POOL_PER_CAMPUS.other;

    // Turns candidate sides into addresses until the campus reaches its target.
    const fill = ( campus, list, candidateSides ) => {
        for ( const side of candidateSides ) {
            if ( list.length >= targetFor( campus ) ) {
                break;
            }
            const existing = existingOnSide( side, lookups.get( `${side.zip}|${side.street.name}` ) );

            // Only streets with real homes on this block count as residential.
            if ( existing.filter( p => p[ 4 ] === "RES" ).length < 3 ) {
                continue;
            }
            const number = unusedNumber( side, lookups.get( `${side.zip}|${side.street.name}` ), taken, null );
            if ( number == null ) {
                continue;
            }
            taken.add( `${side.zip}|${side.fullName}|${number}` );
            const a = toAddress( side, number, existing );
            list.push( [ a.street1, a.city, a.postalCode, a.latitude, a.longitude ] );
        }
    };

    for ( const campus of campuses ) {
        pool[ campus.key ] = [];
        fill( campus, pool[ campus.key ], candidates.get( campus.key ) );
    }

    /*
        Top-up pass for campuses that came up short (some streets turn out to have no homes).
        It prefers blocks already known to be residential, allows more made-up numbers per
        block, and opens only a few new streets.
    */
    maxPicksPerSide = 5;
    const isResidentialSide = s => {
        const points = lookups.get( `${s.zip}|${s.street.name}` );
        return !points || existingOnSide( s, points ).filter( p => p[ 4 ] === "RES" ).length >= 3;
    };
    const topUp = new Map();
    for ( const campus of campuses ) {
        const deficit = targetFor( campus ) - pool[ campus.key ].length;
        if ( deficit > 0 ) {
            topUp.set( campus.key, pickSides( campus, campuses, Math.ceil( deficit * 2.5 ), index, distance, usage, streets, TOPUP_STREET_BUDGET, isResidentialSide ) );
        }
    }
    const newKeys = [ ...new Set( [ ...topUp.values() ].flat().map( s => `${s.zip}|${s.street.name}` ) ) ].filter( k => !lookups.has( k ) );
    console.log( `Top-up: looking up ${newKeys.length} more streets...` );
    for ( const [ k, v ] of await fetchAll( newKeys, 4 ) ) {
        lookups.set( k, v );
    }
    for ( const campus of campuses ) {
        if ( topUp.has( campus.key ) ) {
            fill( campus, pool[ campus.key ], topUp.get( campus.key ) );
        }
        console.log( `  ${campus.key}: ${pool[ campus.key ].length} of ${targetFor( campus )}` );
    }

    // One address per line keeps the file readable and its diffs small.
    const lines = [ "{", `  "$comment": "Real streets with house numbers that do not exist, placed with the profile's distance-to-campus pattern. Built by build-pool.js from Census TIGER/Line ranges and county address points. State is always AZ.",`, `  "columns": [ "street1", "city", "postalCode", "latitude", "longitude" ],`, `  "campuses": {` ];
    const campusKeys = Object.keys( pool );
    campusKeys.forEach( ( key, i ) => {
        lines.push( `    "${key}": [` );
        pool[ key ].forEach( ( row, j ) => lines.push( `      ${JSON.stringify( row )}${j < pool[ key ].length - 1 ? "," : ""}` ) );
        lines.push( `    ]${i < campusKeys.length - 1 ? "," : ""}` );
    } );
    lines.push( "  }", "}" );
    fs.writeFileSync( path.join( __dirname, "pool.json" ), lines.join( "\n" ) + "\n" );
    console.log( "Wrote pool.json" );

    if ( doCast ) {
        console.log( "Assigning Cast addresses..." );
        for ( const line of await assignCast( sides, index, mainCampus, distance, taken, streets ) ) {
            console.log( `  ${line}` );
        }
    }
} )().catch( e => {
    console.error( e );
    process.exit( 1 );
} );

// #endregion Main
