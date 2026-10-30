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
// and never loop, steps filters that name real programs, stages, and step types, persisted views with one real schedule,
// the consumer rule (a view built on a locked view sits under the same lock), the Sample Data guard on every flow
// audience in its category, and access rows that name Cast people and real items. Run: node validation/check-reporting.js

const fs = require( "fs" );
const path = require( "path" );

const root = path.join( __dirname, ".." );
const read = file => JSON.parse( fs.readFileSync( path.join( root, file ), "utf8" ) );
const problems = [];

const reporting = read( "configuration/reporting.json" );
const categories = read( "configuration/categories.json" ).categories;
const roles = new Set( read( "configuration/security-roles.json" ).roles.map( r => r.rockRole ) );
const programs = read( "configuration/steps.json" ).programs;
const guidPattern = /^[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}$/;

// Categories: keys, parents, and security roles.
const categoryByKey = new Map( categories.map( c => [ c.key, c ] ) );
for ( const category of categories ) {
    if ( category.parent && !categoryByKey.has( category.parent ) ) {
        problems.push( `category ${category.key}: parent ${category.parent} is not a category` );
    }

    if ( category.rockShips && category.parent ) {
        problems.push( `category ${category.key}: a category Rock ships keeps Rock's place in the tree, so it has no parent here` );
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
const schedules = new Set( ( reporting.namedSchedules || [] ).map( s => s.key ) );

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
        if ( leaf.selection == null && !leaf.dataView && !leaf.steps ) {
            problems.push( `${name}: a ${leaf.component} filter has no selection, data view, or steps` );
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
        problems.push( `${name}: persisted on ${view.persisted.schedule}, which is not a named schedule` );
    }

    // A flow audience in Sample Data must carry the guard, so no flow emails a real staff address (question 18).
    if ( view.category === "sample-data-data-views" && view.key !== "reserved-or-no-email" && !references( view ).includes( "reserved-or-no-email" ) ) {
        problems.push( `${name}: a Sample Data audience must include reserved-or-no-email` );
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
reporting.dataViews.forEach( v => visit( v.key ) );

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

    if ( ( row.category == null ) === ( row.dataView == null ) ) {
        problems.push( `${name}: name a category or a data view, not both` );
    } else if ( row.category && !categoryByKey.has( row.category ) ) {
        problems.push( `${name}: ${row.category} is not a category` );
    } else if ( row.dataView && !views.has( row.dataView ) ) {
        problems.push( `${name}: ${row.dataView} is not a data view` );
    }

    if ( ![ "View", "Edit", "Administrate" ].includes( row.action ) ) {
        problems.push( `${name}: ${row.action} is not a Rock action` );
    }
}

console.log( `Reporting: ${reporting.dataViews.length} data views, ${( reporting.access || [] ).length} access rows, ${categories.length} categories. ${problems.length} problems.` );
for ( const problem of problems ) {
    console.log( `  ${problem}` );
}

process.exit( problems.length > 0 ? 1 : 0 );
