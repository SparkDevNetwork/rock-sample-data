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

// Checks configuration/calendar.json against the files it names and the event photo brief (docs/calendar.md):
// unique keys and Guids, a Ministry by Design stage on every event (Staff events on the Internal calendar only), known calendars, audiences, contacts, and campuses, a brief section for every event,
// and a photo manifest whose entries name real events and files. Run: node validation/check-calendar.js

const fs = require( "fs" );
const path = require( "path" );

const root = path.join( __dirname, ".." );
const read = file => JSON.parse( fs.readFileSync( path.join( root, file ), "utf8" ) );
const problems = [];

const calendar = read( "configuration/calendar.json" );
const campuses = new Set( read( "configuration/campuses.json" ).campuses.map( c => c.key ) );
const guidPattern = /^[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}$/;
const stages = new Set( [ "Belong", "Equip", "Contribute", "Lead", "Staff" ] );
const kinds = new Set( [ "weekly", "monthly", "yearly", "date", "easter", "occasion", "class", "step-season", "seasons", "pinned" ] );

const keys = new Set();
const guids = new Set();
for ( const event of calendar.events ) {
    const name = `event ${event.key}`;
    if ( keys.has( event.key ) ) {
        problems.push( `${name}: the key is used twice` );
    }

    keys.add( event.key );
    if ( !guidPattern.test( event.guid ) || guids.has( event.guid ) ) {
        problems.push( `${name}: the Guid must be uppercase and unique` );
    }

    guids.add( event.guid );
    for ( const key of event.calendars ) {
        if ( !calendar.calendars[key] ) {
            problems.push( `${name}: calendar ${key} is not in 'calendars'` );
        }
    }

    for ( const key of event.audiences ) {
        if ( !calendar.audiences[key] ) {
            problems.push( `${name}: audience ${key} is not in 'audiences'` );
        }
    }

    if ( event.audiences.length === 0 || event.calendars.length === 0 ) {
        problems.push( `${name}: needs at least one calendar and one audience` );
    }

    if ( event.contact !== "campus-pastor" && !calendar.contacts[event.contact] ) {
        problems.push( `${name}: contact ${event.contact} is not in 'contacts'` );
    }

    if ( Array.isArray( event.campuses ) ) {
        for ( const key of event.campuses.filter( c => !campuses.has( c ) ) ) {
            problems.push( `${name}: campus ${key} is not in configuration/campuses.json` );
        }
    }
    else if ( event.campuses !== "all" && event.campuses !== "church-wide" ) {
        problems.push( `${name}: campuses must be "all", "church-wide", or a list` );
    }

    if ( !stages.has( event.stage ) ) {
        problems.push( `${name}: stage ${event.stage} is not Belong, Equip, Contribute, Lead, or Staff` );
    }

    if ( event.stage === "Staff" && event.calendars.includes( "public" ) ) {
        problems.push( `${name}: a Staff event belongs on the Internal calendar only` );
    }

    if ( !kinds.has( event.when?.kind ) ) {
        problems.push( `${name}: 'when' kind ${event.when?.kind} is not one the loader knows` );
    }

    if ( event.span && !event.endTime ) {
        problems.push( `${name}: a span event needs an endTime` );
    }

    if ( !event.summary || event.description.length === 0 ) {
        problems.push( `${name}: needs a summary and a description` );
    }
}

// Every event has a section in the photo brief, under its stage's heading, and every section is an event.
const brief = fs.readFileSync( path.join( root, "photos/event-brief.md" ), "utf8" ).replace( /\r\n/g, "\n" );
const sections = [];
let briefStage = null;
for ( const line of brief.split( "\n" ) ) {
    const stageHeading = line.match( /^### (\w+)$/ );
    const eventHeading = line.match( /^#### .+ \(`([a-z0-9-]+)`\)$/ );
    if ( stageHeading ) {
        briefStage = stageHeading[1];
    }
    else if ( eventHeading ) {
        sections.push( eventHeading[1] );
        const event = calendar.events.find( e => e.key === eventHeading[1] );
        if ( event && event.stage !== briefStage ) {
            problems.push( `photos/event-brief.md lists ${event.key} under ${briefStage}, but its stage is ${event.stage}` );
        }
    }
}
for ( const key of [ ...keys ].filter( k => !sections.includes( k ) ) ) {
    problems.push( `photos/event-brief.md has no section for event ${key}` );
}

for ( const key of sections.filter( s => !keys.has( s ) ) ) {
    problems.push( `photos/event-brief.md has a section for ${key}, which is not an event` );
}

// Every delivered photo names an event and a 2:1 JPEG that exists.
const manifest = read( "photos/events/manifest.json" );
for ( const image of manifest.images ) {
    if ( !keys.has( image.event ) ) {
        problems.push( `photos/events/manifest.json: ${image.event} is not an event` );
    }

    if ( image.file !== `events/${image.event}.jpg` || !fs.existsSync( path.join( root, "photos", image.file ) ) ) {
        problems.push( `photos/events/manifest.json: ${image.event} must be events/${image.event}.jpg, and the file must exist` );
    }
}

console.log( `Calendar: ${calendar.events.length} events, ${sections.length} brief sections, ${manifest.images.length} photos. ${problems.length} problems.` );
for ( const problem of problems ) {
    console.log( `  ${problem}` );
}

process.exit( problems.length > 0 ? 1 : 0 );
