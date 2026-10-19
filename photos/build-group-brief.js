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
    Writes the per-group sections of group-brief.md: who is in each Cast small group, and where
    each member's profile photo is, so the image model can put the real Cast faces in the photo.

    Usage: node photos/build-group-brief.js

    Only the text between the groups:start and groups:end markers is replaced. Rerun this after
    the Cast's groups are replanned (builder --plan-cast-groups) or new Cast photos are delivered.
*/

const fs = require( "fs" );
const path = require( "path" );

const root = path.join( __dirname, ".." );
const briefPath = path.join( __dirname, "group-brief.md" );
const readJson = file => JSON.parse( fs.readFileSync( path.join( root, file ), "utf8" ) );
const readFolder = folder => fs.readdirSync( path.join( root, folder ) ).filter( f => f.endsWith( ".json" ) ).sort()
    .map( f => readJson( path.join( folder, f ) ) );

// Every group gets its own place, so no two photos share a room or a yard.
const settings = {
    "decker-group": "Outdoors. A backyard lawn in the early evening, with folding lawn chairs pulled into a loose circle, a few kids' bikes and a soccer ball in the background, and desert landscaping along the block wall.",
    "gilbert-group": "Indoors. A cozy living room with warm lamp light, a mid-century modern couch and armchairs, a rug, and a coffee table with Bibles, a candle, and mugs.",
    "greggs-group": "Indoors. A bright sunroom full of houseplants, soft afternoon light through tall windows, wicker chairs, and a side table with tissues, Bibles, and tea.",
    "hughes-group": "Indoors. An open kitchen and dining area, people gathered around a long wooden dining table with open Bibles, sermon notes, and a couple of pizza boxes.",
    "jenkins-group": "Outdoors. The front porch and steps of a ranch-style house at golden hour, some people sitting on the steps and some standing behind them.",
    "lee-group": "Outdoors. The group meets online, so this is their in-person kickoff picnic: a shaded park ramada with picnic tables, a cooler, paper plates, and green grass and palo verde trees behind them.",
    "marble-group": "Indoors. A large farmhouse-style living room with a big sectional couch, a stone fireplace, wood beams, and Bibles and coffee cups on an ottoman.",
    "murphy-group": "Outdoors. A backyard patio at dusk around a gas fire pit, with string lights overhead and blankets on a few laps.",
    "petes-group": "Indoors. A family room den with a worn leather sectional, tall bookshelves, a TV on the wall turned off, and Bibles and a few snacks on the coffee table.",
    "rivera-group": "Outdoors. A backyard patio next to a pool with citrus trees and a stucco wall, couples sitting together on patio furniture in warm evening light.",
    "russell-group": "Indoors. A formal dining room with a long table set for a meal, a china cabinet in the background, and Bibles open beside the plates.",
    "silva-group": "Indoors. A modern condo living room with floor-to-ceiling windows, a city view in the late afternoon, a low gray couch, and floor cushions."
};

const households = readFolder( "cast/households" );
const groups = readFolder( "cast/groups" ).filter( g => g.type === "small-group" && g.lifecycle );
const ethnicities = Object.fromEntries( readJson( "photos/ethnicities.json" ).groups.map( g => [g.key, g.prompt] ) );
const photos = new Map( readJson( "photos/manifest.json" ).images.filter( i => i.set === "cast" ).map( i => [`${i.household}/${i.member}`, i] ) );

const people = new Map();
for ( const household of households ) {
    for ( const member of household.members ) {
        people.set( member.guid.toUpperCase(), { household, member } );
    }
}

const roleOrder = { Leader: 0, Host: 1, Member: 2 };
const lifecycleOrder = { Active: 0, Inactive: 1, Archived: 2 };

const missingSettings = groups.filter( g => !settings[g.key] ).map( g => g.key );
if ( missingSettings.length > 0 ) {
    throw new Error( `No setting for: ${missingSettings.join( ", " )}. Add one to the settings list.` );
}

let made = 0;
let toMakeUp = 0;
const sections = groups
    .sort( ( a, b ) => lifecycleOrder[a.lifecycle.state] - lifecycleOrder[b.lifecycle.state] || a.key.localeCompare( b.key ) )
    .map( group => {
        const members = group.members
            .filter( m => m.state === "Active" )
            .sort( ( a, b ) => roleOrder[a.role] - roleOrder[b.role] || ( b.attendancePercent ?? 0 ) - ( a.attendancePercent ?? 0 ) );

        const rows = members.map( m => {
            const found = people.get( m.guid.toUpperCase() );
            if ( !found ) {
                throw new Error( `${group.key}: member ${m.name} (${m.guid}) is not in any Cast household.` );
            }

            const { household, member } = found;
            const photo = photos.get( `${household.key}/${member.key}` );
            const age = member.birth?.age ?? "";
            if ( photo ) {
                made++;
                return `| ${m.name} | ${m.role} | ${age} | ${member.gender} | \`photos/originals/${originalName( photo.file )}\` |`;
            }

            toMakeUp++;
            const look = member.photo?.ethnicity ? ethnicities[member.photo.ethnicity] : "any background that fits the group";
            return `| ${m.name} | ${m.role} | ${age} | ${member.gender} | No photo. Make up a fictional ${age}-year-old ${member.gender === "female" ? "woman" : "man"}, ${look}. |`;
        } );

        const meets = group.meetingStyle.style === "Online"
            ? `Online, ${group.schedule.day}s at ${group.schedule.time}`
            : `In person at the ${hostName( group.meetingStyle.hostHousehold )} home, ${group.schedule.day}s at ${group.schedule.time}`;
        const state = group.lifecycle.state === "Active" ? "Active" : `${group.lifecycle.state} (lower priority: Rock shows it, but Group Finder does not)`;

        return [
            `### ${group.name}`,
            "",
            `- **File:** \`photos/groups/${group.key}.jpg\``,
            `- **State:** ${state}`,
            `- **Topic:** ${group.topic}. ${group.description}`,
            `- **Meets:** ${meets}${group.childCareProvided ? ", with child care" : ""}`,
            `- **Setting:** ${settings[group.key]}`,
            `- **People in the photo:** ${members.length}`,
            "",
            "| Name | Role | Age | Gender | Profile photo |",
            "|---|---|---|---|---|",
            ...rows,
            ""
        ].join( "\n" );
    } );

const block = [
    "<!-- groups:start (written by node photos/build-group-brief.js; do not edit by hand) -->",
    "",
    `${groups.length} groups and ${made + toMakeUp} places in the photos (some people are in two groups): ${made} with a profile photo and ${toMakeUp} to make up.`,
    "",
    ...sections,
    "<!-- groups:end -->"
].join( "\n" );

const brief = fs.readFileSync( briefPath, "utf8" );
const updated = brief.replace( /<!-- groups:start[\s\S]*?<!-- groups:end -->/, block );
if ( updated === brief && !brief.includes( block ) ) {
    throw new Error( "group-brief.md has no groups:start and groups:end markers." );
}

fs.writeFileSync( briefPath, updated );
console.log( `Wrote ${groups.length} groups (${made} people with photos, ${toMakeUp} to make up) to ${path.relative( root, briefPath )}.` );

/**
 * The original's file name. Originals keep the delivered name but may be PNG or JPEG.
 */
function originalName( file ) {
    const base = file.replace( /\.jpg$/, "" );
    const match = fs.readdirSync( path.join( root, "photos", "originals", path.dirname( file ) ) )
        .find( f => f.replace( /\.(png|jpe?g)$/i, "" ) === path.basename( base ) );
    return match ? `${path.dirname( file )}/${match}` : file;
}

/**
 * The host household's name without "Family", for example "Decker".
 */
function hostName( key ) {
    const household = households.find( h => h.key === key );
    return household ? household.name.replace( / Family$/, "" ) : key;
}
