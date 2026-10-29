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
const { sectionHead, portrait } = require( "../components" );

const features = [
    [ "People and families", "Households at every stage of life, with photos, relationships, and connection statuses." ],
    [ "Attendance", "Years of Sundays for every family, shaped by holidays and seasons, so every report is full." ],
    [ "Giving", "Weekly, monthly, and erratic givers, scheduled gifts, and scanned checks that match each gift." ],
    [ "Giving analytics", "Journey stages, giving bins, and a year of giving alerts, the way Rock computes them." ],
    [ "Check-in", "Kids from the nursery through 6th grade, security codes, allergies, and achievements." ],
    [ "Small groups", "Groups with leaders, hosts, topics, child care, history, and Group Finder test cases." ],
    [ "Serving", "Eight teams at every campus, with rotations, schedules, blackout dates, and volunteer check-in." ],
    [ "Connections", "Requests to serve that follow each family's story, from first contact to placement, with due dates that run by status." ],
    [ "Prayer", "Requests in every state, an active prayer team, comments, and answers." ],
    [ "Notes", "Staff notes in six types with View security, replies, alerts, and mentions, on the profiles demos open first." ],
    [ "Steps", "A seven-step Discipleship Path and a four-step Student Path made from each person's records: classes, groups, baptism, serving, giving, leading, camps, and mission trips." ],
    [ "Calendar", "Easter to the car show: a full year of events at every campus, each serving a Ministry by Design stage, with dates that match the steps and newsletters." ],
    [ "Event registrations", "Camp, retreats, VBS, and classes with Register buttons, fees, discounts, payment plans, and placement groups." ],
    [ "Communications", "Weekly newsletters, church-wide and ministry emails, mass texts, live SMS conversations, serving reminders, and app pushes." ],
    [ "Logins", "Sign in as admin, the Outreach Pastor, or the Senior Pastor, with a password you choose at install. Many church members can sign in too." ],
    [ "Group sync", "Data views in the church's own categories keep staff lists, a prayer role, and two care lists current." ],
    [ "Businesses", "Businesses that give, each with one or two contacts tied to people in the church." ]
];

module.exports = function home( { model, r } ) {
    const c = model.counts;
    const faceKeys = [ "decker-ted", "foster-pete", "marble-alisha", "ramirez-carlos", "carter-margaret", "alvarez-elena", "brooks-rachel", "garcia-daniel", "jackson-mariah", "coffee-phil" ];
    const faces = faceKeys
        .map( key => [ ...model.people.values() ].find( p => `${p.household.key}-${p.key}` === key ) )
        .filter( p => p && p.photo );
    const tour = [ "lobby-toward-worship-center", "worship-center-from-rear", "kids-classroom", "prayer-room", "student-room" ]
        .map( k => model.churchViews.find( v => v.key === k ) )
        .filter( Boolean );

    return {
        file: "index.html",
        active: "home",
        title: "",
        description: "Rock Solid Church is a complete, realistic sample church for Rock RMS, ready in seconds for testing, AI, training, and demos.",
        body: html`
<section class="hero">
  <img class="hero-image" src="${r( "images/church/front-exterior-wide.jpg" )}" alt="The front of Rock Solid Church: a low, warm-white building with a charcoal canopy, glass doors, and desert landscaping." width="1536" height="1024">
  <div class="hero-shade"></div>
  <div class="wrap hero-content">
    <p class="eyebrow eyebrow-light">Rock Sample Data</p>
    <h1 class="display">A whole church,<br><em>ready in seconds.</em></h1>
    <p class="hero-lede">Rock Solid Church is a complete, realistic church for Rock RMS. Its families give, serve, pray, and check in their kids, with years of history that always ends today.</p>
    <div class="actions">
      <a class="button button-gradient" href="${r( "why.html" )}">Why it matters</a>
      <a class="button button-light" href="${r( "cast.html" )}">Meet the Cast</a>
    </div>
  </div>
</section>

<section class="stats-band">
  <div class="wrap stats">
    <div class="stat reveal"><span class="stat-value">3</span><span class="stat-label">datasets, from tests to full demos</span></div>
    <div class="stat reveal"><span class="stat-value">7</span><span class="stat-label">campuses across the Phoenix Valley</span></div>
    <div class="stat reveal"><span class="stat-value">${c.households}</span><span class="stat-label">hand-designed Cast households</span></div>
    <div class="stat reveal"><span class="stat-value">25,000</span><span class="stat-label">people in the largest dataset</span></div>
    <div class="stat reveal"><span class="stat-value">${model.datasets["core-1"].typicalInstallSeconds}<small>s</small></span><span class="stat-label">to load the Cast into Rock</span></div>
  </div>
</section>

<section class="section">
  <div class="wrap">
    ${sectionHead( { eyebrow: "Two visions, one church", title: "Built to be used, again and again.", lede: "We built this sample data for two big jobs. Both need the same thing: a church that behaves like a real one, every time you install it." } )}
    <div class="vision-grid">
      <a class="vision-card reveal" href="${r( "why.html#testing" )}">
        <img src="${r( "images/church/production-booth-wide.jpg" )}" alt="The production booth at the back of the worship center, with the sound console and camera." loading="lazy" width="1536" height="1024">
        <span class="vision-body">
          <span class="vision-kicker">Vision 1</span>
          <span class="vision-title">A proving ground for testing and AI</span>
          <span class="vision-text">Tests and AI agents get a church they can count on. Every Cast person is there by Guid, and every pattern hangs together, so results mean something.</span>
          <span class="link-arrow">Read more</span>
        </span>
      </a>
      <a class="vision-card reveal" href="${r( "why.html#teaching" )}">
        <img src="${r( "images/church/lobby-toward-entrance.jpg" )}" alt="The church lobby, looking toward the front doors, with the welcome counter and teal seating." loading="lazy" width="1536" height="1024">
        <span class="vision-body">
          <span class="vision-kicker">Vision 2</span>
          <span class="vision-title">One church for teaching and showing Rock</span>
          <span class="vision-text">Help articles, training classes, videos, and demos all use the same families, so learners meet Ted Decker once and follow him from lesson to lesson.</span>
          <span class="link-arrow">Read more</span>
        </span>
      </a>
    </div>
  </div>
</section>

<section class="section section-sand">
  <div class="wrap split">
    <div class="split-text reveal">
      <p class="eyebrow">Rock Solid Church</p>
      <h2>A real place, down to every room.</h2>
      <p>Rock Solid Church is a clean, new, contemporary church in the Phoenix Valley. It has a big welcoming lobby, a worship center with three banks of chairs, kids rooms, a student room, a prayer room, and a first-aid room.</p>
      <p>Its serving teams work in those rooms, its kids check in to them, and its photos show them. That makes every screen in Rock feel like the same church.</p>
      <a class="link-arrow" href="${r( "church.html" )}">Tour the church</a>
    </div>
    <div class="mosaic reveal">
      ${tour.map( ( v, i ) => html`<img class="mosaic-${i}" src="${r( "images/church/" + v.jpeg )}" alt="${esc( v.title )}" loading="lazy" width="1536" height="1024">` )}
    </div>
  </div>
</section>

<section class="section">
  <div class="wrap">
    ${sectionHead( { eyebrow: "The Cast", title: "Meet the people in the data.", lede: `The Cast is ${c.households} hand-designed households and ${c.people} people. They are the same in every dataset, and each one exists for a reason.`, center: true } )}
    <div class="face-row reveal">
      ${faces.map( p => html`<a class="face" href="${r( "cast/" + p.household.key + ".html" )}">${portrait( p, r, "lg" )}<span class="face-name">${esc( p.displayName )}</span></a>` )}
    </div>
    <p class="center reveal"><a class="button button-dark" href="${r( "cast.html" )}">See all ${c.households} households</a></p>
  </div>
</section>

<section class="section section-ink">
  <div class="wrap">
    ${sectionHead( { eyebrow: "Every feature, filled in", title: "Open any screen. There's something to see.", lede: "Old sample data left whole features empty. This church has history in every corner of Rock." } )}
    <div class="feature-grid">
      ${features.map( ( [ title, text ] ) => html`<div class="feature reveal"><h3>${esc( title )}</h3><p>${esc( text )}</p></div>` )}
    </div>
  </div>
</section>

<section class="section">
  <div class="wrap"><div class="cta reveal">
    <div>
      <p class="eyebrow">Built in the open</p>
      <h2>Help us build the church.</h2>
      <p class="lede">Suggest a family or a story, or tell us which screen still looks empty. The whole church lives in plain data files anyone can read.</p>
    </div>
    <div class="actions">
      <a class="button button-gradient" href="${r( "contribute.html" )}">Ways to contribute</a>
      <a class="button button-ghost" href="${r( "brand.html" )}">Download the brand kit</a>
    </div>
  </div></div>
</section>
`
    };
};
