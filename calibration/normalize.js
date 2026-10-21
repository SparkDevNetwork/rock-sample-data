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
    Turns raw calibration script output into an anonymous, normalized source file.

    Usage: node normalize.js <source-id> "<description>" <raw-file> [<raw-file> ...] [--out <file>]

    Raw files are either:
      - .json: the single JSON document from Report_SampleDataCalibrationStats.sql, or
      - .tsv/.txt: "Section<TAB>Json" lines from parts 1 to 3, 5, and 6 (see xlsx-to-tsv.ps1).

    Output: profiles/sources/<source-id>.json, or the --out file (validation uses this).

    The output holds ratios only. Church-specific labels (group type names,
    custom connection statuses, custom source types, URLs) never leave this
    script. Absolute totals are kept only where a ratio needs a denominator.
*/

const fs = require( "fs" );
const path = require( "path" );

const args = process.argv.slice( 2 );
const outIndex = args.indexOf( "--out" );
const outOverride = outIndex >= 0 ? args.splice( outIndex, 2 )[ 1 ] : null;
const [ sourceId, description, ...files ] = args;
if ( !sourceId || !description || !files.length ) {
    console.error( "Usage: node normalize.js <source-id> \"<description>\" <raw-file> [<raw-file> ...]" );
    process.exit( 1 );
}

// #region Input

/**
 * Reads every raw file into one map of section name to parsed JSON.
 */
function readSections( paths ) {
    const sections = {};
    for ( const file of paths ) {
        const text = fs.readFileSync( file, "utf8" ).replace( /^﻿/, "" );
        if ( file.toLowerCase().endsWith( ".json" ) ) {
            Object.assign( sections, JSON.parse( text ) );
            continue;
        }
        for ( const line of text.split( /\r?\n/ ) ) {
            const tab = line.indexOf( "\t" );
            if ( tab < 0 ) {
                continue;
            }
            const json = line.substring( tab + 1 ).trim();
            if ( json.startsWith( "{" ) || json.startsWith( "[" ) ) {
                sections[ line.substring( 0, tab ).trim() ] = JSON.parse( json );
            }
        }
    }

    // Part 3 labels its communication sections with the 3 month window.
    sections.communicationSummary ||= sections.communicationSummaryRecipientsLast3Months;
    sections.communicationsByMedium ||= sections.communicationsByMediumLast3Months;
    return sections;
}

const s = readSections( files );
const list = v => v == null ? [] : Array.isArray( v ) ? v : [ v ];
const round = ( n, d = 1 ) => n == null || Number.isNaN( n ) ? null : Math.round( n * 10 ** d ) / 10 ** d;
const pct = ( part, whole ) => whole ? round( part * 100 / whole ) : null;
const sum = a => a.reduce( ( t, x ) => t + x, 0 );
const median = a => {
    const v = [ ...a ].sort( ( x, y ) => x - y );
    const m = Math.floor( v.length / 2 );
    return v.length % 2 ? v[ m ] : ( v[ m - 1 ] + v[ m ] ) / 2;
};

// #endregion Input

// #region Calendar

const day = ( y, m, d ) => new Date( Date.UTC( y, m - 1, d ) );
const iso = dt => dt.toISOString().substring( 0, 10 );
const addDays = ( dt, n ) => new Date( dt.getTime() + n * 86400000 );

/**
 * Rock's SundayDate: the Sunday on or after the given date.
 */
const sundayDate = dt => addDays( dt, ( 7 - dt.getUTCDay() ) % 7 );

/**
 * The nth given weekday of a month (n = -1 means the last one).
 */
function nthWeekday( y, m, weekday, n ) {
    if ( n > 0 ) {
        const first = day( y, m, 1 );
        return addDays( first, ( weekday - first.getUTCDay() + 7 ) % 7 + ( n - 1 ) * 7 );
    }
    const last = day( y, m + 1, 0 );
    return addDays( last, -( ( last.getUTCDay() - weekday + 7 ) % 7 ) );
}

/**
 * Western Easter (anonymous Gregorian algorithm).
 */
function easter( y ) {
    const a = y % 19, b = Math.floor( y / 100 ), c = y % 100, d = Math.floor( b / 4 ), e = b % 4;
    const f = Math.floor( ( b + 8 ) / 25 ), g = Math.floor( ( b - f + 1 ) / 3 ), h = ( 19 * a + b - d - g + 15 ) % 30;
    const i = Math.floor( c / 4 ), k = c % 4, l = ( 32 + 2 * e + 2 * i - h - k ) % 7, m = Math.floor( ( a + 11 * h + 22 * l ) / 451 );
    return day( y, Math.floor( ( h + l - 7 * m + 114 ) / 31 ), ( ( h + l - 7 * m + 114 ) % 31 ) + 1 );
}

/**
 * The weekend (as a Rock SundayDate) affected by each holiday in a year.
 */
function holidayWeekends( y ) {
    const july4 = day( y, 7, 4 );
    const july4Weekend = july4.getUTCDay() === 0 ? july4 : july4.getUTCDay() <= 3 ? addDays( july4, -july4.getUTCDay() ) : sundayDate( july4 );
    return {
        easter: easter( y ),
        mothersDay: nthWeekday( y, 5, 0, 2 ),
        memorialDay: addDays( nthWeekday( y, 5, 1, -1 ), -1 ),
        july4: july4Weekend,
        laborDay: addDays( nthWeekday( y, 9, 1, 1 ), -1 ),
        thanksgiving: addDays( nthWeekday( y, 11, 4, 4 ), 3 ),
        sundayBeforeChristmas: addDays( sundayDate( day( y, 12, 24 ) ), -7 ),
        christmasWeek: sundayDate( day( y, 12, 24 ) ),
        newYear: sundayDate( day( y + 1, 1, 1 ) )
    };
}

// #endregion Calendar

const out = {
    source: sourceId,
    description,
    collected: new Date().toISOString().substring( 0, 7 ),
    period: s.meta ? { start: s.meta.periodStart, endExclusive: s.meta.periodEndExclusive } : { start: s.periodStart, endExclusive: s.periodEndExclusive },
    notes: []
};

// #region People

if ( s.peopleSummary ) {
    const ps = s.peopleSummary;
    const ages = list( s.ageGender );
    const known = ages.filter( a => a.ageBucket !== "99 Unknown" );
    const knownTotal = sum( known.map( a => a.male + a.female + a.unknown ) );
    const allTotal = sum( ages.map( a => a.male + a.female + a.unknown ) );
    const families = list( s.familySize );
    const familyTotal = sum( families.map( f => f.families ) );
    const marital = list( s.maritalStatus );
    const maritalKnown = marital.filter( m => m.maritalStatus !== "None" );
    const newPeople = list( s.newPeopleByMonth );

    out.people = {
        pctAdults: ps.pctAdults,
        pctChildren: ps.pctChildren,
        ageMix: Object.fromEntries( known.map( a => [ a.ageBucket, pct( a.male + a.female + a.unknown, knownTotal ) ] ) ),
        pctFemaleByAge: Object.fromEntries( known.map( a => [ a.ageBucket, pct( a.female, a.male + a.female ) ] ) ),
        pctNoBirthDate: pct( allTotal - knownTotal, allTotal ),
        familySizeMix: Object.fromEntries( families.map( f => [ f.familySize, { pctFamilies: pct( f.families, familyTotal ), avgAdults: f.avgAdults, avgChildren: f.avgChildren } ] ) ),
        adultMaritalMix: Object.fromEntries( maritalKnown.map( m => [ m.maritalStatus.toLowerCase(), pct( m.adults, sum( maritalKnown.map( x => x.adults ) ) ) ] ) ),
        pctAdultsNoMaritalStatus: pct( sum( marital.filter( m => m.maritalStatus === "None" ).map( m => m.adults ) ), sum( marital.map( m => m.adults ) ) ),
        completeness: Object.fromEntries( list( s.dataCompleteness ).map( c => [ c.ageClassification.toLowerCase(), Object.fromEntries( Object.entries( c ).filter( ( [ k ] ) => k.startsWith( "pct" ) ) ) ] ) ),
        newPeoplePerMonthPctOfActive: round( newPeople.length ? sum( newPeople.map( n => n.newPeople ) ) / newPeople.length * 100 / ps.activePeople : null, 2 ),
        newPeopleMonthIndex: monthIndex( newPeople.map( n => [ n.month, n.newPeople ] ) )
    };
}

/**
 * Average value per calendar month divided by the overall average (1.0 = a normal month).
 */
function monthIndex( pairs ) {
    if ( !pairs.length ) {
        return null;
    }
    const overall = sum( pairs.map( p => p[ 1 ] ) ) / pairs.length;
    const byMonth = {};
    for ( const [ month, value ] of pairs ) {
        ( byMonth[ month.substring( 5, 7 ) ] ||= [] ).push( value );
    }
    return Object.fromEntries( Object.keys( byMonth ).sort().map( m => [ m, round( sum( byMonth[ m ] ) / byMonth[ m ].length / overall, 2 ) ] ) );
}

// #endregion People

// #region Giving

if ( s.givingSummary ) {
    const g = s.givingSummary;
    const months = list( s.givingByMonth );
    const lastYear = list( s.paymentMix ).filter( p => p.period === "Last 12" );
    const priorYear = list( s.paymentMix ).filter( p => p.period === "Prior 12" );
    // Rock has renamed a few currency types over the years. Use one name for each, so sources and generated data line up.
    const currencyNames = { "Non-Cash Asset": "Non-Cash", "Android Pay": "Google Pay" };
    const mixOf = rows => Object.fromEntries( rows.map( p => [ currencyNames[ p.currencyType ] || p.currencyType, { pctGifts: p.pctGifts, pctDollars: p.pctDollars } ] ) );

    // Rock's standard source types keep their names; anything custom becomes "other".
    const standardSources = [ "Website", "Kiosk", "Mobile Application", "On-Site", "On-Site Collection", "Bank Checks", "SMS Gift", "Text-to-Give" ];
    const sources = {};
    for ( const row of list( s.sourceMix ) ) {
        const name = standardSources.includes( row.sourceType ) ? row.sourceType : "other";
        sources[ name ] ||= { pctGifts: 0, pctDollars: 0 };
        sources[ name ].pctGifts = round( sources[ name ].pctGifts + row.pctGifts );
        sources[ name ].pctDollars = round( sources[ name ].pctDollars + row.pctDollars );
    }

    out.giving = {
        pctRetainedYearOverYear: pct( g.retainedUnits, g.givingUnitsPrior12 ),
        pctLapsedYearOverYear: pct( g.lapsedUnits, g.givingUnitsPrior12 ),
        pctNewUnitsOfCurrent: pct( g.newUnits, g.givingUnitsLast12 ),
        pctUnitsBusiness: g.pctUnitsBusiness,
        pctDollarsBusiness: g.pctDollarsBusiness,
        pctUnitsWithScheduledGift: g.pctUnitsWithScheduledGift,
        giftsPerUnitPerYear: round( g.giftTransactionsLast12 / g.givingUnitsLast12 ),
        refundsPerThousandGifts: round( g.refundTransactionsLast12 * 1000 / g.giftTransactionsLast12, 2 ),
        pctUnitsWithPledge: g.pctUnitsWithPledge,
        cadenceMix: Object.fromEntries( list( s.givingCadence ).map( c => [ c.cadence.replace( /^\d+\s+/, "" ), {
            pctUnits: c.pctUnits, pctDollars: c.pctDollars, avgGiftDays: c.avgGiftDays, avgAnnualTotal: c.avgAnnualTotal,
            avgAmountVariation: c.avgAmountVariation ?? null, avgPctGiftsAtUsualAmount: c.avgPctGiftsAtUsualAmount, avgPctGapsOnRhythm: c.avgPctGapsOnRhythm ?? null, pctGiftsScheduled: c.pctGiftsScheduled
        } ] ) ),
        unitAnnualTotalPercentiles: s.givingUnitAnnualTotals,
        pctDollarsByDecile: Object.fromEntries( list( s.givingDeciles ).map( d => [ d.decile, d.pctDollars ] ) ),
        pctDollarsTopOnePercent: s.givingTopOnePercent ? s.givingTopOnePercent.pctDollarsTop1PctUnits : null,
        giftAmounts: s.giftAmounts,
        dollarsMonthIndex: monthIndex( months.map( m => [ m.month, m.amount ] ) ),
        giftsMonthIndex: monthIndex( months.map( m => [ m.month, m.gifts ] ) ),
        dayOfWeekMix: Object.fromEntries( list( s.giftDayOfWeek ).map( d => [ [ "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" ][ d.dayOfWeek ], { oneTime: d.pctOneTimeGifts, scheduled: d.pctScheduledGifts } ] ) ),
        scheduledDayOfMonthMix: Object.fromEntries( list( s.scheduledGiftDayOfMonth ).map( d => [ d.dayOfMonth, d.pctScheduledGifts ] ) ),
        paymentMix: { current: mixOf( lastYear ), prior: mixOf( priorYear ) },
        sourceMix: sources,
        fundsPerGiftMix: Object.fromEntries( list( s.fundsPerGift ).map( f => [ f.fundsPerGift, f.pctGifts ] ) )
    };
}

// #endregion Giving

// #region Attendance

if ( s.attendanceWeekly ) {
    const endExclusive = out.period.endExclusive;
    const weeks = list( s.attendanceWeekly ).filter( w => w.weekEnding < endExclusive && w.weekEnding > out.period.start );
    const byWeek = Object.fromEntries( weeks.map( w => [ w.weekEnding, w ] ) );
    const baseKids = median( weeks.map( w => w.checkInChildren ) );
    const baseAdults = median( weeks.map( w => w.checkInAdults ) );
    const baseFirst = median( weeks.map( w => w.firstTimeCheckIns ) );

    // Holiday multipliers, averaged over every year in the window.
    const holidays = {};
    const years = [ ...new Set( weeks.map( w => +w.weekEnding.substring( 0, 4 ) ) ) ];
    for ( const y of [ years[ 0 ] - 1, ...years ] ) {
        for ( const [ name, dt ] of Object.entries( holidayWeekends( y ) ) ) {
            const w = byWeek[ iso( dt ) ];
            if ( w ) {
                ( holidays[ name ] ||= [] ).push( { kids: w.checkInChildren / baseKids, adults: w.checkInAdults / baseAdults, firstTime: w.firstTimeCheckIns / baseFirst } );
            }
        }
    }

    // The fall kickoff: the biggest week between August 10 and September 10.
    for ( const y of years ) {
        const window = weeks.filter( w => w.weekEnding >= `${y}-08-10` && w.weekEnding <= `${y}-09-10` );
        if ( window.length ) {
            const peak = window.reduce( ( a, b ) => b.checkInChildren > a.checkInChildren ? b : a );
            ( holidays.fallKickoff ||= [] ).push( { kids: peak.checkInChildren / baseKids, adults: peak.checkInAdults / baseAdults, firstTime: peak.firstTimeCheckIns / baseFirst } );
        }
    }

    const avg = ( rows, k ) => round( sum( rows.map( r => r[ k ] ) ) / rows.length, 2 );
    const frequency = list( s.checkInFrequency );
    const freqMix = group => {
        const rows = frequency.filter( f => f.ageGroup === group );
        return Object.fromEntries( rows.map( f => [ f.weeksAttendedOf26, pct( f.people, sum( rows.map( r => r.people ) ) ) ] ) );
    };
    const times = list( s.checkInTimes );
    const timeShare = d => round( sum( times.filter( t => t.dayOfWeek === d ).map( t => t.pctCheckIns ) ) );
    const sundayTotal = sum( times.filter( t => t.dayOfWeek === 0 ).map( t => t.pctCheckIns ) );

    // Weeks far below normal that are not holidays (weather closures and similar).
    const holidayDates = new Set( years.flatMap( y => Object.values( holidayWeekends( y ) ).concat( Object.values( holidayWeekends( y - 1 ) ) ) ).map( iso ) );
    const disrupted = weeks.filter( w => !holidayDates.has( w.weekEnding ) && w.checkInChildren < baseKids * 0.7 );

    out.attendance = {
        adultCheckInsPerChildCheckIn: round( baseAdults / baseKids, 2 ),
        firstTimeCheckInsPerHundredChildCheckIns: round( baseFirst * 100 / baseKids, 2 ),
        holidayMultipliers: Object.fromEntries( Object.entries( holidays ).map( ( [ k, rows ] ) => [ k, { kids: avg( rows, "kids" ), adults: avg( rows, "adults" ), firstTime: avg( rows, "firstTime" ), samples: rows.length } ] ) ),
        kidsMonthIndex: monthIndex( weeks.map( w => [ w.weekEnding.substring( 0, 7 ), w.checkInChildren ] ) ),
        disruptedWeeksPerYear: round( disrupted.length / ( weeks.length / 52 ), 1 ),
        childFrequencyMixOf26Weeks: freqMix( "Child" ),
        adultFrequencyMixOf26Weeks: freqMix( "Adult or unknown" ),
        checkInDayMix: { sunday: timeShare( 0 ), saturday: timeShare( 6 ), wednesday: timeShare( 3 ), otherWeekdays: round( 100 - timeShare( 0 ) - timeShare( 6 ) - timeShare( 3 ) ) },
        sundayHourMix: Object.fromEntries( times.filter( t => t.dayOfWeek === 0 ).map( t => [ t.hour, pct( t.pctCheckIns, sundayTotal ) ] ) ),
        kidsPerFamilyCheckInMix: Object.fromEntries( list( s.kidsPerFamilyCheckIn ).map( k => [ k.kidsCheckedIn, k.pctFamilyWeeks ] ) )
    };
}

// #endregion Attendance

// #region Groups and engagement

if ( s.groupTypes ) {
    // Only purpose-tagged group types are used, and only by purpose, never by name.
    const byPurpose = purpose => list( s.groupTypes ).filter( g => g.purpose === purpose && g.takesAttendance );
    const summarize = rows => {
        const groups = sum( rows.map( r => r.activeGroups ) );
        const records = sum( rows.map( r => r.attendanceRecordsLast12 ) );
        return groups ? {
            groupTypes: rows.length,
            weightedMedianSize: round( sum( rows.map( r => r.medianGroupSize * r.activeGroups ) ) / groups ),
            pctLeaders: round( sum( rows.map( r => r.pctLeaders * r.activeMembers ) ) / sum( rows.map( r => r.activeMembers ) ) ),
            pctDidAttend: records ? round( sum( rows.map( r => ( r.pctDidAttend || 0 ) * r.attendanceRecordsLast12 ) ) / records ) : null
        } : null;
    };
    out.groups = { smallGroups: summarize( byPurpose( "Small Group" ) ), servingTeams: summarize( byPurpose( "Serving Area" ) ) };
    out.notes.push( "Group statistics use only group types tagged with the Small Group or Serving Area purpose. Group type names are never stored." );
}

if ( s.engagement ) {
    // Combinations of activity among adults with at least one activity. Custom statuses are ignored.
    const rows = list( s.engagement ).filter( e => e.gives || e.attends || e.inGroup || e.serves );
    const total = sum( rows.map( r => r.adults ) );
    const combos = {};
    for ( const r of rows ) {
        const key = [ r.gives && "gives", r.attends && "attends", r.inGroup && "group", r.serves && "serves" ].filter( Boolean ).join( "+" );
        combos[ key ] = ( combos[ key ] || 0 ) + r.adults;
    }
    const share = f => pct( sum( rows.filter( f ).map( r => r.adults ) ), total );
    const allAdults = sum( list( s.engagement ).map( e => e.adults ) );
    out.engagement = {
        pctAdultRecordsWithAnyActivity: pct( total, allAdults ),
        amongActiveAdults: {
            pctGive: share( r => r.gives ), pctAttend: share( r => r.attends ), pctInGroup: share( r => r.inGroup ), pctServe: share( r => r.serves ),
            combinationMix: Object.fromEntries( Object.entries( combos ).sort( ( a, b ) => b[ 1 ] - a[ 1 ] ).map( ( [ k, v ] ) => [ k, pct( v, total ) ] ) )
        }
    };
    if ( s.newPeopleOutcome ) {
        const n = s.newPeopleOutcome;
        out.engagement.newAdultsAfterOneYear = { pctStillActive: pct( n.stillActive, n.adultsAdded ), pctGave: n.pctGaveLast12, pctAttended: n.pctAttendedLast12 };
    }
}

// #endregion Groups and engagement

// #region Other features

// Yearly volumes are per 1,000 active adults so they scale to any church size.
// The count itself is not written out, because an exact size can identify a church.
const activeAdults = s.engagement ? sum( list( s.engagement ).filter( e => e.gives || e.attends || e.inGroup || e.serves ).map( e => e.adults ) ) : null;
const perThousand = ( count, months ) => activeAdults ? round( count * 12 / months * 1000 / activeAdults ) : null;
const ofActiveAdults = count => activeAdults ? pct( count, activeAdults ) : null;
const without = ( o, ...keys ) => {
    const copy = { ...o };
    keys.forEach( k => delete copy[ k ] );
    return copy;
};

if ( s.registrations ) {
    const r = s.registrations;
    out.registrations = {
        registrantsPerYearPerThousandActiveAdults: perThousand( r.registrants, 12 ),
        avgRegistrantsPerRegistration: r.avgRegistrantsPerRegistration, pctWaitList: r.pctWaitList, pctPaidRegistrants: r.pctPaidRegistrants, pctChildRegistrants: r.pctChildRegistrants,
        registrantsPerInstance: { p25: r.registrantsPerInstanceP25, p50: r.registrantsPerInstanceP50, p90: r.registrantsPerInstanceP90 }
    };
}

if ( s.prayerSummary ) {
    const p = { ...s.prayerSummary };
    const months = list( s.prayerByMonth );
    delete p.requests;
    out.prayer = { requestsPerYearPerThousandActiveAdults: perThousand( s.prayerSummary.requests, Math.max( months.length, 1 ) ), ...p, monthIndex: monthIndex( months.map( m => [ m.month, m.requests ] ) ) };
}

// Part 5 measures how the prayer team works. Its windows are 24 months.
if ( s.prayerDetail && out.prayer ) {
    const requesters = s.prayerRequesters || {};
    const team = s.prayerTeam || {};

    // The share of requests by how many times people prayed for them, as one mix.
    const bands = { 0: "pctPrayerCount0", 1: "pctPrayerCount1", "2-4": "pctPrayerCount2To4", "5-9": "pctPrayerCount5To9", "10-19": "pctPrayerCount10To19", "20+": "pctPrayerCount20Plus" };
    out.prayer.detail = {
        ...without( s.prayerDetail, ...Object.values( bands ) ),
        prayerCountMix: Object.fromEntries( Object.entries( bands ).map( ( [ k, field ] ) => [ k, s.prayerDetail[ field ] ] ) )
    };
    out.prayer.weekdayMix = Object.fromEntries( list( s.prayerByWeekday ).map( w => [ w.weekday, w.pctRequests ] ) );
    out.prayer.hourBandMix = Object.fromEntries( list( s.prayerByHourBand ).map( h => [ h.hourBand, h.pctRequests ] ) );
    out.prayer.categories = s.prayerCategories || null;

    // Ages and genders are shares of requests whose requester's value is known, because missing birth dates are a record quirk.
    const ageBands = { under18: requesters.pctAgeUnder18, "18-29": requesters.pctAge18To29, "30-44": requesters.pctAge30To44, "45-64": requesters.pctAge45To64, "65+": requesters.pctAge65Plus };
    const knownAges = sum( Object.values( ageBands ).map( v => v || 0 ) );
    const knownGenders = ( requesters.pctMale || 0 ) + ( requesters.pctFemale || 0 );
    out.prayer.requesters = {
        pctOfActiveAdultsWhoAskedIn24Months: ofActiveAdults( requesters.requesters ),
        ageMix: knownAges ? Object.fromEntries( Object.entries( ageBands ).map( ( [ k, v ] ) => [ k, pct( v || 0, knownAges ) ] ) ) : null,
        pctFemaleAmongKnown: knownGenders ? pct( requesters.pctFemale || 0, knownGenders ) : null,
        requestsPerRequesterMix: { 1: requesters.pctRequestersWith1Request, "2-3": requesters.pctRequestersWith2To3Requests, "4+": requesters.pctRequestersWith4PlusRequests },
        pctRequestersWhoAlsoPrayed: requesters.pctRequestersWhoAlsoPrayed
    };
    out.prayer.team = {
        prayersPerYearPerThousandActiveAdults: perThousand( team.prayers || 0, 24 ),
        pctOfActiveAdultsWhoPrayedIn24Months: ofActiveAdults( team.prayingPeople ),
        ...without( team, "prayers", "prayingPeople", "prayerComponents" )
    };
    out.prayer.comments = s.prayerComments ? without( s.prayerComments, "comments" ) : null;
}

if ( s.communicationSummary ) {
    const c = s.communicationSummary;
    const months = list( s.communicationsByMonth );
    out.communications = {
        communicationsPerMonthPerThousandActiveAdults: activeAdults && months.length ? round( sum( months.map( m => m.communications ) ) / months.length * 1000 / activeAdults ) : null,
        pctBulk: c.pctBulk,
        recipientsPerCommunication: { p50: c.recipientsP50, p90: c.recipientsP90, p99: c.recipientsP99 },
        byMedium: Object.fromEntries( list( s.communicationsByMedium ).map( m => [ m.medium, { pctOfRecipients: null, pctDelivered: m.pctDelivered, pctOpened: m.pctOpened, pctFailed: m.pctFailed } ] ) )
    };
    const recipients = sum( list( s.communicationsByMedium ).map( m => m.recipients ) );
    for ( const m of list( s.communicationsByMedium ) ) {
        out.communications.byMedium[ m.medium ].pctOfRecipients = pct( m.recipients, recipients );
    }
}

if ( s.connections ) {
    const c = s.connections;
    out.connections = {
        requestsPerYearPerThousandActiveAdults: perThousand( c.requests, 24 ),
        stateMix: { active: c.pctActive, inactive: c.pctInactive, futureFollowUp: c.pctFutureFollowUp, connected: c.pctConnected },
        pctWithConnector: c.pctWithConnector,
        daysToConnect: { p50: c.daysToConnectP50, p90: c.daysToConnectP90 },
        monthIndex: monthIndex( list( s.connectionsByMonth ).map( m => [ m.month, m.requests ] ) )
    };
}

// Part 6 measures notes on person records. Its window is 24 months.
// The section is named personNotes because out.notes holds this file's explanations.
if ( s.noteSummary ) {
    const n = s.noteSummary;
    const authors = s.noteAuthors || {};
    const people = s.notedPeople || {};
    const perThousandActiveAdults = count => activeAdults && count != null ? round( count * 1000 / activeAdults ) : null;

    // Ages and genders are shares of notes whose person's value is known, because missing birth dates are a record quirk.
    const ageBands = { under13: people.pctAgeUnder13, "13-17": people.pctAge13To17, "18-29": people.pctAge18To29, "30-44": people.pctAge30To44, "45-64": people.pctAge45To64, "65+": people.pctAge65Plus };
    const knownAges = sum( Object.values( ageBands ).map( v => v || 0 ) );
    const knownGenders = ( people.pctMale || 0 ) + ( people.pctFemale || 0 );

    out.personNotes = {
        notesPerYearPerThousandActiveAdults: perThousand( n.notes, 24 ),
        notedPeopleIn24MonthsPerThousandActiveAdults: perThousandActiveAdults( n.notedPeople ),
        notedPeopleAllTimePerThousandActiveAdults: perThousandActiveAdults( n.notedPeopleAllTime ),
        pctOfAllTimeNotesInLast24Months: pct( n.notes, n.notesAllTime ),
        pctOnRockTypes: n.pctOnRockTypes ?? null,
        pctImported: n.pctImported ?? null,
        pctWithNoAuthor: n.pctWithNoAuthor ?? null,
        monthIndex: monthIndex( list( s.notesByMonth ).map( m => [ m.month, m.notes ] ) ),
        detail: s.noteDetail || null,
        kindMix: Object.fromEntries( list( s.notesByKind ).map( k => [ k.kind, without( k, "kind" ) ] ) ),
        types: s.noteTypes || null,
        weekdayMix: Object.fromEntries( list( s.notesByWeekday ).map( w => [ w.weekday, w.pctNotes ] ) ),
        hourBandMix: Object.fromEntries( list( s.notesByHourBand ).map( h => [ h.hourBand, h.pctNotes ] ) ),
        authors: {
            authorsPerThousandActiveAdults: perThousandActiveAdults( authors.authors ),
            pctOfStaffWhoWroteNotes: pct( authors.staffAuthors || 0, authors.staff ),
            ...without( authors, "authors", "staffAuthors", "staff" )
        },
        notedPeople: {
            ageMixAmongKnown: knownAges ? Object.fromEntries( Object.entries( ageBands ).map( ( [ k, v ] ) => [ k, pct( v || 0, knownAges ) ] ) ) : null,
            pctFemaleAmongKnown: knownGenders ? pct( people.pctFemale || 0, knownGenders ) : null,
            pctAboutActiveRecords: people.pctAboutActiveRecords ?? null,
            notesPerPersonMix: { 1: people.pctNotedPeopleWith1Note, "2-3": people.pctNotedPeopleWith2To3Notes, "4-9": people.pctNotedPeopleWith4To9Notes, "10+": people.pctNotedPeopleWith10PlusNotes },
            pctWith2PlusKinds: people.pctNotedPeopleWith2PlusKinds ?? null
        }
    };
    out.notes.push( "Person note types are sorted into generic kinds inside part 6. Note type names and note text are never stored." );
}

// #endregion Other features

// #region Distance to campus

if ( s.distancePercentiles ) {
    const bySegment = rows => Object.fromEntries( list( rows ).map( r => [ r.segment.toLowerCase(), r ] ) );
    const percentiles = bySegment( s.distancePercentiles );
    const nearest = bySegment( s.nearestCampus );
    const bands = {};
    for ( const b of list( s.distanceBands ) ) {
        ( bands[ b.segment.toLowerCase() ] ||= {} )[ b.miles ] = b.pctFamilies;
    }
    const strip = r => {
        const copy = { ...r };
        delete copy.segment;
        delete copy.families;
        return copy;
    };

    out.distance = {
        pctFamiliesWithGeocodedHome: s.distanceCoverage ? s.distanceCoverage.pctFamiliesWithGeocodedHome : null,
        milesPercentiles: Object.fromEntries( Object.entries( percentiles ).map( ( [ k, v ] ) => [ k, strip( v ) ] ) ),
        bandMix: bands,
        nearestCampus: Object.fromEntries( Object.entries( nearest ).map( ( [ k, v ] ) => [ k, strip( v ) ] ) ),
        byCampusKind: Object.fromEntries( list( s.distanceByCampusKind ).map( c => [ c.campusKind === "Largest campus" ? "largest" : "others", { pctOfFamilies: c.pctOfFamilies, p50: c.p50, p75: c.p75, p90: c.p90 } ] ) ),
        campusSpacingMiles: s.campusSpacing || null
    };
    out.notes.push( "Distances are straight-line miles between the family's geocoded home and its campus." );
}

// #endregion Distance to campus

const outDir = outOverride ? path.dirname( path.resolve( outOverride ) ) : path.join( __dirname, "profiles", "sources" );
fs.mkdirSync( outDir, { recursive: true } );
const outFile = outOverride ? path.resolve( outOverride ) : path.join( outDir, `${sourceId}.json` );
fs.writeFileSync( outFile, JSON.stringify( out, null, 2 ) + "\n" );
console.log( `Wrote ${path.relative( __dirname, outFile )}` );
