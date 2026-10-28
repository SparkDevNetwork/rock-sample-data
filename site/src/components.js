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

const { esc, html } = require( "./layout" );

function sectionHead( { eyebrow, title, lede, center } ) {
    return html`<div class="section-head${center ? " center" : ""} reveal">
  ${eyebrow ? html`<p class="eyebrow">${esc( eyebrow )}</p>` : ""}
  <h2>${title}</h2>
  ${lede ? html`<p class="lede">${lede}</p>` : ""}
</div>`;
}

function initials( member ) {
    return ( ( member.nickName || member.firstName || "" )[0] || "" ) + ( ( member.lastName || "" )[0] || "" );
}

/**
 * A round portrait, or a monogram for the few Cast people left without a photo on purpose.
 */
function portrait( member, r, size = "md" ) {
    if ( member.photo ) {
        return html`<img class="portrait portrait-${size}" src="${r( "images/cast/" + member.photo )}" alt="${esc( member.displayName )}" loading="lazy" width="200" height="200">`;
    }

    return html`<span class="portrait portrait-${size} monogram" role="img" aria-label="${esc( member.displayName )}, no photo on purpose">${esc( initials( member ) )}</span>`;
}

const stageLabels = {
    "young-kids": "Young kids",
    kids: "School-age kids",
    teens: "Teens",
    single: "Single adults",
    seniors: "Seniors",
    business: "Businesses"
};

function householdCard( h, r ) {
    const faces = h.members.filter( m => m.photo ).slice( 0, 4 );
    const names = h.members.map( m => m.nickName || m.firstName ).join( ", " );
    const filters = [ ...h.stages, h.isFeatured ? "featured" : "" ].filter( Boolean ).join( " " );
    const search = [ h.name, ...h.members.map( m => m.displayName ), ...h.testPurposes ].join( " " ).toLowerCase();
    return html`<a class="household-card reveal" href="${r( "cast/" + h.key + ".html" )}" data-filters="${esc( filters )}" data-search="${esc( search )}">
  <span class="faces faces-${Math.max( faces.length, 1 )}">
    ${faces.length ? faces.map( m => html`<img src="${r( "images/cast/" + m.photo )}" alt="" loading="lazy" width="200" height="200">` ) : html`<span class="monogram">${esc( initials( h.members[0] ) )}</span>`}
  </span>
  <span class="household-body">
    <span class="household-name">${esc( h.name )}</span>
    <span class="household-members">${esc( names )}</span>
    <span class="household-purpose">${esc( h.testPurposes[0] || "" )}</span>
    <span class="tags">
      ${h.isFeatured ? html`<span class="tag tag-accent">Featured</span>` : ""}
      ${h.kind === "business" ? html`<span class="tag">Business</span>` : html`<span class="tag">${h.members.length} ${h.members.length === 1 ? "person" : "people"}</span>`}
    </span>
  </span>
</a>`;
}

function download( { title, detail, href, format } ) {
    return html`<a class="download" href="${esc( href )}" download>
  <span class="download-format">${esc( format )}</span>
  <span class="download-text"><span class="download-title">${esc( title )}</span><span class="download-detail">${esc( detail )}</span></span>
  <svg class="download-icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M12 3v12m0 0-5-5m5 5 5-5M5 19h14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/></svg>
</a>`;
}

/**
 * A photo that opens larger in the page's viewer when clicked.
 */
function zoomable( { src, alt, caption, className = "", width = 1536, height = 1024 } ) {
    return html`<figure class="zoom ${className}">
  <button class="zoom-button" type="button" data-zoom="${esc( src )}" data-caption="${esc( caption || alt )}" aria-label="View larger: ${esc( alt )}">
    <img src="${esc( src )}" alt="${esc( alt )}" loading="lazy" width="${width}" height="${height}">
  </button>
  ${caption ? html`<figcaption>${caption}</figcaption>` : ""}
</figure>`;
}

function formatSize( bytes ) {
    if ( bytes >= 1024 * 1024 ) {
        return `${( bytes / 1024 / 1024 ).toFixed( 1 )} MB`;
    }

    return `${Math.max( 1, Math.round( bytes / 1024 ) )} KB`;
}

// A dataset's typical load time in words, from typicalInstallSeconds in datasets/*.json, so the site never drifts from it.
function loadTime( dataset ) {
    const seconds = dataset.typicalInstallSeconds;
    if ( seconds < 90 ) {
        return `about ${seconds} seconds`;
    }

    return `about ${Math.round( seconds / 60 )} minutes`;
}

module.exports = { sectionHead, portrait, householdCard, download, zoomable, stageLabels, formatSize, initials, loadTime };
