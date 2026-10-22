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

const { esc, html } = require( "../layout" );
const { sectionHead, download, formatSize } = require( "../components" );

const palette = [
    [ "Charcoal", "#202020", "The logo, type, and trim." ],
    [ "Warm White", "#F7F3EC", "Stucco walls and page backgrounds." ],
    [ "Desert Sand", "#E9DECD", "Quiet panels and section backgrounds." ],
    [ "Pale Oak", "#CDB28A", "Slat walls, tables, and warm accents." ],
    [ "Muted Teal", "#4C8581", "Seating, the Starting Point ribbons, and highlights." ],
    [ "Coral", "#E07B5F", "The student room accent and the Starting Point center point. Use sparingly." ]
];

const graphics = [
    [ "Lobby mission wall", "Charcoal type on warm white.", [ "OUR MISSION", "Love God.", "Love people.", "Share Jesus." ] ],
    [ "Lobby Starting Point wall", "Charcoal type on warm ivory, teal ribbons around a coral point, and a teal invitation strip.", [ "YOU BELONG HERE.", "STARTING POINT", "From a first hello to a place you belong.", "Enroll in Starting Point" ] ],
    [ "Prayer room wall", "Charcoal type, with \"strength\" in muted-teal script.", [ "Find strength in prayer.", "“I can do all things through Christ who strengthens me.” Philippians 4:13" ] ],
    [ "Student room wall", "Warm-white type on teal, with a coral underline.", [ "Rock Solid", "Students" ] ]
];

module.exports = function brand( { model, r, downloads } ) {
    const d = name => downloads.files[name];
    const logoFile = ( file, title, detail ) => download( { title, detail: `${detail} · ${formatSize( d( "brand/" + file ).size )}`, href: r( "downloads/brand/" + file ), format: file.split( "." ).pop().toUpperCase() } );

    return {
        file: "brand.html",
        active: "brand",
        title: "Brand and downloads",
        description: "Download the Rock Solid Church logo as SVG or PNG, the color palette, and the building, serving, small group, and Cast photos.",
        body: html`
<section class="page-hero">
  <div class="wrap brand-hero">
    <div>
      <p class="eyebrow">Brand and downloads</p>
      <h1 class="display">Everything you need <em>to show the church.</em></h1>
      <p class="lede">The logo, the colors, and the photos are free to use in Rock training, documentation, demos, and community resources. Grab one file, or everything at once.</p>
      <div class="actions">
        <a class="button button-gradient" href="${r( "downloads/" + downloads.zips.brand.name )}" download>Download the brand kit <span class="button-note">ZIP · ${formatSize( downloads.zips.brand.size )}</span></a>
        <a class="button button-ghost" href="#photos">Get the photos</a>
      </div>
    </div>
    <div class="logo-stage reveal">
      <img src="${r( "downloads/brand/rock-solid-church-logo.svg" )}" alt="Rock Solid Church logo" width="560" height="140">
    </div>
  </div>
</section>

<section class="section section-tight" id="logo">
  <div class="wrap">
    ${sectionHead( { eyebrow: "The logo", title: "The Open Cross.", lede: "Four rounded forms frame a cross in the space between them. The crossbar sits above center, like a traditional cross. ROCK SOLID is heavy and confident, and CHURCH is light and widely spaced." } )}
    <div class="logo-grid">
      <div class="logo-tile tile-paper reveal">
        <img src="${r( "downloads/brand/rock-solid-church-logo.svg" )}" alt="Charcoal logo on warm white" loading="lazy">
        <div class="logo-files">
          ${logoFile( "rock-solid-church-logo.svg", "Vector", "Any size" )}
          ${logoFile( "rock-solid-church-logo-transparent.png", "PNG image", "3000 px, transparent" )}
        </div>
      </div>
      <div class="logo-tile tile-ink reveal">
        <img src="${r( "downloads/brand/rock-solid-church-logo-white.svg" )}" alt="White logo on charcoal" loading="lazy">
        <div class="logo-files">
          ${logoFile( "rock-solid-church-logo-white.svg", "Vector", "Any size" )}
          ${logoFile( "rock-solid-church-logo-white.png", "PNG image", "3000 px, transparent" )}
        </div>
      </div>
      <div class="logo-tile tile-teal reveal">
        <img class="symbol" src="${r( "downloads/brand/rock-solid-church-symbol-white.svg" )}" alt="White symbol on teal" loading="lazy">
        <div class="logo-files">
          ${logoFile( "rock-solid-church-symbol-white.svg", "Vector", "Any size" )}
          ${logoFile( "rock-solid-church-symbol-white.png", "PNG image", "1024 px, transparent" )}
        </div>
      </div>
      <div class="logo-tile tile-sand reveal">
        <img class="symbol" src="${r( "downloads/brand/rock-solid-church-symbol.svg" )}" alt="Charcoal symbol on sand" loading="lazy">
        <div class="logo-files">
          ${logoFile( "rock-solid-church-symbol.svg", "Vector", "Any size" )}
          ${logoFile( "rock-solid-church-symbol.png", "PNG image", "1024 px, transparent" )}
        </div>
      </div>
    </div>
    <p class="fine reveal">The vector files were redrawn from the approved logo artwork and checked against it. The original approved PNG is also available: ${download( { title: "Approved original", detail: `1051 × 271, white background · ${formatSize( d( "brand/rock-solid-church-logo.png" ).size )}`, href: r( "downloads/brand/rock-solid-church-logo.png" ), format: "PNG" } )}</p>
  </div>
</section>

<section class="section section-sand" id="colors">
  <div class="wrap">
    ${sectionHead( { eyebrow: "Color", title: "Colors from the building.", lede: "The palette comes from the church itself: stucco, charcoal trim, pale oak, and teal furniture. Select a color to copy its value." } )}
    <div class="palette">
      ${palette.map( ( [ name, hex, use ] ) => html`<button type="button" class="swatch reveal" data-copy="${hex}" style="--swatch:${hex}">
        <span class="swatch-color"></span>
        <span class="swatch-name">${esc( name )}</span>
        <span class="swatch-hex">${hex}</span>
        <span class="swatch-use">${esc( use )}</span>
      </button>` )}
    </div>
  </div>
</section>

<section class="section">
  <div class="wrap split">
    <div class="split-text reveal">
      <p class="eyebrow">Type</p>
      <h2>Two free typefaces.</h2>
      <p>The logo pairs Figtree ExtraBold for ROCK SOLID with Montserrat SemiBold, widely spaced, for CHURCH. Both are free from Google Fonts under the SIL Open Font License, so you can use them in slides and handouts too.</p>
      <p>This site uses Figtree for text and Fraunces for large headlines.</p>
    </div>
    <div class="type-specimens reveal">
      <p class="specimen specimen-figtree">ROCK SOLID</p>
      <p class="specimen-label">Figtree ExtraBold</p>
      <p class="specimen specimen-montserrat">C H U R C H</p>
      <p class="specimen-label">Montserrat SemiBold</p>
    </div>
  </div>
</section>

<section class="section section-ink">
  <div class="wrap">
    ${sectionHead( { eyebrow: "Using the logo", title: "A few simple rules." } )}
    <div class="rules">
      <div class="rule reveal"><h3>Do</h3><ul class="checks">
        <li>Use the charcoal logo on light backgrounds and the white logo on dark ones.</li>
        <li>Leave clear space around it at least as tall as the CHURCH letters.</li>
        <li>Keep the full logo at least 120 pixels wide on screens. Use the symbol alone when space is tight.</li>
      </ul></div>
      <div class="rule rule-dont reveal"><h3>Don't</h3><ul class="crosses">
        <li>Stretch, squash, rotate, or rearrange the symbol and words.</li>
        <li>Recolor it, or add shadows, outlines, or effects.</li>
        <li>Place it on a busy part of a photo where it's hard to read.</li>
      </ul></div>
    </div>
  </div>
</section>

<section class="section">
  <div class="wrap">
    ${sectionHead( { eyebrow: "In the building", title: "The words on the walls.", lede: "These graphics appear in the building photos. Use this exact wording if you recreate them." } )}
    <div class="graphic-grid">
      ${graphics.map( ( [ title, note, lines ] ) => html`<div class="graphic reveal"><h3>${esc( title )}</h3>${lines.map( l => html`<p class="graphic-line">${esc( l )}</p>` )}<p class="fine">${esc( note )}</p></div>` )}
    </div>
  </div>
</section>

<section class="section section-sand" id="photos">
  <div class="wrap">
    ${sectionHead( { eyebrow: "Photos", title: "Download the pictures.", lede: "Every photo is AI-generated and fictional. Download a whole set as a zip, or pick single building views and small group photos below." } )}
    <div class="pack-grid">
      ${[
            [ "building", "Building views", "12 empty rooms and exterior views, JPEG" ],
            [ "serving", "Serving teams", "8 photos of Cast volunteers at work, JPEG" ],
            [ "groups", "Small groups", "12 Cast small group photos, JPEG" ],
            [ "portraits", "Cast portraits", `${model.counts.photos} profile photos, JPEG` ]
        ].map( ( [ key, title, detail ] ) => html`<a class="pack reveal" href="${r( "downloads/" + downloads.zips[key].name )}" download>
          <span class="pack-title">${esc( title )}</span>
          <span class="pack-detail">${esc( detail )}</span>
          <span class="pack-size">ZIP · ${formatSize( downloads.zips[key].size )}</span>
        </a>` )}
    </div>
    <h3 class="view-heading">Building views</h3>
    <div class="view-grid">
      ${model.churchViews.map( v => html`<figure class="view reveal">
        <img src="${r( "images/church/" + v.jpeg )}" alt="${esc( v.title )}" loading="lazy" width="1536" height="1024">
        <figcaption>
          <span>${esc( v.title )}</span>
          <span class="view-links">
            <a href="${r( "downloads/church/" + v.jpeg )}" download>JPEG ${formatSize( d( "church/" + v.jpeg ).size )}</a>
            <a href="${r( "downloads/church/" + v.file )}" download>PNG ${formatSize( d( "church/" + v.file ).size )}</a>
          </span>
        </figcaption>
      </figure>` )}
    </div>
    <h3 class="view-heading">Small groups</h3>
    <div class="view-grid">
      ${model.smallGroups.filter( g => g.photo ).map( g => {
            const png = g.photo.replace( /\.jpg$/, ".png" );
            return html`<figure class="view reveal">
        <img src="${r( "images/groups/" + g.photo )}" alt="${esc( `${g.name}: ${g.photoPeople.join( ", " )}` )}" loading="lazy" width="1536" height="1024">
        <figcaption>
          <span>${esc( g.name )}${g.topic ? html` <span class="view-topic">${esc( g.topic )}</span>` : ""}</span>
          <span class="view-links">
            <a href="${r( "downloads/groups/" + g.photo )}" download>JPEG ${formatSize( d( "groups/" + g.photo ).size )}</a>
            <a href="${r( "downloads/groups/" + png )}" download>PNG ${formatSize( d( "groups/" + png ).size )}</a>
          </span>
        </figcaption>
      </figure>`;
        } )}
    </div>
    <p class="fine">All images are 1536 by 1024 pixels. The PNG files are the full-quality masters.</p>
  </div>
</section>
`
    };
};
