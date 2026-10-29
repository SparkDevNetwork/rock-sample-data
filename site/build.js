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
    Builds the public site into site/dist. Run from anywhere: node site/build.js

    It needs only Node.js, with no packages. It reads the repository's data files, copies the
    photos the pages show, builds the downloads and their zips, and writes every page. Open
    site/dist/index.html in a browser to look at it; GitHub Pages publishes the same folder.
*/

const fs = require( "fs" );
const path = require( "path" );
const { load } = require( "./src/data" );
const { page, relativeTo } = require( "./src/layout" );
const { writeZip } = require( "./src/zip" );

const siteDir = __dirname;
const dist = path.join( siteDir, "dist" );
const model = load();
const repo = model.root;

function copy( from, to ) {
    fs.mkdirSync( path.dirname( to ), { recursive: true } );
    fs.copyFileSync( from, to );
}

function copyInto( fromDir, toDir, files ) {
    for ( const f of files ) {
        copy( path.join( fromDir, f ), path.join( toDir, f ) );
    }
}

fs.rmSync( dist, { recursive: true, force: true } );
fs.mkdirSync( dist, { recursive: true } );

// Site assets.
fs.cpSync( path.join( siteDir, "src", "assets" ), path.join( dist, "assets" ), { recursive: true } );

// Photos the pages show.
const churchDir = path.join( repo, "photos/references/church" );
const brandDir = path.join( churchDir, "branding" );
const castFiles = fs.readdirSync( path.join( repo, "photos/cast" ) ).filter( f => f.endsWith( ".jpg" ) );
const servingFiles = model.opportunities.filter( o => o.photo ).map( o => o.photo );
const groupFiles = model.smallGroups.filter( g => g.photo ).map( g => g.photo );
const campusFiles = model.campuses.filter( c => c.photo ).map( c => c.photo );
copyInto( churchDir, path.join( dist, "images/church" ), model.churchViews.map( v => v.jpeg ) );
copyInto( path.join( repo, "photos/cast" ), path.join( dist, "images/cast" ), castFiles );
copyInto( path.join( repo, "photos/opportunities" ), path.join( dist, "images/serving" ), servingFiles );
copyInto( path.join( repo, "photos/groups" ), path.join( dist, "images/groups" ), groupFiles );
copyInto( path.join( repo, "photos/campuses" ), path.join( dist, "images/campuses" ), campusFiles );

// Single-file downloads: every logo file, and each building view and small group photo as JPEG and PNG master.
const brandFiles = fs.readdirSync( brandDir ).filter( f => /\.(svg|png)$/.test( f ) );
copyInto( brandDir, path.join( dist, "downloads/brand" ), brandFiles );
copyInto( churchDir, path.join( dist, "downloads/church" ), model.churchViews.flatMap( v => [ v.jpeg, v.file ] ) );
copyInto( path.join( repo, "photos/groups" ), path.join( dist, "downloads/groups" ), groupFiles );
copyInto( path.join( repo, "photos/originals/groups" ), path.join( dist, "downloads/groups" ), groupFiles.map( f => f.replace( /\.jpg$/, ".png" ) ) );

// Calendar event images: shown on the church page, and each as a JPEG and its PNG master on the brand page.
const eventFiles = model.calendarEvents.filter( e => e.photo ).map( e => e.photo );
copyInto( path.join( repo, "photos/events" ), path.join( dist, "images/events" ), eventFiles );
copyInto( path.join( repo, "photos/events" ), path.join( dist, "downloads/events" ), eventFiles );
copyInto( path.join( repo, "photos/originals/events" ), path.join( dist, "downloads/events" ), model.calendarEvents.filter( e => e.original ).map( e => e.original ) );

// The original full-group photos are downloads only: no package carries them (decision 196).
const fullGroupDir = path.join( repo, "photos/groups-full-members" );
const fullGroupFiles = model.fullGroupPhotos.map( p => p.photo );
copyInto( fullGroupDir, path.join( dist, "images/groups-full-members" ), fullGroupFiles );
copyInto( fullGroupDir, path.join( dist, "downloads/groups-full-members" ), fullGroupFiles );
copyInto( path.join( fullGroupDir, "originals" ), path.join( dist, "downloads/groups-full-members" ), model.fullGroupPhotos.map( p => p.original ) );
for ( const initiative of model.initiativeGraphics ) {
    for ( const image of initiative.images ) {
        copy( path.join( repo, "graphics/initiatives", initiative.key, image.file ), path.join( dist, "images/initiatives", image.file ) );
        copy( path.join( repo, "graphics/initiatives", initiative.key, image.file ), path.join( dist, "downloads/initiatives", image.file ) );
    }
}
for ( const image of model.campusPhotos ) {
    copy( path.join( repo, "photos", image.file ), path.join( dist, "downloads/campuses", path.basename( image.file ) ) );
    copy( path.join( repo, "photos", image.original ), path.join( dist, "downloads/campuses", path.basename( image.original ) ) );
}

const downloads = { files: {}, zips: {} };
for ( const sub of [ "brand", "church", "groups", "groups-full-members", "events", "campuses", "initiatives" ].filter( s => fs.existsSync( path.join( dist, "downloads", s ) ) ) ) {
    for ( const f of fs.readdirSync( path.join( dist, "downloads", sub ) ) ) {
        downloads.files[`${sub}/${f}`] = { size: fs.statSync( path.join( dist, "downloads", sub, f ) ).size };
    }
}

// Zips, so a whole set is one click.
const kitReadme = [
    "Rock Solid Church brand kit",
    "",
    "Rock Solid Church is the fictional sample church in Rock RMS's sample data.",
    "Use these files in Rock training, documentation, demos, and community resources.",
    "They are covered by the Rock Community License: http://www.rockrms.com/license",
    "",
    "Logo files",
    "  rock-solid-church-logo.svg                 Full logo, charcoal, vector",
    "  rock-solid-church-logo-white.svg           Full logo, white, vector",
    "  rock-solid-church-logo-transparent.png     Full logo, charcoal, 3000 px, transparent",
    "  rock-solid-church-logo-white.png           Full logo, white, 3000 px, transparent",
    "  rock-solid-church-symbol.svg / .png        Symbol only, charcoal",
    "  rock-solid-church-symbol-white.svg / .png  Symbol only, white",
    "  rock-solid-church-logo.png                 The approved original artwork, 1051 x 271",
    "",
    "Colors",
    "  Charcoal     #202020   Logo, type, and trim",
    "  Warm White   #F7F3EC   Walls and backgrounds",
    "  Desert Sand  #E9DECD   Quiet panels",
    "  Pale Oak     #CDB28A   Warm accents",
    "  Muted Teal   #4C8581   Highlights",
    "  Coral        #E07B5F   Student accent, use sparingly",
    "",
    "Type",
    "  ROCK SOLID: Figtree ExtraBold. CHURCH: Montserrat SemiBold, widely spaced.",
    "  Both are free from Google Fonts under the SIL Open Font License.",
    "",
    "Using the logo",
    "  Use charcoal on light backgrounds and white on dark ones.",
    "  Leave clear space at least as tall as the CHURCH letters.",
    "  Do not stretch, recolor, rearrange, or add effects.",
    ""
].join( "\r\n" );

function zip( key, name, entries ) {
    writeZip( path.join( dist, "downloads", name ), entries );
    downloads.zips[key] = { name, size: fs.statSync( path.join( dist, "downloads", name ) ).size };
}

const fileEntries = ( dir, files, folder ) => files.map( f => ( { name: `${folder}/${f}`, data: fs.readFileSync( path.join( dir, f ) ) } ) );
zip( "brand", "rock-solid-church-brand-kit.zip", [
    { name: "rock-solid-church-brand-kit/README.txt", data: Buffer.from( kitReadme, "utf8" ) },
    ...fileEntries( brandDir, brandFiles, "rock-solid-church-brand-kit" )
] );
zip( "building", "rock-solid-church-building-photos.zip", fileEntries( churchDir, model.churchViews.map( v => v.jpeg ), "rock-solid-church-building-photos" ) );
zip( "campuses", "rock-solid-church-campus-photos.zip", fileEntries( path.join( repo, "photos/campuses" ), campusFiles, "rock-solid-church-campus-photos" ) );
zip( "serving", "rock-solid-church-serving-photos.zip", fileEntries( path.join( repo, "photos/opportunities" ), servingFiles, "rock-solid-church-serving-photos" ) );
zip( "groups", "rock-solid-church-small-group-photos.zip", fileEntries( path.join( repo, "photos/groups" ), groupFiles, "rock-solid-church-small-group-photos" ) );
zip( "events", "rock-solid-church-event-graphics.zip", fileEntries( path.join( repo, "photos/events" ), eventFiles, "rock-solid-church-event-graphics" ) );
zip( "groupsFull", "rock-solid-church-small-group-photos-full-members.zip", fileEntries( fullGroupDir, fullGroupFiles, "rock-solid-church-small-group-photos-full-members" ) );
zip( "portraits", "rock-solid-church-cast-portraits.zip", fileEntries( path.join( repo, "photos/cast" ), castFiles, "rock-solid-church-cast-portraits" ) );

// The favicon is the church symbol.
copy( path.join( brandDir, "rock-solid-church-symbol.svg" ), path.join( dist, "assets/favicon.svg" ) );

// Pages.
const builders = [ "home", "why", "church", "cast", "datasets", "how", "brand", "contribute" ].map( name => require( `./src/pages/${name}` ) );
let pageCount = 0;
for ( const build of builders ) {
    const result = build( { model, r: relativeTo( 0 ), relativeTo, downloads } );
    for ( const p of [].concat( result ) ) {
        const out = path.join( dist, p.file );
        fs.mkdirSync( path.dirname( out ), { recursive: true } );
        fs.writeFileSync( out, page( p ) );
        pageCount++;
    }
}

fs.writeFileSync( path.join( dist, "404.html" ), page( {
    title: "Page not found",
    description: "This page isn't here.",
    body: `<section class="page-hero"><div class="wrap narrow"><p class="eyebrow">404</p><h1 class="display">This room is empty.</h1><p class="lede">The page you're looking for isn't here. <a href="index.html">Go back to the lobby.</a></p></div></section>`
} ) );

console.log( `Built ${pageCount} pages into ${path.relative( process.cwd(), dist ) || dist}` );
console.log( `Downloads: ${Object.values( downloads.zips ).map( z => `${z.name} (${( z.size / 1024 / 1024 ).toFixed( 1 )} MB)` ).join( ", " )}` );
