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
const { sectionHead, zoomable } = require( "../components" );

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
    [ "Midweek", "Students check in", "Students check in again on a weeknight, so their streaks and attendance show up all week." ]
];

function servingTime( slot ) {
    const [ , hhmm ] = slot.split( "-" );
    const h = +hhmm.slice( 0, 2 );
    return `${h > 12 ? h - 12 : h}:${hhmm.slice( 2 )}`;
}

module.exports = function church( { model, r } ) {
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
      <p>Rock Solid Church is a fictional, multi-campus church in the Phoenix area. It started in north Phoenix, where the Cast lives, and has grown to seven campuses across the Valley.</p>
      <p>The building has warm-white stucco, charcoal trim, pale oak, gray floors, and muted teal furniture. Desert plants line the walks. Every campus shares the same rooms, so check-in and serving work the same everywhere.</p>
    </div>
    <div class="mission reveal" aria-label="The church's mission">
      <span class="mission-heading">Our mission</span>
      <span class="mission-line">Love God.</span>
      <span class="mission-line">Love people.</span>
      <span class="mission-line">Share Jesus.</span>
    </div>
  </div>
</section>

<section class="section section-sand">
  <div class="wrap">
    ${sectionHead( { eyebrow: "Seven campuses", title: "One church across the Valley.", lede: "Core 1 has the broadcast campus in Phoenix. Core 3 adds Peoria and Gilbert. Full 7 has all seven, each with its own people and character." } )}
    <div class="campus-grid">
      ${model.campuses.map( ( c, i ) => html`<div class="campus reveal${c.rockDefault ? " campus-main" : ""}">
        <span class="campus-number">${String( i + 1 ).padStart( 2, "0" )}</span>
        <h3>${esc( c.name )}${c.rockDefault ? html` <span class="tag tag-accent">Broadcast campus</span>` : ""}</h3>
        <p>${esc( c.rockDefault ? "North Phoenix, where the Cast lives. The original and largest campus." : c.notes )}</p>
        <p class="campus-meta">Sundays at ${c.schedules.filter( s => s.type === "weekend" ).map( s => servingTime( s.slot ) ).join( ", " )}</p>
        <p class="campus-meta">In ${c.datasets.map( d => ( { "core-1": "Core 1", "core-3": "Core 3", "full-7": "Full 7" } )[d] ).join( ", " )}</p>
      </div>` )}
    </div>
  </div>
</section>

<section class="section" id="tour">
  <div class="wrap">
    ${sectionHead( { eyebrow: "Building tour", title: "Walk in the front door.", lede: "Twelve views of the building, from the parking lot to the prayer room. Select any photo to see it larger." } )}
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

<section class="section section-sand">
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
