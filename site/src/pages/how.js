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
    Explains how the data is made. Calibration is described only as a method. Never add
    anything here that names or describes a calibration source.
*/

const { esc, html, repoUrl } = require( "../layout" );
const { sectionHead } = require( "../components" );

const ideas = [
    [ "Three datasets, one Cast", "A hand-designed Cast is the same everywhere, and a generated Crowd adds size. Tests name the Cast. The Crowd is never named." ],
    [ "Knowledge lives in data, not code", "Targets, stories, church setup, and rules live in versioned data files. When we learn something, we change a file and rebuild. We never start over." ],
    [ "Store the people, generate the timeline", "Files hold who people are. What they did is generated at install, counting back from install day, so history is always current." ],
    [ "Realism is measured", "We copy the shapes and ratios of real churches, never their size or setup, and grade our data against them on every build." ],
    [ "Fast, without skipping the rules", "The loader copies rows in bulk instead of saving one at a time. It then sets the values Rock's own save logic would have set." ],
    [ "Proven against Rock itself", "Every package is loaded into a clean Rock and checked by Rock's own tools before it can ship." ]
];

const pipeline = [
    [ "Data files", "The Cast, the church setup, and the target patterns, in plain JSON." ],
    [ "Builder", "Turns the files into one package per dataset, for one exact Rock." ],
    [ "Verify", "Loads each package into a clean Rock and checks it. No pass, no publish." ],
    [ "Loader", "Loads a package into a fresh Rock database in seconds." ],
    [ "Rock", "A church with years of history, ready to explore." ]
];

const decisions = [
    [ 1, "Sample data installs into a fresh database only." ],
    [ 6, "Store who people are; generate what they did at install time, counting back from install day. Ages and grades never drift." ],
    [ 7, "Calibration copies shapes and ratios from real churches, never their size or local setup." ],
    [ 12, "Every person, church, and organization is fictional, including employers and businesses." ],
    [ 15, "Every Cast household has a story and a test purpose." ],
    [ 26, "Check gifts come with scanned check images that match the gift exactly: the writer, the date, the amount, and the check number." ],
    [ 28, "New people's photos are AI-generated, fictional faces. No new face may resemble a real or famous person." ],
    [ 41, "A dataset ships as a package of data, never SQL." ],
    [ 52, "Each package is for one exact Rock. The loader refuses a database that doesn't match." ],
    [ 66, "A package is published only with a passing receipt from the verify tool." ],
    [ 72, "A connected request adds the person to the team as Pending, as Rock ships it. The team leader makes them active as part of onboarding." ],
    [ 76, "Every team at every campus has volunteers and serving history on install, before any connection request." ]
];

module.exports = function how( { model } ) {
    return {
        file: "how-its-built.html",
        active: "how",
        title: "How it's built",
        description: "How Rock Sample Data is built: data files, a builder, measured realism, and verification against Rock itself.",
        body: html`
<section class="page-hero">
  <div class="wrap narrow">
    <p class="eyebrow">How it's built</p>
    <h1 class="display">Realistic on purpose. <em>Proven every time.</em></h1>
    <p class="lede">Sample data is easy to make and hard to make well. These are the ideas, the tools, and the decisions behind a church that behaves like a real one.</p>
  </div>
</section>

<section class="section section-tight">
  <div class="wrap">
    ${sectionHead( { eyebrow: "The big ideas", title: "Six ideas carry the whole project." } )}
    <ol class="idea-grid">
      ${ideas.map( ( [ title, text ], i ) => html`<li class="idea reveal"><span class="idea-number">${i + 1}</span><h3>${esc( title )}</h3><p>${esc( text )}</p></li>` )}
    </ol>
  </div>
</section>

<section class="section section-ink">
  <div class="wrap">
    ${sectionHead( { eyebrow: "The pipeline", title: "From plain files to a living church." } )}
    <ol class="pipeline">
      ${pipeline.map( ( [ title, text ] ) => html`<li class="reveal"><h3>${esc( title )}</h3><p>${esc( text )}</p></li>` )}
    </ol>
  </div>
</section>

<section class="section" id="realism">
  <div class="wrap split">
    <div class="split-text reveal">
      <p class="eyebrow">Measured realism</p>
      <h2>Patterns from real churches. Never their people.</h2>
      <p>Real churches have weekly, monthly, and erratic givers, Easter peaks, and summer dips. We measure those shapes and ratios, then grade the generated data with the same measurements.</p>
      <p>Today, 91% of measured values fall within tolerance, including serving, connections, and prayer. Anything we skip on purpose is written down with the reason.</p>
      <p>Only patterns are kept, never a church's size, setup, or people. We never name or describe where a measurement came from, and raw measurements never leave the machine that made them.</p>
    </div>
    <div class="big-number reveal" aria-label="91 percent of measured values fall within tolerance">
      <span class="big-number-value">91<small>%</small></span>
      <span class="big-number-label">of measured values within tolerance</span>
    </div>
  </div>
</section>

<section class="section section-sand" id="trust">
  <div class="wrap">
    ${sectionHead( { eyebrow: "Trust", title: "Rock checks our work.", lede: "The loader is fast because it copies data in bulk around Rock's business logic. So the proof has to come from Rock itself. Before any package is published, the verify tool does four things." } )}
    <div class="card-grid">
      <div class="card reveal"><h3>A clean Rock</h3><p>It builds a database made only by Rock's own migrations and start-up steps, then loads the package into a copy.</p></div>
      <div class="card reveal"><h3>Rock's own columns</h3><p>It checks every table, column, and Guid the loader writes against that exact Rock release.</p></div>
      <div class="card reveal"><h3>Rule checks</h3><p>It runs rules such as "every person is in a family" and "nothing is dated in the future," and confirms every Cast story shows what it should.</p></div>
      <div class="card reveal"><h3>Rock Cleanup</h3><p>It runs Rock's own Rock Cleanup job. If Rock finds anything in the sample data to fix, the package fails.</p></div>
    </div>
  </div>
</section>

<section class="section">
  <div class="wrap">
    ${sectionHead( { eyebrow: "Decisions worth reading", title: "A dozen choices, and why we made them.", lede: html`Every choice is written down in the <a href="${repoUrl}/blob/main/docs/decisions.md">decision log</a>, with its reason. These are a few that shape the church most.` } )}
    <div class="decision-grid">
      ${decisions.map( ( [ n, text ] ) => html`<div class="decision reveal"><span class="decision-number">Decision ${n}</span><p>${esc( text )}</p></div>` )}
    </div>
  </div>
</section>

<section class="section section-sand">
  <div class="wrap split">
    <div class="split-text reveal">
      <p class="eyebrow">The photos</p>
      <h2>Made to look like one church.</h2>
      <p>Every portrait, group photo, and building view is AI-generated and fictional. Portraits look like casual profile pictures, and family members look related.</p>
      <p>Group photos are made from each member's own portrait, so the same faces appear on Sunday and in small group. Twelve empty building views keep every new photo in the same rooms, with the same furniture and the same light.</p>
    </div>
    <div class="split-text reveal">
      <p class="eyebrow">Read the details</p>
      <h2>It's all in the repository.</h2>
      <ul class="link-list">
        <li><a href="${repoUrl}/blob/main/docs/strategy.md">Strategy</a>: the problem, the goal, and the big ideas</li>
        <li><a href="${repoUrl}/blob/main/docs/how-it-works.md">How it works</a>: each step of a load</li>
        <li><a href="${repoUrl}/blob/main/docs/verification.md">Verification</a>: every check and why it's there</li>
        <li><a href="${repoUrl}/blob/main/docs/decisions.md">Decisions</a>: every choice and its reason</li>
        <li><a href="${repoUrl}/blob/main/docs/glossary.md">Glossary</a>: the words we use</li>
      </ul>
    </div>
  </div>
</section>
`
    };
};
