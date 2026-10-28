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
    Validates the Cast registry: required fields, Guid format, unique Guids,
    every cross-reference between households and groups, the delivered
    photos in photos/manifest.json, the prayer requests, the notes, and the
    text threads in sms-threads.json, and the staff emails in
    staff-emails.json. Prints errors
    (exit code 1) and warnings (exit code 0).

    Usage: node validate-cast.js
*/

const fs = require( "fs" );
const path = require( "path" );

const castDir = __dirname;
const GUID = /^[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}$/;
const KEY = /^[a-z0-9]+(-[a-z0-9]+)*$/;
const STATUSES = [ "Prospect", "Participant", "Known", "Connected", "Engaged", "Leading" ];

// The sample church uses only the security roles Rock ships, so every role name must be mapped (decision 79).
const securityRoleConfiguration = JSON.parse( fs.readFileSync( path.join( castDir, "..", "configuration", "security-roles.json" ), "utf8" ) );
const SECURITY_ROLE_NAMES = new Set( securityRoleConfiguration.roles.concat( securityRoleConfiguration.notRoles ).map( r => r.name ) );
const ALLOWED_EMAIL_DOMAINS = [ "example.org", "example.com", "rocksolidchurchdemo.com" ];

const errors = [];
const warnings = [];
const smallGroups = JSON.parse( fs.readFileSync( path.join( castDir, "..", "configuration", "small-groups.json" ), "utf8" ) );
const serving = JSON.parse( fs.readFileSync( path.join( castDir, "..", "configuration", "serving.json" ), "utf8" ) );
const connections = JSON.parse( fs.readFileSync( path.join( castDir, "..", "configuration", "connections.json" ), "utf8" ) );

// Photo ethnicity values come from one file, and the schema enum must list the same keys.
const ethnicityFile = JSON.parse( fs.readFileSync( path.join( castDir, "..", "photos", "ethnicities.json" ), "utf8" ) );
const PHOTO_ETHNICITIES = ethnicityFile.groups.map( g => g.key );
const schemaEthnicities = JSON.parse( fs.readFileSync( path.join( castDir, "household.schema.json" ), "utf8" ) ).$defs.person.properties.photo.properties.ethnicity.enum;
if ( [ ...schemaEthnicities ].sort().join() !== [ ...PHOTO_ETHNICITIES ].sort().join() ) {
    errors.push( "household.schema.json photo.ethnicity values do not match photos/ethnicities.json." );
}

// Giving expectations: the allowed values come from the schema, and alert keys from the alert type configuration.
const expectSchema = JSON.parse( fs.readFileSync( path.join( castDir, "household.schema.json" ), "utf8" ) ).$defs.giving.properties.expect.properties;
const EXPECT_VALUES = { frequency: expectSchema.frequency.enum, journeyStage: expectSchema.journeyStage.enum };
const ALERT_TYPE_KEYS = JSON.parse( fs.readFileSync( path.join( castDir, "..", "configuration", "giving-alert-types.json" ), "utf8" ) ).alertTypes.map( t => t.key );
const ACHIEVEMENT_TYPE_KEYS = JSON.parse( fs.readFileSync( path.join( castDir, "..", "configuration", "achievements.json" ), "utf8" ) ).achievementTypes.map( t => t.key );
const CAMPAIGN_SCHEMA = JSON.parse( fs.readFileSync( path.join( castDir, "household.schema.json" ), "utf8" ) ).$defs.campaign.properties;
const COMMITMENT_WEEKS = JSON.parse( fs.readFileSync( path.join( castDir, "..", "configuration", "campaign.json" ), "utf8" ) ).commitmentSundayWeeksBeforeInstall;
const STEP_PROGRAMS = JSON.parse( fs.readFileSync( path.join( castDir, "..", "configuration", "steps.json" ), "utf8" ) ).programs;
const STEP_PROGRAM_KEYS = STEP_PROGRAMS.map( p => p.key );
const STEP_TYPES = new Map( STEP_PROGRAMS.flatMap( p => p.stepTypes ).map( t => [ t.key, t ] ) );
const adultsWithoutPhotoEthnicity = [];

/**
 * Reads every JSON file in a Cast subfolder.
 */
function readFolder( folder ) {
    const dir = path.join( castDir, folder );
    return fs.readdirSync( dir ).filter( f => f.endsWith( ".json" ) ).map( f => ( { file: `${folder}/${f}`, data: JSON.parse( fs.readFileSync( path.join( dir, f ), "utf8" ) ) } ) );
}

const households = readFolder( "households" );
const groups = readFolder( "groups" );

const people = new Map();
const householdKeys = new Set();
const guids = new Map();

/**
 * Records a Guid and reports it if another record already uses it.
 */
function claimGuid( guid, owner ) {
    if ( !GUID.test( guid ) ) {
        errors.push( `${owner}: Guid ${guid} is not an uppercase, hyphenated Guid.` );
    }
    if ( guids.has( guid ) ) {
        errors.push( `${owner}: Guid ${guid} is already used by ${guids.get( guid )}.` );
    }
    guids.set( guid, owner );
}

// Pass 1: structure, and collect every person and household.
for ( const { file, data: h } of households ) {
    for ( const field of [ "schemaVersion", "key", "guid", "name", "kind", "origin", "campusSlot", "story", "testPurposes", "members" ] ) {
        if ( h[ field ] === undefined ) {
            errors.push( `${file}: missing ${field}.` );
        }
    }
    if ( `${h.key}.json` !== path.basename( file ) ) {
        errors.push( `${file}: key "${h.key}" does not match the file name.` );
    }
    if ( !KEY.test( h.key ) ) {
        errors.push( `${file}: key "${h.key}" is not a lowercase slug.` );
    }
    if ( h.campusSlot !== "main" ) {
        errors.push( `${file}: the Cast must use the main campus slot.` );
    }
    householdKeys.add( h.key );
    claimGuid( h.guid, file );

    const memberKeys = new Set();
    for ( const p of h.members || [] ) {
        const owner = `${file} (${p.key})`;
        if ( memberKeys.has( p.key ) ) {
            errors.push( `${owner}: member key is used twice in this household.` );
        }
        memberKeys.add( p.key );
        claimGuid( p.guid, owner );
        people.set( p.guid, { household: h, person: p, file } );

        // A business record has no first name, as in Rock; its name is the last name.
        if ( !p.firstName && p.recordType !== "business" ) {
            errors.push( `${owner}: missing firstName.` );
        }
        if ( !STATUSES.includes( p.connectionStatus ) ) {
            errors.push( `${owner}: connection status "${p.connectionStatus}" is not one of ${STATUSES.join( ", " )}.` );
        }
        for ( const role of p.securityRoles || [] ) {
            if ( !SECURITY_ROLE_NAMES.has( role ) ) {
                errors.push( `${owner}: security role "${role}" is not in configuration/security-roles.json. Only roles Rock ships are used.` );
            }
        }
        // Photo ethnicity lives only in photo.ethnicity, and nothing about race or ethnicity may reach Rock.
        if ( p.appearanceGroup !== undefined ) {
            errors.push( `${owner}: appearanceGroup was replaced by photo.ethnicity.` );
        }
        const ethnicity = p.photo && p.photo.ethnicity;
        if ( ethnicity !== undefined && !PHOTO_ETHNICITIES.includes( ethnicity ) ) {
            errors.push( `${owner}: photo ethnicity "${ethnicity}" is not one of ${PHOTO_ETHNICITIES.join( ", " )}.` );
        }
        if ( ethnicity === undefined && p.familyRole === "adult" && p.recordType !== "business" ) {
            adultsWithoutPhotoEthnicity.push( owner );
        }
        for ( const key of Object.keys( p.attributes || {} ) ) {
            if ( /race|ethnic/i.test( key ) ) {
                errors.push( `${owner}: attribute "${key}" looks like race or ethnicity. Those never go into Rock; use photo.ethnicity.` );
            }
        }
        if ( p.email ) {
            const domain = p.email.split( "@" )[ 1 ].toLowerCase();
            if ( !ALLOWED_EMAIL_DOMAINS.includes( domain ) ) {
                errors.push( `${owner}: email domain ${domain} is not a reserved or church-owned domain.` );
            }
        }
    }
}

const groupKeys = new Set( groups.map( g => g.data.key ) );
const leaders = new Set();
const groupTypesByPerson = new Map();
const duplicateHouseholds = new Set( households.filter( h => h.data.duplicateOf ).map( h => h.data.key ) );

/**
 * Checks a Cast serving team roster against its team in configuration/serving.json.
 */
function checkServingTeam( file, g ) {
    const team = serving.teams.find( t => t.key === g.team );
    if ( !team ) {
        errors.push( `${file}: team "${g.team}" is not in configuration/serving.json.` );
        return;
    }
    if ( g.name !== team.name || g.key !== `serving-${team.key}` ) {
        errors.push( `${file}: a roster for "${team.key}" must be named "${team.name}" with key "serving-${team.key}".` );
    }
    const teamLeaders = ( g.members || [] ).filter( m => m.role === "Leader" );
    if ( teamLeaders.length !== 1 ) {
        errors.push( `${file}: has ${teamLeaders.length} leaders; a serving team has exactly one.` );
    }
    for ( const m of g.members || [] ) {
        const entry = people.get( m.guid );
        if ( entry && ( entry.person.birth?.age ?? 0 ) < 18 ) {
            errors.push( `${file}: ${m.name} is under 18; serving team members are adults.` );
        }
    }
}

/**
 * Checks a small group plan (see group.schema.json and configuration/small-groups.json).
 */
function checkSmallGroupPlan( file, g ) {
    const isActiveGroup = g.lifecycle.state === "Active";
    if ( !smallGroups.topics.some( t => t.value === g.topic ) ) {
        errors.push( `${file}: topic "${g.topic}" is not in configuration/small-groups.json.` );
    }
    if ( g.lifecycle.state === "Inactive" && !smallGroups.inactiveReasons.some( r => r.value === g.lifecycle.reason ) ) {
        errors.push( `${file}: inactive reason "${g.lifecycle.reason}" is not in configuration/small-groups.json.` );
    }
    if ( !( g.schedule.day in smallGroups.schedule.days ) || !( g.schedule.time in smallGroups.schedule.times ) ) {
        errors.push( `${file}: meets ${g.schedule.day} at ${g.schedule.time}, which the schedule rules do not allow.` );
    }
    if ( g.meetingStyle.style === "InPerson" ) {
        const host = households.find( h => h.data.key === g.meetingStyle.hostHousehold );
        if ( !host || !host.data.address || host.data.address.type !== "home" ) {
            errors.push( `${file}: host household "${g.meetingStyle.hostHousehold}" is unknown or has no home address.` );
        }
    }

    const current = g.members.filter( m => m.state === "Active" );
    const groupLeaders = g.members.filter( m => m.role === "Leader" );
    if ( groupLeaders.length !== 1 ) {
        errors.push( `${file}: has ${groupLeaders.length} leaders; a small group has exactly one.` );
    }
    if ( isActiveGroup && ( current.length < smallGroups.size.min || current.length > smallGroups.size.max ) ) {
        errors.push( `${file}: has ${current.length} active members; active groups have ${smallGroups.size.min} to ${smallGroups.size.max}.` );
    }
    if ( g.testCase === "Full" && g.capacity !== current.length ) {
        errors.push( `${file}: a Full group's capacity must equal its ${current.length} active members.` );
    }
    if ( ( g.testCase === "Closed" ) === g.isPublic ) {
        errors.push( `${file}: only Closed groups are hidden from Group Finder.` );
    }

    for ( const m of g.members ) {
        const entry = people.get( m.guid );
        if ( !entry ) {
            continue;
        }
        const p = entry.person;
        const owner = `${file} (${m.name})`;
        if ( m.isLeader !== undefined ) {
            errors.push( `${owner}: small group members use role, not isLeader.` );
        }
        if ( duplicateHouseholds.has( entry.household.key ) ) {
            errors.push( `${owner}: is a duplicate record, and duplicates never join groups.` );
        }
        if ( !p.birth || p.birth.age < smallGroups.members.minimumAge || p.isDeceased || p.recordType === "business" ) {
            errors.push( `${owner}: is not an adult person who can be in a small group.` );
        }
        if ( p.recordStatus === "inactive" && isActiveGroup ) {
            errors.push( `${owner}: has an inactive record, so may only be a former member of an ended group.` );
        }
        if ( m.role === "Leader" && ( p.gender !== smallGroups.leaders.gender || !smallGroups.leaders.statuses.concat( smallGroups.leaders.becomeStatus ).includes( p.connectionStatus ) ) ) {
            errors.push( `${owner}: does not meet the leader rules.` );
        }
        if ( ( m.state === "Inactive" || m.state === "Archived" ) !== ( m.left !== undefined ) ) {
            errors.push( `${owner}: only members who left (Inactive or Archived) have a left date.` );
        }
    }
}

// Pass 2: groups and their members.
for ( const { file, data: g } of groups ) {
    if ( `${g.key}.json` !== path.basename( file ) ) {
        errors.push( `${file}: key "${g.key}" does not match the file name.` );
    }
    claimGuid( g.guid, file );
    if ( g.meeting && g.meeting.atHousehold && !householdKeys.has( g.meeting.atHousehold ) ) {
        errors.push( `${file}: meets at unknown household "${g.meeting.atHousehold}".` );
    }

    const isPlan = g.type === "small-group" && g.lifecycle !== undefined;
    if ( g.type === "small-group" && !isPlan && !smallGroups.cast.notInPattern.includes( g.key ) ) {
        errors.push( `${file}: is a small group but not a plan. Run the builder with --plan-cast-groups.` );
    }
    if ( isPlan ) {
        checkSmallGroupPlan( file, g );
    }
    if ( g.type === "serving" ) {
        checkServingTeam( file, g );
    }

    for ( const m of g.members || [] ) {
        if ( !people.has( m.guid ) ) {
            errors.push( `${file}: member ${m.guid} (${m.name}) is not in any Cast household.` );
            continue;
        }

        // Only current memberships count: an ended group, a member who left, or a request does not.
        const isCurrent = !isPlan || ( g.lifecycle.state === "Active" && m.state === "Active" );
        if ( !isCurrent ) {
            continue;
        }
        if ( m.isLeader || m.role === "Leader" ) {
            leaders.add( m.guid );
        }
        if ( !groupTypesByPerson.has( m.guid ) ) {
            groupTypesByPerson.set( m.guid, new Set() );
        }
        groupTypesByPerson.get( m.guid ).add( g.type );
    }
}

// Pass 3: references from people and households.
for ( const { file, data: h } of households ) {
    const memberKeys = new Set( ( h.members || [] ).map( p => p.key ) );
    const givers = new Set( ( h.giving || [] ).map( gv => gv.person ) );

    for ( const gv of h.giving || [] ) {
        if ( !memberKeys.has( gv.person ) ) {
            errors.push( `${file}: giving refers to unknown member "${gv.person}".` );
        }

        // What Rock should show for the giver: values from the schema, alert keys from the alert type configuration.
        const expect = gv.expect || {};
        for ( const field of [ "frequency", "journeyStage" ] ) {
            if ( expect[field] !== undefined && !EXPECT_VALUES[field].includes( expect[field] ) ) {
                errors.push( `${file}: giving for "${gv.person}" expects ${field} "${expect[field]}", which is not one of ${EXPECT_VALUES[field].join( ", " )}.` );
            }
        }
        for ( const alert of expect.alerts || [] ) {
            if ( !ALERT_TYPE_KEYS.includes( alert ) ) {
                errors.push( `${file}: giving for "${gv.person}" expects alert "${alert}", which is not in configuration/giving-alert-types.json.` );
            }
        }
    }

    // Campaign pledges and gifts (docs/pledges.md). A line pledge rides on the giver's usual gifts, so its
    // frequency must match one of their giving rhythms. Every week named is before install day.
    const pledgers = new Set();
    for ( const c of h.campaign || [] ) {
        const owner = `${file}: campaign for "${c.person}"`;
        if ( !givers.has( c.person ) ) {
            errors.push( `${owner} needs a giving pattern for that member.` );
        }
        for ( const field of [ "frequency", "followThrough", "expect" ] ) {
            if ( c[field] !== undefined && !CAMPAIGN_SCHEMA[field].enum.includes( c[field] ) ) {
                errors.push( `${owner}: ${field} "${c[field]}" is not one of ${CAMPAIGN_SCHEMA[field].enum.join( ", " )}.` );
            }
        }
        const weeks = [ c.pledged?.weeksAfterCommitment, ...( c.gifts || [] ).map( g => g.weeksAfterCommitment ) ].filter( w => w !== undefined );
        if ( weeks.some( w => w >= COMMITMENT_WEEKS ) ) {
            errors.push( `${owner}: every week after Commitment Sunday must be before install day (under ${COMMITMENT_WEEKS}).` );
        }
        if ( c.pledge === undefined ) {
            if ( !( c.gifts || [] ).length || c.frequency || c.followThrough || c.pledged ) {
                errors.push( `${owner}: with no pledge, list gifts and leave out frequency, followThrough, and pledged.` );
            }
            continue;
        }
        if ( pledgers.has( c.person ) ) {
            errors.push( `${owner}: a person has at most one pledge.` );
        }
        pledgers.add( c.person );
        if ( !c.followThrough || !c.pledged || ( typeof c.pledged === "string" && ![ "quiet-phase", "commitment-sunday" ].includes( c.pledged ) ) ) {
            errors.push( `${owner}: a pledge needs followThrough and pledged (quiet-phase, commitment-sunday, or weeksAfterCommitment).` );
        }
        const isLine = [ "ahead", "on-pace", "behind", "stopped" ].includes( c.followThrough );
        if ( isLine && !( h.giving || [] ).some( gv => gv.person === c.person && gv.persona === c.frequency ) ) {
            errors.push( `${owner}: a ${c.followThrough} pledge rides on usual gifts, so its frequency must match one of the giver's giving rhythms.` );
        }
    }

    // What Rock should show for a person's achievements: keys from the achievements configuration, and no type both earned and not.
    for ( const p of h.members || [] ) {
        const achievements = p.expect?.achievements || {};
        for ( const list of [ "earned", "inProgress", "notEarned" ] ) {
            for ( const key of achievements[list] || [] ) {
                if ( !ACHIEVEMENT_TYPE_KEYS.includes( key ) ) {
                    errors.push( `${file}: "${p.key}" expects achievement "${key}", which is not in configuration/achievements.json.` );
                }
            }
        }
        for ( const key of achievements.earned || [] ) {
            if ( ( achievements.notEarned || [] ).includes( key ) ) {
                errors.push( `${file}: "${p.key}" expects achievement "${key}" both earned and not earned.` );
            }
        }

        // Steps: expectations name step types and programs. A story can set classes, student events, a decision, and
        // school years; group, team, giving, and baptism steps come from the person's other records.
        for ( const list of [ "complete", "inProgress" ] ) {
            for ( const key of p.expect?.steps?.[list] || [] ) {
                if ( !STEP_TYPES.has( key ) ) {
                    errors.push( `${file}: "${p.key}" expects step type "${key}", which is not in configuration/steps.json.` );
                }
            }
        }
        for ( const key of p.expect?.steps?.programCompletions || [] ) {
            if ( !STEP_PROGRAM_KEYS.includes( key ) ) {
                errors.push( `${file}: "${p.key}" expects a completion of program "${key}", which is not in configuration/steps.json.` );
            }
        }
        for ( const [ key, choice ] of Object.entries( p.steps || {} ) ) {
            const kind = STEP_TYPES.get( key )?.source.kind;
            if ( ![ "class", "event", "decision", "school-years" ].includes( kind ) ) {
                errors.push( `${file}: "${p.key}" sets step "${key}", which is not a class, event, decision, or school-years step in configuration/steps.json. Other steps come from the person's records.` );
                continue;
            }
            const set = [ choice.classesAgo !== undefined, choice.events !== undefined, choice.taken === true ].filter( Boolean ).length;
            if ( Array.isArray( choice.events ) && choice.events.length === 0 ) {
                errors.push( `${file}: "${p.key}" step "${key}" lists no events. Name at least one, or use taken: true.` );
            }
            if ( set !== 1 ) {
                errors.push( `${file}: "${p.key}" step "${key}" needs exactly one of classesAgo, events, or taken: true.` );
            }
            if ( choice.classesAgo !== undefined && kind !== "class" ) {
                errors.push( `${file}: "${p.key}" step "${key}" sets classesAgo, which only a class can have.` );
            }
            if ( choice.events !== undefined ) {
                // A decision names the one event it was made at, which the person must have gone to.
                const eventKey = kind === "decision" ? STEP_TYPES.get( key ).source.atEvent.stepType : key;
                const seasons = ( STEP_TYPES.get( eventKey )?.source.seasons || [] ).map( s => s.key );
                if ( kind !== "event" && kind !== "decision" ) {
                    errors.push( `${file}: "${p.key}" step "${key}" sets events, which only an event or decision step can have.` );
                }
                for ( const e of choice.events ) {
                    if ( !seasons.includes( e.season ) ) {
                        errors.push( `${file}: "${p.key}" step "${key}" names season "${e.season}", which ${eventKey} does not hold.` );
                    }
                }
                if ( kind === "decision" ) {
                    const went = p.steps[eventKey]?.events || [];
                    if ( choice.events.length !== 1 || !went.some( e => e.season === choice.events[0].season && e.yearsAgo === choice.events[0].yearsAgo ) ) {
                        errors.push( `${file}: "${p.key}" step "${key}" must name one event from the person's own ${eventKey} events.` );
                    }
                }
            }
        }
    }
    if ( h.adultsGiveSeparately && ( h.members || [] ).filter( p => p.familyRole === "adult" && p.maritalStatus === "married" ).length !== 2 ) {
        errors.push( `${file}: adultsGiveSeparately is set, but the household does not have a married couple.` );
    }
    if ( h.duplicateOf !== undefined && ( !householdKeys.has( h.duplicateOf ) || h.duplicateOf === h.key ) ) {
        errors.push( `${file}: duplicateOf "${h.duplicateOf}" is not another Cast household.` );
    }
    for ( const s of h.sharedMembers || [] ) {
        const target = people.get( s.guid );
        if ( !target || target.household.key !== s.household ) {
            errors.push( `${file}: shared member ${s.guid} is not in household "${s.household}".` );
        }
    }
    /*
        Engaged means in a small group, serving, or giving. Giving counts for the whole family,
        because Rock's giving unit is the family. Children carry their family's status, as Rock
        sets it when a family is added, so a child is Engaged when an adult in the home qualifies.
    */
    const isPersonActive = p => {
        const types = groupTypesByPerson.get( p.guid ) || new Set();
        return types.has( "small-group" ) || types.has( "serving" );
    };
    const isFamilyGiving = givers.size > 0;
    const hasActiveAdult = ( h.members || [] ).some( p => p.familyRole === "adult" && isPersonActive( p ) );

    for ( const p of h.members || [] ) {
        const owner = `${file} (${p.key})`;
        for ( const c of p.connectionRequests || [] ) {
            if ( !connections.opportunities.some( o => o.team === c.opportunity ) ) {
                errors.push( `${owner}: connection request for "${c.opportunity}", which no opportunity in configuration/connections.json places people on.` );
            }
            if ( !connections.requestStatuses.some( s => s.name === c.status ) ) {
                errors.push( `${owner}: connection request status "${c.status}" is not in configuration/connections.json.` );
            }
            if ( ( c.state === "Connected" ) !== ( c.connected !== undefined ) ) {
                errors.push( `${owner}: a connection request has a connected date exactly when its state is Connected.` );
            }
            if ( ( c.state === "FutureFollowUp" ) !== ( c.followUpInDays !== undefined ) ) {
                errors.push( `${owner}: a connection request has followUpInDays exactly when its state is FutureFollowUp.` );
            }
        }
        for ( const r of p.relationships || [] ) {
            if ( !people.has( r.with ) ) {
                errors.push( `${owner}: relationship "${r.type}" points to unknown person ${r.with}.` );
            }
            else if ( r.type === "business" && people.get( r.with ).person.recordType !== "business" ) {
                errors.push( `${owner}: relationship "business" points to ${r.with}, which is not a business record.` );
            }
        }
        for ( const f of p.follows || [] ) {
            const exists = f.type === "group" ? groupKeys.has( f.target ) : people.has( f.target );
            if ( !exists ) {
                errors.push( `${owner}: follows unknown ${f.type} ${f.target}.` );
            }
        }

        // Status must agree with the data.
        if ( p.connectionStatus === "Leading" && !leaders.has( p.guid ) ) {
            warnings.push( `${owner}: Leading, but does not lead any Cast group.` );
        }
        const isEngagedByFamily = p.familyRole === "child" && hasActiveAdult;
        if ( p.connectionStatus === "Engaged" && !isPersonActive( p ) && !isFamilyGiving && !isEngagedByFamily ) {
            warnings.push( `${owner}: Engaged, but has no small group, serving team, or giving${p.familyRole === "child" ? ", and no adult at home does either" : ""}.` );
        }
    }
}

/*
    Notes (decision 78). Each file in notes/ holds one household's hand-written staff notes. Types
    and staff come from configuration/notes.json, and the allowed fields from note.schema.json.
    Staff never write about their own household, a reply comes from someone else on or after the
    note's day, and a private note, which only its author sees, has no replies and no mentions.
*/
const noteConfiguration = JSON.parse( fs.readFileSync( path.join( castDir, "..", "configuration", "notes.json" ), "utf8" ) );
const NOTE_TYPES = new Map( noteConfiguration.types.map( t => [ t.key, t ] ) );
const STAFF = new Map( noteConfiguration.staff.map( s => [ s.key, s ] ) );
const noteSchema = JSON.parse( fs.readFileSync( path.join( castDir, "note.schema.json" ), "utf8" ) );
const TIME = /^([01][0-9]|2[0-3]):[0-5][0-9]$/;
const MENTION = /\{@([^}]*)\}/g;
const noteFiles = readFolder( "notes" );
let noteCount = 0;

/**
 * Checks an object's fields against a definition in note.schema.json: required fields are present and no others appear.
 */
function checkFields( owner, value, definition ) {
    if ( !value || typeof value !== "object" || Array.isArray( value ) ) {
        errors.push( `${owner}: must be an object.` );
        return false;
    }
    for ( const field of definition.required ) {
        if ( value[ field ] === undefined ) {
            errors.push( `${owner}: missing ${field}.` );
        }
    }
    for ( const field of Object.keys( value ) ) {
        if ( !( field in definition.properties ) ) {
            errors.push( `${owner}: unknown field "${field}".` );
        }
    }
    return true;
}

/**
 * Checks what a note and its replies share: Guid, a staff author outside the household, the date, the time, the text, and mentions.
 * Returns the staff keys the text mentions.
 */
function checkWrittenEntry( owner, entry, householdGuids ) {
    claimGuid( entry.guid, owner );
    const staff = STAFF.get( entry.author );
    if ( !staff ) {
        errors.push( `${owner}: author "${entry.author}" is not a staff key in configuration/notes.json.` );
    }
    else if ( householdGuids.has( staff.guid ) ) {
        errors.push( `${owner}: ${staff.name} is a member of this household, and staff never write notes about their own household.` );
    }
    const daysAgo = entry.written && entry.written.daysAgo;
    if ( !entry.written || Object.keys( entry.written ).join() !== "daysAgo" || !Number.isInteger( daysAgo ) || daysAgo < 0 ) {
        errors.push( `${owner}: written must be { "daysAgo": n } with a whole number of days, 0 or more.` );
    }
    if ( !TIME.test( entry.time || "" ) ) {
        errors.push( `${owner}: time "${entry.time}" is not HH:mm on a 24-hour clock.` );
    }
    const text = typeof entry.text === "string" ? entry.text : "";
    if ( !text.trim() ) {
        errors.push( `${owner}: text is empty.` );
    }
    // Writing rule 7 in content/notes/README.md: straight quotes, no dashes as punctuation, no emoji.
    if ( /[^ -~]/.test( text ) || text.includes( "--" ) ) {
        errors.push( `${owner}: text has characters that are not plain. Use straight quotes, commas, or periods.` );
    }
    const mentions = [ ...text.matchAll( MENTION ) ].map( m => m[ 1 ] );
    for ( const key of mentions.filter( k => !STAFF.has( k ) ) ) {
        errors.push( `${owner}: mention {@${key}} is not a staff key in configuration/notes.json.` );
    }
    return mentions;
}

for ( const { file, data: n } of noteFiles ) {
    if ( !checkFields( file, n, noteSchema ) ) {
        continue;
    }
    if ( n.schemaVersion !== 1 ) {
        errors.push( `${file}: schemaVersion must be 1.` );
    }
    if ( `${n.household}.json` !== path.basename( file ) ) {
        errors.push( `${file}: household "${n.household}" does not match the file name.` );
    }
    const household = households.find( h => h.data.key === n.household );
    if ( !household ) {
        errors.push( `${file}: household "${n.household}" is not a Cast household.` );
        continue;
    }
    const members = new Map( household.data.members.map( p => [ p.key, p ] ) );
    const householdGuids = new Set( household.data.members.map( p => p.guid ) );
    if ( !Array.isArray( n.notes ) || n.notes.length === 0 ) {
        errors.push( `${file}: notes must be a non-empty list.` );
        continue;
    }

    const noteKeys = new Set();
    for ( const note of n.notes ) {
        noteCount++;
        const owner = `${file} (${note.key})`;
        if ( !checkFields( owner, note, noteSchema.$defs.note ) ) {
            continue;
        }
        if ( !KEY.test( note.key || "" ) ) {
            errors.push( `${owner}: key is not a lowercase kebab-case slug.` );
        }
        if ( noteKeys.has( note.key ) ) {
            errors.push( `${owner}: note key is used twice in this file.` );
        }
        noteKeys.add( note.key );
        if ( !members.has( note.about ) ) {
            errors.push( `${owner}: about "${note.about}" is not a member of household "${n.household}".` );
        }
        const type = NOTE_TYPES.get( note.type );
        if ( !type ) {
            errors.push( `${owner}: type "${note.type}" is not a note type in configuration/notes.json.` );
        }
        else if ( type.isAlwaysAlert && note.isAlert === false ) {
            errors.push( `${owner}: a ${type.name} note is always an alert, so isAlert may not be false.` );
        }
        for ( const flag of [ "isAlert", "isPrivate", "isPinned" ] ) {
            if ( note[ flag ] !== undefined && typeof note[ flag ] !== "boolean" ) {
                errors.push( `${owner}: ${flag} must be true or false.` );
            }
        }

        const mentions = checkWrittenEntry( owner, note, householdGuids );
        const replies = note.replies || [];
        if ( note.isPrivate && ( replies.length || mentions.length ) ) {
            errors.push( `${owner}: a private note is seen only by its author, so it has no replies or mentions.` );
        }

        // A confidential type (Counseling) is viewed by only a few roles, so nothing may pull in other staff.
        const isConfidential = type && type.isConfidential;
        if ( isConfidential && !( note.author in type.authors ) ) {
            errors.push( `${owner}: only the ${type.name} type's own authors write its notes.` );
        }
        if ( isConfidential && mentions.length ) {
            errors.push( `${owner}: a ${type.name} note is confidential, so it mentions no one.` );
        }
        for ( const [ index, reply ] of replies.entries() ) {
            const replyOwner = `${owner} reply ${index + 1}`;
            if ( !checkFields( replyOwner, reply, noteSchema.$defs.reply ) ) {
                continue;
            }
            const replyMentions = checkWrittenEntry( replyOwner, reply, householdGuids );
            if ( isConfidential && ( replyMentions.length || !( reply.author in type.authors ) ) ) {
                errors.push( `${replyOwner}: a ${type.name} note is confidential, so only its own authors reply, and mention no one.` );
            }
            if ( reply.author === note.author ) {
                errors.push( `${replyOwner}: a reply comes from a different staff member than the note's author.` );
            }
            const noteDay = note.written && note.written.daysAgo;
            const replyDay = reply.written && reply.written.daysAgo;
            const isBeforeNote = replyDay > noteDay || ( replyDay === noteDay && reply.time < note.time );
            if ( isBeforeNote ) {
                errors.push( `${replyOwner}: written before the note it answers.` );
            }
        }
    }
}

/*
    Businesses. A church keeps a business record because the business gives, so every Cast
    business has a contact and giving that has not stopped. The main campus has at least six
    businesses, and the Cast supplies all of them for Core 1 and Core 3 (the loader's
    BusinessGenerator.MainCampusMinimum).
*/
const MAIN_CAMPUS_BUSINESS_MINIMUM = 6;
const contactedBusinesses = new Set( [ ...people.values() ].flatMap( ( { person } ) => ( person.relationships || [] ).filter( r => r.type === "business" ).map( r => r.with ) ) );
const businessHouseholds = households.filter( h => h.data.kind === "business" );
for ( const { file, data: h } of businessHouseholds ) {
    const business = h.members.find( p => p.recordType === "business" );
    if ( !business || h.members.length !== 1 ) {
        errors.push( `${file}: a business household has exactly one member, its business record.` );
        continue;
    }
    if ( !contactedBusinesses.has( business.guid ) ) {
        errors.push( `${file}: no Cast person is a contact of this business (a "business" relationship on the contact).` );
    }
    if ( !( h.giving || [] ).some( g => g.person === business.key && !g.ended ) ) {
        errors.push( `${file}: every business gives, and its giving must not have ended.` );
    }
    if ( h.address && h.address.type !== "work" ) {
        errors.push( `${file}: a business's address is a work address.` );
    }
}
if ( businessHouseholds.length < MAIN_CAMPUS_BUSINESS_MINIMUM ) {
    errors.push( `The Cast has ${businessHouseholds.length} businesses; the main campus needs at least ${MAIN_CAMPUS_BUSINESS_MINIMUM}.` );
}

/**
 * Reads a JPEG's width and height from its start-of-frame marker, or returns null when the file is not a JPEG.
 */
function jpegSize( file ) {
    const bytes = fs.readFileSync( file );
    if ( bytes[ 0 ] !== 0xFF || bytes[ 1 ] !== 0xD8 ) {
        return null;
    }
    let offset = 2;
    while ( offset + 9 < bytes.length && bytes[ offset ] === 0xFF ) {
        const marker = bytes[ offset + 1 ];
        const isStartOfFrame = marker >= 0xC0 && marker <= 0xCF && marker !== 0xC4 && marker !== 0xC8 && marker !== 0xCC;
        if ( isStartOfFrame ) {
            return { height: bytes.readUInt16BE( offset + 5 ), width: bytes.readUInt16BE( offset + 7 ) };
        }
        offset += 2 + bytes.readUInt16BE( offset + 2 );
    }
    return null;
}

// Pass 4: photos. Every manifest entry has a 400-pixel JPEG and matches its Cast person,
// and every Cast person who should have a photo has one.
const photosDir = path.join( castDir, "..", "photos" );
const photoManifest = JSON.parse( fs.readFileSync( path.join( photosDir, "manifest.json" ), "utf8" ) );
const photoFiles = new Set();
const castPhotos = new Set();
for ( const image of photoManifest.images ) {
    const owner = `photos/manifest.json (${image.file})`;
    if ( photoFiles.has( image.file ) ) {
        errors.push( `${owner}: listed twice.` );
    }
    photoFiles.add( image.file );

    const file = path.join( photosDir, image.file );
    const size = fs.existsSync( file ) ? jpegSize( file ) : null;
    if ( !size ) {
        errors.push( `${owner}: the file is missing or is not a JPEG.` );
    }
    else if ( size.width !== 400 || size.height !== 400 ) {
        errors.push( `${owner}: is ${size.width} by ${size.height}; delivered photos are 400 by 400.` );
    }

    // The image model's original is kept beside it, in whatever format the model made.
    const originalBase = path.join( photosDir, "originals", image.file.replace( /\.jpg$/, "" ) );
    if ( ![ ".jpg", ".jpeg", ".png" ].some( ext => fs.existsSync( originalBase + ext ) ) ) {
        errors.push( `${owner}: has no original in photos/originals/.` );
    }

    if ( image.set !== "cast" ) {
        continue;
    }
    const household = households.find( h => h.data.key === image.household );
    const p = household && household.data.members.find( m => m.key === image.member );
    if ( !p ) {
        errors.push( `${owner}: no Cast member "${image.member}" in household "${image.household}".` );
        continue;
    }
    if ( image.file !== `cast/${image.household}-${image.member}.jpg` ) {
        errors.push( `${owner}: Cast photos are named cast/{household}-{member}.jpg.` );
    }
    if ( ( p.photo && p.photo.isOmitted ) || p.recordType === "business" ) {
        errors.push( `${owner}: ${image.household} (${image.member}) has no photo on purpose.` );
    }
    if ( p.gender !== image.gender ) {
        errors.push( `${owner}: photo gender ${image.gender} does not match the Cast person (${p.gender}).` );
    }
    // Ages drift as stories are edited; a photo a few years off still works.
    if ( p.birth && Math.abs( p.birth.age - image.age ) > 3 ) {
        warnings.push( `${owner}: photo age ${image.age} is more than 3 years from the Cast person's age (${p.birth.age}).` );
    }
    castPhotos.add( `${image.household}/${image.member}` );
}
for ( const { file, data: h } of households ) {
    for ( const p of h.members || [] ) {
        const needsPhoto = !( p.photo && p.photo.isOmitted ) && p.recordType !== "business";
        if ( needsPhoto && !castPhotos.has( `${h.key}/${p.key}` ) ) {
            warnings.push( `${file} (${p.key}): has no photo yet. See photos/brief.md.` );
        }
    }
}

// Pass 5: the Cast's prayer requests (decision 67). Each names a real Cast adult or a guest, a prayer category
// that gets requests, and a state whose dates make sense. At least 8 are always current, so every dataset's
// prayer list and prayer session have requests to pray through.
const PRAYER_STATES = [ "current", "waiting", "flagged", "expired", "answered" ];
const MINIMUM_CURRENT_PRAYER_REQUESTS = 8;
const prayerFile = path.join( castDir, "prayer-requests.json" );
let prayerRequestCount = 0;
if ( fs.existsSync( prayerFile ) ) {
    const prayerCategories = new Set( JSON.parse( fs.readFileSync( path.join( castDir, "..", "configuration", "prayer.json" ), "utf8" ) ).categories.filter( c => c.weight > 0 ).map( c => c.key ) );
    const requests = JSON.parse( fs.readFileSync( prayerFile, "utf8" ) ).requests || [];
    const requestKeys = new Set();
    prayerRequestCount = requests.length;
    for ( const r of requests ) {
        const owner = `prayer-requests.json (${r.key})`;
        if ( !KEY.test( r.key || "" ) || requestKeys.has( r.key ) ) {
            errors.push( `${owner}: key must be a unique lowercase slug.` );
        }
        requestKeys.add( r.key );
        claimGuid( r.guid, owner );
        if ( !!r.requester === !!r.guest ) {
            errors.push( `${owner}: needs exactly one of requester or guest.` );
        }
        const requester = r.requester ? people.get( r.requester ) : null;
        if ( r.requester && !requester ) {
            errors.push( `${owner}: requester ${r.requester} is not a Cast person.` );
        }
        else if ( requester && ( requester.person.familyRole !== "adult" || requester.person.isDeceased || [ "inactive", "pending" ].includes( requester.person.recordStatus ) || requester.household.duplicateOf ) ) {
            errors.push( `${owner}: requester must be a living adult on an active record who is not a duplicate.` );
        }
        if ( r.guest && ( !r.guest.firstName || !r.guest.lastName || ( r.guest.email && !ALLOWED_EMAIL_DOMAINS.includes( r.guest.email.split( "@" )[ 1 ] ) ) ) ) {
            errors.push( `${owner}: a guest needs a first and last name, and any email must use a reserved domain.` );
        }
        if ( !prayerCategories.has( r.category ) ) {
            errors.push( `${owner}: category "${r.category}" is not a prayer category that gets requests in configuration/prayer.json.` );
        }
        if ( !PRAYER_STATES.includes( r.state ) ) {
            errors.push( `${owner}: state "${r.state}" is not one of ${PRAYER_STATES.join( ", " )}.` );
        }

        // A current or waiting request is a few days old, a flagged one was flagged within its 14 days, and an
        // expired or answered one is past its 14 days.
        const daysAgo = r.entered && r.entered.daysAgo;
        const maximumDaysAgo = r.state === "flagged" ? 13 : 5;
        if ( [ "current", "waiting", "flagged" ].includes( r.state ) && !( Number.isInteger( daysAgo ) && daysAgo >= 1 && daysAgo <= maximumDaysAgo ) ) {
            errors.push( `${owner}: a ${r.state} request must be entered 1 to ${maximumDaysAgo} days ago (entered.daysAgo).` );
        }
        if ( [ "expired", "answered" ].includes( r.state ) && Number.isInteger( daysAgo ) && daysAgo < 20 ) {
            errors.push( `${owner}: an ${r.state} request must be entered at least 20 days ago.` );
        }
        if ( ( r.state === "answered" ) !== !!r.answer ) {
            errors.push( `${owner}: only answered requests have an answer, and every answered request has one.` );
        }
        if ( r.state === "waiting" ? ( r.prayers || 0 ) !== 0 : !( r.prayers >= 1 ) ) {
            errors.push( `${owner}: a waiting request has no prayers; every other request has at least one.` );
        }

        const comments = r.comments || [];
        if ( comments.some( c => ![ "team", "requester" ].includes( c.by ) || !c.text ) ) {
            errors.push( `${owner}: each comment needs text and is by "team" or "requester".` );
        }
        if ( comments.some( c => c.by === "requester" ) && ( !r.requester || !comments.some( c => c.by === "team" ) ) ) {
            errors.push( `${owner}: a requester reply needs a Cast requester and a team comment to reply to.` );
        }
        if ( comments.filter( c => c.by === "requester" ).length > 1 ) {
            errors.push( `${owner}: a request has at most one requester reply.` );
        }
        if ( comments.filter( c => c.by === "team" ).length > ( r.prayers || 0 ) ) {
            errors.push( `${owner}: each team comment follows a prayer, so there cannot be more team comments than prayers.` );
        }
        if ( comments.length && ( !r.allowComments || r.state === "waiting" ) ) {
            errors.push( `${owner}: comments need allowComments and an approved request.` );
        }

        if ( r.group ) {
            const group = groups.find( g => g.data.key === r.group );
            if ( !group ) {
                errors.push( `${owner}: group "${r.group}" is not a Cast group.` );
            }
            else if ( !( group.data.members || [] ).some( m => m.guid === r.requester && !m.left ) ) {
                errors.push( `${owner}: the requester is not a current member of group "${r.group}".` );
            }
        }

        for ( const text of [ r.text, r.answer, ...comments.map( c => c.text ) ].filter( Boolean ) ) {
            if ( /[–—‘’“”]/.test( text ) ) {
                errors.push( `${owner}: use plain dashes and straight quotes.` );
            }
        }
        if ( !r.text || r.text.length < 40 || r.text.length > 450 ) {
            errors.push( `${owner}: text must be 40 to 450 characters.` );
        }
    }

    const current = requests.filter( r => r.state === "current" ).length;
    if ( current < MINIMUM_CURRENT_PRAYER_REQUESTS ) {
        errors.push( `prayer-requests.json: ${current} current requests; at least ${MINIMUM_CURRENT_PRAYER_REQUESTS} keep every dataset's prayer list full.` );
    }
    for ( const state of PRAYER_STATES ) {
        if ( !requests.some( r => r.state === state ) ) {
            errors.push( `prayer-requests.json: no request is ${state}, so that prayer screen state is not covered.` );
        }
    }
}

/*
    Pass 6: the Cast's text threads (cast/sms-threads.md). Each is one Cast person texting one of the church's
    phone numbers, with messages in time order and a state SMS Conversations can show. Together they cover every
    state, a long back-and-forth, a STOP opt-out, the student line, and every number, and a few are unread from
    the last days before install, so every dataset opens to a live inbox.
*/
const SMS_STATES = [ "unread", "read", "stopped" ];
const SMS_FROM = [ "person", "staff" ];
const SMS_THREAD_FIELDS = [ "key", "person", "name", "number", "story", "state", "messages" ];
const SMS_MESSAGE_FIELDS = [ "from", "daysAgo", "time", "text" ];
const SMS_TEXT_LENGTH = { min: 1, max: 320 };
const SMS_STOP = "STOP";
const SMS_STOP_WINDOW_DAYS = 60;
const SMS_COVERAGE = {
    unread: { count: 4, minDaysAgo: 1, maxDaysAgo: 3 },
    read: { count: 4, minDaysAgo: 2, maxDaysAgo: 300, newestAtMost: 30, oldestAtLeast: 180 },
    longMessages: 12,
    longSenderChanges: 6,
    stopped: { count: 1, minDaysBefore: 3, maxDaysBefore: 50 },
    perNumber: 2
};

// A Cast child with no grade on file is in grade (age - 5): Ava Brooks at 14 is a freshman, Ethan Fischer at 17 a senior.
const GRADE_AGE_OFFSET = 5;
const communicationConfiguration = JSON.parse( fs.readFileSync( path.join( castDir, "..", "configuration", "communications.json" ), "utf8" ) );
const PHONE_NUMBERS = new Map( communicationConfiguration.phoneNumbers.numbers.map( n => [ n.key, n ] ) );
const STUDENT_GRADES = communicationConfiguration.lists.membership.studentGrades;

/**
 * True when a Cast person is a student in the student ministry's grades.
 */
function isStudent( p ) {
    const grade = p.grade !== undefined ? p.grade : ( p.birth ? p.birth.age - GRADE_AGE_OFFSET : undefined );
    return p.familyRole === "child" && grade >= STUDENT_GRADES.min && grade <= STUDENT_GRADES.max;
}

const smsFile = path.join( castDir, "sms-threads.json" );
let smsThreadCount = 0;
let smsMessageCount = 0;
if ( fs.existsSync( smsFile ) ) {
    const threads = JSON.parse( fs.readFileSync( smsFile, "utf8" ) ).threads;
    if ( !Array.isArray( threads ) || threads.length === 0 ) {
        errors.push( "sms-threads.json: threads must be a non-empty list." );
    }
    const threadKeys = new Set();
    const valid = [];
    for ( const t of Array.isArray( threads ) ? threads : [] ) {
        smsThreadCount++;
        const owner = `sms-threads.json (${t && t.key})`;
        if ( !t || typeof t !== "object" || Array.isArray( t ) ) {
            errors.push( `${owner}: must be an object.` );
            continue;
        }
        for ( const field of SMS_THREAD_FIELDS.filter( f => t[ f ] === undefined ) ) {
            errors.push( `${owner}: missing ${field}.` );
        }
        for ( const field of Object.keys( t ).filter( f => !SMS_THREAD_FIELDS.includes( f ) ) ) {
            errors.push( `${owner}: unknown field "${field}".` );
        }
        if ( !KEY.test( t.key || "" ) || threadKeys.has( t.key ) ) {
            errors.push( `${owner}: key must be a unique lowercase slug.` );
        }
        threadKeys.add( t.key );

        // The person: a living Cast person on an active record, not a duplicate, with a mobile phone.
        const entry = people.get( t.person );
        if ( !entry ) {
            errors.push( `${owner}: person ${t.person} is not a Cast person.` );
        }
        else {
            const p = entry.person;
            if ( p.isDeceased || p.recordStatus !== "active" || p.recordType === "business" || entry.household.duplicateOf ) {
                errors.push( `${owner}: the person must be living, on an active record, and not a business or duplicate.` );
            }
            if ( !p.phones || !p.phones.mobile ) {
                errors.push( `${owner}: ${p.firstName} ${p.lastName} has no mobile phone in ${entry.file}.` );
            }
            const name = `${p.nickName || p.firstName} ${p.lastName}`;
            if ( t.name !== name ) {
                errors.push( `${owner}: name "${t.name}" does not match the Cast person, "${name}".` );
            }
        }

        // The number: a church phone number, never the person's own, and the student line only for students and their parents.
        const number = PHONE_NUMBERS.get( t.number );
        if ( !number ) {
            errors.push( `${owner}: number "${t.number}" is not a phone number key in configuration/communications.json (${[ ...PHONE_NUMBERS.keys() ].join( ", " )}).` );
        }
        else if ( number.assignedTo && number.assignedTo === t.person ) {
            errors.push( `${owner}: the person owns the "${t.number}" number, so they cannot text it.` );
        }
        if ( t.number === "students" && entry ) {
            const isParent = entry.person.familyRole === "adult" && entry.household.members.some( isStudent );
            if ( !isStudent( entry.person ) && !isParent ) {
                errors.push( `${owner}: the student line is for students in grades ${STUDENT_GRADES.min} to ${STUDENT_GRADES.max} and their parents.` );
            }
        }
        if ( typeof t.story !== "string" || !t.story.trim() ) {
            errors.push( `${owner}: story is empty.` );
        }
        if ( !SMS_STATES.includes( t.state ) ) {
            errors.push( `${owner}: state "${t.state}" is not one of ${SMS_STATES.join( ", " )}.` );
        }

        // The messages: known fields, plain text, and oldest first.
        const messages = Array.isArray( t.messages ) ? t.messages : [];
        if ( messages.length === 0 ) {
            errors.push( `${owner}: messages must be a non-empty list.` );
            continue;
        }
        let isWellFormed = true;
        messages.forEach( ( msg, i ) => {
            smsMessageCount++;
            const at = `${owner} message ${i + 1}`;
            if ( !msg || typeof msg !== "object" || Array.isArray( msg ) ) {
                errors.push( `${at}: must be an object.` );
                isWellFormed = false;
                return;
            }
            const fields = Object.keys( msg );
            if ( SMS_MESSAGE_FIELDS.some( f => msg[ f ] === undefined ) || fields.some( f => !SMS_MESSAGE_FIELDS.includes( f ) ) ) {
                errors.push( `${at}: has exactly the fields ${SMS_MESSAGE_FIELDS.join( ", " )}.` );
            }
            if ( !SMS_FROM.includes( msg.from ) ) {
                errors.push( `${at}: from "${msg.from}" is not one of ${SMS_FROM.join( ", " )}.` );
            }
            if ( !Number.isInteger( msg.daysAgo ) || msg.daysAgo < 0 ) {
                errors.push( `${at}: daysAgo must be a whole number of days, 0 or more.` );
                isWellFormed = false;
            }
            if ( !TIME.test( msg.time || "" ) ) {
                errors.push( `${at}: time "${msg.time}" is not HH:mm on a 24-hour clock.` );
                isWellFormed = false;
            }
            const text = typeof msg.text === "string" ? msg.text : "";
            if ( text.length < SMS_TEXT_LENGTH.min || text.length > SMS_TEXT_LENGTH.max || text !== text.trim() ) {
                errors.push( `${at}: text must be ${SMS_TEXT_LENGTH.min} to ${SMS_TEXT_LENGTH.max} characters with no leading or trailing spaces.` );
            }
            if ( /[^ -~]/.test( text ) || text.includes( "--" ) ) {
                errors.push( `${at}: text has characters that are not plain. Use straight quotes, commas, or periods.` );
            }
            const previous = messages[ i - 1 ];
            if ( i > 0 && previous && ( previous.daysAgo < msg.daysAgo || ( previous.daysAgo === msg.daysAgo && previous.time > msg.time ) ) ) {
                errors.push( `${at}: comes before the message above it. Messages are oldest first.` );
            }
            if ( msg.from === "person" && text === SMS_STOP && !( t.state === "stopped" && i === messages.length - 1 ) ) {
                errors.push( `${at}: only a stopped thread has a STOP, and it is the last message.` );
            }
        } );
        if ( !isWellFormed ) {
            continue;
        }

        // The state must agree with the messages.
        const lastStaff = messages.map( msg => msg.from ).lastIndexOf( "staff" );
        const lastPerson = messages.map( msg => msg.from ).lastIndexOf( "person" );
        if ( t.state === "unread" && lastPerson < lastStaff ) {
            errors.push( `${owner}: an unread thread needs at least one person text after the last staff text.` );
        }
        if ( t.state === "stopped" ) {
            const stop = messages[ messages.length - 1 ];
            const staffBefore = messages.filter( msg => msg.from === "staff" && msg.daysAgo - stop.daysAgo <= SMS_STOP_WINDOW_DAYS );
            if ( stop.from !== "person" || stop.text !== SMS_STOP ) {
                errors.push( `${owner}: a stopped thread ends with a person text that is exactly "${SMS_STOP}".` );
            }
            else if ( staffBefore.length === 0 ) {
                errors.push( `${owner}: a stopped thread needs a staff text within the ${SMS_STOP_WINDOW_DAYS} days before the STOP.` );
            }
        }
        valid.push( t );
    }

    // Coverage (cast/sms-threads.md, "What the threads cover").
    const days = t => t.messages.map( msg => msg.daysAgo );
    const unread = valid.filter( t => t.state === "unread" && days( t ).every( d => d >= SMS_COVERAGE.unread.minDaysAgo && d <= SMS_COVERAGE.unread.maxDaysAgo ) );
    if ( unread.length < SMS_COVERAGE.unread.count ) {
        errors.push( `sms-threads.json: ${unread.length} unread threads with every text ${SMS_COVERAGE.unread.minDaysAgo} to ${SMS_COVERAGE.unread.maxDaysAgo} days ago; at least ${SMS_COVERAGE.unread.count} keep every dataset's inbox live.` );
    }
    const readRule = SMS_COVERAGE.read;
    const read = valid.filter( t => t.state === "read" );
    for ( const t of read.filter( t => days( t ).some( d => d < readRule.minDaysAgo || d > readRule.maxDaysAgo ) ) ) {
        errors.push( `sms-threads.json (${t.key}): a read thread's texts are ${readRule.minDaysAgo} to ${readRule.maxDaysAgo} days ago.` );
    }
    const lastDays = read.map( t => Math.min( ...days( t ) ) );
    if ( read.length < readRule.count || Math.min( ...lastDays ) > readRule.newestAtMost || Math.max( ...lastDays ) < readRule.oldestAtLeast ) {
        errors.push( `sms-threads.json: needs at least ${readRule.count} read threads, spread so one ends within ${readRule.newestAtMost} days and one ends ${readRule.oldestAtLeast} or more days ago.` );
    }
    const senderChanges = t => t.messages.filter( ( msg, i ) => i > 0 && msg.from !== t.messages[ i - 1 ].from ).length;
    if ( !valid.some( t => t.messages.length >= SMS_COVERAGE.longMessages && senderChanges( t ) >= SMS_COVERAGE.longSenderChanges ) ) {
        errors.push( `sms-threads.json: needs a back-and-forth of ${SMS_COVERAGE.longMessages} or more texts that changes sides at least ${SMS_COVERAGE.longSenderChanges} times.` );
    }
    const stopRule = SMS_COVERAGE.stopped;
    const stopped = valid.filter( t => t.state === "stopped" );
    if ( stopped.length !== stopRule.count ) {
        errors.push( `sms-threads.json: has ${stopped.length} stopped threads; exactly ${stopRule.count} covers the STOP opt-out.` );
    }
    for ( const t of stopped ) {
        const stopDay = t.messages[ t.messages.length - 1 ].daysAgo;
        if ( !t.messages.some( msg => msg.from === "staff" && msg.daysAgo - stopDay >= stopRule.minDaysBefore && msg.daysAgo - stopDay <= stopRule.maxDaysBefore ) ) {
            errors.push( `sms-threads.json (${t.key}): the STOP needs a staff text ${stopRule.minDaysBefore} to ${stopRule.maxDaysBefore} days before it.` );
        }
    }
    for ( const key of PHONE_NUMBERS.keys() ) {
        const count = valid.filter( t => t.number === key ).length;
        if ( count < SMS_COVERAGE.perNumber ) {
            errors.push( `sms-threads.json: ${count} threads on the "${key}" number; every number needs at least ${SMS_COVERAGE.perNumber}.` );
        }
    }
}

/*
    Pass 7: the Cast's staff emails (cast/staff-emails.md). Each is one personal email from a staff member
    in configuration/notes.json to a living Cast person on an active record whose email is active and
    allows email, dated inside the last two years, with a short subject and a few plain-text paragraphs.
*/
const EMAIL_FIELDS = [ "key", "from", "to", "name", "story", "daysAgo", "time", "subject", "body" ];
const EMAIL_DAYS_AGO = { min: 1, max: 730 };
const EMAIL_SUBJECT_LENGTH = { min: 10, max: 70 };
const EMAIL_PARAGRAPHS = { min: 3, max: 8 };
const EMAIL_PARAGRAPH_LENGTH = { min: 2, max: 400 };
const STAFF_KEYS = JSON.parse( fs.readFileSync( path.join( castDir, "..", "configuration", "notes.json" ), "utf8" ) ).staff.map( s => s.key );

/**
 * True when a string is plain printable ASCII with no double hyphen.
 */
function isPlainText( text ) {
    return !/[^ -~]/.test( text ) && !text.includes( "--" );
}

const staffEmailFile = path.join( castDir, "staff-emails.json" );
let staffEmailCount = 0;
if ( fs.existsSync( staffEmailFile ) ) {
    const emails = JSON.parse( fs.readFileSync( staffEmailFile, "utf8" ) ).emails;
    if ( !Array.isArray( emails ) || emails.length === 0 ) {
        errors.push( "staff-emails.json: emails must be a non-empty list." );
    }
    const emailKeys = new Set();
    for ( const e of Array.isArray( emails ) ? emails : [] ) {
        staffEmailCount++;
        const owner = `staff-emails.json (${e && e.key})`;
        if ( !e || typeof e !== "object" || Array.isArray( e ) ) {
            errors.push( `${owner}: must be an object.` );
            continue;
        }
        for ( const field of EMAIL_FIELDS.filter( f => e[ f ] === undefined ) ) {
            errors.push( `${owner}: missing ${field}.` );
        }
        for ( const field of Object.keys( e ).filter( f => !EMAIL_FIELDS.includes( f ) ) ) {
            errors.push( `${owner}: unknown field "${field}".` );
        }
        if ( !KEY.test( e.key || "" ) || emailKeys.has( e.key ) ) {
            errors.push( `${owner}: key must be a unique lowercase slug.` );
        }
        emailKeys.add( e.key );
        if ( !STAFF_KEYS.includes( e.from ) ) {
            errors.push( `${owner}: from "${e.from}" is not a staff key in configuration/notes.json (${STAFF_KEYS.join( ", " )}).` );
        }

        // The recipient: a living Cast person on an active record, not a duplicate, with an active email that allows email.
        const entry = people.get( e.to );
        if ( !entry ) {
            errors.push( `${owner}: to ${e.to} is not a Cast person.` );
        }
        else {
            const p = entry.person;
            if ( p.isDeceased || p.recordStatus !== "active" || p.recordType === "business" || entry.household.duplicateOf ) {
                errors.push( `${owner}: the recipient must be living, on an active record, and not a business or duplicate.` );
            }
            if ( !p.email || p.emailIsActive !== true || p.emailDoNotEmail === true ) {
                errors.push( `${owner}: ${p.firstName} ${p.lastName} needs an email that is active and allows email in ${entry.file}.` );
            }
            const name = `${p.nickName || p.firstName} ${p.lastName}`;
            if ( e.name !== name ) {
                errors.push( `${owner}: name "${e.name}" does not match the Cast person, "${name}".` );
            }
        }
        if ( typeof e.story !== "string" || !e.story.trim() ) {
            errors.push( `${owner}: story is empty.` );
        }
        if ( !Number.isInteger( e.daysAgo ) || e.daysAgo < EMAIL_DAYS_AGO.min || e.daysAgo > EMAIL_DAYS_AGO.max ) {
            errors.push( `${owner}: daysAgo must be a whole number from ${EMAIL_DAYS_AGO.min} to ${EMAIL_DAYS_AGO.max}.` );
        }
        if ( !TIME.test( e.time || "" ) ) {
            errors.push( `${owner}: time "${e.time}" is not HH:mm on a 24-hour clock.` );
        }

        // The subject and body: plain text, short, and a few paragraphs.
        const subject = typeof e.subject === "string" ? e.subject : "";
        if ( subject.length < EMAIL_SUBJECT_LENGTH.min || subject.length > EMAIL_SUBJECT_LENGTH.max || subject !== subject.trim() ) {
            errors.push( `${owner}: subject must be ${EMAIL_SUBJECT_LENGTH.min} to ${EMAIL_SUBJECT_LENGTH.max} characters with no leading or trailing spaces.` );
        }
        if ( !isPlainText( subject ) ) {
            errors.push( `${owner}: subject has characters that are not plain. Use straight quotes, commas, or periods.` );
        }
        const body = Array.isArray( e.body ) ? e.body : [];
        if ( body.length < EMAIL_PARAGRAPHS.min || body.length > EMAIL_PARAGRAPHS.max ) {
            errors.push( `${owner}: body must have ${EMAIL_PARAGRAPHS.min} to ${EMAIL_PARAGRAPHS.max} paragraphs.` );
        }
        body.forEach( ( paragraph, i ) => {
            const at = `${owner} paragraph ${i + 1}`;
            const text = typeof paragraph === "string" ? paragraph : "";
            if ( text.length < EMAIL_PARAGRAPH_LENGTH.min || text.length > EMAIL_PARAGRAPH_LENGTH.max || text !== text.trim() ) {
                errors.push( `${at}: must be ${EMAIL_PARAGRAPH_LENGTH.min} to ${EMAIL_PARAGRAPH_LENGTH.max} characters with no leading or trailing spaces.` );
            }
            if ( !isPlainText( text ) ) {
                errors.push( `${at}: has characters that are not plain. Use straight quotes, commas, or periods.` );
            }
        } );
    }
}

// One line instead of one per person, since photo ethnicity is filled in household by household.
if ( adultsWithoutPhotoEthnicity.length ) {
    warnings.push( `${adultsWithoutPhotoEthnicity.length} adults have no photo.ethnicity yet (needed before their photos are generated).` );
}

for ( const w of warnings ) {
    console.log( `WARNING ${w}` );
}
for ( const e of errors ) {
    console.log( `ERROR   ${e}` );
}
console.log( `
${households.length} households, ${people.size} people, ${groups.length} groups, ${prayerRequestCount} prayer requests, ${noteCount} notes in ${noteFiles.length} note files, ${smsThreadCount} text threads with ${smsMessageCount} texts, ${staffEmailCount} staff emails. ${errors.length} errors, ${warnings.length} warnings.` );
process.exit( errors.length ? 1 : 0 );
