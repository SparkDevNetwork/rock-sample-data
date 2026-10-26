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
const { sectionHead } = require( "../components" );

const problems = [
    [ "It was slow.", "The old loader saved one record at a time through the full Rock model.", "The new loader loads the Cast into Rock in about 15 seconds, and 25,000 people with two years of email and texts in about 4 minutes." ],
    [ "It was thin.", "Connections, prayer, serving, and communications had little or no data.", "Every feature has history, from check-in streaks to giving alerts." ],
    [ "It wasn't realistic.", "Random numbers hid bugs and made demos look fake.", "Patterns come from summary-only queries on real churches, run with permission, and are graded every build." ],
    [ "It went stale.", "Fixed dates fell out of \"last 12 months\" reports.", "Every date counts back from install day, so reports are always full." ]
];

const audiences = [
    [ "Core developers", "Load a realistic church in seconds, then build and debug against people you know by name." ],
    [ "QA and AI testing", "Write tests and agent evaluations that name real Cast people and expect real answers." ],
    [ "Trainers and writers", "Teach every class and write every help article with the same families and the same screens." ],
    [ "Demos and partners", "Show Rock with a church that looks alive, from the dashboard to the last report." ],
    [ "Plugin developers", "Test a plugin against the same data the core team uses, at the same size." ],
    [ "The community", "Propose families and stories, and help the church cover more of Rock." ]
];

module.exports = function why( { model, r } ) {
    return {
        file: "why.html",
        active: "why",
        title: "Why sample data matters",
        description: "Why the Rock team invested in realistic sample data: a proving ground for testing and AI, and one shared church for teaching and showing Rock.",
        body: html`
<section class="page-hero">
  <div class="wrap narrow">
    <p class="eyebrow">Why it matters</p>
    <h1 class="display">Good software needs <em>a good church to test it on.</em></h1>
    <p class="lede">Every screen in Rock is about people: who came, who gave, who serves, who needs prayer. You can't test those screens, teach them, or show them well with empty or random data. So we built a church.</p>
  </div>
</section>

<section class="section section-tight">
  <div class="wrap">
    ${sectionHead( { eyebrow: "The problem", title: "The old sample data had four problems." } )}
    <div class="problem-grid">
      ${problems.map( ( [ title, before, after ] ) => html`<div class="problem reveal">
        <h3>${esc( title )}</h3>
        <p class="before">${esc( before )}</p>
        <p class="after"><span class="after-label">Now</span>${esc( after )}</p>
      </div>` )}
    </div>
  </div>
</section>

<section class="section section-sand" id="testing">
  <div class="wrap split split-reverse">
    <div class="split-text reveal">
      <p class="eyebrow">Vision 1</p>
      <h2>A proving ground for testing and AI.</h2>
      <p>A test is only as good as the data under it. When data is random, a test can pass for the wrong reason. When a family's giving, attendance, and groups don't fit together, a bug can hide in plain sight.</p>
      <p>AI raises the bar again. An assistant asked "who stopped giving this spring," "which kids missed check-in," or "who is ready to serve" needs answers that really exist in the data. Then we can judge whether it found them.</p>
      <ul class="checks">
        <li><strong>Known people.</strong> The Cast is the same in every dataset, so a test can name Ted Decker by Guid and trust that he is there.</li>
        <li><strong>Stories that hang together.</strong> A family's attendance, giving, groups, and serving fit together the way they do at a real church.</li>
        <li><strong>Repeatable.</strong> The same seed builds the same church, so a failure can be reproduced.</li>
        <li><strong>Big when you need it.</strong> Full 7 has about 25,000 people for performance and reporting tests.</li>
        <li><strong>Proven against Rock.</strong> Every package is loaded into a clean Rock and checked by Rock's own cleanup job before it ships.</li>
      </ul>
    </div>
    <div class="split-media reveal">
      <img class="rounded shadow" src="${r( "images/serving/production-team.jpg" )}" alt="Ted Decker and Jim Simmons running sound and cameras in the production booth during a service." loading="lazy" width="1536" height="1024">
    </div>
  </div>
</section>

<section class="section" id="teaching">
  <div class="wrap split">
    <div class="split-text reveal">
      <p class="eyebrow">Vision 2</p>
      <h2>One church for teaching and showing Rock.</h2>
      <p>Rock is learned through help articles, screenshots, videos, and training classes. When each one uses different made-up people, learners have to start over every time.</p>
      <p>With one shared church, they don't. They meet the Decker family in the first lesson and follow them through check-in, giving, groups, and serving. Ted Decker is already the face of most Rock documentation, and now his whole church is real enough to explore.</p>
      <ul class="checks">
        <li><strong>Documentation.</strong> Every screenshot shows the same families, with full, believable history.</li>
        <li><strong>Training classes.</strong> Each student installs the same church, so exercises work the same for everyone.</li>
        <li><strong>Demos and promotion.</strong> Rock looks the way it does at a real church, not like an empty test site.</li>
        <li><strong>A shared look.</strong> Rock Solid Church has a logo, a building, and photos anyone can use. <a href="${r( "brand.html" )}">Get the brand kit.</a></li>
      </ul>
    </div>
    <div class="split-media reveal">
      <img class="rounded shadow" src="${r( "images/serving/greeters.jpg" )}" alt="Larry and Deb Bell greeting a young family at the church's front doors on a sunny morning." loading="lazy" width="1536" height="1024">
    </div>
  </div>
</section>

<section class="section section-ink">
  <div class="wrap narrow center">
    <p class="eyebrow eyebrow-light">Vision 3</p>
    <h2>A resource the whole community can grow.</h2>
    <p class="lede">Everything about the church lives in plain data files: the families, their stories, the campuses, the service times. Anyone can read them, and anyone can suggest a change. This site is the front door for that work.</p>
    <p><a class="button button-gradient" href="${r( "contribute.html" )}">See how to contribute</a></p>
  </div>
</section>

<section class="section">
  <div class="wrap">
    ${sectionHead( { eyebrow: "Who it's for", title: "One church, many jobs." } )}
    <div class="card-grid">
      ${audiences.map( ( [ title, text ] ) => html`<div class="card reveal"><h3>${esc( title )}</h3><p>${esc( text )}</p></div>` )}
    </div>
  </div>
</section>

<section class="section section-sand">
  <div class="wrap narrow center reveal">
    <blockquote class="pull">
      <p>Store who people are. Generate what they did, counting back from install day.</p>
      <cite>The idea behind every dataset. <a href="${r( "how-its-built.html" )}">How it's built</a></cite>
    </blockquote>
  </div>
</section>
`
    };
};
