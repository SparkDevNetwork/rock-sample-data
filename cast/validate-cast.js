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
    every cross-reference between households and groups, and the delivered
    photos in photos/manifest.json. Prints errors
    (exit code 1) and warnings (exit code 0).

    Usage: node validate-cast.js
*/

const fs = require( "fs" );
const path = require( "path" );

const castDir = __dirname;
const GUID = /^[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}$/;
const KEY = /^[a-z0-9]+(-[a-z0-9]+)*$/;
const STATUSES = [ "Prospect", "Participant", "Known", "Connected", "Engaged", "Leading" ];
const ALLOWED_EMAIL_DOMAINS = [ "example.org", "example.com", "rocksolidchurchdemo.com" ];

const errors = [];
const warnings = [];
const smallGroups = JSON.parse( fs.readFileSync( path.join( castDir, "..", "configuration", "small-groups.json" ), "utf8" ) );

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

        if ( !STATUSES.includes( p.connectionStatus ) ) {
            errors.push( `${owner}: connection status "${p.connectionStatus}" is not one of ${STATUSES.join( ", " )}.` );
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
        for ( const r of p.relationships || [] ) {
            if ( !people.has( r.with ) ) {
                errors.push( `${owner}: relationship "${r.type}" points to unknown person ${r.with}.` );
            }
        }
        for ( const f of p.follows || [] ) {
            const exists = f.type === "group" ? groupKeys.has( f.target ) : people.has( f.target );
            if ( !exists ) {
                errors.push( `${owner}: follows unknown ${f.type} ${f.target}.` );
            }
        }
        for ( const n of p.notes || [] ) {
            if ( n.by && !people.has( n.by ) ) {
                errors.push( `${owner}: a note is written by unknown person ${n.by}.` );
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
console.log( `\n${households.length} households, ${people.size} people, ${groups.length} groups. ${errors.length} errors, ${warnings.length} warnings.` );
process.exit( errors.length ? 1 : 0 );
