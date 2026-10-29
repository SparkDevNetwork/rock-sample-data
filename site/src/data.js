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
    Reads the repository's own data files into one model for the site. The site never keeps
    a copy of the Cast or the church: every page is built from these files, so it cannot drift.

    Only these folders are read: cast/, configuration/, datasets/, and photos/. Calibration data
    is never read, so nothing about a calibration source can reach the public site.
*/

const fs = require( "fs" );
const path = require( "path" );

const root = path.resolve( __dirname, "..", ".." );

function readJson( relative ) {
    return JSON.parse( fs.readFileSync( path.join( root, relative ), "utf8" ) );
}

function listJson( relative ) {
    return fs.readdirSync( path.join( root, relative ) )
        .filter( f => f.endsWith( ".json" ) )
        .sort()
        .map( f => readJson( path.join( relative, f ) ) );
}

function exists( relative ) {
    return fs.existsSync( path.join( root, relative ) );
}

/**
 * Sorts people the way a family is introduced: adults by age, then children by age.
 */
function familyOrder( a, b ) {
    const roleA = a.familyRole === "child" ? 1 : 0;
    const roleB = b.familyRole === "child" ? 1 : 0;
    if ( roleA !== roleB ) {
        return roleA - roleB;
    }

    return ( b.birth ? b.birth.age : 0 ) - ( a.birth ? a.birth.age : 0 );
}

/**
 * The life stages a household fits, used by the Cast gallery filters.
 */
function lifeStages( household ) {
    if ( household.kind === "business" ) {
        return [ "business" ];
    }

    const ages = household.members.filter( m => !m.isDeceased && m.birth ).map( m => m.birth.age );
    const adults = household.members.filter( m => m.familyRole !== "child" && !m.isDeceased );
    const stages = [];
    if ( ages.some( a => a < 6 ) ) {
        stages.push( "young-kids" );
    }
    if ( ages.some( a => a >= 6 && a < 13 ) ) {
        stages.push( "kids" );
    }
    if ( ages.some( a => a >= 13 && a < 18 ) ) {
        stages.push( "teens" );
    }
    if ( adults.length === 1 ) {
        stages.push( "single" );
    }
    if ( adults.some( m => m.birth && m.birth.age >= 65 ) ) {
        stages.push( "seniors" );
    }

    return stages;
}

/**
 * A check-in group key as people say it, such as "grades-k-1" as "Grades K-1".
 */
function checkInGroupName( key ) {
    const names = { "crawlers-walkers": "Crawlers and Walkers", "grades-k-1": "Grades K-1", "grades-2-3": "Grades 2-3", "grades-4-6": "Grades 4-6" };
    return names[key] || key.replace( /-/g, " " ).replace( /\b\w/g, c => c.toUpperCase() );
}

function load() {
    const households = listJson( "cast/households" );
    const groups = listJson( "cast/groups" );
    const castPhotos = new Set( fs.readdirSync( path.join( root, "photos/cast" ) ) );
    const people = new Map();

    for ( const household of households ) {
        household.members.sort( familyOrder );
        household.isFeatured = household.members.some( m => m.bio );
        household.stages = lifeStages( household );
        for ( const member of household.members ) {
            const photo = `${household.key}-${member.key}.jpg`;
            member.isPhotoOmitted = !!( member.photo && member.photo.isOmitted ) || household.kind === "business";
            member.photo = castPhotos.has( photo ) ? photo : null;
            member.displayName = `${member.nickName || member.firstName} ${member.lastName}`;
            member.household = household;
            people.set( member.guid.toUpperCase(), member );
        }
    }

    // Each household's hand-written staff notes, counted for its page. The note text stays in the files.
    for ( const household of households ) {
        const file = `cast/notes/${household.key}.json`;
        const notes = exists( file ) ? readJson( file ).notes : [];
        household.noteCount = notes.length;
        household.replyCount = notes.reduce( ( sum, n ) => sum + ( n.replies || [] ).length, 0 );
    }

    // Shared members (a child in two homes) appear in each household they belong to.
    for ( const household of households ) {
        household.shared = ( household.sharedMembers || [] )
            .map( s => people.get( s.guid.toUpperCase() ) )
            .filter( Boolean );
    }

    const serving = readJson( "configuration/serving.json" );
    const connections = readJson( "configuration/connections.json" );
    const opportunityPhotos = readJson( "photos/opportunities/manifest.json" ).images;
    const groupPhotos = readJson( "photos/groups/manifest.json" ).images;

    const opportunities = connections.opportunities.map( o => {
        const team = serving.teams.find( t => t.key === o.team );
        const photo = opportunityPhotos.find( p => p.opportunity === o.team );
        const roster = groups.find( g => g.type === "serving" && g.team === o.team );
        return {
            key: o.team,
            name: o.publicName,
            team: team ? team.name : o.publicName,
            summary: o.summary,
            teamDescription: team ? team.description : "",
            rooms: team ? team.positions.map( p => p.room || checkInGroupName( p.checkInGroup ) ) : [],
            requiresBackgroundCheck: !!( team && team.requiresBackgroundCheck ),
            photo: photo ? path.basename( photo.file ) : null,
            photoPeople: photo ? photo.people : [],
            volunteers: roster ? roster.members.length : 0
        };
    } );

    const smallGroups = groups
        .filter( g => g.type === "small-group" )
        .map( g => {
            const photo = groupPhotos.find( p => p.group === g.key );
            return {
                key: g.key,
                name: g.name,
                topic: g.topic,
                description: g.description,
                day: g.schedule ? g.schedule.day : null,
                time: g.schedule ? g.schedule.time : null,
                childCare: !!g.childCareProvided,
                members: g.members.length,
                photo: photo ? path.basename( photo.file ) : null,
                photoPeople: photo ? photo.people : []
            };
        } );

    // Which groups and serving teams each Cast person is in.
    for ( const g of groups ) {
        for ( const m of g.members || [] ) {
            const person = people.get( ( m.guid || "" ).toUpperCase() );
            if ( !person ) {
                continue;
            }
            if ( g.type === "serving" ) {
                ( person.serving = person.serving || [] ).push( { name: g.name, role: m.role } );
            }
            else if ( g.type === "small-group" ) {
                ( person.groups = person.groups || [] ).push( { name: g.name, role: m.role, topic: g.topic } );
            }
        }
    }

    const church = readJson( "photos/references/church/manifest.json" );
    const branding = readJson( "photos/references/church/branding/manifest.json" );
    const campusPhotos = readJson( "photos/campuses/manifest.json" ).images;
    const campuses = readJson( "configuration/campuses.json" ).campuses.map( campus => {
        const photo = campusPhotos.find( image => image.campus === campus.key );
        return {
            ...campus,
            photo: photo ? path.basename( photo.file ) : null,
            photoDescription: photo ? photo.description : "",
            buildingType: photo ? photo.buildingType : ""
        };
    } );

    // Each household's campus. Most of the Cast lives on the main campus; a campus pastor's household lives on
    // its own campus and loads only in the datasets that have it (decision D-staff-2).
    const mainCampus = campuses.find( c => c.rockDefault );
    for ( const household of households ) {
        household.campus = household.campusSlot === "main" ? mainCampus : campuses.find( c => c.key === household.campusSlot ) || mainCampus;
    }

    // The church's staff, every one a Cast person, and each campus's campus pastor (configuration/staff.json).
    const staffFile = exists( "configuration/staff.json" ) ? readJson( "configuration/staff.json" ) : { staff: [], campusPastors: {} };
    const staff = staffFile.staff
        .map( s => {
            const member = people.get( s.person.toUpperCase() );
            const campus = s.campus ? campuses.find( c => c.key === s.campus ) : null;
            return member ? { ...s, member, campusName: campus ? campus.name : null } : null;
        } )
        .filter( Boolean );
    for ( const s of staff ) {
        s.member.staffTitle = s.title;
        if ( !s.member.household.stages.includes( "staff" ) ) {
            s.member.household.stages.push( "staff" );
        }
    }
    for ( const campus of campuses ) {
        const pastor = staff.find( s => s.key === staffFile.campusPastors[campus.key] );
        campus.pastor = pastor ? pastor.member : null;
    }

    // Each folder in graphics/initiatives/ is one initiative and its artwork (graphics/README.md).
    const initiativeGraphics = fs.readdirSync( path.join( root, "graphics/initiatives" ) )
        .filter( folder => exists( `graphics/initiatives/${folder}/manifest.json` ) )
        .sort()
        .map( folder => {
            const manifest = readJson( `graphics/initiatives/${folder}/manifest.json` );
            return { key: folder, name: manifest.program, images: manifest.images };
        } );

    return {
        root,
        households,
        people,
        groups,
        smallGroups,
        opportunities,
        campuses,
        staff,
        campusPhotos,
        initiativeGraphics,
        campaign: readJson( "configuration/campaign.json" ),
        communicationLists: exists( "configuration/communications.json" ) ? readJson( "configuration/communications.json" ).lists.lists : [],
        syncedListKeys: exists( "configuration/group-syncs.json" ) ? readJson( "configuration/group-syncs.json" ).syncs.map( s => s.target.list ).filter( Boolean ) : [],
        churchViews: church.images,
        branding,
        prayerRequests: exists( "cast/prayer-requests.json" ) ? readJson( "cast/prayer-requests.json" ) : null,
        calendarEvents: exists( "configuration/calendar.json" ) ? readJson( "configuration/calendar.json" ).events : [],
        registrations: exists( "configuration/registrations.json" ) ? readJson( "configuration/registrations.json" ) : { categories: [], templates: [], instances: [] },
        datasets: Object.fromEntries( listJson( "datasets" ).map( d => [ d.key, d ] ) ),
        counts: {
            households: households.length,
            people: households.reduce( ( sum, h ) => sum + h.members.length, 0 ),
            families: households.filter( h => h.kind === "family" ).length,
            businesses: households.filter( h => h.kind === "business" ).length,
            photos: castPhotos.size,
            featured: households.filter( h => h.isFeatured ).length,
            smallGroups: smallGroups.length,
            volunteers: opportunities.reduce( ( sum, o ) => sum + o.volunteers, 0 ),
            logins: households.reduce( ( sum, h ) => sum + h.members.filter( m => ( m.logins || [] ).length ).length, 0 ),
            notes: households.reduce( ( sum, h ) => sum + h.noteCount, 0 )
        }
    };
}

module.exports = { load, root };
