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

const { esc, html, repoUrl } = require( "../layout" );
const { sectionHead } = require( "../components" );

const inside = [
    [ "People and households", "The Cast in every dataset, plus generated households in Core 3 and Full 7. Every Cast person has a photo, except a few left without one on purpose." ],
    [ "Attendance and check-in", "Years of Sundays, with kids checked in by family security code and students checking in midweek." ],
    [ "Giving", "Gifts on each giver's rhythm, through scheduled, online, and Sunday channels, in batches, with scanned checks that match each gift." ],
    [ "Giving analytics", "Giving attributes, journey stages, and a year of giving alerts, filled in the way Rock computes them." ],
    [ "Small groups", "Groups with leaders, hosts, topics, history, photos, and Group Finder test cases at every campus." ],
    [ "Serving and scheduling", "Eight teams per campus, with rotations, preferences, blackout dates, six weeks of future schedules, and volunteer check-in." ],
    [ "Connections", "Requests to serve that follow each household's journey, with activities and placements." ],
    [ "Prayer", "Requests in every state, the prayer team's prayers and comments, and the Cast's own requests tied to their stories." ],
    [ "Achievements", "The Kids Check-in streak and its seven achievements, so check-in shows real progress." ],
    [ "Businesses", "Businesses at every campus that give, each with one or two contacts." ]
];

module.exports = function datasets( { model } ) {
    return {
        file: "datasets.html",
        active: "datasets",
        title: "Datasets",
        description: "Core 1, Core 3, and Full 7: three Rock sample datasets built from one Cast, from fast test installs to a 25,000-person demo church.",
        body: html`
<section class="page-hero">
  <div class="wrap narrow">
    <p class="eyebrow">Datasets</p>
    <h1 class="display">Three sizes. <em>One church.</em></h1>
    <p class="lede">Pick the size that fits the job. Every dataset has the same Cast at the same broadcast campus, so what you learn on one carries to the others.</p>
  </div>
</section>

<section class="section section-tight">
  <div class="wrap dataset-grid">
    <article class="dataset reveal">
      <p class="dataset-size">Core 1</p>
      <p class="dataset-time">Loads in about 4 seconds</p>
      <ul>
        <li>The Cast: ${model.counts.households} households, ${model.counts.people} people</li>
        <li>1 campus: Phoenix</li>
        <li>Every feature, with full history</li>
      </ul>
      <p class="dataset-for">For tests and most development.</p>
    </article>
    <article class="dataset dataset-mid reveal">
      <p class="dataset-size">Core 3</p>
      <p class="dataset-time">Loads in under 10 seconds</p>
      <ul>
        <li>The Cast, plus 75 generated households at each of Peoria and Gilbert</li>
        <li>3 campuses</li>
        <li>Campus test groups at every new campus</li>
      </ul>
      <p class="dataset-for">For campus features and filters.</p>
    </article>
    <article class="dataset dataset-big reveal">
      <p class="dataset-size">Full 7</p>
      <p class="dataset-time">Loads in 20 to 40 seconds</p>
      <ul>
        <li>The Cast, plus the Crowd: about 25,000 people</li>
        <li>7 campuses across the Valley</li>
        <li>Volume for reports, charts, and search</li>
      </ul>
      <p class="dataset-for">For demos, performance, and reporting.</p>
    </article>
  </div>
</section>

<section class="section">
  <div class="wrap">
    ${sectionHead( { eyebrow: "What's inside", title: "Every dataset fills every feature." } )}
    <div class="feature-grid feature-grid-light">
      ${inside.map( ( [ title, text ] ) => html`<div class="feature reveal"><h3>${esc( title )}</h3><p>${esc( text )}</p></div>` )}
    </div>
  </div>
</section>

<section class="section section-sand">
  <div class="wrap split">
    <div class="split-text reveal">
      <p class="eyebrow">Always fresh</p>
      <h2>History that always ends today.</h2>
      <p>Old sample data used fixed dates, so "last 12 months" reports slowly emptied out. This data stores who people are and generates what they did when you install it, counting back from install day.</p>
      <p>Ages and grades never drift. A first-time guest always visited two weeks ago. Christmas and Easter always land where they should.</p>
    </div>
    <div class="split-text reveal">
      <p class="eyebrow">One exact Rock</p>
      <h2>Made for your version.</h2>
      <p>Each package is built for one exact Rock release. It records that Rock's version and newest migration, and the loader refuses a database that doesn't match.</p>
      <p>Before a package is published, it's loaded into a clean copy of that Rock and checked. See <a href="how-its-built.html#trust">how we prove it</a>.</p>
    </div>
  </div>
</section>

<section class="section">
  <div class="wrap narrow">
    ${sectionHead( { eyebrow: "Install it", title: "From the repository, in three steps." } )}
    <ol class="steps reveal">
      <li><h3>Check the data</h3><pre><code>node cast/validate-cast.js
node validation/check-standards.js</code></pre></li>
      <li><h3>Build the packages for your Rock</h3><pre><code>cd builder
dotnet run -- --rock C:\\Development\\Rock</code></pre></li>
      <li><h3>Load one into a fresh Rock database</h3><pre><code>cd ../loader-cli
dotnet run -- --package ../packages/core-1.zip --connection "Server=localhost;Database=RockSample;Integrated Security=true;TrustServerCertificate=true"</code></pre></li>
    </ol>
    <p class="fine">You need the .NET 10 SDK, Node.js 20 or later, and SQL Server with a freshly installed Rock database. Sample data installs into a fresh database only. See the <a href="${repoUrl}#quick-start">quick start</a> for every option.</p>
  </div>
</section>
`
    };
};
