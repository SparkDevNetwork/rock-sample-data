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
    Builds this folder from ../email-sections.json (decision 301):

    1. Each section's markup, <key>.html, made by Rock's own email editor code. It runs make-sections.spec.ts with
       the Jest, jsdom, and TypeScript a built Rock checkout already has in Rock.JavaScript.Obsidian, as Rock's own
       tests do, and writes nothing into the checkout.
    2. Each section's thumbnail, <key>.png, the way Rock's editor makes one when staff save a section: the section
       drawn at the width of the template's body, with a clear background. A section that lists the campuses' service
       times gets one thumbnail per dataset, <key>-<dataset>.png, with that dataset's campuses. Chrome draws it.

    Run it after any change to ../email-sections.json, the template's colors, or the event photos a section shows:

      node content/communications/email-sections/make-sections.js --rock C:\Development\Rock-21

    Rock 20.0.10 and 21.0.2 build the same markup for these components, so either checkout works. Then run
    node content/communications/check-library.js.
*/

const fs = require( "fs" );
const os = require( "os" );
const path = require( "path" );
const { execFileSync } = require( "child_process" );

const folder = __dirname;
const repository = path.join( folder, "..", "..", ".." );
const readJson = file => JSON.parse( fs.readFileSync( path.join( repository, file ), "utf8" ) );

/** The width of the branded template's body inside its padding, where a section is dropped. */
const contentWidth = 504;

/** The loader fills this in with a line for each of the dataset's campuses (loader/loading/EmailSectionsLoader.cs). */
const campusServiceTimesToken = "{campusServiceTimes}";

const args = process.argv.slice( 2 );
const option = name => {
    const index = args.indexOf( `--${name}` );
    return index >= 0 ? args[index + 1] : null;
};

const rock = option( "rock" ) || process.env.ROCK_PATH;
if ( !rock ) {
    console.error( "Give a built Rock checkout with --rock, such as --rock C:\\Development\\Rock-21." );
    process.exit( 1 );
}

const obsidian = path.join( rock, "Rock.JavaScript.Obsidian" );
const jest = path.join( obsidian, "node_modules", "jest", "bin", "jest.js" );
if ( !fs.existsSync( jest ) ) {
    console.error( `${obsidian} has no node_modules with Jest. Run npm install there first.` );
    process.exit( 1 );
}

// 1. The markup, from Rock's own email editor code.
const work = fs.mkdtempSync( path.join( os.tmpdir(), "rock-sample-data-sections-" ) );
const slash = p => p.replace( /\\/g, "/" );
fs.writeFileSync( path.join( work, "jest.config.js" ), `module.exports = ${JSON.stringify( {
    rootDir: slash( obsidian ),
    roots: [ slash( folder ) ],
    testEnvironment: "jsdom",
    testEnvironmentOptions: { customExportConditions: [ "node", "node-addons" ] },
    testMatch: [ "**/make-sections.spec.ts" ],
    transform: { "^.+\\.ts$": [ slash( path.join( obsidian, "node_modules", "ts-jest" ) ), { tsconfig: slash( path.join( obsidian, "Tests", "tsconfig.json" ) ), diagnostics: false } ] },
    moduleFileExtensions: [ "js", "ts", "json", "obs" ],
    moduleNameMapper: { "^@Obsidian/(.*)$": `${slash( obsidian )}/Framework/$1` }
}, null, 4 )};\n` );
execFileSync( process.execPath, [ jest, "--config", path.join( work, "jest.config.js" ) ], { stdio: "inherit" } );

// 2. The thumbnails, drawn by a browser.
// Edge on Windows draws the same, but prints nothing for --dump-dom, which measures each section.
const browser = option( "browser" ) || [
    "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
    "/usr/bin/google-chrome",
    "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
].find( p => fs.existsSync( p ) );
if ( !browser ) {
    console.error( "Found no Chrome to draw the thumbnails. Give a Chromium browser that prints --dump-dom with --browser <path>." );
    process.exit( 1 );
}

const library = readJson( "content/communications/email-sections.json" );
const photos = Object.fromEntries( readJson( "photos/events/manifest.json" ).images.map( i => [ i.event, i.file ] ) );
const campuses = readJson( "configuration/campuses.json" ).campuses;
const slots = Object.fromEntries( readJson( "configuration/schedules.json" ).slots.map( s => [ s.key, s.start ] ) );
const datasets = [ ...new Set( campuses.flatMap( c => c.datasets ) ) ].sort();
const browserFlags = [ "--headless=new", "--disable-gpu", "--hide-scrollbars", "--default-background-color=00000000", `--user-data-dir=${path.join( work, "browser" )}` ];

for ( const section of library.sections ) {
    let html = fs.readFileSync( path.join( folder, `${section.key}.html` ), "utf8" ).trim();
    if ( section.image ) {
        const photoUrl = `file:///${slash( path.join( repository, "photos", photos[section.image.event] ) )}`;
        html = html.replace( new RegExp( `src="/GetImage\\.ashx\\?[^"]*guid=${section.image.guid}[^"]*"`, "i" ), `src="${photoUrl}"` );
    }

    const variants = html.includes( campusServiceTimesToken )
        ? datasets.map( d => [ `${section.key}-${d}`, html.replace( campusServiceTimesToken, campusServiceTimes( d ) ) ] )
        : [ [ section.key, html ] ];
    for ( const [ name, variant ] of variants ) {
        const page = path.join( work, `${name}.html` );
        fs.writeFileSync( page, `<!DOCTYPE html><html><head><meta charset="utf-8"><title>0</title><style>html, body { margin: 0; padding: 0; background: transparent; }</style></head>
<body><div id="frame" style="width: ${contentWidth}px;">${variant}</div>
<script>document.title = Math.ceil( document.getElementById( "frame" ).getBoundingClientRect().height );</script></body></html>\n` );
        const url = `file:///${slash( page )}`;
        const dom = execFileSync( browser, [ ...browserFlags, "--dump-dom", url ], { encoding: "utf8" } );
        const height = Number( ( dom.match( /<title>(\d+)<\/title>/ ) || [] )[1] );
        if ( !height ) {
            throw new Error( `The browser did not measure ${name}.` );
        }

        const png = path.join( folder, `${name}.png` );
        execFileSync( browser, [ ...browserFlags, `--window-size=${contentWidth},${height}`, `--screenshot=${png}`, url ], { stdio: "ignore" } );
        console.log( `${path.basename( png )}: ${contentWidth} by ${height}` );
    }
}

fs.rmSync( work, { recursive: true, force: true } );

/**
 * One line for each of a dataset's campuses with its Sunday service times, the way the loader fills the token.
 */
function campusServiceTimes( dataset ) {
    return campuses.filter( c => c.datasets.includes( dataset ) )
        .map( c => `<p style="margin: 0;"><strong>${c.name}</strong>&nbsp;&nbsp;${serviceTimes( c.schedules.filter( s => s.type === "weekend" ).map( s => slots[s.slot] ) )}</p>` )
        .join( "" );
}

/**
 * "9:00 and 10:30 a.m.", as loader/generation/MessageRenderer.cs ServiceTimes writes it.
 */
function serviceTimes( starts ) {
    const minutes = [ ...new Set( starts ) ].map( s => Number( s.slice( 0, 2 ) ) * 60 + Number( s.slice( 3, 5 ) ) ).sort( ( a, b ) => a - b );
    const clock = m => `${Math.floor( m / 60 ) % 12 === 0 ? 12 : Math.floor( m / 60 ) % 12}:${String( m % 60 ).padStart( 2, "0" )}`;
    const half = m => m < 720 ? "a.m." : "p.m.";
    const parts = minutes.map( ( m, i ) => i === minutes.length - 1 || half( minutes[i + 1] ) !== half( m ) ? `${clock( m )} ${half( m )}` : clock( m ) );
    return parts.length === 1 ? parts[0]
        : parts.length === 2 ? `${parts[0]} and ${parts[1]}`
            : `${parts.slice( 0, -1 ).join( ", " )}, and ${parts[parts.length - 1]}`;
}
