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

// Checks configuration/reporting.json and the categories it files under (docs/reporting.md): unique data view keys and
// Guids across every configuration file, real categories and security roles, views that point only at views that exist
// and never loop, steps filters that name real programs, stages, and step types, signal filters that name real signal
// types, persisted views with one real schedule,
// the consumer rule (a view built on a locked view sits under the same lock), the Sample Data guard on every flow
// audience in its category, run history overrides that make sense, metrics on a stage view with a metric schedule,
// access rows that name Cast people and real items, and the personalization segments in adaptive-messages.json, each on
// a real person view and locked like it. Run: node validation/check-reporting.js

const fs = require( "fs" );
const path = require( "path" );

const root = path.join( __dirname, ".." );
const read = file => JSON.parse( fs.readFileSync( path.join( root, file ), "utf8" ) );
const problems = [];

const reporting = read( "configuration/reporting.json" );
const categories = read( "configuration/categories.json" ).categories;
const roles = new Set( read( "configuration/security-roles.json" ).roles.map( r => r.rockRole ) );
const programs = read( "configuration/steps.json" ).programs;
const signalTypes = new Set( read( "configuration/signals.json" ).types.map( t => t.key ) );
const guidPattern = /^[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}$/;

// Categories: keys, parents, and security roles.
const categoryByKey = new Map( categories.map( c => [ c.key, c ] ) );
for ( const category of categories ) {
    if ( category.parent && !categoryByKey.has( category.parent ) ) {
        problems.push( `category ${category.key}: parent ${category.parent} is not a category` );
    }

    if ( category.rockShips && ( category.parent || category.parentGuid ) ) {
        problems.push( `category ${category.key}: a category Rock ships keeps Rock's place in the tree, so it has no parent here` );
    }

    if ( category.parent && category.parentGuid ) {
        problems.push( `category ${category.key}: name a parent by key or a Rock category by parentGuid, not both` );
    }

    for ( const rule of category.security || [] ) {
        for ( const role of rule.allow || [] ) {
            if ( !roles.has( role ) ) {
                problems.push( `category ${category.key}: ${rule.action} allows ${role}, which is not a role in configuration/security-roles.json` );
            }
        }
    }
}

// The category whose View rule decides who sees an item filed under this one: the nearest one, walking up, with a View rule.
const viewLock = key => {
    for ( let category = categoryByKey.get( key ); category; category = categoryByKey.get( category.parent ) ) {
        if ( ( category.security || [] ).some( r => r.action === "View" ) ) {
            return category.key;
        }
    }

    return null;
};

// Every data view in every file, for keys and for what a view may point at.
const files = {
    "configuration/reporting.json": reporting.dataViews,
    "configuration/group-syncs.json": read( "configuration/group-syncs.json" ).dataViews,
    "configuration/registrations.json": read( "configuration/registrations.json" ).dataViews
};
const views = new Map();
const guids = new Set();
for ( const [ file, list ] of Object.entries( files ) ) {
    for ( const view of list ) {
        if ( views.has( view.key ) ) {
            problems.push( `${file}: data view key ${view.key} is used twice` );
        }

        if ( !guidPattern.test( view.guid ) || guids.has( view.guid ) ) {
            problems.push( `${file}: data view ${view.key} needs its own upper-case Guid` );
        }

        views.set( view.key, view );
        guids.add( view.guid );
    }
}

const leaves = filter => filter.component ? [ filter ] : ( filter.all || filter.any || filter.none || [] ).flatMap( leaves );
const references = view => leaves( view.filter ).filter( f => f.dataView ).map( f => f.dataView );
const schedules = new Set( ( reporting.namedSchedules || [] ).filter( s => !s.forMetrics ).map( s => s.key ) );
const metricSchedules = new Set( ( reporting.namedSchedules || [] ).filter( s => s.forMetrics ).map( s => s.key ) );
const days = [ "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" ];
for ( const schedule of reporting.namedSchedules || [] ) {
    if ( schedule.weekday != null && !days.includes( schedule.weekday ) ) {
        problems.push( `named schedule ${schedule.key}: ${schedule.weekday} is not a day of the week` );
    }
}

// A run history override: whole days, and a last run no earlier than when it was made.
const checkUsage = ( name, usage ) => {
    if ( !usage ) {
        return;
    }

    const values = [ usage.createdDaysAgo, usage.runs, usage.lastRunDaysAgo ].filter( v => v != null );
    if ( values.some( v => !Number.isInteger( v ) || v < 1 ) || ( usage.createdDaysAgo != null && usage.lastRunDaysAgo != null && usage.lastRunDaysAgo > usage.createdDaysAgo ) ) {
        problems.push( `${name}: its usage needs whole numbers of 1 or more, and a last run no earlier than when it was made` );
    }
};

for ( const view of reporting.dataViews ) {
    const name = `data view ${view.key}`;
    const category = categoryByKey.get( view.category );
    if ( !category || category.entityType !== "Rock.Model.DataView" || category.rockShips ) {
        problems.push( `${name}: category ${view.category} is not one of the church's data view categories` );
    }

    for ( const key of references( view ) ) {
        if ( !views.has( key ) ) {
            problems.push( `${name}: points at ${key}, which is not a data view in any configuration file` );
        } else if ( viewLock( views.get( key ).category ) !== viewLock( view.category ) && viewLock( views.get( key ).category ) !== "church-data-views" ) {
            problems.push( `${name}: is built on ${key}, which only ${viewLock( views.get( key ).category )} can see, so it must sit under the same lock (docs/reporting.md section 1.8)` );
        }
    }

    for ( const leaf of leaves( view.filter ) ) {
        if ( leaf.selection == null && !leaf.dataView && !leaf.steps && !leaf.signalType ) {
            problems.push( `${name}: a ${leaf.component} filter has no selection, data view, steps, or signal type` );
        }

        if ( leaf.signalType && !signalTypes.has( leaf.signalType ) ) {
            problems.push( `${name}: a signal filter names ${leaf.signalType}, which is not a signal type in configuration/signals.json` );
        }

        if ( leaf.steps ) {
            const program = programs.find( p => p.key === leaf.steps.program );
            const types = !program ? [] : program.stepTypes.filter( t => leaf.steps.stepType ? t.key === leaf.steps.stepType : !leaf.steps.stage || t.stage === leaf.steps.stage );
            if ( !program || types.length === 0 ) {
                problems.push( `${name}: a steps filter names ${leaf.steps.program}, ${leaf.steps.stage || leaf.steps.stepType || "every step"}, which matches no step type in configuration/steps.json` );
            }
        }
    }

    if ( view.persisted && ( view.persisted.schedule == null ) === ( view.persisted.intervalMinutes == null ) ) {
        problems.push( `${name}: a persisted view needs a schedule or an interval, not both` );
    }

    if ( view.persisted && view.persisted.schedule && !schedules.has( view.persisted.schedule ) ) {
        problems.push( `${name}: persisted on ${view.persisted.schedule}, which is not a named schedule for persisted views` );
    }

    checkUsage( name, view.usage );

    // A flow audience in Sample Data must carry the guard, so no flow emails a real staff address (question 18).
    if ( view.category === "sample-data-data-views" && view.key !== "reserved-or-no-email" && !references( view ).includes( "reserved-or-no-email" ) ) {
        problems.push( `${name}: a Sample Data audience must include reserved-or-no-email` );
    }
}

// A group sync or registration view may be built on the foundations (slice 6), so its views must exist too.
for ( const [ file, list ] of Object.entries( files ).filter( ( [ file ] ) => file !== "configuration/reporting.json" ) ) {
    for ( const view of list ) {
        for ( const key of references( view ).filter( k => !views.has( k ) ) ) {
            problems.push( `${file}: data view ${view.key} points at ${key}, which is not a data view in any configuration file` );
        }
    }
}

// Reports: a church report category, a real data view, real column kinds, and the consumer rule. A report shows its
// columns to anyone who can view it, so one on a locked view sits under the same lock, except the listed ones, which
// show no locked columns on purpose (docs/reporting.md section 6.4).
const reportExceptions = new Set( [ "first-time-givers-to-thank" ] );
const reportLock = key => viewLock( key ) && viewLock( key ).replace( /-reports$/, "" );
const reports = new Map();
for ( const report of reporting.reports || [] ) {
    const name = `report ${report.key}`;
    if ( reports.has( report.key ) || !guidPattern.test( report.guid ) || guids.has( report.guid ) ) {
        problems.push( `${name}: needs its own key and upper-case Guid` );
    }

    reports.set( report.key, report );
    guids.add( report.guid );
    const category = categoryByKey.get( report.category );
    if ( !category || category.entityType !== "Rock.Model.Report" ) {
        problems.push( `${name}: category ${report.category} is not one of the church's report categories` );
    }

    const view = views.get( report.dataView );
    if ( !view ) {
        problems.push( `${name}: data view ${report.dataView} is not a data view in any configuration file` );
    } else {
        const viewArea = viewLock( view.category ) === "church-data-views" ? null : viewLock( view.category ).replace( /-data-views$/, "" );
        const reportArea = reportLock( report.category ) === "church" ? null : reportLock( report.category );
        if ( viewArea && viewArea !== reportArea && !reportExceptions.has( report.key ) ) {
            problems.push( `${name}: its data view ${view.key} is locked to ${viewArea}, so the report must sit under the same lock` );
        }
    }

    checkUsage( name, report.usage );

    for ( const column of report.columns ) {
        const kinds = [ column.property, column.attribute, column.component, column.lava ].filter( k => k != null );
        if ( kinds.length !== 1 || !column.header ) {
            problems.push( `${name}: column ${column.header || "(no header)"} needs a header and exactly one of property, attribute, component, or lava` );
        }

        if ( column.dataView && !views.has( column.dataView ) ) {
            problems.push( `${name}: column ${column.header} names data view ${column.dataView}, which does not exist` );
        }
    }
}

// A view whose past weeks the loader can replay (loader/loading/ViewHistory.cs): every filter, through the views it
// points at, is an attendance count, a steps filter, group membership, or a filter on a person's fields.
const replayable = new Set( [
    "Rock.Reporting.DataFilter.Person.GroupTypeAttendanceFilter",
    "Rock.Reporting.DataFilter.Person.StepsTakenFilter",
    "Rock.Reporting.DataFilter.Person.InGroupGroupTypeFilter",
    "Rock.Reporting.DataFilter.PropertyFilter"
] );
function canReplay( view, seen = new Set() ) {
    if ( !view || view.transform || seen.has( view.key ) ) {
        return !!view && !view.transform;
    }

    seen.add( view.key );
    return leaves( view.filter ).every( l => l.steps
        || ( l.dataView && [ "Rock.Reporting.DataFilter.OtherDataViewFilter", "Rock.Reporting.DataFilter.NotInOtherDataViewFilter" ].includes( l.component )
            && canReplay( reporting.dataViews.find( v => v.key === l.dataView ), seen ) )
        || ( !l.dataView && replayable.has( l.component ) ) );
}

// Metrics: a metric category, a metric schedule, and either the church's SQL with its weeks of history, or a view of the
// church's whose past weeks the loader can replay.
for ( const metric of reporting.metrics || [] ) {
    const name = `metric ${metric.key}`;
    for ( const guid of [ metric.guid, metric.partitionGuid ] ) {
        if ( !guidPattern.test( guid ) || guids.has( guid ) ) {
            problems.push( `${name}: needs its own upper-case Guids` );
        }

        guids.add( guid );
    }

    const category = categoryByKey.get( metric.category );
    if ( !category || category.entityType !== "Rock.Model.MetricCategory" ) {
        problems.push( `${name}: category ${metric.category} is not a metric category` );
    }

    if ( metric.sourceSql ) {
        if ( metric.dataView || !Array.isArray( metric.sourceSql ) || !metric.sourceSql.every( l => typeof l === "string" ) || !( metric.weeks > 0 ) ) {
            problems.push( `${name}: a SQL metric needs its SQL as a list of lines, weeks of history, and no data view` );
        }
    } else {
        const view = reporting.dataViews.find( v => v.key === metric.dataView );
        if ( !view || !canReplay( view ) ) {
            problems.push( `${name}: ${metric.dataView} is not a reporting view whose past weeks the loader can replay: attendance counts, steps, and a person's fields` );
        }
    }

    if ( !metricSchedules.has( metric.schedule ) ) {
        problems.push( `${name}: ${metric.schedule} is not a named schedule for metrics` );
    }
}

// No loops among the views.
const visiting = new Set();
const done = new Set();
const visit = key => {
    if ( done.has( key ) || !views.has( key ) ) {
        return;
    }

    if ( visiting.has( key ) ) {
        problems.push( `data view ${key} is part of a loop of views that point at each other` );
        return;
    }

    visiting.add( key );
    references( views.get( key ) ).forEach( visit );
    visiting.delete( key );
    done.add( key );
};
[ ...views.keys() ].forEach( visit );

// Access rows: Cast people, real items, and Rock's actions.
const castPeople = new Set();
for ( const file of fs.readdirSync( path.join( root, "cast", "households" ) ).filter( f => f.endsWith( ".json" ) ) ) {
    for ( const member of read( `cast/households/${file}` ).members || [] ) {
        castPeople.add( member.guid );
    }
}

for ( const row of reporting.access || [] ) {
    const name = `access row for ${row.name}`;
    if ( !castPeople.has( row.person ) ) {
        problems.push( `${name}: ${row.person} is not a Cast person` );
    }

    if ( [ row.category, row.dataView, row.report ].filter( k => k != null ).length !== 1 ) {
        problems.push( `${name}: name exactly one category, data view, or report` );
    } else if ( row.category && !categoryByKey.has( row.category ) ) {
        problems.push( `${name}: ${row.category} is not a category` );
    } else if ( row.dataView && !views.has( row.dataView ) ) {
        problems.push( `${name}: ${row.dataView} is not a data view` );
    } else if ( row.report && !reports.has( row.report ) ) {
        problems.push( `${name}: ${row.report} is not a report` );
    }

    if ( ![ "View", "Edit", "Administrate" ].includes( row.action ) ) {
        problems.push( `${name}: ${row.action} is not a Rock action` );
    }
}

// Personalization segments (configuration/adaptive-messages.json): each is a person data view, and the consumer rule
// applies to it as to a report. Rock secures a segment by its own rules only, never by a category, so a segment on a
// locked view carries that lock's View rule itself. Rock's segment Results page lists any segment's people without
// checking the segment, so when any segment is locked, the page is locked to no more than every such lock allows.
const adaptive = read( "configuration/adaptive-messages.json" );
const viewRule = rules => ( rules || [] ).find( r => r.action === "View" );
const allowsNoMore = ( rule, lock ) => rule && rule.denyAllUsers && ( rule.allow || [] ).every( role => ( lock.allow || [] ).includes( role ) );
const segmentKeys = new Set();
const segmentLocks = [];
for ( const segment of adaptive.segments || [] ) {
    const name = `segment ${segment.key}`;
    if ( segmentKeys.has( segment.key ) || !guidPattern.test( segment.guid ) || guids.has( segment.guid ) ) {
        problems.push( `${name}: needs its own key and upper-case Guid` );
    }

    segmentKeys.add( segment.key );
    guids.add( segment.guid );
    for ( const rule of segment.security || [] ) {
        for ( const role of rule.allow || [] ) {
            if ( !roles.has( role ) ) {
                problems.push( `${name}: ${rule.action} allows ${role}, which is not a role in configuration/security-roles.json` );
            }
        }
    }

    const view = views.get( segment.dataView );
    if ( !view || ( view.entityType && view.entityType !== "Rock.Model.Person" ) ) {
        problems.push( `${name}: ${segment.dataView} is not a person data view in any configuration file` );
        continue;
    }

    const lockKey = viewLock( view.category );
    if ( lockKey && lockKey !== "church-data-views" ) {
        const lock = viewRule( categoryByKey.get( lockKey ).security );
        segmentLocks.push( lock );
        if ( !allowsNoMore( viewRule( segment.security ), lock ) ) {
            problems.push( `${name}: its data view ${view.key} is locked to ${lockKey}, so the segment needs a View rule that allows no more than that lock and denies All Users (docs/reporting.md section 1.8)` );
        }
    }
}

if ( segmentLocks.length > 0 && !segmentLocks.every( lock => allowsNoMore( viewRule( adaptive.resultsPage && adaptive.resultsPage.security ), lock ) ) ) {
    problems.push( "adaptive-messages.json: a segment is locked, so resultsPage needs a View rule that allows no more than every segment lock and denies All Users" );
}

for ( const row of adaptive.access || [] ) {
    const name = `adaptive message access row for ${row.name}`;
    if ( !castPeople.has( row.person ) ) {
        problems.push( `${name}: ${row.person} is not a Cast person` );
    }

    if ( [ row.segment, row.page ].filter( k => k != null ).length !== 1 || ( row.segment && !segmentKeys.has( row.segment ) ) || ( row.page && !guidPattern.test( row.page ) ) ) {
        problems.push( `${name}: name exactly one segment of the file, or one page by upper-case Guid` );
    }

    if ( ![ "View", "Edit", "Administrate" ].includes( row.action ) ) {
        problems.push( `${name}: ${row.action} is not a Rock action` );
    }
}

console.log( `Reporting: ${reporting.dataViews.length} data views, ${reports.size} reports, ${( reporting.metrics || [] ).length} metrics, ${( reporting.access || [] ).length} access rows, ${segmentKeys.size} personalization segments, ${categories.length} categories. ${problems.length} problems.` );
for ( const problem of problems ) {
    console.log( `  ${problem}` );
}

process.exit( problems.length > 0 ? 1 : 0 );
