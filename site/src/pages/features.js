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
const { person } = require( "../components" );

const doc = file => `${repoUrl}/blob/main/${file}`;

/**
 * Names joined as a sentence list: "A and B", or "A, B, and C".
 */
const messageList = items => items.length < 3 ? items.join( " and " ) : `${items.slice( 0, -1 ).join( ", " )}, and ${items[ items.length - 1 ]}`;

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

module.exports = function features( { model } ) {
    const castDecker = "cast/decker.html";
    const flowNames = model.flows.map( f => f.name );
    const signals = require( path.join( model.root, "configuration/signals.json" ) );
    const personalLinks = require( path.join( model.root, "configuration/personal-links.json" ) );
    const tags = require( path.join( model.root, "configuration/tags.json" ) ).tags;
    const tagCategories = model.categories.filter( c => c.entityType === "Rock.Model.Tag" );
    const homebound = tags.find( t => t.key === "homebound" );

    // The staff follow-up tasks steps start (configuration/step-workflows.json). A Cast story's task belongs to its
    // campus's pastor, the campus leader in Rock, or to the named staff member.
    const stepWorkflows = require( path.join( model.root, "configuration/step-workflows.json" ) );
    const castMember = guid => model.people.get( String( guid || "" ).toUpperCase() );
    const stepWorkflowStories = stepWorkflows.cast.map( story => {
        const workflow = stepWorkflows.workflows.find( w => w.key === story.workflow );
        const member = castMember( story.person );
        const owner = workflow.assignee.campusLeader ? member && member.household.campus.pastor : castMember( workflow.assignee.staff );
        const ownerName = owner ? person( model, owner.displayName ) : "its assignee";
        const login = owner && owner.logins && owner.logins.length ? html`, signed in as <code>${esc( owner.logins[0] )}</code>` : "";
        return story.expect === "open"
            ? html`${person( model, story.personName )}'s ${esc( workflow.name )}, open in ${ownerName}'s My Workflows${login}`
            : html`${person( model, story.personName )}'s ${esc( workflow.name )}, completed by ${ownerName}`;
    } );

    const backgroundChecks = require( path.join( model.root, "configuration/background-checks.json" ) );
    const benevolence = require( path.join( model.root, "configuration/benevolence.json" ) );
    const areas = [
        {
            id: "people", eyebrow: "People and families", title: "A church you can get to know.",
            text: html`The Cast is ${model.counts.households} households and ${model.counts.people} people, written by hand, at the main campus. Full 7 adds the Crowd, about ${model.datasets["full-7"].people.toLocaleString( "en-US" )} people across ${model.campuses.length} campuses. Every family's phone numbers are its own, so a check-in search or a texted STOP finds only them.`,
            checks: [ "Families, single parents, and children shared between two homes", "Crowd names from real name data for each birth decade, and nicknames such as Bob for Robert", html`Businesses at every campus, each with one or two contacts`, "Duplicates in Rock's Duplicate Finder on day one, with a waiting merge request, and an allergy that prints on check-in labels", "Employers, schools, visit information, and the day each engaged adult first joined a group, served, or gave", "Mapped addresses, each marked as checked, so none ever goes to an address service", "Every campus with its exterior photo on its Location, for Lava pages and the app" ],
            look: [ html`${person( model, "Jon Smith" )}, with two duplicate records and a merge request`, html`${person( model, "Sarah Simmons" )}'s History, where her old Sarah Jones record was merged`, html`The <a href="${castDecker}">Decker family</a>, the household most demos open first`, html`Every household on <a href="cast.html">The Cast</a>` ],
            links: [ { label: "Meet the Cast", href: "cast.html" }, { label: "How the Crowd is made", href: doc( "docs/crowd.md" ) }, { label: "Duplicates", href: doc( "docs/duplicates.md" ) } ]
        },
        {
            id: "giving", eyebrow: "Giving and check images", title: "Gifts on each giver's rhythm.", sand: true,
            text: "Each giver has a pattern and a channel: scheduled, online, the Sunday offering, or a mailed check. Gifts land in batches by day, campus, and channel, and Rock's giving analytics are filled in as its nightly job would leave them.",
            checks: [ "Giving bins, journey stages, and a year of giving alerts", html`The ${esc( model.campaign.name )} building campaign, with pledges and how each is going`, "Refunds at a real church's rate, returned checks among them, each with Rock's reason, in a refund batch", "Stock, vehicles, and equipment given in kind, each with Rock's asset type and a description for the statement", "Scanned check fronts and backs that match each check gift, when you ask for them" ],
            look: [ html`The <a href="${castDecker}">Deckers</a>, monthly and consistent givers`, html`${person( model, "Gabe Hernandez" )}'s duplicate gift, refunded the next day`, html`<a href="cast/javelina-wash-coffee.html">Javelina Wash Coffee</a>'s check that came back for insufficient funds`, html`The <a href="cast/price.html">Prices</a>' stock gift, under Non-Cash Gifts on their statement`, "Rock's Pledge Analytics page for the campaign", "A check batch, with each check's scan" ],
            links: [ { label: "Pledges and the campaign", href: doc( "docs/pledges.md" ) }, { label: "Refunds", href: doc( "docs/refunds.md" ) }, { label: "Uncommon gifts", href: doc( "docs/uncommon-gifts.md" ) } ]
        },
        {
            id: "attendance", eyebrow: "Attendance and check-in", title: "Years of Sundays.",
            text: "Each household comes at its own rate, through holidays and summer. Kids check in at their campus's kiosks to the room for their age or grade, with a family security code, and the adult who brought them checks them out after the service. Students come on their own too, on Sundays and at Wednesday Student Night. Every row has the check-in status Rock gives it, so check-in screens show who was in each room.",
            checks: [ "Worship attendance for adults, and check-in for kids and students", "Allergies, pickup notes, and nursery ability levels that check-in reads", html`The Kids Check-in streak and its ${require( path.join( model.root, "configuration/achievements.json" ) ).achievementTypes.length} achievements, from High Five to Century Club`, "Volunteers who check in to serve", "Kids, student, and volunteer kiosks at every campus, each with a label printer at an address no real printer has", "A check-in session for every check-in, found by the last four digits of a phone", "Kids checked out after their service by the adult who brought them", "Wednesday Student Night for grades 7 to 12", "eRA, Rock's regular attender status, with years of eRA steps and history, as its weekly job would leave it" ],
            look: [ "Next-gen check-in, which shows each child's progress", "Rock's attendance analytics for the Service Attendance group type", html`The <a href="${castDecker}">Decker kids</a>, regulars on Sunday, who always check in at Phoenix Kids 1`, html`${person( model, "Grace Fischer" )} at Student Night most Wednesdays`, html`The eRA badge on the <a href="${castDecker}">Deckers</a>, eRAs for years` ],
            links: [ { label: "How it works", href: doc( "docs/how-it-works.md" ) } ]
        },
        {
            id: "groups-and-serving", eyebrow: "Groups and serving", title: "Groups that meet, and teams that serve.", sand: true,
            text: html`${model.counts.smallGroups} Cast small groups plus generated ones at every campus, with leaders, hosts, topics, photos, and a real church year. Every campus also has ${model.opportunities.length} serving teams, with rotations, preferences, and schedules six weeks ahead.`,
            checks: [ "Group Finder test cases at every campus", "Women's Bible studies led by women, and men's Bible studies", "New groups on launch Sundays, most led by someone who was Ready to Lead", "Members who join after a family starts coming and leave after it stops", "Weekly group attendance, with holiday weeks marked \"did not meet\"", "Blackout dates, and volunteer check-in on Sundays and at Wednesday Student Night", "Peer networks from shared groups, teams, and follows, with Rock's trend arrows" ],
            look: [ html`<a href="church.html#groups">The church's small groups</a>`, html`<a href="church.html#serving">The serving teams</a>`, "Rock's Group Scheduler for next Sunday", html`Ted Decker's Peer Network, in the <a href="${castDecker}">Decker family</a>` ],
            links: [ { label: "Small groups", href: doc( "docs/small-groups.md" ) }, { label: "Serving", href: doc( "docs/serving.md" ) } ]
        },
        {
            id: "steps", eyebrow: "Steps", title: "A path people really walk.",
            text: html`Rock's Discipleship Path, built out to seven steps, and a Student Path, ${model.stepTypes.length} step types in all. Every step comes from a record the data already has: a group, a team, a gift, a baptism, or a camp check-in. ${stepWorkflows.workflows.length} step workflows give staff their follow-up tasks. A baptism starts one for its campus pastor, a decision to follow Jesus starts one for the Student Pastor, and staff start a volunteer check-in by hand from a Serve step. Tasks from the last ${stepWorkflows.openDays} days wait in My Workflows, and staff completed the older ones.`,
            checks: [ "Classes in progress at every campus", "Camps and mission trips from student check-ins", "Program completions, as Rock's job records them", "Follow-up tasks after baptisms and decisions, open and completed, that Rock's own workflow jobs leave as they are" ],
            look: [ html`Ted Decker, who completed the Discipleship Path, in the <a href="${castDecker}">Decker family</a>`, "The Step Program page, which charts steps by month", "The Student Path's Follow Jesus step after camp", ...stepWorkflowStories ],
            links: [ { label: "Steps", href: doc( "docs/steps.md" ) }, { label: "Step workflows", href: doc( "docs/steps.md#step-workflows" ) } ]
        },
        {
            id: "connections", eyebrow: "Connections", title: "Requests to serve, and the stories behind them.", sand: true,
            text: "A connected request sits behind every recent volunteer, with its activity history. New requests wait to be connected, and due dates run by status, so every board has requests due later, due this week, and past due.",
            checks: [ "Team leaders connect their teams' requests", "Celebrations: the story a connector records, which the Celebrations Report lists", "Active and inactive requests in a real mix" ],
            look: [ html`${person( model, "Alisha Marble" )}'s requests in the Connections Hub, when you sign in as <code>admin</code>`, "Rock's Celebrations Report", "A request's activity history" ],
            links: [ { label: "Requests to serve", href: doc( "docs/serving.md#requests-to-serve" ) } ]
        },
        {
            id: "background-checks", eyebrow: "Background checks", title: "Who is cleared to lead.",
            text: html`Every small group leader needs a current background check, and so does every adult on the kids and student teams. Rock shows who meets it: ${backgroundChecks.leaders.mix.current}% of generated leaders are current, and the rest expire soon, have expired, or were never checked. Each check is saved the way Rock's Checkr provider saves it, with its report as a PDF.`,
            checks: [ "Requirement results that Rock's own job leaves unchanged", "Checks in progress for requests to serve that wait on one", "A Leader Background Checks report, locked to the safety team" ],
            look: [ html`${person( model, "Ted Decker" )}, whose only check is too old to count`, html`${person( model, "Bill Marble" )}, whose check runs out in about five weeks`, html`${person( model, "Frank Dexter" )}'s failed check, linked to his request to serve` ],
            links: [ { label: "How background checks load", href: doc( "loader/README.md#how-background-checks-load" ) }, { label: "Serving", href: doc( "docs/serving.md" ) } ]
        },
        {
            id: "benevolence", eyebrow: "Benevolence", title: "Help when someone asks.", sand: true,
            text: html`People ask the church for help with rent, bills, food, cars, and medical costs. ${esc( benevolence.caseWorker.name )}, the Care Director, works each request, and the team records what it gave. ${benevolence.types.length - 1} church types join Rock's own, and every one is locked to the benevolence team.`,
            checks: [ "Requests that are Approved, Denied, and Pending", "Help paid straight to a landlord, a utility, a shop, or a pharmacy", "Signed assistance agreements as PDFs", "A workflow that gives each new request to the case worker and sends no email" ],
            look: [ html`${person( model, "Ashley Kelly" )}'s rent help, which matches her notes`, "Rock's Benevolence page under Finance, signed in as admin", html`${person( model, "Dee Navarro" )}, the case worker` ],
            links: [ { label: "How benevolence loads", href: doc( "loader/README.md#how-benevolence-loads" ) } ]
        },
        {
            id: "notes", eyebrow: "Notes", title: "What staff write down.",
            text: html`Six note types with View security, ${model.counts.notes} hand-written Cast notes, and generated notes in the larger datasets, with replies, alerts, private notes, and mentions of other staff. A mention in a recent note waits in that staff member's bell, as Rock's notification, and opens the note.`,
            checks: [ "Note types locked to the staff who should read them", "Replies from other staff", "Alerts, pinned, and private notes", "Mentions from the last few days still unread in the bell" ],
            look: [ html`The <a href="${castDecker}">Deckers'</a> profiles`, "A pastoral note, signed in as a staff member", "A note with a mention of another staff member", html`The bell in the top bar, signed in as <code>admin</code>, with ${person( model, "Alisha Marble" )}'s unread mentions` ],
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
            text: html`Texting numbers, ${model.communicationLists.length} communication lists, SMS snippets staff reply with, and a branded email template, ready in Rock's email wizard with sections that match it. Then two years of newsletters and ministry emails with opens, clicks, bounces, and unsubscribes, texts with live conversations, one-to-one emails, and the church's app with its pushes. Nothing ever sends at install.`,
            checks: [ "Who is on each list follows how involved they are", "Texted STOPs and unsubscribes that follow Rock's rules", "The Cast's hand-written text threads and staff emails", "Shared SMS snippets, and personal ones only their owner sees" ],
            look: [ "Rock's Communication History for any adult", "The Unsubscribe and Saturation reports", "The SMS Conversations page on a staff member's number", "The snippet picker in SMS Conversations", "The Sections panel in Rock's email editor" ],
            links: [ { label: "Communications", href: doc( "docs/communications.md" ) } ]
        },
        {
            id: "communication-flows", eyebrow: "Communication flows", title: "Follow-up that runs on its own.", sand: true,
            text: html`${flowNames.length ? html`${flowNames.length} communication ${flowNames.length === 1 ? "flow" : "flows"}: ${esc( flowNames.length > 1 ? `${flowNames.slice( 0, -1 ).join( ", " )}, and ${flowNames[flowNames.length - 1]}` : flowNames[0] )}. ` : ""}Each serves a Ministry by Design stage. Some follow up after a step or a form, some run on a schedule to a data view, and one ran once, after Commitment Sunday. Each has its messages, its goal, and the people who met it, and runs still going on install day keep sending, only to reserved addresses. One Christmas Eve email waits to send on December 23, also only to reserved addresses.`,
            checks: [ "Flows filed by Ministry by Design stage", "Each flow's messages, sent on their days", "Conversions, when someone meets the flow's goal", "On-Demand, recurring, one-time, and paused flows", "An email scheduled to send after install", "A recurring flow's audience matches its Rock data view" ],
            look: [ "Rock's Communication Flows page", "A flow's instances and their messages", "The Baptism Interest workflow, which starts Baptism Next Steps", "The Christmas Eve email, scheduled in the Communication List" ],
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
        },
        {
            id: "adaptive-messages", eyebrow: "Adaptive messages", title: "A next step for each person.", sand: true,
            text: html`${model.adaptiveMessages.messages.length} adaptive messages, in this order on the homepage: ${esc( messageList( model.adaptiveMessages.messages.map( m => m.name ) ) )}. Each has versions for different people, on ${model.adaptiveMessages.segments.length} personalization segments built on the church's own data views. Rock's adaptivemessage Lava command shows each person the first version that fits, so two people on different stages see different invitations in the same spot. Each segment holds the people Rock's job would put in it on install day.`,
            checks: [ "A giving message with a layer for each stage of Rock's giving journey", "Leaders of a group or team asked to invite a friend", "Segments on Finance views locked like Finance", "Seasonal versions for Baptism Sunday and Student Camp", "A card for everyone in each message's season", "People who stopped coming hear first that we miss them" ],
            look: [ "The External Website homepage, signed in as a Cast person", "Rock's Adaptive Messages and Personalization Segments pages", html`${person( model, "Ted Decker" )}, thanked for his scheduled gift and asked to invite a friend`, html`${person( model, "Margaret Carter" )}, invited to make her monthly check automatic`, html`${person( model, "Luis Ortiz" )}, told we miss him before any growth ask` ],
            links: [ { label: "Adaptive messages", href: doc( "docs/adaptive-messages.md" ) } ]
        },
        {
            id: "signals", eyebrow: "Person signals", title: "Flags that ask staff to act.",
            text: html`${signals.types.length} signal types, ${esc( signals.types.map( t => t.name ).slice( 0, -1 ).join( ", " ) )}, and ${esc( signals.types[signals.types.length - 1].name )}, each locked to the roles that act on it. ${signals.cast.length} Cast people carry one tied to their story, and in the larger datasets each campus pastor follows up with ${signals.crowd.perCampus} people, most of them done. The Top Person Signal badge shows each person's flag, and an ended signal shows none.`,
            checks: [ "The Top Person Signal badge on the profile", "Notes and end dates that only the right roles can read", "A Safety Team Review data view in the Safety category" ],
            look: [ html`${person( model, "Frank Dexter" )}, waiting on a Safety Team Review`, html`${person( model, "Maddie Lowe" )}, with Pickup Instructions for check-in`, html`${person( model, "Derek Ward" )}, whose follow-up has ended, so no flag shows` ],
            links: [ { label: "How signals load", href: doc( "loader/README.md#how-signals-load" ) } ]
        },
        {
            id: "personal-links", eyebrow: "Personal links", title: "A bookmark menu that opens full.", sand: true,
            text: html`The bookmarks behind the icon beside Rock's search bar: ${personalLinks.sharedSections.length} shared sections, ${esc( personalLinks.sharedSections.map( s => s.name ).slice( 0, -1 ).join( ", " ) )}, and ${esc( personalLinks.sharedSections[personalLinks.sharedSections.length - 1].name )}, each locked to the roles that use it, and personal sections for ${personalLinks.personalSections.length} staff and every campus pastor. Every link opens a Rock page with sample data on it, and a link to a part you left out is skipped.`,
            checks: [ "Shared sections locked by role", "Personal sections in each owner's own order", "Links only to Rock pages" ],
            look: [ html`${person( model, "Pete Foster" )}'s Leadership section, signed in as <code>pfoster</code>`, html`${person( model, "Alisha Marble" )}'s Office section, signed in as <code>admin</code>`, "A campus pastor's My Campus section" ],
            links: [ { label: "How personal links load", href: doc( "loader/README.md#how-personal-links-load" ) } ]
        },
        {
            id: "tags", eyebrow: "Tags", title: "Labels staff really use.",
            text: html`Tags are Rock's quick labels on a person. ${tags.filter( t => t.category ).length} organizational tags sit in ${tagCategories.length} tag categories, ${esc( new Intl.ListFormat( "en", { type: "conjunction" } ).format( tagCategories.map( c => c.name ) ) )}, each locked to the roles that should use it. Staff keep personal tags that only they see, and Rock's own Staff tag holds every staff member.`,
            checks: [ "Organizational tags locked to the roles that may see and use them", "Personal tags, such as each campus pastor's Coffee This Month", homebound && homebound.draw ? html`In the larger datasets, Homebound on ${homebound.draw.percent}% of generated adults ${homebound.draw.minAge} and older who live alone` : "", "Data views in Staff Workspace built on tags, each made by the staff member who uses it" ].filter( Boolean ),
            look: [ html`${person( model, "Hal Russell" )}, on Homebound`, html`${person( model, "Jamal Pierce" )}'s personal Possible Group Leaders tag, signed in as him`, "Rock's Tag Report, and the tag bar on each profile" ],
            links: [ { label: "Tags setup", href: doc( "configuration/README.md#tags" ) } ]
        }
    ];

    // The staff homepage follows tags, which are plain, so it is on sand and the sections keep alternating.
    const staffHomepage = require( path.join( model.root, "configuration/staff-homepage.json" ) );
    areas.push( {
        id: "staff-homepage", eyebrow: "Staff homepage", title: "A homepage staff really use.", sand: true,
        text: html`Rock's staff homepage has a staff update for each Monday of the past ${staffHomepage.updates.weeks} weeks, from the Director of Communications, with articles from the staff. They follow the church calendar: Easter planning, VBS volunteers, Commitment Sunday, and year-end statements. The metric tiles show a year of history, and the Quick Links point at the pages staff open most.`,
        checks: [ "A feature and one to three staff articles every week", "Event images and staff photos, each stored once", "Active Records, Active Families, and Active Connection Requests, with the Ministry Path metrics beside them" ],
        look: [ "Rock's homepage, signed in as any staff member", "The Prev button, which steps back a week at a time", "The Internal Communications - Homepage content channel" ],
        links: [ { label: "Staff homepage", href: doc( "docs/staff-homepage.md" ) } ]
    } );

    // Data views and reports follow the staff homepage, which is on sand. The whole story is on the Reporting page.
    const lockedReportCategories = model.categories
        .filter( c => c.parent === "church-reports" && ( c.security || [] ).some( r => r.action === "View" ) )
        .map( c => c.name );
    const unusedViews = model.reportingDataViews.filter( v => v.usage && v.usage.lastRunDaysAgo > 365 ).length;
    const reportingArea = {
        id: "data-views-and-reports", eyebrow: "Data views and reports", title: "Built in layers, locked like a real church.",
        text: html`${model.reportingDataViews.length} data views and ${model.reports.length} reports, filed by the Ministry by Design systems. Foundations hold the shared rules, stage views build on them from steps, and next-step views such as Ready to Serve build on the stages. A Rock Metric counts each stage every week.`,
        checks: [ "Foundations, stage views, and next-step views that build on each other", html`${esc( messageList( lockedReportCategories ) )}, each locked to its own roles`, "A weekly metric for each stage, with values back to when its view was made", `A run history on every view and report${unusedViews ? `, with ${unusedViews === 1 ? "one view" : `${unusedViews} views`} not run in over a year` : ""}` ],
        look: [ "Rock's Data Views and Reports pages, under Tools", "The Ministry Path metrics on Rock's staff homepage", html`<a href="reporting.html#sign-in-as">The Cast people to sign in as</a>, and what each can see` ],
        links: [ { label: "The Reporting page", href: "reporting.html" }, { label: "Data views and reports", href: doc( "docs/reporting.md" ) } ]
    };

    // Assessments come after the data views and reports, which are plain, so the page keeps alternating.
    const assessments = require( path.join( model.root, "configuration/assessments.json" ) );
    const startingPoint = assessments.classes.find( c => c.stepType === "starting-point-class" );
    const assessmentsArea = {
        id: "assessments", eyebrow: "Assessments", title: "Results with real answers behind them.", sand: true,
        text: html`Rock's ${assessments.types.length} assessments: DISC, Spiritual Gifts, Conflict Profile, Emotional Intelligence, and Motivators. Most staff took all of them, every leader took DISC and Conflict Profile before leading, and each Starting Point class asks for Spiritual Gifts, which ${startingPoint.percentFinished}% finish. Every result comes from a full set of answers, scored the way Rock scores them.`,
        checks: [ "Requests still pending from the current classes, with reminders where Rock's job would leave them", "Answers and results that always agree, checked against Rock's own scoring", "Results as person attributes, ready for data views" ],
        look: [ html`The Assessments badge on ${person( model, "Ted Decker" )}'s profile`, html`${person( model, "Bri Hall" )}'s Spiritual Gifts, which fit a greeter`, html`${person( model, "Ty McClintock" )} and ${person( model, "Paul Smith" )}'s DISC results, side by side` ],
        links: [ { label: "Assessments", href: doc( "docs/assessments.md" ) } ]
    };

    // Reminders come after assessments, which are on sand, so the page keeps alternating.
    const reminders = require( path.join( model.root, "configuration/reminders.json" ) );
    const shows = { due: "due now", upcoming: "coming up", complete: "complete" };
    const remindersArea = {
        id: "reminders", eyebrow: "Reminders", title: "Follow-ups staff set for themselves.",
        text: html`Staff set reminders about the people they wrote notes on, and small group leaders set them about their groups, in Rock's own Personal and Group reminder types. Each is dated from the install day, so some are due, some are coming up, some are complete, and a few repeat. The Cast has ${reminders.cast.length} of its own, tied to their stories. Rock's bell counts each person's reminders due, as Rock's Process Reminders job would.`,
        checks: [ "Reminders about people and about groups", "Due, upcoming, complete, and repeating reminders on any install day", "A bell count that Rock's own job leaves as it is" ],
        look: [ html`The bell in Rock's top bar, signed in as <code>tdecker</code>`, ...reminders.cast.map( r => html`${person( model, r.ownerName )}'s reminder about ${r.group ? `the ${esc( r.aboutName )}` : person( model, r.aboutName )}, ${esc( shows[r.expect] || r.expect )}` ) ],
        links: [ { label: "How reminders load", href: doc( "loader/README.md#how-reminders-load" ) } ]
    };

    const sections = [ ...areas, reportingArea, assessmentsArea, remindersArea ];

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
      ${sections.map( a => html`<a href="#${a.id}">${esc( a.eyebrow )}</a>` )}
    </nav>
  </div>
</section>
${sections.map( area )}
`
    };
};
