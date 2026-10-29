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
const { sectionHead, zoomable, portrait } = require( "../components" );

// The staff departments, in the order the church page shows them (configuration/staff.json).
const departments = [
    [ "executive", "Leadership" ],
    [ "administration", "Operations" ],
    [ "worship", "Worship and production" ],
    [ "kids", "Kids" ],
    [ "students", "Students" ],
    [ "connections", "Outreach, groups, and care" ],
    [ "campuses", "Campuses" ]
];

// The church's initiatives, in the order they started. Starting Point is the first.
// Artwork comes from graphics/initiatives/<key>/ when it exists; until then the card shows instead.
const initiatives = [
    {
        key: "starting-point",
        eyebrow: "The first initiative",
        paragraphs: [
            "Starting Point is the class for people who are new to the church. It is the first step on the Discipleship Path, in the Belong stage.",
            "Each campus starts a class every four weeks. It meets on four Wednesday nights, so every install has a class in progress at every campus.",
            "Serving teams ask for Starting Point first, and Rock enforces it: a Serve step needs a completed Starting Point Class. About 40% of active adults and every volunteer have taken it.",
            "In the Cast, Brian and Wendy Gilbert are taking the class now so they can greet together. Bri Hall finished it last month and waits as a Pending greeter."
        ],
        card: [ "Four Wednesday nights.", "Every four weeks.", "At every campus." ]
    },
    {
        key: "built-on-the-rock",
        eyebrow: "A two-year building campaign",
        paragraphs: [
            "Built on the Rock pays for a Student Center at the Phoenix campus and more kids rooms at every campus. The name comes from the wise builder in Matthew 7, who built his house on the rock.",
            "People and businesses at every campus made pledges. Rock's Building Fund is the campaign's fund, renamed Built on the Rock, and it holds only campaign gifts, so Rock's pledge screens show real progress.",
            "Every install lands about 40% through the campaign. Some pledges are ahead and some are behind, so each one has a story."
        ],
        card: [ "A Student Center at Phoenix.", "Kids rooms at every campus.", "Two years." ]
    }
];

// The tour walks the building from the parking lot to the back rooms.
const tour = [
    [ "front-exterior-wide", "Arrive", "A low, warm-white lobby with a charcoal canopy sits in front of the taller worship center. Palo verde trees and agaves line the walk." ],
    [ "main-entrance-close", "The front doors", "Glass doors under the canopy, with the Rock Solid Church logo set on the stucco wall." ],
    [ "lobby-toward-entrance", "The lobby", "A big gathering space with the welcome counter, an oak slat column, and teal seating groups." ],
    [ "lobby-toward-worship-center", "Into the worship center", "Three pairs of double doors lead in. The walls carry the mission and the invitation to Starting Point." ],
    [ "worship-center-from-rear", "The worship center", "Three banks of chairs and two aisles face a low stage, with warm house lights and oak slat walls." ],
    [ "worship-center-from-stage", "From the stage", "The view the worship team sees, looking back toward the production booth." ],
    [ "stage-from-side", "The stage", "Keys on the left, drums behind a shield, microphones across the front, and a guitar on the right." ],
    [ "production-booth-wide", "The production booth", "Sound, slides, and cameras run from the center of the room, with cables in neat, separate runs." ],
    [ "kids-classroom", "Kids classroom", "Coral and teal walls, low oak tables, and cubbies sized for small hands." ],
    [ "student-room", "Student room", "Sofas and poufs for conversation, a table-tennis corner, and the Rock Solid Students wall." ],
    [ "prayer-room", "Prayer room", "Six teal chairs around an oak table, soft daylight, and a verse on the wall." ],
    [ "first-aid-room", "First-aid room", "A padded cot, stocked cabinets, and a door that opens onto the lobby." ]
];

const week = [
    [ "Sunday, 9:00", "First service", "Greeters open the doors, ushers seat guests, and families check their kids in with one security code for the whole family." ],
    [ "Sunday, 10:30", "Second service and students", "The busiest hour. Students meet in their own service while volunteers serve on their rotation." ],
    [ "Sunday, 12:00", "Third service", "Phoenix, Chandler, and Tempe add a noon service for later risers and young adults." ],
    [ "Sunday afternoon", "Prayer and follow-up", "The prayer team prays through new requests, and connectors follow up with people who asked to serve." ],
    [ "Sunday to Thursday", "Small groups", "Groups meet in homes, mostly at 7:00 in the evening, through fall and spring seasons and a light summer." ],
    [ "Wednesday night", "Classes and Student Night", "Starting Point and Foundations classes meet, and Student Night is on the calendar every week. Students still check in only on Sunday." ]
];

// The calendar's Ministry by Design stages, in path order. The events in each come from configuration/calendar.json.
const calendarStages = [
    [ "Belong", "Front-door, family, community, and care events. A church's front door is wide, so this stage has the most." ],
    [ "Equip", "Classes, studies, retreats, and the Sundays that help people grow." ],
    [ "Contribute", "Serving, outreach, and generosity." ],
    [ "Lead", "Training and huddles for the people who lead groups and teams." ],
    [ "Staff", "Staff events on the Internal calendar. They run the church rather than serve a stage." ]
];

function servingTime( slot ) {
    const [ , hhmm ] = slot.split( "-" );
    const h = +hhmm.slice( 0, 2 );
    return `${h > 12 ? h - 12 : h}:${hhmm.slice( 2 )}`;
}

function initiativeBlock( initiative, image, index, r ) {
    const art = image
        ? zoomable( { src: r( "images/initiatives/" + image.file ), alt: `${initiative.name} artwork: ${image.copy.slice( 0, 3 ).join( " " )}`, className: "initiative-art", width: image.width, height: image.height } )
        : html`<div class="mission initiative-card" aria-label="${esc( initiative.name )}">
      <span class="mission-heading">${esc( initiative.name )}</span>
      ${initiative.card.map( line => html`<span class="mission-line">${esc( line )}</span>` )}
    </div>`;
    return html`<div class="split initiative${index % 2 ? " split-reverse" : ""}">
    <div class="split-text reveal">
      <p class="eyebrow">${esc( initiative.eyebrow )}</p>
      <h3>${esc( initiative.name )}</h3>
      ${initiative.paragraphs.map( p => html`<p>${esc( p )}</p>` )}
    </div>
    <div class="reveal">${art}</div>
  </div>`;
}

module.exports = function church( { model, r } ) {
    // Every initiative folder shows up, even one this page has no words for yet.
    const known = new Set( initiatives.map( i => i.key ) );
    const shown = [
        ...initiatives.map( i => ( { ...i, name: ( model.initiativeGraphics.find( g => g.key === i.key ) || {} ).name || ( i.key === "built-on-the-rock" ? model.campaign.name : i.key ) } ) ),
        ...model.initiativeGraphics.filter( g => !known.has( g.key ) ).map( g => ( { key: g.key, name: g.name, eyebrow: "Initiative", paragraphs: [ g.images[0] ? g.images[0].concept : "" ], card: [] } ) )
    ];
    const views = tour.map( ( [ key, title, text ] ) => ( { view: model.churchViews.find( v => v.key === key ), title, text } ) ).filter( t => t.view );

    return {
        file: "church.html",
        active: "church",
        title: "Rock Solid Church",
        description: "Tour Rock Solid Church: seven Phoenix Valley campuses, a contemporary building, eight serving teams, and the small groups that fill the sample data.",
        body: html`
<section class="hero hero-short">
  <img class="hero-image" src="${r( "images/church/lobby-toward-worship-center.jpg" )}" alt="The lobby looking toward the worship center, with three pairs of doors and the mission wall." width="1536" height="1024">
  <div class="hero-shade"></div>
  <div class="wrap hero-content">
    <img class="hero-logo" src="${r( "downloads/brand/rock-solid-church-logo-white.svg" )}" alt="Rock Solid Church" width="420" height="105">
    <h1 class="display">A church you can walk through.</h1>
    <p class="hero-lede">Clean, new, and contemporary, in the Phoenix Valley. Every room in Rock's data is a room you can see.</p>
  </div>
</section>

<section class="section">
  <div class="wrap split">
    <div class="split-text reveal">
      <p class="eyebrow">Who we are</p>
      <h2>Rock Solid Church</h2>
      <p>Rock Solid Church is a fictional, multi-campus church in the Phoenix area. It started in north Phoenix, where most of the Cast lives, and has grown to seven campuses across the Valley.</p>
      <p>The original Phoenix building has warm-white stucco, charcoal trim, pale oak, gray floors, and muted teal furniture. The other campuses have their own buildings, including a high school auditorium in Goodyear, where volunteers set up and tear down every Sunday. Scottsdale meets in a traditional church building. It was an independent church until it merged with Rock Solid Church. The sample data uses the same room setup at every campus, so check-in and serving work the same everywhere.</p>
      <p>The church is in the middle of Built on the Rock, a two-year building campaign for a Student Center at Phoenix and more kids rooms at every campus. People and businesses at every campus have pledged.</p>
    </div>
    <div class="mission reveal" aria-label="The church's mission">
      <span class="mission-heading">Our mission</span>
      <span class="mission-line">Love God.</span>
      <span class="mission-line">Love people.</span>
      <span class="mission-line">Share Jesus.</span>
    </div>
  </div>
</section>

${model.staff.length ? html`<section class="section section-sand" id="staff">
  <div class="wrap">
    ${sectionHead( { eyebrow: "Staff", title: `${model.staff.length} people, every one in the Cast.`, lede: "Each staff member is a Cast person with a story, a family, and a login, and is the same in every load. Every campus has its own named campus pastor, who writes that campus's notes, welcomes its first-time guests, and is the contact for its events. A campus pastor outside Phoenix is in the datasets that have their campus. Rock's Org Chart page shows the same staff in seven units, each with its leader." } )}
    ${departments.filter( ( [ key ] ) => model.staff.some( s => s.department === key ) ).map( ( [ key, label ] ) => html`<h3 class="staff-department">${esc( label )}</h3>
    <div class="staff-grid">
      ${model.staff.filter( s => s.department === key ).map( s => html`<a class="staff-card reveal" href="${r( "cast/" + s.member.household.key + ".html" )}">
        ${s.staffPhoto ? html`<img class="portrait portrait-lg" src="${r( "images/staff/" + s.staffPhoto )}" alt="${esc( s.member.displayName )}" loading="lazy" width="200" height="200">` : portrait( s.member, r, "lg" )}
        <span class="staff-name">${esc( s.member.displayName )}</span>
        <span class="staff-title">${esc( s.title )}</span>
      </a>` )}
    </div>` )}
  </div>
</section>` : ""}

<section class="section" id="initiatives">
  <div class="wrap">
    ${sectionHead( { eyebrow: "Initiatives", title: "What the church invites people into.", lede: "Starting Point was the first. The sample data follows each initiative in Rock, and their artwork is on the brand page." } )}
    ${shown.map( ( initiative, i ) => {
            const graphic = model.initiativeGraphics.find( g => g.key === initiative.key );
            return initiativeBlock( initiative, graphic ? graphic.images[0] : null, i, r );
        } )}
  </div>
</section>

<section class="section section-sand">
  <div class="wrap">
    ${sectionHead( { eyebrow: "Seven campuses", title: "One church across the Valley.", lede: "Core 1 has the broadcast campus in Phoenix. Core 3 adds Peoria and Gilbert. Full 7 has all seven, each with its own people and character." } )}
    <div class="campus-grid">
      ${model.campuses.map( ( c, i ) => html`<div class="campus reveal${c.rockDefault ? " campus-main" : ""}">
        ${c.photo ? zoomable( { src: r( "images/campuses/" + c.photo ), alt: c.photoDescription, className: "campus-photo" } ) : ""}
        <span class="campus-number">${String( i + 1 ).padStart( 2, "0" )}</span>
        <h3>${esc( c.name )}${c.rockDefault ? html` <span class="tag tag-accent">Broadcast campus</span>` : ""}</h3>
        <p>${esc( c.rockDefault ? "North Phoenix, where most of the Cast lives. The original and largest campus." : c.notes )}</p>
        <p>${esc( c.buildingType )}</p>
        <p class="campus-meta">Sundays at ${c.schedules.filter( s => s.type === "weekend" ).map( s => servingTime( s.slot ) ).join( ", " )}</p>
        ${c.pastor ? html`<p class="campus-meta">Campus pastor: <a href="${r( "cast/" + c.pastor.household.key + ".html" )}">${esc( c.pastor.displayName )}</a></p>` : ""}
        <p class="campus-meta">In ${c.datasets.map( d => ( { "core-1": "Core 1", "core-3": "Core 3", "full-7": "Full 7" } )[d] ).join( ", " )}</p>
      </div>` )}
    </div>
  </div>
</section>

<section class="section" id="tour">
  <div class="wrap">
    ${sectionHead( { eyebrow: "Phoenix building tour", title: "Walk in the front door.", lede: "Twelve views of the original Phoenix building, from the parking lot to the prayer room. Select any photo to see it larger." } )}
  </div>
  <div class="tour" tabindex="0" aria-label="Building tour, scroll sideways">
    ${views.map( ( t, i ) => html`<div class="tour-stop reveal">
      ${zoomable( { src: r( "images/church/" + t.view.jpeg ), alt: t.view.title, caption: html`<span class="tour-step">${String( i + 1 ).padStart( 2, "0" )}</span><strong>${esc( t.title )}</strong> ${esc( t.text )}` } )}
    </div>` )}
  </div>
</section>

<section class="section section-ink" id="serving">
  <div class="wrap">
    ${sectionHead( { eyebrow: "Ministries and serving", title: "Eight teams keep Sunday running.", lede: `Every campus has the same eight serving teams, with rotations, schedules, and volunteer check-in. At Phoenix, ${model.counts.volunteers} Cast volunteers fill them, and the photos show them at work.` } )}
    <div class="serve-grid">
      ${model.opportunities.map( o => html`<article class="serve reveal">
        ${o.photo ? zoomable( { src: r( "images/serving/" + o.photo ), alt: `${o.team}: ${o.photoPeople.join( " and " )} serving` } ) : ""}
        <div class="serve-body">
          <h3>${esc( o.team )}</h3>
          <p>${esc( o.summary )}</p>
          <p class="serve-meta">${o.photoPeople.length ? html`Pictured: ${esc( o.photoPeople.join( ", " ) )}` : ""}</p>
          <p class="serve-meta">Serves in: ${esc( o.rooms.join( ", " ) )}</p>
          ${o.requiresBackgroundCheck ? html`<span class="tags"><span class="tag">Needs a background check</span></span>` : ""}
        </div>
      </article>` )}
    </div>
  </div>
</section>

<section class="section" id="groups">
  <div class="wrap">
    ${sectionHead( { eyebrow: "Small groups", title: "Where the church gets small.", lede: "Groups of about a dozen adults meet in homes during the week. Each Cast group has a photo of its actual members, made from their profile photos." } )}
    <div class="group-grid">
      ${model.smallGroups.filter( g => g.photo ).map( g => html`<article class="group reveal">
        ${zoomable( { src: r( "images/groups/" + g.photo ), alt: `${g.name}: ${g.photoPeople.join( ", " )}` } )}
        <div class="group-body">
          <p class="group-topic">${esc( g.topic || "Small group" )}${g.day ? html` · ${esc( g.day )}s` : ""}</p>
          <h3>${esc( g.name )}</h3>
          <p>${esc( g.description )}</p>
        </div>
      </article>` )}
    </div>
  </div>
</section>

${model.communicationLists.length ? html`<section class="section" id="lists">
  <div class="wrap">
    ${sectionHead( { eyebrow: "Staying in touch", title: "The lists the church writes to.", lede: `Each campus sends a weekly newsletter, and ministries write to their own lists. Who is on each list follows how involved they are, so the people who lead and serve hear the most.${model.syncedListKeys.length ? " Rock's group sync keeps some lists current from data views, so a new volunteer or a new guest joins the right list on their own." : ""}` } )}
    <div class="card-grid">
      ${model.communicationLists.map( l => html`<div class="card reveal">
        <h3>${esc( l.campus ? l.publicName.replace( "{campus} ", "" ) : l.publicName )}</h3>
        <p>${esc( l.description )}</p>
        <span class="tags"><span class="tag">${l.isPublic ? "Anyone can join" : "Staff-managed"}</span>${l.campus ? html` <span class="tag">One per campus</span>` : ""}${model.syncedListKeys.includes( l.key ) ? html` <span class="tag">Kept by group sync</span>` : ""}</span>
      </div>` )}
    </div>
  </div>
</section>` : ""}

${model.calendarEvents.length ? html`<section class="section section-sand" id="calendar">
  <div class="wrap">
    ${sectionHead( { eyebrow: "The calendar", title: "A year of events, on purpose.", lede: `The church has ${model.calendarEvents.length} events on Rock's Public and Internal calendars, at every campus a dataset has. Each event serves one Ministry by Design stage, the same path the step programs follow: Belong, then Equip, then Contribute, then Lead.` } )}
    <div class="card-grid">
      ${calendarStages.map( ( [ stage, text ] ) => {
          const events = model.calendarEvents.filter( e => e.stage === stage );
          return events.length ? html`<div class="card reveal">
        <h3>${esc( stage )} <small class="fine">${events.length} ${events.length === 1 ? "event" : "events"}</small></h3>
        <p>${esc( text )}</p>
        <span class="tags">${events.map( e => html`<span class="tag">${esc( e.name )}</span>` )}</span>
      </div>` : "";
      } )}
    </div>
    <p class="fine">Dates count from install day and match the steps, the newsletters, and the Cast's stories. The car show was two days ago, and the student retreat is coming up.</p>
  </div>
</section>` : ""}

${model.registrations.templates.length ? html`<section class="section" id="registrations">
  <div class="wrap">
    ${sectionHead( { eyebrow: "Event registrations", title: "Register now, on every install day.", lede: `${model.registrations.templates.length} kinds of registration are linked to their calendar events, so each event's page shows a Register button. Each opens and closes on a rule counted from install day. Most open again the day after the last one closes, so some are always open and every feature can be tried.` } )}
    <div class="card-grid">
      ${model.registrations.categories.map( category => {
          const templates = model.registrations.templates.filter( t => t.category === category.key );
          return templates.length ? html`<div class="card reveal">
        <h3>${esc( category.name )}</h3>
        <span class="tags">${templates.map( t => html`<span class="tag">${esc( t.name )}</span>` )}</span>
      </div>` : "";
      } )}
    </div>
    <p class="fine">Student Camp has a girls' and a boys' registration, each with its own limit, sharing one set of buses. Paid ones are set up with deposits, payment plans, fees, and discounts. The people who register come in a later release.</p>
  </div>
</section>` : ""}

<section class="section">
  <div class="wrap">
    ${sectionHead( { eyebrow: "A week at the church", title: "What the data remembers." } )}
    <ol class="timeline">
      ${week.map( ( [ when, title, text ] ) => html`<li class="reveal"><span class="when">${esc( when )}</span><h3>${esc( title )}</h3><p>${esc( text )}</p></li>` )}
    </ol>
  </div>
</section>

<section class="section">
  <div class="wrap walls">
    <div class="wall wall-paper reveal">
      <span class="wall-eyebrow">YOU BELONG HERE.</span>
      <span class="wall-big">STARTING POINT</span>
      <span>From a first hello to a place you belong.</span>
      <span class="wall-strong">Enroll in Starting Point</span>
    </div>
    <div class="wall wall-paper reveal">
      <span class="wall-big">Find <em>strength</em> in prayer.</span>
      <span>"I can do all things through Christ who strengthens me." Philippians 4:13</span>
    </div>
  </div>
</section>

<dialog class="viewer" aria-label="Photo viewer">
  <form method="dialog"><button class="viewer-close" aria-label="Close">×</button></form>
  <img alt="">
  <p class="viewer-caption"></p>
</dialog>
`
    };
};
