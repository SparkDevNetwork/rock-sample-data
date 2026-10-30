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
const { sectionHead, loadTime, databaseSpace } = require( "../components" );

const inside = model => [
    [ "People and households", "The Cast in every dataset, plus generated households in Core 3 and Full 7. Every Cast person has a photo, except a few left without one on purpose." ],
    [ "Logins", "Most people have no login, as in a real church. In Full 7, about 30% of adults can sign in: staff, leaders, volunteers, online givers, and app users. Each login looks made and used." ],
    [ "Attendance and check-in", "Years of Sundays, with kids checked in by family security code and students checking in to the Sunday student service, sometimes on their own." ],
    [ "Giving", "Gifts on each giver's rhythm, through scheduled, online, and Sunday channels, in batches, with scanned checks that match each gift." ],
    [ "Giving analytics", "Giving attributes, journey stages, and a year of giving alerts, filled in the way Rock computes them." ],
    [ "Small groups", "Groups with leaders, hosts, topics, history, photos, and Group Finder test cases at every campus." ],
    [ "Serving and scheduling", "Eight teams per campus, with rotations, preferences, blackout dates, six weeks of future schedules, and volunteer check-in." ],
    [ "Connections", "Requests to serve that follow each household's journey, with activities and placements. Due dates run by status, so each board has requests due later, due this week, and past due." ],
    [ "Prayer", "Requests in every state, the prayer team's prayers and comments, and the Cast's own requests tied to their stories." ],
    [ "Notes", "Six note types with View security, the Cast's hand-written notes, and generated notes with replies, alerts, and mentions. The profiles demos open first, such as the Deckers and the Fosters, have notes from other staff." ],
    [ "Steps", "Rock's Discipleship Path built out to seven steps and a four-step Student Path, each step made from a record the data already has, with classes in progress at every campus, camps and mission trips from student check-ins, and program completions." ],
    [ "Calendar", `${model.calendarEvents.length} events on the Public and Internal calendars: holidays, classes, student and kids events, men's and women's events, outreach, and staff meetings, at every campus the dataset has. Each event serves a Ministry by Design stage: Belong, Equip, Contribute, or Lead.` ],
    [ "Event registrations", `${model.registrations.templates.length} kinds of registration, from camp and the mission trip to VBS and classes, linked to their calendar events with fees, discounts, payment plans, wait lists, and placement groups. Some are always open, so every feature can be tried on any install day.` ],
    [ "Communications", "Texting numbers, campus newsletters and ministry lists, a branded email template, two years of email with opens, clicks, bounces, and unsubscribes, texts with live SMS conversations, one-to-one emails for serving, connections, prayer, and first visits, the church's app with its devices and pushes, and Communication Flows after Starting Point and after Foundations, with the most recent class's run still sending on install day, a Baptism flow, started from a Baptism Interest form, that stops when the person is baptized, and recurring flows for first-time guests, people who stopped coming (paused), and baptism Sundays." ],
    [ "Group sync", `${model.syncDataViews.length} data views, filed in the church's own category tree, keep the Volunteers and Small Group Leaders lists, a prayer security role, and the New Here and We Miss You care lists current through Rock's Group Sync job.` ],
    [ "Data views", `${model.reportingDataViews.length} layered data views: foundations that hold the shared rules, Ministry by Design stages built from steps, and next-step views such as Ready to Grow and Ready to Serve, filed by Ministry System, and ${model.reports.length} reports on them, from the pastors' path reports to the kids allergy list. Each view and report has a run history, and ${model.metrics.length} metrics count the stages week by week. The categories are locked to staff, with Leadership, Safety, and Finance each locked to their own roles.` ],
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
      <p class="dataset-time">Loads in ${loadTime( model.datasets["core-1"] )}</p>
      <ul>
        <li>The Cast at Phoenix: ${model.datasets["core-1"].households} households, ${model.datasets["core-1"].people} people</li>
        <li>1 campus: Phoenix</li>
        <li>Needs ${databaseSpace( model.datasets["core-1"] )} of database space</li>
        <li>Every feature, with full history</li>
      </ul>
      <p class="dataset-for">For tests and most development.</p>
    </article>
    <article class="dataset dataset-mid reveal">
      <p class="dataset-size">Core 3</p>
      <p class="dataset-time">Loads in ${loadTime( model.datasets["core-3"] )}</p>
      <ul>
        <li>The Cast, plus 75 generated households at each of Peoria and Gilbert</li>
        <li>3 campuses</li>
        <li>Needs ${databaseSpace( model.datasets["core-3"] )} of database space</li>
        <li>Campus test groups at every new campus</li>
      </ul>
      <p class="dataset-for">For campus features and filters.</p>
    </article>
    <article class="dataset dataset-big reveal">
      <p class="dataset-size">Full 7</p>
      <p class="dataset-time">Loads in ${loadTime( model.datasets["full-7"] )}</p>
      <ul>
        <li>The Cast, plus the Crowd: about 25,000 people</li>
        <li>7 campuses across the Valley, each with its own people</li>
        <li>Needs ${databaseSpace( model.datasets["full-7"] )} of database space</li>
        <li>Volume for reports, charts, and search</li>
      </ul>
      <p class="dataset-for">For demos, performance, and reporting.</p>
    </article>
  </div>
</section>

<section class="section section-sand" id="sign-in">
  <div class="wrap">
    ${sectionHead( { eyebrow: "Sign in", title: "Sign in as someone you know.", lede: "When you install, you choose one password. Every sample login uses it, and it is never saved. Leave it blank, and no sample logins load. The admin login keeps the password your Rock already had." } )}
    <div class="card-grid card-grid-four">
      <div class="card reveal"><h3><code>admin</code></h3><p>Alisha Marble, the Rock administrator. Rock's own admin person becomes her, in the Marble family, with her photo, her story, and real connection requests to work, many with the celebrations she wrote.</p></div>
      <div class="card reveal"><h3><code>tdecker</code></h3><p>Ted Decker, the Outreach Pastor. He holds most staff security roles, so he is the login for testing staff screens.</p></div>
      <div class="card reveal"><h3><code>pfoster</code></h3><p>Pete Foster, the Senior Pastor, with pastoral and staff security roles.</p></div>
      <div class="card reveal"><h3><code>cdecker</code></h3><p>Cindy Decker, a volunteer with staff-like and finance access, who also serves on the prayer team.</p></div>
    </div>
    <p class="fine">Use a strong password on any site other people can reach. Staff logins such as <code>tdecker</code> can see private notes and giving. Every Cast person's username is on their <a href="cast.html">Cast page</a>.</p>
  </div>
</section>

<section class="section">
  <div class="wrap">
    ${sectionHead( { eyebrow: "What's inside", title: "Every dataset fills every feature." } )}
    <div class="feature-grid feature-grid-light">
      ${inside( model ).map(( [ title, text ] ) => html`<div class="feature reveal"><h3>${esc( title )}</h3><p>${esc( text )}</p></div>` )}
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
      <p class="eyebrow">Your Rock version</p>
      <h2>Made for your version.</h2>
      <p>Each package is built for one Rock major release, such as Rock 21, and works on every update of it. The loader reads your Rock's version and refuses a different major release. Packages are made for Rock 20 and Rock 21.</p>
      <p>Before a package is published, it's loaded into a clean copy of that Rock and checked, and every new Rock update is checked the same way. See <a href="how-its-built.html#trust">how we prove it</a>.</p>
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
dotnet run -- --package ../packages/core-1.zip --login-password "choose-one" --connection "Server=localhost;Database=RockSample;Integrated Security=true;TrustServerCertificate=true"</code></pre></li>
    </ol>
    <p class="fine">You need the .NET 10 SDK, Node.js 20 or later, and SQL Server with a freshly installed Rock database. Sample data installs into a fresh database only. Leave out <code>--login-password</code> to load no sample logins. See the <a href="${repoUrl}#quick-start">quick start</a> for every option.</p>
  </div>
</section>
`
    };
};
