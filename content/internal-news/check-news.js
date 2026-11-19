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
    Checks the staff homepage's weekly updates against README.md: updates.json in this folder and
    configuration/staff-homepage.json. Keys are unique, every occasion is one the loader dates, every feature shows an
    approved event image, every article's author is a staff member with a staff photo, lengths fit Rock's homepage,
    text uses plain characters with no Lava or full web addresses, and there are enough evergreen pieces to fill a
    year. Prints a summary and exits with 1 when anything fails.

    Usage: node content/internal-news/check-news.js
*/

const fs = require( "fs" );
const path = require( "path" );

const root = path.join( __dirname, "..", ".." );
const read = file => JSON.parse( fs.readFileSync( path.join( root, file ), "utf8" ) );
const news = read( "content/internal-news/updates.json" );
const homepage = read( "configuration/staff-homepage.json" );
const eventImages = read( "photos/events/manifest.json" ).images;
const staffPhotos = read( "photos/staff/manifest.json" ).images;
const calendarEvents = read( "configuration/calendar.json" ).events.map( e => e.key );

// The occasions loader/generation/CommunicationCalendar.cs dates, and event:<key> for any calendar event.
const OCCASIONS = [ "easter", "christmas", "thanksgiving", "year-end", "giving-statement", "commitment-sunday", "campaign-update", "baptism-sunday",
    "student-camp", "mission-trip", "starting-point", "foundations", "vbs", "group-launch", "serve-sunday" ];
const LENGTHS = {
    title: { min: 5, max: 60 },
    paragraph: { min: 20, max: 250 }
};
const PARAGRAPHS = { min: 1, max: 3 };
const WEEKS_BEFORE = { min: -4, max: 12 };
const KEY_PATTERN = /^[a-z0-9]+(-[a-z0-9]+)*$/;
const GUID_PATTERN = /^[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}$/;
const METRICS = [ "active-records", "active-families", "active-connection-requests" ];

const errors = [];
const keys = new Set();

const checkText = ( at, value ) => {
    if ( typeof value !== "string" ) {
        return;
    }

    const notPlain = [ ...value ].filter( c => c < " " || c > "~" );
    if ( notPlain.length ) {
        errors.push( `${at}: has characters that are not plain. Use straight quotes, commas, or periods. "${value}"` );
    }

    if ( value.includes( "--" ) ) {
        errors.push( `${at}: uses "--". Use a comma or a period.` );
    }

    if ( /\{\{|\}\}|\{%|%\}|[{}<>]/.test( value ) ) {
        errors.push( `${at}: has Lava, braces, or HTML. The loader writes the HTML.` );
    }

    if ( /https?:|www\./i.test( value ) ) {
        errors.push( `${at}: has a full web address. Every update links to the site in configuration/staff-homepage.json.` );
    }

    for ( const address of value.match( /[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}/g ) || [] ) {
        if ( ![ "example.org", "rocksolidchurchdemo.com" ].includes( address.split( "@" )[1].toLowerCase() ) ) {
            errors.push( `${at}: has the email ${address}. Use example.org or rocksolidchurchdemo.com.` );
        }
    }
};

const checkLength = ( at, value, limits ) => {
    if ( typeof value !== "string" || value.length < limits.min || value.length > limits.max ) {
        errors.push( `${at}: must be ${limits.min} to ${limits.max} characters (it is ${typeof value === "string" ? value.length : "missing"}).` );
    }

    checkText( at, value );
};

const checkPiece = ( kind, piece, isDated ) => {
    const at = `${kind} ${piece.key}`;
    if ( !KEY_PATTERN.test( piece.key || "" ) || keys.has( piece.key ) ) {
        errors.push( `${at}: needs a unique kebab-case key.` );
    }

    keys.add( piece.key );
    if ( isDated ) {
        const occasion = piece.occasion || "";
        const isKnown = occasion.startsWith( "event:" ) ? calendarEvents.includes( occasion.slice( "event:".length ) ) : OCCASIONS.includes( occasion );
        if ( !isKnown ) {
            errors.push( `${at}: occasion ${occasion} is not one the loader dates. Use ${OCCASIONS.join( ", " )}, or event:<key> for a calendar event.` );
        }

        if ( !Number.isInteger( piece.weeksBefore ) || piece.weeksBefore < WEEKS_BEFORE.min || piece.weeksBefore > WEEKS_BEFORE.max ) {
            errors.push( `${at}: weeksBefore must be a whole number from ${WEEKS_BEFORE.min} to ${WEEKS_BEFORE.max}.` );
        }
    } else if ( piece.occasion != null || piece.weeksBefore != null ) {
        errors.push( `${at}: an evergreen piece has no occasion or weeksBefore.` );
    }

    checkLength( `${at} title`, piece.title, LENGTHS.title );
};

for ( const [ list, isDated ] of [ [ news.features, true ], [ news.evergreenFeatures, false ] ] ) {
    for ( const feature of list || [] ) {
        checkPiece( "feature", feature, isDated );
        const text = feature.text || [];
        if ( text.length < PARAGRAPHS.min || text.length > PARAGRAPHS.max ) {
            errors.push( `feature ${feature.key}: needs ${PARAGRAPHS.min} to ${PARAGRAPHS.max} paragraphs.` );
        }

        text.forEach( ( p, i ) => checkLength( `feature ${feature.key} text[${i}]`, p, LENGTHS.paragraph ) );
        if ( !eventImages.some( i => i.event === feature.image && String( i.status ).startsWith( "approved" ) ) ) {
            errors.push( `feature ${feature.key}: image ${feature.image} is not an approved image in photos/events/manifest.json.` );
        }
    }
}

for ( const [ list, isDated ] of [ [ news.articles, true ], [ news.evergreenArticles, false ] ] ) {
    for ( const article of list || [] ) {
        checkPiece( "article", article, isDated );
        checkLength( `article ${article.key} text`, article.text, LENGTHS.paragraph );
        if ( !staffPhotos.some( p => `${p.household}/${p.member}` === article.author ) ) {
            errors.push( `article ${article.key}: author ${article.author} is not a staff member with a photo in photos/staff/manifest.json.` );
        }
    }
}

// Every Monday needs a feature, and most need two or three articles from different authors.
if ( ( news.evergreenFeatures || [] ).length < 6 ) {
    errors.push( "evergreenFeatures: needs at least 6, so a quiet stretch of the year does not repeat one every few weeks." );
}

if ( new Set( ( news.evergreenArticles || [] ).map( a => a.author ) ).size < 3 ) {
    errors.push( "evergreenArticles: needs at least 3 authors, so an update can have three articles from different people." );
}

// The homepage configuration.
const updates = homepage.updates || {};
for ( const [ key, value ] of [ [ "updates.contentChannel", updates.contentChannel ], [ "updates.rockItem", updates.rockItem ], [ "updates.author", updates.author ], [ "block", homepage.block ], [ "quickLinks.content", ( homepage.quickLinks || {} ).content ] ] ) {
    if ( !GUID_PATTERN.test( value || "" ) ) {
        errors.push( `configuration/staff-homepage.json: ${key} needs an upper-case Guid.` );
    }
}

if ( !Number.isInteger( updates.weeks ) || updates.weeks < 1 || updates.weeks > 104 ) {
    errors.push( "configuration/staff-homepage.json: updates.weeks must be 1 to 104." );
}

if ( !/^([01][0-9]|2[0-3]):[0-5][0-9]$/.test( updates.postTime || "" ) ) {
    errors.push( "configuration/staff-homepage.json: updates.postTime must be a time such as 08:00." );
}

if ( !/^https:\/\/[a-z0-9.-]+(\/.*)?$/.test( updates.link || "" ) ) {
    errors.push( "configuration/staff-homepage.json: updates.link must be an https link. Rock's homepage template adds https:// to it." );
}

for ( const count of Object.keys( updates.articleCounts || {} ) ) {
    if ( ![ "1", "2", "3" ].includes( count ) || !( updates.articleCounts[count] > 0 ) ) {
        errors.push( `configuration/staff-homepage.json: articleCounts ${count} must be 1, 2, or 3 with a positive weight. Rock's article list holds one to three.` );
    }
}

for ( const metric of homepage.metrics || [] ) {
    if ( !METRICS.includes( metric.key ) || !GUID_PATTERN.test( metric.guid || "" ) || !/^\d\d:\d\d$/.test( ( metric.schedule || {} ).time || "" ) ) {
        errors.push( `configuration/staff-homepage.json: metric ${metric.key} needs one of ${METRICS.join( ", " )}, Rock's Guid, and a schedule time.` );
    }
}

for ( const link of ( homepage.quickLinks || {} ).links || [] ) {
    checkText( `quick link ${link.label}`, link.label );
    if ( !/^~\/[a-z0-9/-]+$/.test( link.href || "" ) ) {
        errors.push( `quick link ${link.label}: href ${link.href} must be a Rock route such as ~/web/calendars.` );
    }
}

const authors = new Set( [ ...( news.articles || [] ), ...( news.evergreenArticles || [] ) ].map( a => a.author ) );
console.log( `Staff updates: ${( news.features || [] ).length} dated and ${( news.evergreenFeatures || [] ).length} evergreen features, ${( news.articles || [] ).length} dated and ${( news.evergreenArticles || [] ).length} evergreen articles by ${authors.size} staff. ${errors.length} problems.` );
for ( const error of errors ) {
    console.log( `  ${error}` );
}

process.exit( errors.length > 0 ? 1 : 0 );
