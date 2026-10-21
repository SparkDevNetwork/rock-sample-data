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
    The issue questions and the roadmap are read from docs/contributing.md and docs/roadmap.md,
    so this page changes when they do.
*/

const fs = require( "fs" );
const path = require( "path" );
const { esc, html, repoUrl } = require( "../layout" );
const { sectionHead } = require( "../components" );

/**
 * Turns a line of Markdown into plain text: drops links, code ticks, and emphasis.
 */
function plain( text ) {
    return text
        .replace( /\[([^\]]+)\]\([^)]+\)/g, "$1" )
        .replace( /`([^`]+)`/g, "$1" )
        .replace( /\*\*([^*]+)\*\*/g, "$1" )
        .replace( /\s*\(see [^)]*\)/gi, "" )
        .trim();
}

/**
 * The lines of one "## Heading" section of a Markdown file.
 */
function section( markdown, heading ) {
    const lines = markdown.split( /\r?\n/ );
    const start = lines.findIndex( l => l.trim() === `## ${heading}` );
    if ( start < 0 ) {
        return [];
    }
    const end = lines.findIndex( ( l, i ) => i > start && l.startsWith( "## " ) );
    return lines.slice( start + 1, end < 0 ? undefined : end );
}

/**
 * Numbered items of the form "1. **Title** text" in a section.
 */
function numbered( lines ) {
    return lines
        .map( l => l.match( /^\d+\.\s+\*\*(.+?)\*\*\s*(.*)$/ ) )
        .filter( Boolean )
        .map( m => ( { title: plain( m[1] ), text: plain( m[2] ) } ) );
}

module.exports = function contribute( { model } ) {
    const contributing = fs.readFileSync( path.join( model.root, "docs/contributing.md" ), "utf8" );
    const roadmap = fs.readFileSync( path.join( model.root, "docs/roadmap.md" ), "utf8" );
    const questions = numbered( section( contributing, "Open an issue" ) );
    const next = numbered( section( roadmap, "Next" ) );
    const done = section( roadmap, "Done" ).filter( l => l.startsWith( "- " ) ).map( l => plain( l.slice( 2 ) ) );
    const issueUrl = `${repoUrl}/issues/new`;

    return {
        file: "contribute.html",
        active: "contribute",
        title: "Contribute",
        description: "Help grow the Rock Solid Church sample data: suggest families, stories, and features for the Rock community's shared sample church.",
        body: html`
<section class="page-hero">
  <div class="wrap narrow">
    <p class="eyebrow">Contribute</p>
    <h1 class="display">Help us build <em>the church.</em></h1>
    <p class="lede">Good sample data comes from people who know what churches look like and what Rock needs to show. Tell us what's missing, and we'll build it into the church everyone shares.</p>
    <div class="actions"><a class="button button-gradient" href="${issueUrl}">Open an issue</a><a class="button button-ghost" href="${repoUrl}">Browse the repository</a></div>
  </div>
</section>

<section class="section section-tight">
  <div class="wrap">
    ${sectionHead( { eyebrow: "Ways to help", title: "Three good places to start." } )}
    <div class="card-grid">
      <div class="card reveal"><h3>Suggest a family</h3><p>Is there a kind of household Rock should show, such as a grandparent raising grandkids or a family new to the country? Describe who they are and what they'd test.</p></div>
      <div class="card reveal"><h3>Find an empty screen</h3><p>Open a Rock feature on a sample install. If its lists, charts, or reports look empty or wrong, tell us which screen and what it should show.</p></div>
      <div class="card reveal"><h3>Share a use case</h3><p>Writing a help article, a class, or a test? Tell us what you need the data to show, and whether you need to name a specific person.</p></div>
    </div>
  </div>
</section>

<section class="section section-sand">
  <div class="wrap split">
    <div class="split-text reveal">
      <p class="eyebrow">How to suggest a change</p>
      <h2>Open an issue.</h2>
      <p>For now, the way to contribute is to open an issue on GitHub. The team makes the changes, so we don't take pull requests yet.</p>
      <p>We aim for the broad reach of Rock's features: each one working the way most churches use it. A rare edge case is better set up by hand in your own test database.</p>
      <p><a class="button button-dark" href="${issueUrl}">Open an issue</a></p>
    </div>
    <ol class="steps steps-plain reveal">
      ${questions.map( q => html`<li><p><strong>${esc( q.title )}</strong> ${esc( q.text )}</p></li>` )}
    </ol>
  </div>
</section>

<section class="section">
  <div class="wrap">
    ${sectionHead( { eyebrow: "The ground rules", title: "What every addition follows." } )}
    <div class="card-grid">
      <div class="card reveal"><h3>Everything is fictional</h3><p>Every person, church, employer, and business is made up. No face may look like a real or famous person.</p></div>
      <div class="card reveal"><h3>Reserved contact details</h3><p>Phone numbers use the reserved 555-0100 to 555-0199 range, and emails use example.org, or rocksolidchurchdemo.com for church staff, so no real person is ever contacted.</p></div>
      <div class="card reveal"><h3>Every household has a reason</h3><p>Each Cast household has a story and a list of what it's used to test. If we can't say why it's there, it isn't.</p></div>
    </div>
  </div>
</section>

<section class="section section-ink" id="roadmap">
  <div class="wrap">
    ${sectionHead( { eyebrow: "Roadmap", title: "What's next.", lede: `${done.length} milestones are done, from the Cast and its photos to serving and prayer. Here's what comes next.` } )}
    <ol class="roadmap">
      ${next.map( ( item, i ) => html`<li class="reveal"><span class="roadmap-number">${i + 1}</span><p><strong>${esc( item.title )}</strong> ${esc( item.text )}</p></li>` )}
    </ol>
    <p class="fine"><a href="${repoUrl}/blob/main/docs/roadmap.md">Read the full roadmap</a> and the <a href="${repoUrl}/blob/main/docs/planned.md">planned work</a>.</p>
  </div>
</section>
`
    };
};
