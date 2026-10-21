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
    <p class="lede">The most helpful thing you can tell us is a use case we're missing: something churches do in Rock every week that the sample data can't show yet. Tell us, and we'll build it into the church everyone shares.</p>
    <div class="actions"><a class="button button-gradient" href="${issueUrl}">Share a use case</a><a class="button button-ghost" href="${repoUrl}">Browse the repository</a></div>
  </div>
</section>

<section class="section section-tight">
  <div class="wrap">
    ${sectionHead( { eyebrow: "What we're looking for", title: "Big, common use cases first.", lede: "The sample data is meant to show each Rock feature the way most churches use it. Especially now, while the church is young, we're looking for the large patterns that touch many screens, not every small detail." } )}
    <div class="rules rules-light">
      <div class="rule reveal"><h3>A great fit</h3><ul class="checks">
        <li><strong>A whole feature that looks empty.</strong> A Rock feature whose lists, charts, and reports would all be blank on a sample install.</li>
        <li><strong>A common kind of family.</strong> "Grandparents raising grandkids are common, and Rock should show how check-in and giving work for them."</li>
        <li><strong>A pattern most churches see.</strong> "Most churches see giving dip in summer and jump at year end."</li>
        <li><strong>Something a class or help article needs.</strong> "Our training on follow-up needs a few people who visited twice and never came back."</li>
      </ul></div>
      <div class="rule rule-dont reveal"><h3>Better in your own test database</h3><ul class="crosses">
        <li><strong>Rare combinations of settings</strong> that only a few churches use.</li>
        <li><strong>One-off situations</strong> you need for a single test or bug.</li>
        <li><strong>Small tweaks</strong> to one person's details, such as a different birthday or phone number.</li>
        <li><strong>Exact numbers from your own church.</strong> Every church is different, and the data aims for what's typical.</li>
      </ul></div>
    </div>
    <p class="fine reveal">Not sure where your idea fits? Open an issue anyway and say how common you think it is. We'd rather hear it.</p>
  </div>
</section>

<section class="section section-sand">
  <div class="wrap split">
    <div class="split-text reveal">
      <p class="eyebrow">How to suggest a change</p>
      <h2>Open an issue.</h2>
      <p>For now, the way to contribute is to open an issue on GitHub. The team makes the changes, so we don't take pull requests yet.</p>
      <p>Answer these four questions, so we can see who needs the data and how common it is.</p>
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
  </div>
</section>
`
    };
};
