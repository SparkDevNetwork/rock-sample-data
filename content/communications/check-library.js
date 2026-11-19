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
    Checks the message library in this folder against README.md: series.json, newsletter.json,
    emails.json, texts.json, conversations.json, snippets.json, and email-sections.json. For each entry it checks the required and allowed keys, unique
    keys, known occasions, audiences, topics, senders, lines, and mediums, the Lengths table,
    the allowed tokens and the one Lava merge field, links and button paths, email domains,
    plain characters, and no two entries in one list that start with the same five words. It also checks that the
    wizard-ready email template keeps the Legacy template's colors, logo, and unsubscribe option.
    Prints a summary and exits with 1 when anything fails.

    Usage: node content/communications/check-library.js
*/

const fs = require( "fs" );
const path = require( "path" );

const libraryDir = __dirname;
const configurationDir = path.join( libraryDir, "..", "..", "configuration" );
const readConfiguration = file => JSON.parse( fs.readFileSync( path.join( configurationDir, file ), "utf8" ) );
const configuration = readConfiguration( "communications.json" );
const CATEGORIES = readConfiguration( "categories.json" ).categories;
const CAMPUSES = readConfiguration( "campuses.json" ).campuses;
const CAMPUS_KEYS = CAMPUSES.map( c => c.key );
const CAMPUS_NAMES = Object.fromEntries( CAMPUSES.map( c => [ c.key, c.name ] ) );
const STAFF = readConfiguration( "staff.json" ).staff;
const STAFF_KEYS = STAFF.map( s => s.key );
const STAFF_PEOPLE = STAFF.map( s => s.person );

// The occasions in the README. 'newsletter' belongs only to newsletter.json.
const OCCASIONS = [
    "newsletter", "sunday", "series-start", "easter", "christmas", "thanksgiving", "year-end",
    "giving-statement", "commitment-sunday", "campaign-update", "baptism-sunday", "starting-point",
    "foundations", "student-camp", "mission-trip", "vbs", "group-launch", "serve-sunday", "weather",
    "serving-scheduled", "serving-reminder", "connection", "prayer-answered", "first-visit",
    "baptized", "after-starting-point", "after-foundations", "baptism-interest",
    "guest-welcome", "we-miss-you", "baptism-invitation", "lead-invitation", "womens-retreat-invitation", "camp-invitation", "christmas-invitation", "campaign-follow-up"
];

// One-to-one occasions send to one person, and only they use the 'person' audience.
const PERSON_OCCASIONS = [ "serving-scheduled", "serving-reminder", "connection", "prayer-answered", "first-visit", "baptized", "after-starting-point", "after-foundations", "baptism-interest", "guest-welcome", "we-miss-you", "baptism-invitation", "lead-invitation", "womens-retreat-invitation", "camp-invitation", "christmas-invitation", "campaign-follow-up" ];

// The 'class' audience is the people in the class or course the occasion names.
const CLASS_OCCASIONS = [ "starting-point", "foundations" ];

// A flow's daysBefore counts days after the class or course ends, so it is negative.
const FLOW_OCCASIONS = [ "after-starting-point", "after-foundations" ];

const LIST_KEYS = configuration.lists.lists.map( l => l.key );
// The 'scheduled' audience belongs to the one email configuration/communications.json schedules to send after install.
const SCHEDULED_EMAIL = configuration.scheduled ? configuration.scheduled.email : null;
const AUDIENCES = [ "newsletter", "everyone", "students-and-parents", "class", "person", "scheduled" ].concat( LIST_KEYS.map( k => `list:${k}` ) );
const TOPICS = Object.keys( configuration.topics ).filter( k => k !== "$comment" );
const SENDERS = [ "church", "pastor", "students" ];
const LINES = configuration.phoneNumbers.numbers.map( n => n.key ).filter( k => k === "main" || k === "students" );
const MEDIUMS = [ "sms", "push" ];

// The slots and their lengths in weeks, from the README's series.json example.
const SLOTS = { "new-year": 7, "after-easter": 7, "fall": 10 };
const SERIES_PER_SLOT = 2;
const CAMPAIGN_WEEKS = 4;

// Character lengths before tokens are filled, from the README's Lengths table.
const LENGTHS = {
    subject: { min: 20, max: 70 },
    preheader: { min: 20, max: 110 },
    sms: { min: 40, max: 300 },
    pushTitle: { min: 5, max: 40 },
    pushText: { min: 20, max: 120 },
    reply: { min: 2, max: 120 },
    conversation: { min: 2, max: 300 },
    snippetName: { min: 3, max: 100 },
    sectionName: { min: 3, max: 100 },
    usageSummary: { min: 20, max: 500 }
};

// email-sections.json: the site the markup's links point at (loader/generation/MessageRenderer.cs), and the token the
// loader fills with each of the dataset's campuses and its service times.
const SITE = "https://rocksolidchurchdemo.com";
const CAMPUS_SERVICE_TIMES = "{campusServiceTimes}";
const EVENT_PHOTOS = JSON.parse( fs.readFileSync( path.join( libraryDir, "..", "..", "photos", "events", "manifest.json" ), "utf8" ) ).images;

// snippets.json: its own tokens, filled at load, and the owner that gives each campus pastor a copy.
const SNIPPET_TOKENS = [ "{campus}", "{serviceTimes}", "{staff}" ];
const CAMPUS_PASTOR = "campus-pastor";
const GUID_PATTERN = /^[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}$/;

// conversations.json: where a topic happens, who texts, its own tokens, and how many entries each list needs
// so a generated thread rarely repeats. Every entry stands alone.
const CONVERSATION_NUMBERS = [ "personal", "main", "students" ];
const CONVERSATION_AUDIENCES = [ "adult", "parent", "student" ];
const CONVERSATION_TOKENS = [ "{name}", "{staff}", "{campus}" ];
const CONVERSATION_LISTS = { openers: 6, staffReplies: 6, personFollowUps: 5, staffFollowUps: 5, closings: 4 };

// Most SMS texts should fit in one segment. It is reported, not enforced.
const SMS_TYPICAL_MAX = 160;

// A newsletter piece's text is one to three sentences, and evergreen pieces fill a newsletter to four.
// A piece's "from" and "until" may be negative, for a piece that runs after its occasion.
const PIECE_SENTENCES = { min: 1, max: 3 };
const NEWSLETTER_PIECES = 4;
const EMAIL_BLOCKS = { min: 2, max: 6 };

const TOKENS = [ "{eventDate}", "{eventDay}", "{eventYear}", "{registerBy}", "{sendDate}", "{campus}", "{campusKey}", "{serviceTimes}", "{series}", "{seriesTagline}", "{seriesWeek}" ];
const EVENT_TOKENS = [ "{eventDate}", "{eventDay}", "{eventYear}", "{registerBy}" ];
const SERIES_TOKENS = [ "{series}", "{seriesTagline}", "{seriesWeek}" ];
const CAMPUS_KEY_TOKEN = "{campusKey}";
const LAVA = "{{ Person.NickName }}";

const LINK_PATHS = [
    "/", "/events", "/easter", "/christmas", "/give", "/give/statements", "/watch", "/watch/live",
    "/watch/series", "/groups", "/serve", "/starting-point", "/baptism", "/foundations", "/students",
    "/students/camp", "/students/mission-trip", "/kids", "/kids/vbs", "/prayer", "/built-on-the-rock",
    "/next-steps", "/men", "/women", "/app", "/campuses/{campusKey}"
];
const EMAIL_DOMAINS = [ "example.org", "rocksolidchurchdemo.com" ];

const KEY_PATTERN = /^[a-z0-9]+(-[a-z0-9]+)*$/;
const LINK_PATTERN = /\[([^\]]*)\]\(([^)]*)\)/g;
const FIRST_WORDS = 5;

const errors = [];
const summary = [];

/**
 * Reads a library file, or records an error and returns null when it is missing or not valid JSON.
 */
function readFile( file ) {
    try {
        return JSON.parse( fs.readFileSync( path.join( libraryDir, file ), "utf8" ) );
    }
    catch ( e ) {
        errors.push( `${file}: cannot be read as JSON (${e.message}).` );
        return null;
    }
}

/**
 * Checks that an object has the required keys and no others, ignoring $comment.
 */
function checkKeys( owner, value, required, optional = [] ) {
    if ( !value || typeof value !== "object" || Array.isArray( value ) ) {
        errors.push( `${owner}: must be an object.` );
        return false;
    }
    for ( const key of required ) {
        if ( !( key in value ) ) {
            errors.push( `${owner}: is missing "${key}".` );
        }
    }
    for ( const key of Object.keys( value ) ) {
        if ( key !== "$comment" && !required.includes( key ) && !optional.includes( key ) ) {
            errors.push( `${owner}: has unexpected "${key}".` );
        }
    }
    return true;
}

/**
 * Checks that a value is a non-empty list, and returns it, or an empty list.
 */
function checkArray( owner, value ) {
    if ( !Array.isArray( value ) || value.length === 0 ) {
        errors.push( `${owner}: must be a non-empty list.` );
        return [];
    }
    return value;
}

/**
 * Checks that a value is a string with no leading or trailing spaces. Returns false when it is not a string.
 */
function checkString( at, value ) {
    if ( typeof value !== "string" || value.length === 0 ) {
        errors.push( `${at}: must be a non-empty string.` );
        return false;
    }
    if ( value !== value.trim() ) {
        errors.push( `${at}: has leading or trailing spaces.` );
    }
    return true;
}

/**
 * Checks a string's length against one row of the Lengths table.
 */
function checkLength( at, value, limits, name ) {
    if ( typeof value === "string" && ( value.length < limits.min || value.length > limits.max ) ) {
        errors.push( `${at}: is ${value.length} characters; ${name} is ${limits.min} to ${limits.max}. "${value}"` );
    }
}

/**
 * Checks that an integer field holds an integer, within an optional range.
 */
function checkInteger( at, value, min = -Infinity, max = Infinity ) {
    if ( !Number.isInteger( value ) || value < min || value > max ) {
        const range = Number.isFinite( min ) ? ` from ${min} to ${max}` : "";
        errors.push( `${at}: must be an integer${range}.` );
        return false;
    }
    return true;
}

/**
 * Checks a key's format and that no other entry in the file has it.
 */
function checkKey( at, key, seen ) {
    if ( typeof key !== "string" || !KEY_PATTERN.test( key ) ) {
        errors.push( `${at}: key "${key}" must be lowercase words joined by hyphens.` );
        return;
    }
    if ( seen.has( key ) ) {
        errors.push( `${at}: key "${key}" is already used by ${seen.get( key )}.` );
    }
    else {
        seen.set( key, at );
    }
}

/**
 * Checks an uppercase Guid and that no other entry in the file has it.
 */
function checkGuid( at, guid, seen ) {
    if ( typeof guid !== "string" || !GUID_PATTERN.test( guid ) ) {
        errors.push( `${at}: "${guid}" must be an uppercase Guid.` );
        return;
    }
    if ( seen.has( guid ) ) {
        errors.push( `${at}: Guid ${guid} is already used by ${seen.get( guid )}.` );
    }
    else {
        seen.set( guid, at );
    }
}

/**
 * Checks that a link path or button href is one of the allowed paths.
 */
function checkPath( at, href ) {
    if ( typeof href !== "string" || !LINK_PATHS.includes( href ) ) {
        errors.push( `${at}: links to "${href}", which is not an allowed path.` );
    }
}

/**
 * Checks a piece of message text: plain characters, the one Lava field, tokens, links, and email
 * addresses. 'allowed' is the list of tokens this field may use. 'linkable' fields may hold
 * Markdown links. 'known' is the file's own token list, and 'lava' is false for a file that uses no Lava.
 */
function checkText( at, text, { allowed = TOKENS, linkable = false, known = TOKENS, lava = true } = {} ) {
    if ( typeof text !== "string" ) {
        return;
    }

    // Markdown links: only in fields that render them, and only to allowed paths.
    let outsideLinks = text;
    const links = [ ...text.matchAll( LINK_PATTERN ) ];
    if ( links.length && !linkable ) {
        errors.push( `${at}: has a Markdown link, which only email paragraphs, lists, and newsletter text may hold.` );
    }
    for ( const link of links ) {
        if ( !link[1].trim() ) {
            errors.push( `${at}: has a link with no text.` );
        }
        checkPath( at, link[2] );
        outsideLinks = outsideLinks.replace( link[0], `[${link[1]}]` );
    }
    if ( /https?:|www\./i.test( text ) ) {
        errors.push( `${at}: has a full web address. Write links as Markdown paths, such as [Give](/give).` );
    }

    // Lava: only {{ Person.NickName }}, written exactly.
    let withoutLava = lava ? outsideLinks.split( LAVA ).join( "" ) : outsideLinks;
    if ( /\{\{|\}\}|\{%|%\}/.test( withoutLava ) ) {
        errors.push( lava ? `${at}: has Lava other than ${LAVA}.` : `${at}: has Lava, which this file never uses.` );
        withoutLava = withoutLava.replace( /\{\{.*?\}\}|\{%.*?%\}/g, "" );
    }

    // Tokens: only the documented ones, and {campusKey} only inside a link path.
    const tokens = withoutLava.match( /\{[^{}]*\}/g ) || [];
    for ( const token of tokens ) {
        if ( token === CAMPUS_KEY_TOKEN && known.includes( token ) ) {
            errors.push( `${at}: uses ${CAMPUS_KEY_TOKEN} outside a link path.` );
        }
        else if ( !known.includes( token ) ) {
            errors.push( `${at}: uses unknown token ${token}.` );
        }
        else if ( !allowed.includes( token ) ) {
            errors.push( `${at}: uses ${token}, which this field cannot use.` );
        }
    }
    if ( /[{}]/.test( withoutLava.replace( /\{[^{}]*\}/g, "" ) ) ) {
        errors.push( `${at}: has an unmatched brace.` );
    }
    for ( const link of links ) {
        const pathTokens = link[2].match( /\{[^{}]*\}/g ) || [];
        for ( const token of pathTokens.filter( t => t !== CAMPUS_KEY_TOKEN ) ) {
            errors.push( `${at}: link path uses ${token}; only ${CAMPUS_KEY_TOKEN} belongs in a path.` );
        }
    }

    // Email addresses: only the reserved domains.
    for ( const address of text.match( /[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}/g ) || [] ) {
        const domain = address.split( "@" )[1].toLowerCase();
        if ( !EMAIL_DOMAINS.includes( domain ) ) {
            errors.push( `${at}: has the email ${address}. Use ${EMAIL_DOMAINS.join( " or " )}.` );
        }
    }
}

/**
 * Checks every string in a file for plain printable characters and "--", including comments.
 */
function checkCharacters( owner, value ) {
    if ( typeof value === "string" ) {
        const notPlain = [ ...value ].filter( c => c < " " || c > "~" );
        if ( notPlain.length ) {
            const codes = [ ...new Set( notPlain ) ].map( c => `U+${c.codePointAt( 0 ).toString( 16 ).toUpperCase().padStart( 4, "0" )}` );
            errors.push( `${owner}: has characters that are not plain (${codes.join( ", " )}). Use straight quotes, commas, or periods. "${value}"` );
        }
        if ( value.includes( "--" ) ) {
            errors.push( `${owner}: uses "--". Use a comma or a period. "${value}"` );
        }
    }
    else if ( Array.isArray( value ) ) {
        value.forEach( ( v, i ) => checkCharacters( `${owner}[${i}]`, v ) );
    }
    else if ( value && typeof value === "object" ) {
        for ( const [ k, v ] of Object.entries( value ) ) {
            checkCharacters( `${owner}.${k}`, v );
        }
    }
}

/**
 * The first few words of an entry, lowercased and without punctuation, so near-identical openings match.
 * The Lava merge field counts as one word.
 */
function firstWords( text ) {
    return text.split( LAVA ).join( "{name}" ).toLowerCase().replace( /[^a-z0-9{}\s]/g, " " ).split( /\s+/ ).filter( w => w ).slice( 0, FIRST_WORDS ).join( " " );
}

/**
 * Reports entries in one list whose text starts with the same five words. 'texts' pairs a label with the text.
 */
function checkOpenings( owner, texts ) {
    const openings = new Map();
    for ( const [ label, text ] of texts ) {
        if ( typeof text !== "string" ) {
            continue;
        }
        const opening = firstWords( text );
        if ( openings.has( opening ) ) {
            errors.push( `${owner} ${label}: starts with the same ${FIRST_WORDS} words as ${openings.get( opening )} ("${opening}").` );
        }
        else {
            openings.set( opening, label );
        }
    }
}

/**
 * Counts the sentences in a piece of text, ignoring periods inside tokens and links.
 */
function sentenceCount( text ) {
    const plain = text.replace( LINK_PATTERN, "$1" ).replace( /\{[^{}]*\}/g, "x" ).replace( /\b(a\.m|p\.m)\./g, "am" );
    return ( plain.match( /[.!?]+(\s|$)/g ) || [] ).length;
}

/**
 * Checks an occasion name and that the audience fits it.
 */
function checkOccasion( at, entry ) {
    if ( !OCCASIONS.includes( entry.occasion ) || entry.occasion === "newsletter" ) {
        errors.push( `${at}: occasion "${entry.occasion}" is not a known occasion for this file.` );
        return;
    }
    if ( !AUDIENCES.includes( entry.audience ) ) {
        errors.push( `${at}: audience "${entry.audience}" is not known. A list audience must be list:<key> for a list in configuration/communications.json.` );
    }
    const isPersonOccasion = PERSON_OCCASIONS.includes( entry.occasion );
    if ( isPersonOccasion !== ( entry.audience === "person" ) ) {
        errors.push( `${at}: the 'person' audience goes with the one-to-one occasions, and only with them.` );
    }
    if ( ( entry.audience === "scheduled" ) !== ( SCHEDULED_EMAIL !== null && entry.key === SCHEDULED_EMAIL ) ) {
        errors.push( `${at}: the 'scheduled' audience goes with the one email configuration/communications.json schedules, and only with it.` );
    }
    if ( entry.audience === "class" && !CLASS_OCCASIONS.includes( entry.occasion ) ) {
        errors.push( `${at}: the 'class' audience needs a class occasion (${CLASS_OCCASIONS.join( ", " )}).` );
    }
    if ( Number.isInteger( entry.daysBefore ) && FLOW_OCCASIONS.includes( entry.occasion ) && entry.daysBefore >= 0 ) {
        errors.push( `${at}: ${entry.occasion} counts days after the class or course, so daysBefore must be negative.` );
    }
}

/**
 * Checks a topic key.
 */
function checkTopic( at, topic ) {
    if ( !TOPICS.includes( topic ) ) {
        errors.push( `${at}: topic "${topic}" is not a topic in configuration/communications.json.` );
    }
}

// series.json: the slots, two series in each, and the campaign series.
const seriesFile = readFile( "series.json" );
const seriesNames = [];
if ( seriesFile && checkKeys( "series.json", seriesFile, [ "slots", "series", "campaign" ] ) ) {
    checkCharacters( "series.json", seriesFile );
    const keys = new Map();
    if ( checkKeys( "series.json slots", seriesFile.slots, Object.keys( SLOTS ) ) ) {
        for ( const [ slot, weeks ] of Object.entries( SLOTS ) ) {
            const value = seriesFile.slots[slot];
            if ( value && checkKeys( `series.json slots.${slot}`, value, [ "weeks" ] ) && value.weeks !== weeks ) {
                errors.push( `series.json slots.${slot}: runs ${weeks} weeks, not ${value.weeks}.` );
            }
        }
    }
    const perSlot = Object.fromEntries( Object.keys( SLOTS ).map( s => [ s, 0 ] ) );
    for ( const [ index, entry ] of checkArray( "series.json series", seriesFile.series ).entries() ) {
        const at = `series.json series[${index}]`;
        if ( !checkKeys( at, entry, [ "key", "name", "tagline", "slot" ] ) ) {
            continue;
        }
        checkKey( at, entry.key, keys );
        for ( const field of [ "name", "tagline" ] ) {
            if ( checkString( `${at} ${field}`, entry[field] ) ) {
                checkText( `${at} ${field}`, entry[field], { allowed: [] } );
            }
        }
        if ( entry.slot in perSlot ) {
            perSlot[entry.slot]++;
        }
        else {
            errors.push( `${at}: slot "${entry.slot}" is not one of ${Object.keys( SLOTS ).join( ", " )}.` );
        }
        seriesNames.push( entry.name );
    }
    for ( const [ slot, count ] of Object.entries( perSlot ) ) {
        if ( count !== SERIES_PER_SLOT ) {
            errors.push( `series.json: slot "${slot}" has ${count} series; each slot has exactly ${SERIES_PER_SLOT}.` );
        }
    }
    const campaign = seriesFile.campaign;
    if ( checkKeys( "series.json campaign", campaign, [ "key", "name", "tagline", "weeks" ] ) ) {
        checkKey( "series.json campaign", campaign.key, keys );
        for ( const field of [ "name", "tagline" ] ) {
            if ( checkString( `series.json campaign ${field}`, campaign[field] ) ) {
                checkText( `series.json campaign ${field}`, campaign[field], { allowed: [] } );
            }
        }
        if ( campaign.weeks !== CAMPAIGN_WEEKS ) {
            errors.push( `series.json campaign: runs ${CAMPAIGN_WEEKS} weeks, not ${campaign.weeks}.` );
        }
    }
    const names = seriesNames.concat( campaign && campaign.name ? [ campaign.name ] : [] );
    for ( const name of names.filter( ( n, i ) => names.indexOf( n ) !== i ) ) {
        errors.push( `series.json: the name "${name}" is used twice.` );
    }
    summary.push( `series.json\n    ${seriesNames.length} series in ${Object.keys( SLOTS ).length} slots, plus the campaign series` );
}

// newsletter.json: subjects, openers, series lines, pieces, evergreen pieces, and closings.
const NEWSLETTER_TOKENS = [ "{sendDate}", "{campus}", "{serviceTimes}" ];
const newsletter = readFile( "newsletter.json" );
if ( newsletter && checkKeys( "newsletter.json", newsletter, [ "subjects", "openers", "series", "pieces", "evergreen", "closings" ] ) ) {
    checkCharacters( "newsletter.json", newsletter );
    const counts = {};

    // The plain string lists. Series tokens belong only in the series lines, which must name the series.
    for ( const list of [ "subjects", "openers", "series", "closings" ] ) {
        const entries = checkArray( `newsletter.json ${list}`, newsletter[list] );
        const allowed = list === "series" ? NEWSLETTER_TOKENS.concat( SERIES_TOKENS ) : NEWSLETTER_TOKENS;
        for ( const [ index, entry ] of entries.entries() ) {
            const at = `newsletter.json ${list}[${index}]`;
            if ( !checkString( at, entry ) ) {
                continue;
            }
            checkText( at, entry, { allowed, linkable: list !== "subjects" } );
            if ( list === "subjects" ) {
                checkLength( at, entry, LENGTHS.subject, "an email subject" );
            }
            if ( list === "series" && !entry.includes( "{series}" ) ) {
                errors.push( `${at}: a series line must name the series with {series}.` );
            }
        }
        checkOpenings( `newsletter.json ${list}`, entries.map( ( e, i ) => [ `[${i}]`, e ] ) );
        counts[list] = entries.length;
    }

    // Pieces tied to an occasion, and evergreen pieces with none. Keys are unique across both.
    const keys = new Map();
    for ( const list of [ "pieces", "evergreen" ] ) {
        const entries = checkArray( `newsletter.json ${list}`, newsletter[list] );
        const isOccasion = list === "pieces";
        const required = isOccasion ? [ "key", "occasion", "from", "until", "heading", "text", "button", "href" ] : [ "key", "heading", "text", "button", "href" ];
        const allowed = isOccasion ? NEWSLETTER_TOKENS.concat( EVENT_TOKENS ) : NEWSLETTER_TOKENS;
        for ( const [ index, entry ] of entries.entries() ) {
            const at = `newsletter.json ${list}[${index}]`;
            if ( !checkKeys( at, entry, required ) ) {
                continue;
            }
            checkKey( at, entry.key, keys );
            if ( isOccasion ) {
                if ( !OCCASIONS.includes( entry.occasion ) || entry.occasion === "newsletter" || PERSON_OCCASIONS.includes( entry.occasion ) ) {
                    errors.push( `${at}: occasion "${entry.occasion}" is not a known church-wide occasion.` );
                }
                if ( checkInteger( `${at} from`, entry.from ) && checkInteger( `${at} until`, entry.until ) && entry.from < entry.until ) {
                    errors.push( `${at}: "from" (${entry.from}) must be at least "until" (${entry.until}).` );
                }
            }
            for ( const field of [ "heading", "text", "button" ] ) {
                if ( checkString( `${at} ${field}`, entry[field] ) ) {
                    checkText( `${at} ${field}`, entry[field], { allowed, linkable: field === "text" } );
                }
            }
            checkPath( at, entry.href );
            if ( typeof entry.text === "string" ) {
                const sentences = sentenceCount( entry.text );
                if ( sentences < PIECE_SENTENCES.min || sentences > PIECE_SENTENCES.max ) {
                    errors.push( `${at}: text has ${sentences} sentences; a piece has ${PIECE_SENTENCES.min} to ${PIECE_SENTENCES.max}.` );
                }
            }
        }
        checkOpenings( `newsletter.json ${list}`, entries.map( ( e, i ) => [ `[${i}]`, e && e.text ] ) );
        counts[list] = entries.length;
    }
    if ( counts.evergreen < NEWSLETTER_PIECES ) {
        errors.push( `newsletter.json: needs at least ${NEWSLETTER_PIECES} evergreen pieces to fill a newsletter.` );
    }
    summary.push( `newsletter.json\n    ${Object.entries( counts ).map( ( [ k, v ] ) => `${v} ${k}` ).join( ", " )}` );
}

// emails.json: every email other than the newsletter.
const emailsFile = readFile( "emails.json" );
if ( emailsFile && checkKeys( "emails.json", emailsFile, [ "emails" ] ) ) {
    checkCharacters( "emails.json", emailsFile );
    const keys = new Map();
    const emails = checkArray( "emails.json emails", emailsFile.emails );
    const bySender = {};
    let withButton = 0;
    let notBulk = 0;
    for ( const [ index, entry ] of emails.entries() ) {
        const at = `emails.json emails[${index}]`;
        if ( !checkKeys( at, entry, [ "key", "occasion", "daysBefore", "audience", "topic", "sender", "subject", "preheader", "body" ], [ "bulk" ] ) ) {
            continue;
        }
        const label = `${at} (${entry.key})`;
        checkKey( at, entry.key, keys );
        checkInteger( `${label} daysBefore`, entry.daysBefore );
        checkOccasion( label, entry );
        checkTopic( label, entry.topic );
        if ( !SENDERS.includes( entry.sender ) ) {
            errors.push( `${label}: sender "${entry.sender}" is not one of ${SENDERS.join( ", " )}.` );
        }
        bySender[entry.sender] = ( bySender[entry.sender] || 0 ) + 1;
        if ( "bulk" in entry ) {
            if ( entry.bulk !== false ) {
                errors.push( `${label}: "bulk" may only be false.` );
            }
            notBulk++;
        }
        for ( const field of [ "subject", "preheader" ] ) {
            if ( checkString( `${label} ${field}`, entry[field] ) ) {
                checkText( `${label} ${field}`, entry[field] );
                checkLength( `${label} ${field}`, entry[field], LENGTHS[field], `an email ${field}` );
            }
        }

        // Body blocks: h, p, list, or button with href.
        const body = checkArray( `${label} body`, entry.body );
        if ( body.length && ( body.length < EMAIL_BLOCKS.min || body.length > EMAIL_BLOCKS.max ) ) {
            errors.push( `${label}: has ${body.length} body blocks; an email has ${EMAIL_BLOCKS.min} to ${EMAIL_BLOCKS.max}.` );
        }
        let hasButton = false;
        for ( const [ b, block ] of body.entries() ) {
            const blockAt = `${label} body[${b}]`;
            if ( !block || typeof block !== "object" || Array.isArray( block ) ) {
                errors.push( `${blockAt}: must be an object.` );
                continue;
            }
            if ( "button" in block ) {
                if ( checkKeys( blockAt, block, [ "button", "href" ] ) ) {
                    if ( checkString( blockAt, block.button ) ) {
                        checkText( blockAt, block.button );
                    }
                    checkPath( blockAt, block.href );
                    hasButton = true;
                }
            }
            else if ( "h" in block ) {
                if ( checkKeys( blockAt, block, [ "h" ] ) && checkString( blockAt, block.h ) ) {
                    checkText( blockAt, block.h );
                }
            }
            else if ( "p" in block ) {
                if ( checkKeys( blockAt, block, [ "p" ] ) && checkString( blockAt, block.p ) ) {
                    checkText( blockAt, block.p, { linkable: true } );
                }
            }
            else if ( "list" in block ) {
                if ( checkKeys( blockAt, block, [ "list" ] ) ) {
                    for ( const [ i, item ] of checkArray( blockAt, block.list ).entries() ) {
                        if ( checkString( `${blockAt} list[${i}]`, item ) ) {
                            checkText( `${blockAt} list[${i}]`, item, { linkable: true } );
                        }
                    }
                }
            }
            else {
                errors.push( `${blockAt}: must be an h, p, list, or button block.` );
            }
        }
        if ( hasButton ) {
            withButton++;
        }
    }
    checkOpenings( "emails.json subject", emails.map( ( e, i ) => [ `emails[${i}]`, e && e.subject ] ) );
    checkOpenings( "emails.json preheader", emails.map( ( e, i ) => [ `emails[${i}]`, e && e.preheader ] ) );
    const senders = Object.entries( bySender ).map( ( [ k, v ] ) => `${v} ${k}` ).join( ", " );
    summary.push( `emails.json\n    ${emails.length} emails (${senders}), ${withButton} with a button, ${notBulk} not bulk` );
}

// texts.json: texts and pushes, and the reply pools.
const textsFile = readFile( "texts.json" );
if ( textsFile && checkKeys( "texts.json", textsFile, [ "texts", "replies" ] ) ) {
    checkCharacters( "texts.json", textsFile );
    const keys = new Map();
    const texts = checkArray( "texts.json texts", textsFile.texts );
    let sms = 0;
    let push = 0;
    let shortSms = 0;
    let asksReply = 0;
    for ( const [ index, entry ] of texts.entries() ) {
        const at = `texts.json texts[${index}]`;
        if ( !entry || typeof entry !== "object" ) {
            errors.push( `${at}: must be an object.` );
            continue;
        }
        const isPush = entry.medium === "push";
        const common = [ "key", "medium", "occasion", "daysBefore", "hour", "audience", "topic", "text" ];
        const required = isPush ? common.concat( "title" ) : common.concat( "line" );
        const optional = isPush ? [] : [ "asksReply" ];
        if ( !checkKeys( at, entry, required, optional ) ) {
            continue;
        }
        const label = `${at} (${entry.key})`;
        checkKey( at, entry.key, keys );
        if ( !MEDIUMS.includes( entry.medium ) ) {
            errors.push( `${label}: medium "${entry.medium}" is not one of ${MEDIUMS.join( ", " )}.` );
        }
        checkInteger( `${label} daysBefore`, entry.daysBefore );
        checkInteger( `${label} hour`, entry.hour, 0, 23 );
        checkOccasion( label, entry );
        checkTopic( label, entry.topic );
        if ( checkString( `${label} text`, entry.text ) ) {
            checkText( `${label} text`, entry.text );
        }
        if ( isPush ) {
            push++;
            if ( checkString( `${label} title`, entry.title ) ) {
                checkText( `${label} title`, entry.title );
                checkLength( `${label} title`, entry.title, LENGTHS.pushTitle, "a push title" );
            }
            checkLength( `${label} text`, entry.text, LENGTHS.pushText, "a push text" );
        }
        else {
            sms++;
            if ( !LINES.includes( entry.line ) ) {
                errors.push( `${label}: line "${entry.line}" is not one of ${LINES.join( ", " )}.` );
            }
            if ( "asksReply" in entry ) {
                if ( entry.asksReply !== true ) {
                    errors.push( `${label}: "asksReply" may only be true.` );
                }
                asksReply++;
            }
            checkLength( `${label} text`, entry.text, LENGTHS.sms, "an SMS text" );
            if ( typeof entry.text === "string" && entry.text.length <= SMS_TYPICAL_MAX ) {
                shortSms++;
            }
        }
    }
    checkOpenings( "texts.json", texts.map( ( e, i ) => [ `texts[${i}]`, e && e.text ] ) );

    const replyCounts = [];
    if ( checkKeys( "texts.json replies", textsFile.replies, [ "yes", "no", "question", "thanks", "staff" ] ) ) {
        for ( const pool of [ "yes", "no", "question", "thanks", "staff" ] ) {
            const entries = checkArray( `texts.json replies.${pool}`, textsFile.replies[pool] );
            for ( const [ index, entry ] of entries.entries() ) {
                const at = `texts.json replies.${pool}[${index}]`;
                if ( checkString( at, entry ) ) {
                    checkText( at, entry, { allowed: [] } );
                    checkLength( at, entry, LENGTHS.reply, "a reply" );
                }
            }
            checkOpenings( `texts.json replies.${pool}`, entries.map( ( e, i ) => [ `[${i}]`, e ] ) );
            replyCounts.push( `${entries.length} ${pool}` );
        }
    }
    const share = sms ? Math.round( 100 * shortSms / sms ) : 0;
    summary.push( `texts.json\n    ${sms} texts (${share}% are ${SMS_TYPICAL_MAX} characters or fewer, ${asksReply} ask for a reply), ${push} pushes\n    replies: ${replyCounts.join( ", " )}` );
}

// conversations.json: the topics generated text conversations are built from.
const conversationsFile = readFile( "conversations.json" );
if ( conversationsFile && checkKeys( "conversations.json", conversationsFile, [ "topics" ] ) ) {
    checkCharacters( "conversations.json", conversationsFile );
    const keys = new Map();
    const topics = checkArray( "conversations.json topics", conversationsFile.topics );
    const byNumber = Object.fromEntries( CONVERSATION_NUMBERS.map( n => [ n, 0 ] ) );
    let entries = 0;
    let shortEntries = 0;
    for ( const [ index, topic ] of topics.entries() ) {
        const at = `conversations.json topics[${index}]`;
        if ( !checkKeys( at, topic, [ "key", "numbers", "audience" ].concat( Object.keys( CONVERSATION_LISTS ) ) ) ) {
            continue;
        }
        const label = `${at} (${topic.key})`;
        checkKey( at, topic.key, keys );
        const numbers = checkArray( `${label} numbers`, topic.numbers );
        for ( const number of numbers ) {
            if ( CONVERSATION_NUMBERS.includes( number ) ) {
                byNumber[number]++;
            }
            else {
                errors.push( `${label}: number "${number}" is not one of ${CONVERSATION_NUMBERS.join( ", " )}.` );
            }
        }
        if ( new Set( numbers ).size !== numbers.length ) {
            errors.push( `${label}: lists a number twice.` );
        }
        if ( !CONVERSATION_AUDIENCES.includes( topic.audience ) ) {
            errors.push( `${label}: audience "${topic.audience}" is not one of ${CONVERSATION_AUDIENCES.join( ", " )}.` );
        }
        for ( const [ list, minimum ] of Object.entries( CONVERSATION_LISTS ) ) {
            const texts = checkArray( `${label} ${list}`, topic[list] );
            if ( texts.length && texts.length < minimum ) {
                errors.push( `${label} ${list}: has ${texts.length} entries; it needs at least ${minimum}.` );
            }
            for ( const [ i, text ] of texts.entries() ) {
                const textAt = `${label} ${list}[${i}]`;
                if ( !checkString( textAt, text ) ) {
                    continue;
                }
                checkText( textAt, text, { allowed: CONVERSATION_TOKENS, known: CONVERSATION_TOKENS, lava: false } );
                checkLength( textAt, text, LENGTHS.conversation, "a conversation text" );
                entries++;
                if ( text.length <= SMS_TYPICAL_MAX ) {
                    shortEntries++;
                }
            }
            checkOpenings( `${label} ${list}`, texts.map( ( t, i ) => [ `[${i}]`, t ] ) );
        }
    }
    const share = entries ? Math.round( 100 * shortEntries / entries ) : 0;
    const where = Object.entries( byNumber ).map( ( [ k, v ] ) => `${v} ${k}` ).join( ", " );
    summary.push( `conversations.json\n    ${topics.length} topics (${where}), ${entries} texts (${share}% are ${SMS_TYPICAL_MAX} characters or fewer)` );
}

// snippets.json: the SMS snippets staff insert in SMS Conversations (decision 299). A shared snippet has a
// category so staff find it in the picker. An owned one belongs to a staff member, or to each campus pastor. A snippet
// with campusGuids loads once per campus, so it may use the campus tokens. Every one fills in the person's nickname.
const snippetsFile = readFile( "snippets.json" );
if ( snippetsFile && checkKeys( "snippets.json", snippetsFile, [ "replyShare", "snippets", "access" ], [ "$accessComment" ] ) ) {
    checkCharacters( "snippets.json", snippetsFile );
    if ( typeof snippetsFile.replyShare !== "number" || !( snippetsFile.replyShare > 0 && snippetsFile.replyShare <= 1 ) ) {
        errors.push( "snippets.json replyShare: must be a share above 0 and up to 1." );
    }
    const topicKeys = conversationsFile && Array.isArray( conversationsFile.topics ) ? conversationsFile.topics.map( t => t && t.key ) : [];
    const snippetCategories = CATEGORIES.filter( c => c.entityType === "Rock.Model.Snippet" ).map( c => c.key );
    const longestCampus = CAMPUS_KEYS.map( k => CAMPUS_NAMES[k] ).reduce( ( a, b ) => b.length > a.length ? b : a, "" );
    const keys = new Map();
    const guids = new Map();
    const usedCategories = new Set();
    const snippets = checkArray( "snippets.json snippets", snippetsFile.snippets );
    let personal = 0;
    let perCampus = 0;
    let withTopics = 0;
    let short = 0;
    for ( const [ index, snippet ] of snippets.entries() ) {
        const at = `snippets.json snippets[${index}]`;
        if ( !checkKeys( at, snippet, [ "key", "name", "description", "topics", "text" ], [ "owner", "category", "guid", "campusGuids" ] ) ) {
            continue;
        }
        const label = `${at} (${snippet.key})`;
        checkKey( at, snippet.key, keys );

        const isPerCampus = "campusGuids" in snippet;
        if ( isPerCampus === ( "guid" in snippet ) ) {
            errors.push( `${label}: needs a guid, or campusGuids with one per campus, but not both.` );
        }
        if ( isPerCampus ) {
            perCampus++;
            if ( checkKeys( `${label} campusGuids`, snippet.campusGuids, CAMPUS_KEYS ) ) {
                for ( const [ campus, guid ] of Object.entries( snippet.campusGuids ) ) {
                    checkGuid( `${label} campusGuids.${campus}`, guid, guids );
                }
            }
        }
        else if ( "guid" in snippet ) {
            checkGuid( `${label} guid`, snippet.guid, guids );
        }

        if ( "owner" in snippet ) {
            personal++;
            if ( snippet.owner === CAMPUS_PASTOR && !isPerCampus ) {
                errors.push( `${label}: a campus pastor's snippet needs campusGuids, one per campus.` );
            }
            else if ( snippet.owner !== CAMPUS_PASTOR && !STAFF_KEYS.includes( snippet.owner ) ) {
                errors.push( `${label}: owner "${snippet.owner}" is not a staff key in configuration/staff.json or "${CAMPUS_PASTOR}".` );
            }
        }
        else if ( !( "category" in snippet ) ) {
            errors.push( `${label}: a shared snippet needs a category, so staff find it in the snippet picker.` );
        }
        if ( "category" in snippet ) {
            if ( snippetCategories.includes( snippet.category ) ) {
                usedCategories.add( snippet.category );
            }
            else {
                errors.push( `${label}: category "${snippet.category}" is not a Rock.Model.Snippet category in configuration/categories.json.` );
            }
        }

        if ( !Array.isArray( snippet.topics ) ) {
            errors.push( `${label} topics: must be a list of topic keys from conversations.json, or empty.` );
        }
        else {
            withTopics += snippet.topics.length ? 1 : 0;
            for ( const topic of snippet.topics ) {
                if ( !topicKeys.includes( topic ) ) {
                    errors.push( `${label}: topic "${topic}" is not a topic in conversations.json.` );
                }
            }
            if ( new Set( snippet.topics ).size !== snippet.topics.length ) {
                errors.push( `${label}: lists a topic twice.` );
            }
        }

        // The campus tokens belong to a snippet that loads once per campus, and {staff} to a campus pastor's.
        const campusTokens = isPerCampus ? [ "{campus}" ] : [];
        const textTokens = campusTokens.concat( isPerCampus ? [ "{serviceTimes}" ] : [], snippet.owner === CAMPUS_PASTOR ? [ "{staff}" ] : [] );
        if ( checkString( `${label} name`, snippet.name ) ) {
            checkText( `${label} name`, snippet.name, { allowed: campusTokens, known: SNIPPET_TOKENS, lava: false } );
            checkLength( `${label} name`, snippet.name.split( "{campus}" ).join( longestCampus ), LENGTHS.snippetName, "a snippet name" );
        }
        if ( checkString( `${label} description`, snippet.description ) ) {
            checkText( `${label} description`, snippet.description, { allowed: campusTokens, known: SNIPPET_TOKENS, lava: false } );
        }
        if ( checkString( `${label} text`, snippet.text ) ) {
            checkText( `${label} text`, snippet.text, { allowed: textTokens, known: SNIPPET_TOKENS } );
            checkLength( `${label} text`, snippet.text, LENGTHS.sms, "a snippet" );
            if ( snippet.text.split( LAVA ).length !== 2 ) {
                errors.push( `${label} text: must fill in the person's nickname with ${LAVA} once.` );
            }
            short += snippet.text.length <= SMS_TYPICAL_MAX ? 1 : 0;
        }
    }
    for ( const category of snippetCategories.filter( c => !usedCategories.has( c ) ) ) {
        errors.push( `snippets.json: no snippet is in the ${category} category.` );
    }

    // The access walkthrough: each question names a staff member, one of this file's snippets, and Rock's answer.
    for ( const [ index, row ] of checkArray( "snippets.json access", snippetsFile.access ).entries() ) {
        const at = `snippets.json access[${index}]`;
        if ( !checkKeys( at, row, [ "person", "name", "snippet", "action", "allowed" ] ) ) {
            continue;
        }
        if ( !STAFF_PEOPLE.includes( row.person ) ) {
            errors.push( `${at}: person ${row.person} is not a staff member in configuration/staff.json.` );
        }
        if ( !guids.has( row.snippet ) ) {
            errors.push( `${at}: snippet ${row.snippet} is not a snippet Guid in this file.` );
        }
        if ( row.action !== "View" && row.action !== "Edit" ) {
            errors.push( `${at}: action must be View or Edit.` );
        }
        if ( typeof row.allowed !== "boolean" ) {
            errors.push( `${at}: allowed must be true or false.` );
        }
    }
    checkOpenings( "snippets.json", snippets.map( ( s, i ) => [ `snippets[${i}]`, s && s.text ] ) );
    const share = snippets.length ? Math.round( 100 * short / snippets.length ) : 0;
    summary.push( `snippets.json\n    ${snippets.length} snippets (${snippets.length - personal} shared, ${personal} personal, ${perCampus} once per campus) in ${usedCategories.size} categories, ${withTopics} used in generated replies (${share}% are ${SMS_TYPICAL_MAX} characters or fewer)` );
}

// email-sections.json: the church's sections for Rock's newer email editor (decision 301). Each section's
// markup and thumbnails in email-sections/ come from make-sections.js, so a section whose markup no longer holds its
// own words, Guids, and link was changed without running it again.
const sectionsFile = readFile( "email-sections.json" );
if ( sectionsFile && checkKeys( "email-sections.json", sectionsFile, [ "category", "sections" ] ) ) {
    checkCharacters( "email-sections.json", sectionsFile );
    const sectionCategories = CATEGORIES.filter( c => c.entityType === "Rock.Model.EmailSection" ).map( c => c.key );
    if ( !sectionCategories.includes( sectionsFile.category ) ) {
        errors.push( `email-sections.json category: "${sectionsFile.category}" is not a Rock.Model.EmailSection category in configuration/categories.json.` );
    }
    const approvedPhotos = Object.fromEntries( EVENT_PHOTOS.filter( p => p.status.startsWith( "approved" ) ).map( p => [ p.event, p.file ] ) );
    const datasets = [ ...new Set( CAMPUSES.flatMap( c => c.datasets ) ) ];
    const keys = new Map();
    const guids = new Map();
    let thumbnails = 0;
    const sections = checkArray( "email-sections.json sections", sectionsFile.sections );
    for ( const [ index, section ] of sections.entries() ) {
        const at = `email-sections.json sections[${index}]`;
        if ( !checkKeys( at, section, [ "key", "name", "usageSummary", "guid", "thumbnailGuid", "title", "text", "button" ], [ "card", "eyebrow", "serviceTimes", "image" ] ) ) {
            continue;
        }
        const label = `${at} (${section.key})`;
        checkKey( at, section.key, keys );
        checkGuid( `${label} guid`, section.guid, guids );
        checkGuid( `${label} thumbnailGuid`, section.thumbnailGuid, guids );
        for ( const field of [ "name", "usageSummary", "title", "text" ].concat( "eyebrow" in section ? [ "eyebrow" ] : [] ) ) {
            if ( checkString( `${label} ${field}`, section[field] ) ) {
                checkText( `${label} ${field}`, section[field], { allowed: [], known: [], lava: false } );
            }
        }
        checkLength( `${label} name`, section.name, LENGTHS.sectionName, "a section name" );
        checkLength( `${label} usageSummary`, section.usageSummary, LENGTHS.usageSummary, "a usage summary" );
        if ( "card" in section && section.card !== "light" && section.card !== "accent" ) {
            errors.push( `${label} card: must be "light" or "accent".` );
        }
        if ( "serviceTimes" in section && section.serviceTimes !== true ) {
            errors.push( `${label} serviceTimes: may only be true.` );
        }
        if ( "image" in section && checkKeys( `${label} image`, section.image, [ "event", "guid", "alt" ] ) ) {
            checkGuid( `${label} image guid`, section.image.guid, guids );
            if ( !approvedPhotos[section.image.event] ) {
                errors.push( `${label} image: event "${section.image.event}" has no approved photo in photos/events/manifest.json.` );
            }
            if ( checkString( `${label} image alt`, section.image.alt ) ) {
                checkText( `${label} image alt`, section.image.alt, { allowed: [], known: [], lava: false } );
            }
        }
        if ( checkKeys( `${label} button`, section.button, [ "text", "href" ] ) ) {
            if ( checkString( `${label} button text`, section.button.text ) ) {
                checkText( `${label} button text`, section.button.text, { allowed: [], known: [], lava: false } );
            }
            checkPath( `${label} button`, section.button.href );
        }

        // The markup make-sections.js built from this entry, with Rock's editor code.
        const markupFile = `email-sections/${section.key}.html`;
        let markup = null;
        try {
            markup = fs.readFileSync( path.join( libraryDir, markupFile ), "utf8" );
        }
        catch {
            errors.push( `${label}: ${markupFile} is missing. Run email-sections/make-sections.js.` );
        }
        if ( markup !== null ) {
            const html = text => text.replace( /&/g, "&amp;" ).replace( /</g, "&lt;" ).replace( />/g, "&gt;" );
            const expected = [
                [ "a section component", 'class="margin-wrapper margin-wrapper-for-section component component-section"' ],
                [ "its Guid", `data-email-section-guid="${String( section.guid ).toLowerCase()}"` ],
                [ "its title", `>${html( section.title )}</h2>` ],
                [ "its text", `>${html( section.text )}</p>` ],
                [ "its button's link", `href="${SITE}${section.button.href}"` ],
                [ "its button's text", `>${html( section.button.text )}</a>` ]
            ];
            if ( section.image ) {
                expected.push( [ "its image", `data-image-guid="${section.image.guid}"` ] );
            }
            for ( const [ what, text ] of expected ) {
                if ( !markup.includes( text ) ) {
                    errors.push( `${label}: ${markupFile} does not have ${what} (${text}). Run email-sections/make-sections.js.` );
                }
            }
            if ( ( markup.split( CAMPUS_SERVICE_TIMES ).length - 1 ) !== ( section.serviceTimes ? 1 : 0 ) ) {
                errors.push( `${label}: ${markupFile} must hold ${CAMPUS_SERVICE_TIMES} once when the section lists service times, and never otherwise.` );
            }
            if ( /<script|\son[a-z]+\s*=|javascript:/i.test( markup ) ) {
                errors.push( `${label}: ${markupFile} has a script, an event attribute, or a javascript: link, which Rock's editor does not save in a section.` );
            }
        }

        for ( const thumbnail of section.serviceTimes ? datasets.map( d => `${section.key}-${d}.png` ) : [ `${section.key}.png` ] ) {
            let bytes = null;
            try {
                bytes = fs.readFileSync( path.join( libraryDir, "email-sections", thumbnail ) );
            }
            catch {
                errors.push( `${label}: email-sections/${thumbnail} is missing. Run email-sections/make-sections.js.` );
            }
            if ( bytes && bytes.subarray( 0, 8 ).toString( "hex" ) !== "89504e470d0a1a0a" ) {
                errors.push( `${label}: email-sections/${thumbnail} is not a PNG.` );
            }
            thumbnails += bytes ? 1 : 0;
        }
    }
    summary.push( `email-sections.json\n    ${sections.length} sections (${sections.filter( s => s && s.image ).length} with a photo, ${sections.filter( s => s && s.card ).length} cards), their markup, and ${thumbnails} thumbnails` );
}

// The wizard-ready template (decision 269) is the Legacy template in Rock's newer editor format. That editor has
// no Lava fields, so it writes in each Lava field's color, and its logo image component points at the Legacy logo file.
if ( configuration.wizardTemplate ) {
    const file = configuration.wizardTemplate.html;
    const html = fs.readFileSync( path.join( libraryDir, "..", "..", file ), "utf8" );
    for ( const [ field, color ] of Object.entries( configuration.template.lavaFields ) ) {
        if ( !html.toUpperCase().includes( color.toUpperCase() ) ) {
            errors.push( `${file}: does not use the Legacy template's ${field} ${color}.` );
        }
    }
    const required = [
        [ "the structure dropzone the editor adds rows to", 'class="structure-dropzone"' ],
        [ "the logo file", `data-image-guid="${configuration.template.logoGuid}"` ],
        [ "Rock's unsubscribe option", "[[ UnsubscribeOption ]]" ]
    ];
    for ( const [ what, text ] of required ) {
        if ( !html.toUpperCase().includes( text.toUpperCase() ) ) {
            errors.push( `${file}: has no ${what} (${text}).` );
        }
    }
    const lavaFieldNames = Object.keys( configuration.template.lavaFields ).join( "|" );
    if ( new RegExp( `\\{\\{\\s*(${lavaFieldNames})\\s*\\}\\}|lava-fields` ).test( html ) ) {
        errors.push( `${file}: uses Lava fields, which Rock's newer email editor does not have.` );
    }
    summary.push( `${path.basename( file )}\n    the Legacy template's ${Object.keys( configuration.template.lavaFields ).length} colors, its logo, and the unsubscribe option` );
}

for ( const line of summary ) {
    console.log( line );
}
for ( const e of errors ) {
    console.log( `ERROR   ${e}` );
}
console.log( `\nMessage library: 7 files checked. ${errors.length} errors.` );
process.exit( errors.length ? 1 : 0 );
