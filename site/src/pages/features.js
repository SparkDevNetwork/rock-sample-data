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
    The Features page: one short section per part of Rock the sample data fills, each with what is there, where to
    look, and a link to the deeper page or doc. Every count comes from the model, so nothing here goes stale. Other
    sessions add to their own section by its id.
*/

const path = require( "path" );

const { esc, html, repoUrl } = require( "../layout" );
const { sectionHead } = require( "../components" );

const doc = file => `${repoUrl}/blob/main/${file}`;

/**
 * A Cast person's name, linked to their household page. The name is plain text when no Cast person has it.
 */
function person( model, name ) {
    const member = [ ...model.people.values() ].find( m => m.displayName === name );
    return member ? html`<a href="cast/${esc( member.household.key )}.html">${esc( name )}</a>` : esc( name );
}

/**
 * One feature area: the words on the left, and where to look on the right.
 */
function area( { id, eyebrow, title, text, checks, look, links, sand } ) {
    return html`
<section class="section${sand ? " section-sand" : ""}" id="${id}">
  <div class="wrap split feature-area">
    <div class="split-text reveal">
      <p class="eyebrow">${esc( eyebrow )}</p>
      <h2>${esc( title )}</h2>
      <p>${text}</p>
      <ul class="checks checks-tight">${checks.map( c => html`<li>${c}</li>` )}</ul>
      <p class="feature-links">${links.map( l => html`<a class="link-arrow" href="${l.href}">${esc( l.label )}</a>` )}</p>
    </div>
    <div class="panel reveal">
      <p class="panel-title">Where to look</p>
      <ul class="link-list look-list">${look.map( l => html`<li>${l}</li>` )}</ul>
    </div>
  </div>
</section>`;
}

/**
 * The roles a category's View rule allows, without Rock's "RSR - " prefix, or what its Edit rule allows.
 */
function lockText( category ) {
    const rules = category.security || [];
    const view = rules.find( r => r.action === "View" );
    const edit = rules.find( r => r.action === "Edit" );
    const roles = rule => rule.allow.map( r => r.replace( /^RSR - /, "" ) ).join( ", " );
    if ( view ) {
        return `Locked to ${roles( view )}`;
    }

    return edit ? `${roles( edit )} can edit` : "";
}

/**
 * A category and its children, as a nested list, each with its lock.
 */
function tree( categories, key ) {
    const category = categories.find( c => c.key === key );
    if ( !category ) {
        return "";
    }

    const children = categories.filter( c => c.parent === key );
    const lock = lockText( category );
    return html`<li><span class="tree-name">${esc( category.name )}</span>${lock ? html` <span class="tag${category.security.some( r => r.action === "View" ) ? " tag-accent" : ""}">${esc( lock )}</span>` : ""}${children.length ? html`<ul>${children.map( c => tree( categories, c.key ) )}</ul>` : ""}</li>`;
}

/**
 * The name of what an access row asks about: a category, a data view, or a report.
 */
function itemName( model, row ) {
    if ( row.category ) {
        const category = model.categories.find( c => c.key === row.category );
        return category ? `${category.name} ${category.entityType === "Rock.Model.Report" ? "reports" : "data views"}` : row.category;
    }

    const views = model.reportingDataViews.concat( model.syncDataViews );
    const found = row.dataView ? views.find( v => v.key === row.dataView ) : model.reports.find( r => r.key === row.report );
    return found ? `the ${found.name} ${row.dataView ? "data view" : "report"}` : row.dataView || row.report;
}

// The security walkthrough rows the page shows, by person and item. Each must be a row in reporting.json.
const walkthrough = [
    [ "Pete Foster", "leadership-reports", "View" ],
    [ "Priya Raman", "leadership-reports", "View" ],
    [ "Rachel Brooks", "kids-with-an-allergy", "View" ],
    [ "Jamal Pierce", "kids-with-an-allergy", "View" ],
    [ "Priya Raman", "top-givers", "View" ],
    [ "Cindy Decker", "top-givers", "View" ],
    [ "Pete Foster", "first-time-givers-to-thank", "View" ],
    [ "Jerry Wright", "church-reports", "View" ],
    [ "Pete Foster", "staff-workspace-data-views", "Edit" ]
];

// The example reports the page shows, by key.
const exampleReports = [ "path-movement-last-90-days", "guests-coming-back", "ready-to-serve", "drifting-adults", "kids-allergy-list", "top-givers" ];

function reporting( model ) {
    const rows = walkthrough
        .map( ( [ name, item, action ] ) => model.reportingAccess.find( a => a.name === name && ( a.category || a.dataView || a.report ) === item && a.action === action ) )
        .filter( Boolean );
    const reports = exampleReports.map( key => model.reports.find( r => r.key === key ) ).filter( Boolean );
    const categoryName = key => ( model.categories.find( c => c.key === key ) || {} ).name || key;
    return html`
<section class="section section-sand" id="data-views-and-reports">
  <div class="wrap">
    ${sectionHead( { eyebrow: "Data views and reports", title: "Built in layers, locked like a real church.", lede: html`${model.reportingDataViews.length} data views and ${model.reports.length} reports, filed by the Ministry by Design systems. A few foundations hold the shared rules, such as Active People and Adults. The stage views build on them from steps, and the next-step views, such as Ready to Serve, build on the stages. Every view and report has a run history, and one view has not run in over a year, so Rock's advice to remove unused data views has an example.` } )}
    <div class="split split-top">
      <div class="panel reveal">
        <p class="panel-title">Data view categories</p>
        <ul class="tree">${tree( model.categories, "church-data-views" )}</ul>
      </div>
      <div class="panel reveal">
        <p class="panel-title">Report categories</p>
        <ul class="tree">${tree( model.categories, "church-reports" )}</ul>
        <p class="fine">Rock's own Foundational Views category is renamed Foundational Views (Shipped), so the church's views stand out.</p>
      </div>
    </div>
  </div>
</section>

<section class="section" id="ministry-path">
  <div class="wrap">
    ${sectionHead( { eyebrow: "The path", title: "Every stage, counted every week.", lede: html`Each Ministry by Design stage is a data view built from steps. A person can be in more than one. Every Monday, ${model.metrics.length} Rock Metrics count the stages, with a value for each week since the stage view was made, so a dashboard shows the trend on install day.` } )}
    <div class="card-grid card-grid-four">
      ${model.metrics.map( m => {
        const stage = m.title.replace( / Stage$/, "" );
        const steps = model.stepTypes.filter( t => t.stage === stage ).map( t => t.name );
        return html`<div class="card reveal"><h3>${esc( m.title )}</h3><p>Reached by ${esc( steps.length > 2 ? `${steps.slice( 0, -1 ).join( ", " )}, or ${steps[steps.length - 1]}` : steps.join( " or " ) )}.</p></div>`;
      } )}
    </div>
  </div>
</section>

<section class="section section-sand" id="example-reports">
  <div class="wrap">
    ${sectionHead( { eyebrow: "Reports", title: "Reports a church would really run." } )}
    <div class="card-grid">
      ${reports.map( r => html`<div class="card reveal"><h3>${esc( r.name )}</h3><p>${esc( r.description )}</p><div class="tags"><span class="tag">${esc( categoryName( r.category ) )}</span></div></div>` )}
    </div>
  </div>
</section>

<section class="section" id="who-can-see-what">
  <div class="wrap split split-top">
    <div class="split-text reveal">
      <p class="eyebrow">Security</p>
      <h2>Who can see what.</h2>
      <p>The church's categories are locked to staff. Leadership is for the pastors, and Safety and Finance are for their own roles. A report runs with only the right to view it, so a report on a locked data view sits under the same lock. Rock's own security check answers every one of these, on every verified load.</p>
      <p class="feature-links"><a class="link-arrow" href="${doc( "docs/reporting.md" )}">Data views and reports</a></p>
    </div>
    <div class="panel reveal">
      <p class="panel-title">Ask the Cast</p>
      <ul class="access-list">
        ${rows.map( a => html`<li class="${a.allowed ? "can" : "cannot"}">${person( model, a.name )} <span>${a.allowed ? "can" : "cannot"} ${a.action === "Edit" ? "edit" : "see"} ${esc( itemName( model, a ) )}</span></li>` )}
      </ul>
    </div>
  </div>
</section>`;
}

module.exports = function features( { model } ) {
    const castDecker = "cast/decker.html";
    const flowNames = model.flows.map( f => f.name );
    const areas = [
        {
            id: "people", eyebrow: "People and families", title: "A church you can get to know.",
            text: html`The Cast is ${model.counts.households} households and ${model.counts.people} people, written by hand, at the main campus. Full 7 adds the Crowd, about ${model.datasets["full-7"].people.toLocaleString( "en-US" )} people across ${model.campuses.length} campuses.`,
            checks: [ "Families, single parents, and children shared between two homes", html`Businesses at every campus, each with one or two contacts`, "Duplicates to merge, and an allergy that prints on check-in labels" ],
            look: [ html`${person( model, "Jon Smith" )}, with two duplicate records to merge`, html`The <a href="${castDecker}">Decker family</a>, the household most demos open first`, html`Every household on <a href="cast.html">The Cast</a>` ],
            links: [ { label: "Meet the Cast", href: "cast.html" }, { label: "How the Crowd is made", href: doc( "docs/crowd.md" ) } ]
        },
        {
            id: "giving", eyebrow: "Giving and check images", title: "Gifts on each giver's rhythm.", sand: true,
            text: "Each giver has a pattern and a channel: scheduled, online, the Sunday offering, or a mailed check. Gifts land in batches by day, campus, and channel, and Rock's giving analytics are filled in as its nightly job would leave them.",
            checks: [ "Giving bins, journey stages, and a year of giving alerts", html`The ${esc( model.campaign.name )} building campaign, with pledges and how each is going`, "Scanned check fronts and backs that match each check gift, when you ask for them" ],
            look: [ html`The <a href="${castDecker}">Deckers</a>, monthly and consistent givers`, "Rock's Pledge Analytics page for the campaign", "A check batch, with each check's scan" ],
            links: [ { label: "Pledges and the campaign", href: doc( "docs/pledges.md" ) } ]
        },
        {
            id: "attendance", eyebrow: "Attendance and check-in", title: "Years of Sundays.",
            text: "Each household comes at its own rate, through holidays and summer. Kids check in to the room for their age or grade with a family security code, and students come on their own too.",
            checks: [ "Worship attendance for adults, and check-in for kids and students", html`The Kids Check-in streak and its ${require( path.join( model.root, "configuration/achievements.json" ) ).achievementTypes.length} achievements, from High Five to Century Club`, "Volunteers who check in to serve" ],
            look: [ "Next-gen check-in, which shows each child's progress", "Rock's attendance analytics for the Service Attendance group type", html`The <a href="${castDecker}">Decker kids</a>, regulars on Sunday` ],
            links: [ { label: "How it works", href: doc( "docs/how-it-works.md" ) } ]
        },
        {
            id: "groups-and-serving", eyebrow: "Groups and serving", title: "Groups that meet, and teams that serve.", sand: true,
            text: html`${model.counts.smallGroups} Cast small groups plus generated ones at every campus, with leaders, hosts, topics, photos, and a real church year. Every campus also has ${model.opportunities.length} serving teams, with rotations, preferences, and schedules six weeks ahead.`,
            checks: [ "Group Finder test cases at every campus", "Weekly group attendance, with holiday weeks marked \"did not meet\"", "Blackout dates, and volunteer check-in on Sundays" ],
            look: [ html`<a href="church.html#groups">The church's small groups</a>`, html`<a href="church.html#serving">The serving teams</a>`, "Rock's Group Scheduler for next Sunday" ],
            links: [ { label: "Small groups", href: doc( "docs/small-groups.md" ) }, { label: "Serving", href: doc( "docs/serving.md" ) } ]
        },
        {
            id: "steps", eyebrow: "Steps", title: "A path people really walk.",
            text: html`Rock's Discipleship Path, built out to seven steps, and a Student Path, ${model.stepTypes.length} step types in all. Every step comes from a record the data already has: a group, a team, a gift, a baptism, or a camp check-in.`,
            checks: [ "Classes in progress at every campus", "Camps and mission trips from student check-ins", "Program completions, as Rock's job records them" ],
            look: [ html`Ted Decker, who completed the Discipleship Path, in the <a href="${castDecker}">Decker family</a>`, "The Step Program page, which charts steps by month", "The Student Path's Follow Jesus step after camp" ],
            links: [ { label: "Steps", href: doc( "docs/steps.md" ) } ]
        },
        {
            id: "connections", eyebrow: "Connections", title: "Requests to serve, and the stories behind them.", sand: true,
            text: "A connected request sits behind every recent volunteer, with its activity history. New requests wait to be connected, and due dates run by status, so every board has requests due later, due this week, and past due.",
            checks: [ "Team leaders connect their teams' requests", "Celebrations: the story a connector records, which the Celebrations Report lists", "Active and inactive requests in a real mix" ],
            look: [ html`${person( model, "Alisha Marble" )}'s requests in the Connections Hub, when you sign in as <code>admin</code>`, "Rock's Celebrations Report", "A request's activity history" ],
            links: [ { label: "Requests to serve", href: doc( "docs/serving.md#requests-to-serve" ) } ]
        },
        {
            id: "notes", eyebrow: "Notes", title: "What staff write down.",
            text: html`Six note types with View security, ${model.counts.notes} hand-written Cast notes, and generated notes in the larger datasets, with replies, alerts, private notes, and mentions of other staff.`,
            checks: [ "Note types locked to the staff who should read them", "Replies from other staff", "Alerts, pinned, and private notes" ],
            look: [ html`The <a href="${castDecker}">Deckers'</a> profiles`, "A pastoral note, signed in as a staff member", "A note with a mention of another staff member" ],
            links: [ { label: "Notes setup", href: doc( "configuration/README.md#notes" ) } ]
        },
        {
            id: "prayer", eyebrow: "Prayer", title: "Requests in every state.", sand: true,
            text: html`Requests come in mostly from involved adults, and are approved, prayed for, and answered. The prayer team's prayers are recorded the way Rock's prayer session records them. The Cast has ${( model.prayerRequests ? model.prayerRequests.requests.length : 0 )} of its own requests, tied to their stories.`,
            checks: [ "Requests waiting for approval, active, expired, and answered", "Prayed interactions, comments, and replies", "A few flagged back to the approval queue" ],
            look: [ "The Prayer Request List", html`${person( model, "Cindy Decker" )}, on the prayer team` ],
            links: [ { label: "How it works", href: doc( "docs/how-it-works.md" ) } ]
        },
        {
            id: "communications", eyebrow: "Communications", title: "Two years of messages.",
            text: html`Texting numbers, ${model.communicationLists.length} communication lists, and a branded email template. Then two years of newsletters and ministry emails with opens, clicks, bounces, and unsubscribes, texts with live conversations, one-to-one emails, and the church's app with its pushes. Nothing ever sends at install.`,
            checks: [ "Who is on each list follows how involved they are", "Texted STOPs and unsubscribes that follow Rock's rules", "The Cast's hand-written text threads and staff emails" ],
            look: [ "Rock's Communication History for any adult", "The Unsubscribe and Saturation reports", "The SMS Conversations page on a staff member's number" ],
            links: [ { label: "Communications", href: doc( "docs/communications.md" ) } ]
        },
        {
            id: "communication-flows", eyebrow: "Communication flows", title: "Follow-up that runs on its own.", sand: true,
            text: html`${flowNames.length ? html`${flowNames.length} communication ${flowNames.length === 1 ? "flow" : "flows"}: ${esc( flowNames.length > 1 ? `${flowNames.slice( 0, -1 ).join( ", " )}, and ${flowNames[flowNames.length - 1]}` : flowNames[0] )}. ` : ""}Each serves a Ministry by Design stage. Some follow up after a step or a form, and some run on a schedule to a data view. Each has its messages, its goal, and the people who met it, and runs still going on install day keep sending, only to reserved addresses.`,
            checks: [ "Flows filed by Ministry by Design stage", "Each flow's messages, sent on their days", "Conversions, when someone meets the flow's goal", "On-Demand, recurring, and paused flows", "A recurring flow's audience matches its Rock data view" ],
            look: [ "Rock's Communication Flows page", "A flow's instances and their messages", "The Baptism Interest workflow, which starts Baptism Next Steps" ],
            links: [ { label: "Communication flows", href: doc( "docs/communications.md#communication-flows" ) } ]
        },
        {
            id: "calendar", eyebrow: "Calendar and event images", title: "A year of events, each with its artwork.",
            text: html`${model.calendarEvents.length} events on Rock's Public and Internal calendars, ${model.calendarEvents.filter( e => e.photo ).length} with their own artwork, at every campus the dataset has. Each serves a Ministry by Design stage, and Easter, the baptism Sundays, and camp fall on the same days everywhere.`,
            checks: [ "Occurrences from the history window to 13 months ahead", "Audiences, contacts, and places", "Registration links on the events that take them" ],
            look: [ html`<a href="church.html#calendar">The church's calendar</a>`, "Rock's Calendars page and each event's details" ],
            links: [ { label: "Calendar", href: doc( "docs/calendar.md" ) } ]
        },
        {
            id: "registrations", eyebrow: "Event registrations", title: "Registrations you can try any day.", sand: true,
            text: html`${model.registrations.templates.length} kinds of registration, from ${esc( model.registrations.templates[0] ? model.registrations.templates[0].name : "camp" )} to ${esc( model.registrations.templates.length ? model.registrations.templates[model.registrations.templates.length - 1].name : "retreats" )}, linked to their calendar events. Some are always open, so every feature can be tried on any install day.`,
            checks: [ "Fees, discounts, payment plans, and wait lists", "Placements: buses, cabins, crews, and rooms", "Signed releases with Rock's verification hash" ],
            look: [ html`<a href="church.html#registrations">The church's registrations</a>`, "Last summer's camp and its placements" ],
            links: [ { label: "Event registrations", href: doc( "docs/registrations.md" ) } ]
        },
        {
            id: "staff", eyebrow: "Staff and the org chart", title: "A staff that reads like a real one.",
            text: html`${model.staff.length} staff with titles, departments, supervisors, bios, and photos, and ${model.orgUnits.length} units on Rock's org chart. Every campus has a campus pastor.`,
            checks: [ "The Staff attributes on each staff profile", "Rock's Org Chart page, unit by unit", "Campus staff on each campus" ],
            look: [ html`<a href="church.html#staff">The church's staff</a>`, html`${person( model, "Pete Foster" )}, the Senior Pastor` ],
            links: [ { label: "Meet the staff", href: "church.html#staff" } ]
        },
        {
            id: "logins", eyebrow: "Logins", title: "Sign in as someone you know.", sand: true,
            text: html`${model.counts.logins} Cast people can sign in, with the one password you choose at install. In the larger datasets, many adults can too, as in a real church: staff, leaders, volunteers, online givers, and app users. Each login looks made and used.`,
            checks: [ "admin signs in as Alisha Marble, the Rock administrator", "Staff logins for testing staff screens", "Account protection set the way Rock sets it" ],
            look: [ html`<a href="datasets.html#sign-in">The logins to start with</a>`, "Every Cast person's username, on their Cast page" ],
            links: [ { label: "Sign in", href: "datasets.html#sign-in" } ]
        },
        {
            id: "group-sync", eyebrow: "Group sync", title: "Lists that keep themselves current.",
            text: html`${model.syncDataViews.length} data views keep the Volunteers and Small Group Leaders lists, the New Here and We Miss You care lists, and a prayer security role current through Rock's Group Sync job. Each group is filled the way the job would leave it, so its first run changes nobody.`,
            checks: [ "New Here and We Miss You are built on the foundation views", "Welcome and exit emails only for people who join or leave later" ],
            look: [ html`${person( model, "Luis Ortiz" )} and ${person( model, "Teresa Ortiz" )} on We Miss You`, html`${person( model, "Jordan Ellis" )} and ${person( model, "Hannah Ellis" )} on New Here` ],
            links: [ { label: "Group sync", href: doc( "docs/communications.md#group-sync" ) } ]
        }
    ];

    return {
        file: "features.html",
        active: "features",
        title: "Features",
        description: "What the Rock sample data fills in, feature by feature: people, giving, check-in, groups, steps, communications, registrations, data views and reports, and more.",
        body: html`
<section class="page-hero">
  <div class="wrap narrow">
    <p class="eyebrow">Features</p>
    <h1 class="display">Every part of Rock, <em>filled in.</em></h1>
    <p class="lede">Each section says what the sample data puts in Rock, where to look, and where to read more. Every dataset fills every feature, so Core 1 shows all of it.</p>
  </div>
</section>

<section class="section section-tight">
  <div class="wrap">
    <nav class="feature-index" aria-label="Features on this page">
      ${areas.map( a => html`<a href="#${a.id}">${esc( a.eyebrow )}</a>` )}
      <a href="#data-views-and-reports">Data views and reports</a>
    </nav>
  </div>
</section>
${areas.map( area )}
${reporting( model )}
`
    };
};
