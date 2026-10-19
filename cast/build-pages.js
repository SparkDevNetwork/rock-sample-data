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
    Builds the Cast pages in families/ from the household and group files.

    Usage: node build-pages.js

    The JSON files are the source of truth; never edit the pages by hand. Rerun this after
    any Cast change. Families with at least one bio are "featured" and listed first.
*/

const fs = require( "fs" );
const path = require( "path" );

const castDir = __dirname;
const outDir = path.join( castDir, "families" );
const photoDir = path.join( castDir, "..", "photos", "cast" );
const campuses = JSON.parse( fs.readFileSync( path.join( castDir, "..", "configuration", "campuses.json" ), "utf8" ) ).campuses;
const achievementNames = new Map( JSON.parse( fs.readFileSync( path.join( castDir, "..", "configuration", "achievements.json" ), "utf8" ) ).achievementTypes.map( a => [ a.key, a.name ] ) );
const mainCampus = campuses.find( c => c.rockDefault ).name;

const readFolder = folder => fs.readdirSync( path.join( castDir, folder ) ).filter( f => f.endsWith( ".json" ) ).sort()
    .map( f => JSON.parse( fs.readFileSync( path.join( castDir, folder, f ), "utf8" ) ) );
const households = readFolder( "households" );
const groups = readFolder( "groups" );

/**
 * How a membership reads on a family page: the group's name and the person's role or state.
 * Small groups use role and state; other groups mark leaders with isLeader.
 */
function membershipLabel( g, m ) {
    const notes = [];
    if ( m.role && m.role !== "Member" ) {
        notes.push( m.role.toLowerCase() );
    }
    if ( m.isLeader ) {
        notes.push( "leader" );
    }
    if ( m.state === "Pending" ) {
        notes.push( "asked to join" );
    }
    if ( m.state === "Inactive" || m.state === "Archived" ) {
        notes.push( `left${m.state === "Archived" ? ", archived" : ""}` );
    }
    if ( g.lifecycle && g.lifecycle.state !== "Active" ) {
        notes.push( `group ${g.lifecycle.state === "Archived" ? "archived" : "ended"}` );
    }
    return `${g.name}${notes.length ? ` (${notes.join( ", " )})` : ""}`;
}

// Group memberships by person Guid, current ones first.
const memberships = new Map();
for ( const g of groups ) {
    for ( const m of g.members || [] ) {
        if ( !memberships.has( m.guid ) ) {
            memberships.set( m.guid, [] );
        }
        const isCurrent = ( !g.lifecycle || g.lifecycle.state === "Active" ) && ( !m.state || m.state === "Active" );
        memberships.get( m.guid ).push( { isCurrent, label: membershipLabel( g, m ) } );
    }
}
for ( const [ guid, list ] of memberships ) {
    memberships.set( guid, [ ...list.filter( x => x.isCurrent ), ...list.filter( x => !x.isCurrent ) ].map( x => x.label ) );
}

// Business contacts: each "business" relationship, both ways, by person Guid.
const personByGuid = new Map( households.flatMap( h => h.members.map( p => [ p.guid, p ] ) ) );
const contactsOf = new Map();
const businessesOf = new Map();
for ( const p of personByGuid.values() ) {
    for ( const r of ( p.relationships || [] ).filter( r => r.type === "business" ) ) {
        contactsOf.set( r.with, [ ...( contactsOf.get( r.with ) || [] ), p ] );
        businessesOf.set( p.guid, [ ...( businessesOf.get( p.guid ) || [] ), personByGuid.get( r.with ) ] );
    }
}

// #region Formatting

// A business record has no first name; its name is the last name, as in Rock.
const name = p => !p.firstName ? p.lastName : `${p.nickName && p.nickName !== p.firstName ? `${p.nickName} (${p.firstName})` : p.firstName} ${p.lastName}`;
const shortName = p => !p.firstName ? p.lastName : `${p.nickName || p.firstName} ${p.lastName}`;
const money = n => `$${Number( n ).toLocaleString( "en-US" )}`;
const ago = d => {
    if ( !d ) {
        return null;
    }
    if ( d.daysAgo != null ) {
        return d.daysAgo < 60 ? `${d.daysAgo} days ago` : `about ${Math.round( d.daysAgo / 30 )} months ago`;
    }
    return d.yearsAgo === 0 ? "this year" : d.yearsAgo === 1 ? "a year ago" : `${d.yearsAgo} years ago`;
};

/**
 * The image for a person: their delivered Cast photo, else a note.
 */
function photoFor( h, p, size ) {
    const file = `${h.key}-${p.key}.jpg`;
    if ( fs.existsSync( path.join( photoDir, file ) ) ) {
        return `<img src="../../photos/cast/${file}" width="${size}" alt="${shortName( p )}">`;
    }
    if ( p.recordType === "business" ) {
        return "(business)";
    }
    return p.photo && p.photo.isOmitted ? "(no photo, on purpose)" : "(photo coming)";
}

function describeGiving( g, people ) {
    const giver = people.find( p => p.key === g.person );
    const funds = Object.entries( g.funds ).map( ( [ f, a ] ) => `${money( a )} ${f}` ).join( " + " );
    const parts = [ `**${g.persona}** by ${giver ? shortName( giver ) : g.person}: ${funds} per gift` ];
    if ( g.currency ) {
        parts.push( { check: "by check", ach: "by bank transfer", card: "by card", cash: "in cash" }[ g.currency ] );
    }
    if ( g.percentOfPeriodsGiven != null && g.persona !== "one-time" ) {
        parts.push( `gives in ${g.percentOfPeriodsGiven}% of periods` );
    }
    if ( g.growth ) {
        parts.push( `grows ${g.growth.percent}% every ${g.growth.everyWeeks} weeks` );
    }
    if ( g.specialGiftPercent ) {
        parts.push( `${g.specialGiftPercent}% chance of a year-end gift` );
    }
    if ( g.started ) {
        parts.push( `started ${ago( g.started )}` );
    }
    if ( g.ended ) {
        parts.push( `stopped ${ago( g.ended )}` );
    }
    if ( g.expect ) {
        // What Rock should show for this giver; the builder checks it on every load.
        const shows = [];
        if ( g.expect.frequency ) {
            shows.push( `a ${g.expect.frequency} gift frequency` );
        }
        if ( g.expect.journeyStage ) {
            shows.push( `the ${g.expect.journeyStage} journey stage` );
        }
        for ( const alert of g.expect.alerts || [] ) {
            shows.push( `a ${alert.replace( /-/g, " " )} alert` );
        }
        parts.push( `and Rock shows ${shows.join( " and " )}` );
    }
    return parts.join( ", " );
}

// #endregion Formatting

// #region Pages

function familyPage( h ) {
    const people = h.members;
    const featured = people.some( p => p.bio );
    const lines = [];
    lines.push( `# ${h.name}`, "" );
    lines.push( `${featured ? "**Featured** · " : ""}${h.origin === "new" ? "New household" : "Legacy household"} · ${h.kind === "business" ? "Business" : `${people.length} ${people.length === 1 ? "person" : "people"}`} · ${mainCampus} campus · [source](../households/${h.key}.json)`, "" );
    lines.push( h.story, "" );

    lines.push( "| Photo | Name | Age | Role | Status |", "|---|---|---|---|---|" );
    for ( const p of people ) {
        const age = p.birth ? p.birth.age : "?";
        const role = p.recordType === "business" ? "business" : p.familyRole;
        lines.push( `| ${photoFor( h, p, 96 )} | ${name( p )}${p.isDeceased ? " (deceased)" : ""} | ${age} | ${role} | ${p.connectionStatus} |` );
    }
    lines.push( "" );

    lines.push( "## People", "" );
    for ( const p of people ) {
        lines.push( `### ${name( p )}`, "" );
        if ( p.bio ) {
            lines.push( p.bio, "" );
        }
        const facts = [];
        if ( p.birth ) {
            facts.push( `Age ${p.birth.age}` );
        }
        if ( p.maritalStatus ) {
            facts.push( p.maritalStatus );
        }
        if ( p.recordStatus && p.recordStatus !== "active" ) {
            facts.push( `record ${p.recordStatus}${p.recordStatusReason ? ` (${p.recordStatusReason})` : ""}` );
        }
        const a = p.attributes || {};
        if ( a.Employer ) {
            facts.push( `${a.Position ? `${a.Position}, ` : ""}${a.Employer}` );
        }
        facts.push( p.email ? `email ${p.email}` : "no email" );
        if ( memberships.has( p.guid ) ) {
            facts.push( `groups: ${memberships.get( p.guid ).join( ", " )}` );
        }
        if ( businessesOf.has( p.guid ) ) {
            facts.push( `business contact for ${businessesOf.get( p.guid ).map( b => b.lastName ).join( ", " )}` );
        }
        if ( contactsOf.has( p.guid ) ) {
            facts.push( `contacts: ${contactsOf.get( p.guid ).map( shortName ).join( ", " )}` );
        }
        if ( p.securityRoles && p.securityRoles.length ) {
            facts.push( `${p.securityRoles.length} security roles` );
        }
        if ( a.Allergy ) {
            facts.push( `allergy: ${a.Allergy}` );
        }
        if ( a.BackgroundCheckResult ) {
            facts.push( `background check: ${a.BackgroundCheckResult}` );
        }
        const achievements = p.expect?.achievements;
        if ( achievements ) {
            // What Rock should show for this person's achievements; every load checks it.
            const shows = [
                ...( achievements.earned || [] ).map( k => `${achievementNames.get( k )} earned` ),
                ...( achievements.inProgress || [] ).map( k => `${achievementNames.get( k )} in progress` ),
                ...( achievements.notEarned || [] ).filter( k => !( achievements.inProgress || [] ).includes( k ) ).map( k => `${achievementNames.get( k )} not earned` )
            ];
            facts.push( `Rock shows ${shows.join( ", " )}` );
        }
        lines.push( `- ${facts.join( " · " )}` );
        lines.push( `- Person Guid \`${p.guid}\``, "" );
    }

    lines.push( "## What this family tests", "" );
    h.testPurposes.forEach( t => lines.push( `- ${t}` ) );
    lines.push( "" );

    lines.push( "## Activity", "" );
    if ( h.attendance ) {
        const att = h.attendance;
        lines.push( `- **Attendance:** ${att.persona}, ${att.percentAttended}% of Sundays${att.percentPrimaryService ? `, ${att.percentPrimaryService}% at their usual service` : ""}${att.started ? `, since ${ago( att.started )}` : ""}${att.ended ? `, stopped ${ago( att.ended )}` : ""}.` );
    }
    else if ( h.kind === "business" ) {
        lines.push( "- **Attendance:** none. A business does not attend." );
    }
    else {
        lines.push( "- **Attendance:** set by connection status." );
    }
    for ( const g of h.giving || [] ) {
        lines.push( `- **Giving:** ${describeGiving( g, people )}.` );
    }
    const isMarried = p => /^married$/i.test( p.maritalStatus || "" );
    const couple = people.filter( p => p.familyRole === "adult" && isMarried( p ) );
    if ( couple.length === 2 ) {
        lines.push( h.adultsGiveSeparately ? "- **Giving units:** the spouses give separately; each is their own giving unit." : "- **Giving units:** the couple gives together as one giving unit." );
        people.filter( p => p.familyRole === "adult" && !isMarried( p ) ).forEach( p => lines.push( `- **Giving units:** ${shortName( p )} is a separate giving unit.` ) );
    }
    if ( !( h.giving || [] ).length ) {
        lines.push( "- **Giving:** none." );
    }
    lines.push( "" );

    if ( h.address ) {
        lines.push( "## Address", "", `${h.address.street1}, ${h.address.city}, ${h.address.state} ${h.address.postalCode} (a real street with a house number that does not exist)`, "" );
    }

    if ( h.reviewNotes && h.reviewNotes.length ) {
        lines.push( "## Open questions", "" );
        h.reviewNotes.forEach( n => lines.push( `- ${n}` ) );
        lines.push( "" );
    }

    lines.push( "---", "", "Generated by `cast/build-pages.js` from the household file. Edit the JSON, then rerun the script.", "" );
    return lines.join( "\n" );
}

function indexPage() {
    const featured = households.filter( h => h.members.some( p => p.bio ) );
    const people = households.reduce( ( s, h ) => s + h.members.length, 0 );
    const lines = [];
    lines.push( "# Meet the Cast", "" );
    lines.push( `The Cast is ${households.length} hand-designed households and ${people} people. They are the same in every dataset and always live on the ${mainCampus} campus, so tests and documentation can rely on them by Guid.`, "" );
    lines.push( "Each family has its own page with photos, backstories, and what it is used to test. **Featured** families are the ones most used in testing; they have a short bio for each person.", "" );
    lines.push( "Every photo is AI-generated to look like a casual profile picture. Legacy families have new photos based on their old ones, so the faces Rock users know stay recognizable (see [Photos](../../photos/README.md)).", "" );

    lines.push( "## Featured families", "" );
    lines.push( "| | Family | Who | Used to test |", "|---|---|---|---|" );
    for ( const h of featured ) {
        const lead = h.members.find( p => p.familyRole === "adult" ) || h.members[ 0 ];
        lines.push( `| ${photoFor( h, lead, 56 )} | [${h.name}](${h.key}.md) | ${h.members.map( p => p.nickName || p.firstName || p.lastName ).join( ", " )} | ${h.testPurposes[ 0 ]} |` );
    }
    lines.push( "" );

    lines.push( "## All families", "" );
    lines.push( "| Family | Origin | People | Statuses | Used to test |", "|---|---|---|---|---|" );
    for ( const h of households ) {
        const statuses = [ ...new Set( h.members.map( p => p.connectionStatus ) ) ].join( ", " );
        lines.push( `| [${h.name}](${h.key}.md) | ${h.origin === "new" ? "new" : "legacy"} | ${h.members.length} | ${statuses} | ${h.testPurposes[ 0 ]} |` );
    }
    lines.push( "", "---", "", "Generated by `cast/build-pages.js`. Edit the household files, then rerun the script.", "" );
    return lines.join( "\n" );
}

// #endregion Pages

fs.mkdirSync( outDir, { recursive: true } );
for ( const f of fs.readdirSync( outDir ).filter( f => f.endsWith( ".md" ) ) ) {
    fs.unlinkSync( path.join( outDir, f ) );
}
for ( const h of households ) {
    fs.writeFileSync( path.join( outDir, `${h.key}.md` ), familyPage( h ) );
}
fs.writeFileSync( path.join( outDir, "README.md" ), indexPage() );
console.log( `Wrote ${households.length} family pages and families/README.md.` );
