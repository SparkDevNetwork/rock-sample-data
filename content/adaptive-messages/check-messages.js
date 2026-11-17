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
    Checks the adaptive messages against README.md: configuration/adaptive-messages.json and messages.json in this
    folder. For the configuration it checks unique keys and Guids, real segments and categories, that only a
    message's last adaptation has no segments, the dates rules, saturation, pages, and the Cast rows. For the text it
    checks that every adaptation has text and no text is left over, the lengths, links to real External Website routes
    or the configuration's pages, approved event images, plain characters, no Lava or full web addresses, and no two
    summaries in one message that start with the same five words. Prints a summary and exits with 1 when anything fails.

    Usage: node content/adaptive-messages/check-messages.js
*/

const fs = require( "fs" );
const path = require( "path" );

const root = path.join( __dirname, "..", ".." );
const read = file => JSON.parse( fs.readFileSync( path.join( root, file ), "utf8" ) );
const configuration = read( "configuration/adaptive-messages.json" );
const text = read( "content/adaptive-messages/messages.json" );
const categories = read( "configuration/categories.json" ).categories;
const eventImages = read( "photos/events/manifest.json" ).images;

// Routes Rock ships on its External Website, the same in Rock 20 and Rock 21.
const ROUTES = [ "/Give", "/GroupFinder", "/MyAccount", "/watch", "/learn", "/connect/get-connected" ];
const DATES_RULES = [ "campaign", "before-baptism-sunday", "camp-registration" ];
const LENGTHS = {
    callToAction: { min: 3, max: 30 },
    summary: { min: 40, max: 160 },
    detail: { min: 20, max: 200 }
};
const DETAILS = { min: 1, max: 3 };
const FIRST_WORDS = 5;
const KEY_PATTERN = /^[a-z0-9]+(-[a-z0-9]+)*$/;
const GUID_PATTERN = /^[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}$/;

const errors = [];
const guids = new Set();
const checkGuid = ( at, guid ) => {
    if ( !GUID_PATTERN.test( guid || "" ) || guids.has( guid ) ) {
        errors.push( `${at}: needs its own upper-case Guid.` );
    }
    guids.add( guid );
};

// The configuration.
const category = categories.find( c => c.key === configuration.category );
if ( !category || category.entityType !== "Rock.Model.AdaptiveMessageCategory" ) {
    errors.push( `category ${configuration.category} is not an adaptive message category in configuration/categories.json.` );
}

if ( !Number.isInteger( configuration.segmentRefreshMinutes ) || configuration.segmentRefreshMinutes < 1 ) {
    errors.push( "segmentRefreshMinutes must be a whole number of minutes." );
}

for ( const [ key, page ] of Object.entries( configuration.pages || {} ) ) {
    if ( !KEY_PATTERN.test( key ) || !page.name ) {
        errors.push( `page ${key}: needs a kebab-case key and a name.` );
    }
    if ( !GUID_PATTERN.test( page.guid || "" ) ) {
        errors.push( `page ${key}: needs Rock's page Guid, upper case.` );
    }
}

const segments = new Set();
for ( const segment of configuration.segments || [] ) {
    if ( !KEY_PATTERN.test( segment.key ) || segments.has( segment.key ) || !segment.name || !segment.dataView ) {
        errors.push( `segment ${segment.key}: needs a unique kebab-case key, a name, and a data view.` );
    }
    segments.add( segment.key );
    checkGuid( `segment ${segment.key}`, segment.guid );
}

const used = new Set();
const messageKeys = new Set();
for ( const message of configuration.messages || [] ) {
    const at = `message ${message.key}`;
    if ( !KEY_PATTERN.test( message.key ) || messageKeys.has( message.key ) || !message.name ) {
        errors.push( `${at}: needs a unique kebab-case key and a name.` );
    }
    messageKeys.add( message.key );
    checkGuid( at, message.guid );
    if ( message.dates && message.dates.rule !== "campaign" ) {
        errors.push( `${at}: a message's dates can only follow the campaign.` );
    }

    const adaptationKeys = new Set();
    ( message.adaptations || [] ).forEach( ( adaptation, index ) => {
        const where = `${at} adaptation ${adaptation.key}`;
        if ( !KEY_PATTERN.test( adaptation.key ) || adaptationKeys.has( adaptation.key ) || !adaptation.name ) {
            errors.push( `${where}: needs a key unique in its message, and a name.` );
        }
        adaptationKeys.add( adaptation.key );
        checkGuid( where, adaptation.guid );

        for ( const segment of adaptation.segments || [] ) {
            used.add( segment );
            if ( !segments.has( segment ) ) {
                errors.push( `${where}: segment ${segment} is not in the file's segments.` );
            }
        }

        // Rock shows the first adaptation that fits, and one with no segments fits everyone.
        if ( ( adaptation.segments || [] ).length === 0 && index !== message.adaptations.length - 1 ) {
            errors.push( `${where}: has no segments, so it fits everyone and must be the message's last adaptation.` );
        }

        if ( adaptation.dates ) {
            if ( !DATES_RULES.includes( adaptation.dates.rule ) || adaptation.dates.rule === "campaign" ) {
                errors.push( `${where}: dates rule ${adaptation.dates.rule} is not one an adaptation can use.` );
            }
            if ( adaptation.dates.rule === "before-baptism-sunday" && !( Number.isInteger( adaptation.dates.days ) && adaptation.dates.days > 0 ) ) {
                errors.push( `${where}: before-baptism-sunday needs a whole number of days.` );
            }
        }

        if ( adaptation.saturation && !( Number.isInteger( adaptation.saturation.views ) && adaptation.saturation.views > 0 && Number.isInteger( adaptation.saturation.days ) && adaptation.saturation.days > 0 ) ) {
            errors.push( `${where}: saturation needs whole numbers of views and days.` );
        }
    } );

    if ( adaptationKeys.size === 0 ) {
        errors.push( `${at}: has no adaptations.` );
    }
}

for ( const segment of segments ) {
    if ( !used.has( segment ) ) {
        errors.push( `segment ${segment}: no adaptation uses it.` );
    }
}

const castPeople = new Set();
for ( const file of fs.readdirSync( path.join( root, "cast", "households" ) ).filter( f => f.endsWith( ".json" ) ) ) {
    for ( const member of read( `cast/households/${file}` ).members || [] ) {
        castPeople.add( member.guid );
    }
}

for ( const row of configuration.cast || [] ) {
    const at = `cast row for ${row.name}`;
    const message = ( configuration.messages || [] ).find( m => m.key === row.message );
    if ( !castPeople.has( row.person ) ) {
        errors.push( `${at}: ${row.person} is not a Cast person.` );
    }
    if ( !message || ( row.adaptation != null && !message.adaptations.some( a => a.key === row.adaptation ) ) ) {
        errors.push( `${at}: ${row.message} ${row.adaptation} is not a message and adaptation in the file.` );
    }
    if ( !row.reason ) {
        errors.push( `${at}: needs a reason from the person's story.` );
    }
}

// The text.
const checkCharacters = ( at, value ) => {
    if ( typeof value === "string" ) {
        const notPlain = [ ...value ].filter( c => c < " " || c > "~" );
        if ( notPlain.length ) {
            errors.push( `${at}: has characters that are not plain. Use straight quotes, commas, or periods. "${value}"` );
        }
        if ( value.includes( "--" ) ) {
            errors.push( `${at}: uses "--". Use a comma or a period.` );
        }
        if ( /\{\{|\}\}|\{%|%\}|[{}]/.test( value ) ) {
            errors.push( `${at}: has Lava or braces, which adaptation text never uses.` );
        }
        if ( /https?:|www\./i.test( value ) ) {
            errors.push( `${at}: has a full web address. Put links in link.` );
        }
        for ( const address of value.match( /[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}/g ) || [] ) {
            if ( ![ "example.org", "rocksolidchurchdemo.com" ].includes( address.split( "@" )[1].toLowerCase() ) ) {
                errors.push( `${at}: has the email ${address}. Use example.org or rocksolidchurchdemo.com.` );
            }
        }
    } else if ( Array.isArray( value ) ) {
        value.forEach( ( v, i ) => checkCharacters( `${at}[${i}]`, v ) );
    } else if ( value && typeof value === "object" ) {
        for ( const [ k, v ] of Object.entries( value ) ) {
            checkCharacters( `${at}.${k}`, v );
        }
    }
};
checkCharacters( "messages.json", text );

const checkLength = ( at, value, limits ) => {
    if ( typeof value !== "string" || value.length < limits.min || value.length > limits.max ) {
        errors.push( `${at}: must be ${limits.min} to ${limits.max} characters (it is ${typeof value === "string" ? value.length : "missing"}).` );
    }
};
const firstWords = value => value.toLowerCase().replace( /[^a-z0-9\s]/g, " " ).split( /\s+/ ).filter( w => w ).slice( 0, FIRST_WORDS ).join( " " );

let adaptations = 0;
for ( const message of configuration.messages || [] ) {
    const texts = text.messages[message.key] || {};
    const openings = new Map();
    for ( const adaptation of message.adaptations || [] ) {
        const at = `${message.key} ${adaptation.key}`;
        const entry = texts[adaptation.key];
        if ( !entry ) {
            errors.push( `${at}: has no text in messages.json.` );
            continue;
        }
        adaptations++;

        const allowed = [ "callToAction", "link", "summary", "details", "image" ];
        for ( const key of Object.keys( entry ).filter( k => !allowed.includes( k ) ) ) {
            errors.push( `${at}: ${key} is not a field. Use ${allowed.join( ", " )}.` );
        }

        checkLength( `${at} callToAction`, entry.callToAction, LENGTHS.callToAction );
        checkLength( `${at} summary`, entry.summary, LENGTHS.summary );
        const details = entry.details || [];
        if ( details.length < DETAILS.min || details.length > DETAILS.max ) {
            errors.push( `${at}: needs ${DETAILS.min} to ${DETAILS.max} details paragraphs.` );
        }
        details.forEach( ( d, i ) => checkLength( `${at} details[${i}]`, d, LENGTHS.detail ) );

        const link = entry.link || "";
        const page = link.startsWith( "page:" ) ? link.slice( "page:".length ) : null;
        if ( page ? !( configuration.pages || {} )[page] : !ROUTES.includes( link ) ) {
            errors.push( `${at}: links to "${link}". Use one of ${ROUTES.join( ", " )}, or page:<key> for a page in the configuration.` );
        }

        if ( entry.image != null && !eventImages.some( i => i.event === entry.image && String( i.status ).startsWith( "approved" ) ) ) {
            errors.push( `${at}: image ${entry.image} is not an approved image in photos/events/manifest.json.` );
        }

        if ( typeof entry.summary === "string" ) {
            const opening = firstWords( entry.summary );
            if ( openings.has( opening ) ) {
                errors.push( `${at}: its summary starts with the same ${FIRST_WORDS} words as ${openings.get( opening )}.` );
            }
            openings.set( opening, adaptation.key );
        }
    }

    for ( const key of Object.keys( texts ).filter( k => !( message.adaptations || [] ).some( a => a.key === k ) ) ) {
        errors.push( `${message.key} ${key}: text for an adaptation the configuration does not have.` );
    }
}

for ( const key of Object.keys( text.messages || {} ).filter( k => !messageKeys.has( k ) ) ) {
    errors.push( `${key}: text for a message the configuration does not have.` );
}

const withoutImage = ( configuration.messages || [] ).flatMap( m => ( m.adaptations || [] ).filter( a => text.messages[m.key] && text.messages[m.key][a.key] && !text.messages[m.key][a.key].image ).map( a => `${m.key} ${a.key}` ) );
console.log( `Adaptive messages: ${segments.size} segments, ${messageKeys.size} messages, ${adaptations} adaptations with text, ${withoutImage.length} waiting for an image (${withoutImage.join( ", " )}). ${errors.length} problems.` );
for ( const error of errors ) {
    console.log( `  ${error}` );
}

process.exit( errors.length > 0 ? 1 : 0 );
