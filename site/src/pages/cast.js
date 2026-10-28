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
    The Cast gallery and one page per household, both built from cast/households. Pages show
    names, ages, stories, and patterns, but never addresses, emails, or phone numbers.
*/

const { esc, html, repoUrl } = require( "../layout" );
const { sectionHead, portrait, householdCard, stageLabels } = require( "../components" );

const attendanceWords = {
    core: "Attends almost every week",
    regular: "Attends most weeks",
    occasional: "Attends now and then",
    "holiday-only": "Comes at Christmas and Easter",
    lapsing: "Attending less and less",
    lapsed: "Used to attend, then stopped"
};

const givingWords = {
    weekly: "weekly",
    "bi-weekly": "every other week",
    monthly: "monthly",
    quarterly: "quarterly",
    irregular: "now and then",
    occasional: "now and then",
    "year-end": "at year end",
    "one-time": "once"
};

const currencyWords = { card: "by card", ach: "from a bank account", check: "by check", cash: "in cash" };

function givingLine( g, h ) {
    const who = g.person ? h.members.find( m => m.key === g.person ) : null;
    const funds = Object.keys( g.funds || {} );
    const parts = [ who ? who.nickName || who.firstName : "The household", "gives", givingWords[g.persona] || g.persona ];
    if ( currencyWords[g.currency] ) {
        parts.push( currencyWords[g.currency] );
    }
    if ( funds.length ) {
        parts.push( "to the " + funds.join( " and " ) );
    }
    if ( g.ended ) {
        parts.push( "(stopped)" );
    }

    return parts.join( " " ) + ".";
}

function ageLine( m ) {
    if ( m.recordType === "business" ) {
        return "Business";
    }
    if ( m.isDeceased ) {
        return "Deceased";
    }
    if ( !m.birth ) {
        return m.familyRole === "child" ? "Child" : "Adult";
    }

    return m.familyRole === "child" ? `Child, age ${m.birth.age}` : `Age ${m.birth.age}`;
}

// The username a Cast person signs in with. Logins load only when the installer gives a password.
function loginLine( m ) {
    if ( m.replacesAdmin ) {
        return html`Signs in as <code>admin</code>, Rock's own administrator login.`;
    }
    if ( m.logins && m.logins.length ) {
        return html`Signs in as ${m.logins.map( l => html`<code>${esc( l )}</code>` ).join( ", " )}.`;
    }

    return "";
}

function memberCard( m, r ) {
    const chips = [ ...( m.groups || [] ).map( g => `${g.name}${g.role && g.role !== "Member" ? ` (${g.role})` : ""}` ), ...( m.serving || [] ).map( s => `${s.name}${s.role && s.role !== "Member" ? ` (${s.role})` : ""}` ) ];
    return html`<article class="member reveal">
  ${portrait( m, r, "xl" )}
  <div class="member-body">
    <h3>${esc( m.displayName )}</h3>
    <p class="member-meta">${esc( ageLine( m ) )}${m.connectionStatus ? html` · ${esc( m.connectionStatus )}` : ""}${m.attributes && m.attributes.Position ? html` · ${esc( m.attributes.Position )}` : ""}</p>
    ${m.staffTitle ? html`<span class="tags"><span class="tag tag-accent">Staff: ${esc( m.staffTitle )}</span></span>` : ""}
    ${m.bio ? html`<p>${esc( m.bio )}</p>` : ""}
    ${!m.photo ? html`<p class="fine">${m.isPhotoOmitted ? "No photo, on purpose." : "Photo coming soon."}</p>` : ""}
    ${loginLine( m ) ? html`<p class="fine">${loginLine( m )}</p>` : ""}
    ${chips.length ? html`<span class="tags">${chips.map( c => html`<span class="tag">${esc( c )}</span>` )}</span>` : ""}
  </div>
</article>`;
}

function householdPage( h, i, all, r ) {
    const prev = all[( i - 1 + all.length ) % all.length];
    const next = all[( i + 1 ) % all.length];
    const att = h.attendance && attendanceWords[h.attendance.persona];
    const duplicateOf = h.duplicateOf ? all.find( x => x.key === h.duplicateOf ) : null;
    const lead = h.members.find( m => m.photo );

    return {
        file: `cast/${h.key}.html`,
        depth: 1,
        active: "cast",
        title: h.name,
        description: `${h.name} in the Rock Solid Church sample data: ${h.testPurposes[0] || h.story}`,
        body: html`
<section class="page-hero page-hero-tight">
  <div class="wrap">
    <p class="crumbs"><a href="${r( "cast.html" )}">The Cast</a> <span aria-hidden="true">/</span> ${esc( h.name )}</p>
    <div class="household-hero">
      <span class="faces faces-hero faces-${Math.min( Math.max( h.members.filter( m => m.photo ).length, 1 ), 4 )}">
        ${h.members.filter( m => m.photo ).slice( 0, 4 ).map( m => html`<img src="${r( "images/cast/" + m.photo )}" alt="" width="200" height="200">` )}
      </span>
      <div>
        <h1 class="display display-md">${esc( h.name )}</h1>
        <span class="tags">
          ${h.isFeatured ? html`<span class="tag tag-accent">Featured</span>` : ""}
          <span class="tag">${h.origin === "new" ? "New household" : "Legacy household"}</span>
          <span class="tag">${h.kind === "business" ? "Business" : `${h.members.length} ${h.members.length === 1 ? "person" : "people"}`}</span>
          <span class="tag">${esc( h.campus.name )} campus</span>
          ${h.campus.rockDefault ? "" : html`<span class="tag">In ${esc( h.campus.datasets.map( d => ( { "core-1": "Core 1", "core-3": "Core 3", "full-7": "Full 7" } )[d] ).join( " and " ) )} only</span>`}
          ${h.stages.filter( s => s !== "business" ).map( s => html`<span class="tag">${esc( stageLabels[s] )}</span>` )}
        </span>
      </div>
    </div>
  </div>
</section>

<section class="section section-tight">
  <div class="wrap household-layout">
    <div>
      <p class="story">${esc( h.story )}</p>
      ${duplicateOf ? html`<p class="callout">This record is a deliberate duplicate of the <a href="${r( "cast/" + duplicateOf.key + ".html" )}">${esc( duplicateOf.name )}</a>, for testing how Rock finds and merges duplicates.</p>` : ""}
      <div class="members">
        ${h.members.map( m => memberCard( m, r ) )}
        ${h.shared.map( m => memberCard( m, r ) )}
      </div>
    </div>
    <aside class="household-aside">
      <div class="panel reveal">
        <h2 class="panel-title">What this household tests</h2>
        <ul class="checks checks-tight">${h.testPurposes.map( t => html`<li>${esc( t )}</li>` )}</ul>
      </div>
      ${att || ( h.giving && h.giving.length ) ? html`<div class="panel reveal">
        <h2 class="panel-title">Their pattern</h2>
        ${att ? html`<p>${esc( att )}${h.attendance.percentAttended ? html`, about ${h.attendance.percentAttended}% of weekends` : ""}.</p>` : ""}
        ${( h.giving || [] ).map( g => html`<p>${esc( givingLine( g, h ) )}</p>` )}
        <p class="fine">History is generated at install, counting back from install day.</p>
      </div>` : ""}
      ${h.noteCount ? html`<div class="panel reveal">
        <h2 class="panel-title">Staff notes</h2>
        <p>${h.noteCount} hand-written ${h.noteCount === 1 ? "note" : "notes"} on their profiles${h.replyCount ? `, with ${h.replyCount} ${h.replyCount === 1 ? "reply" : "replies"}` : ""}, each tied to their story.</p>
        <p class="fine">Every load checks that each note shows on its person's profile.</p>
      </div>` : ""}
      <div class="panel panel-quiet reveal">
        <p class="fine">Every name, photo, and story here is fictional. <a href="${repoUrl}/blob/main/cast/households/${esc( h.key )}.json">See this household's data file.</a></p>
      </div>
    </aside>
  </div>
</section>

<nav class="wrap pager" aria-label="More households">
  <a href="${r( "cast/" + prev.key + ".html" )}"><span class="pager-label">Previous</span>${esc( prev.name )}</a>
  <a href="${r( "cast/" + next.key + ".html" )}"><span class="pager-label">Next</span>${esc( next.name )}</a>
</nav>
`
    };
}

module.exports = function cast( { model, r, relativeTo } ) {
    const featured = model.households.filter( h => h.isFeatured );
    const others = model.households.filter( h => !h.isFeatured );
    const ordered = [ ...featured, ...others ];
    const filters = [ [ "all", "All" ], [ "featured", "Featured" ], ...Object.entries( stageLabels ) ];

    const index = {
        file: "cast.html",
        active: "cast",
        title: "The Cast",
        description: `Meet the Cast: ${model.counts.households} hand-designed households and ${model.counts.people} people who are the same in every Rock sample dataset.`,
        body: html`
<section class="page-hero">
  <div class="wrap narrow">
    <p class="eyebrow">The Cast</p>
    <h1 class="display">${model.counts.households} households. <em>Every one has a reason.</em></h1>
    <p class="lede">The Cast is ${model.counts.people} hand-designed people who are the same in every dataset. Most live on the Phoenix campus. Each other campus's pastor lives on their own campus, so their family is in the datasets that have it. Tests, help articles, and training classes can name them and trust they're there. Each household has a story and a list of what it's used to test. Each household page shows who can sign in, and with which username, and how many staff notes are on their profiles.</p>
  </div>
</section>

<section class="section section-tight">
  <div class="wrap">
    <div class="filter-bar" role="search">
      <label class="search"><span class="visually-hidden">Search the Cast</span><input type="search" placeholder="Search by name or what it tests" data-cast-search></label>
      <div class="chips" role="group" aria-label="Filter households">
        ${filters.map( ( [ key, label ], i ) => html`<button type="button" class="chip" data-filter="${key}" aria-pressed="${i === 0}">${esc( label )}</button>` )}
      </div>
    </div>
    <p class="result-count" data-cast-count aria-live="polite">${model.counts.households} households</p>
    <div class="household-grid" data-cast-grid>
      ${ordered.map( h => householdCard( h, r ) )}
    </div>
    <p class="empty" data-cast-empty hidden>No households match. Try another word.</p>
  </div>
</section>

<section class="section section-sand">
  <div class="wrap split">
    <div class="split-text reveal">
      <p class="eyebrow">The Crowd</p>
      <h2>And about 25,000 more.</h2>
      <p>Core 3 and Full 7 add generated households around the Cast. These people give the church its size and its statistics: who lives near which campus, how often people attend, and how giving changes over a year.</p>
      <p>Crowd people are never named in tests or documentation. They're there so reports, charts, and searches behave the way they do at a large church.</p>
    </div>
    <div class="split-text reveal">
      <p class="eyebrow">About the photos</p>
      <h2>Fictional faces, real-life feel.</h2>
      <p>Every portrait is AI-generated to look like a casual profile picture a friend took, never a studio shot. Family members look related, and the long-time families Rock users know kept recognizable faces when their photos were refreshed.</p>
      <p>A few people have no photo on purpose, such as duplicate records made from forms.</p>
    </div>
  </div>
</section>
`
    };

    const pages = ordered.map( ( h, i ) => householdPage( h, i, ordered, relativeTo( 1 ) ) );
    return [ index, ...pages ];
};
