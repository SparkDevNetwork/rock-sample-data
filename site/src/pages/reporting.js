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
    The Reporting page: the church's data views and reports in one place. It shows the layers with an example of
    each, the Ministry by Design stages and their metrics, every report with who can see it, the locked categories,
    and the Cast people to sign in as. Everything comes from configuration/reporting.json, categories.json,
    steps.json, security-roles.json, and the Cast, so nothing here goes stale. How many people each view finds in
    each dataset stays out: only verify receipts hold it, and the site never reads them.
*/

const path = require( "path" );

const { esc, html, repoUrl } = require( "../layout" );
const { sectionHead, person, signInName, portrait } = require( "../components" );

const doc = file => `${repoUrl}/blob/main/${file}`;
const all = items => new Intl.ListFormat( "en", { type: "conjunction" } ).format( items );
const either = items => new Intl.ListFormat( "en", { type: "disjunction" } ).format( items );

// The Ministry by Design systems, in path order. Each has a data view category "<system>-data-views" and a report
// category "<system>-reports" (docs/reporting.md section 8.1).
const systems = [ "notice", "belong", "equip", "contribute", "lead", "reengage" ];

// The report the layer examples follow, from the report down to its foundations.
const exampleReport = "ready-to-serve";

// The order of the Cast cards, as in docs/reporting.md section 7. Anyone else with access rows comes after them.
const castOrder = [ "Alisha Marble", "Pete Foster", "Greg Holloway", "Priya Raman", "Cindy Decker", "Rachel Brooks", "Jamal Pierce", "Jenny Michaels", "Ted Decker", "Jerry Wright" ];

/**
 * A Rock role as people say it, without Rock's "RSR - " prefix.
 */
const roleName = role => role.replace( /^RSR - /, "" );

/**
 * The roles a security rule allows, as a sentence list.
 */
const roles = rule => all( rule.allow.map( roleName ) );

/**
 * The View rule Rock reaches first for a report, data view, or category: its own, or its nearest category's. Each
 * rule ends by denying All Users, so the first one found decides for everyone. Null when none is found.
 */
function viewRule( model, item ) {
    for ( let at = item; at; at = model.categories.find( c => c.key === ( at.parent || at.category ) ) ) {
        const rule = ( at.security || [] ).find( r => r.action === "View" );
        if ( rule ) {
            return { rule, at };
        }
    }

    return null;
}

/**
 * A category's name, or its key when no category has it.
 */
const categoryName = ( model, key ) => ( model.categories.find( c => c.key === key ) || {} ).name || key;

/**
 * Every value of one field on the leaves of a data view's filter, such as each "dataView" it points at, or each
 * "steps" choice of Rock's Steps Taken filter.
 */
function leaves( view, field ) {
    const found = [];
    const walk = node => {
        if ( node && typeof node === "object" ) {
            if ( node[field] ) {
                found.push( node[field] );
            }

            Object.values( node ).forEach( walk );
        }
    };
    walk( view.filter );
    return found;
}

/**
 * The roles a category's View rule allows, or what its Edit rule allows.
 */
function lockText( category ) {
    const rules = category.security || [];
    const view = rules.find( r => r.action === "View" );
    const edit = rules.find( r => r.action === "Edit" );
    if ( view ) {
        return `Locked to ${view.allow.map( roleName ).join( ", " )}`;
    }

    return edit ? `${edit.allow.map( roleName ).join( ", " )} can edit` : "";
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

/**
 * The four layers, each with one example, all from one report traced down to its foundations.
 */
function layers( model ) {
    const views = model.reportingDataViews;
    const view = key => views.find( v => v.key === key );
    const inCategory = key => views.filter( v => v.category === key );
    const report = model.reports.find( r => r.key === exampleReport );
    const nextStep = view( report.dataView );
    const parents = [ ...new Set( leaves( nextStep, "dataView" ) ) ].map( view ).filter( Boolean );
    const foundation = parents.find( v => v.category === "foundation-data-views" );
    const stage = parents.find( v => v.category === "path-data-views" );
    const foundations = inCategory( "foundation-data-views" );
    const pathViews = inCategory( "path-data-views" );
    const stageViews = pathViews.filter( v => model.metrics.some( m => m.dataView === v.key ) );
    const otherPathViews = pathViews.filter( v => !stageViews.includes( v ) );
    const systemViews = views.filter( v => systems.some( s => v.category === `${s}-data-views` ) );
    const layered = new Set( [ "foundation-data-views", "path-data-views", ...systems.map( s => `${s}-data-views` ) ] );
    const otherCategories = model.categories.filter( c => !layered.has( c.key ) && views.some( v => v.category === c.key ) ).map( c => c.name );
    const retired = views.filter( v => v.usage && v.usage.lastRunDaysAgo > 365 ).map( v => v.name );

    return html`
<section class="section" id="layers">
  <div class="wrap">
    ${sectionHead( { eyebrow: "The layers", title: "Each rule is written once.", lede: html`Rock's reporting guide says to build data views that extend each other, so a rule lives in one place. The church's views and reports follow it in four layers. The examples below are one chain, from the ${esc( report.name )} report down to its foundations.` } )}
    <ol class="steps reveal">
      <li><h3>Foundations: ${foundations.length} views</h3><p>Each holds one shared rule, such as who is active, who is an adult, or who serves. For example, <strong>${esc( foundation.name )}</strong>: ${esc( foundation.description )}</p></li>
      <li><h3>Stage views: ${stageViews.length} views</h3><p>The Ministry by Design stages, built from steps. A person can be in more than one stage. For example, <strong>${esc( stage.name )}</strong>: ${esc( stage.description )} ${otherPathViews.length ? html`The Path category also holds ${esc( all( otherPathViews.map( v => v.name ) ) )}.` : ""}</p></li>
      <li><h3>Next-step views: ${systemViews.length} views</h3><p>Filed by Ministry System. Most find the people in one stage who are ready for the next. For example, <strong>${esc( nextStep.name )}</strong>, in ${esc( categoryName( model, nextStep.category ) )}: ${esc( nextStep.description )} It builds on ${esc( all( parents.map( v => v.name ) ) )}.</p></li>
      <li><h3>Reports: ${model.reports.length} reports</h3><p>Each shows the columns a staff member needs for one question. Several reports can share one view. For example, <strong>${esc( report.name )}</strong>, on the ${esc( nextStep.name )} view: ${esc( report.description )} Its columns are ${esc( all( report.columns.map( c => c.header ) ) )}.</p></li>
    </ol>
    <p class="fine reveal">The other data views sit in ${esc( all( otherCategories ) )}. Every view and report has a run history${retired.length ? html`, and ${esc( all( retired ) )} has not run in over a year, so Rock's advice to remove unused data views has an example` : ""}.</p>
    <p class="feature-links"><a class="link-arrow" href="${doc( "docs/reporting.md#3-the-layers" )}">How the layers are built</a></p>
  </div>
</section>`;
}

/**
 * Each Ministry by Design stage: the steps its stage view counts, its weekly metric, and its report.
 */
function stages( model ) {
    const schedules = require( path.join( model.root, "configuration/reporting.json" ) ).namedSchedules || [];

    // Each "steps" choice names a program and either a stage or one step type, as the loader reads it.
    const stepsFor = choice => model.stepTypes.filter( t => t.program === choice.program && ( choice.stepType ? t.key === choice.stepType : t.stage === choice.stage ) );
    return html`
<section class="section section-sand" id="ministry-path">
  <div class="wrap">
    ${sectionHead( { eyebrow: "The path", title: "Every stage, counted every week.", lede: html`Each Ministry by Design stage is a data view built from steps. A step counts once it is complete, and a person can be in more than one stage. A Rock Metric counts each stage every week, with a value for each week since the stage view was made, so a dashboard shows the trend on install day.` } )}
    <div class="card-grid card-grid-four">
      ${model.metrics.map( m => {
        const stageView = model.reportingDataViews.find( v => v.key === m.dataView );
        const schedule = schedules.find( s => s.key === m.schedule );
        const report = model.reports.find( r => r.dataView === m.dataView );
        const reached = ( stageView ? leaves( stageView, "steps" ) : [] )
            .map( stepsFor )
            .filter( types => types.length )
            .map( types => ( { program: types[0].programName, steps: types.map( t => t.name ) } ) );
        const when = schedule ? ` ${schedule.name.charAt( 0 ).toLowerCase()}${schedule.name.slice( 1 )}` : "";
        return html`<div class="card reveal">
  <h3>${esc( m.title )}</h3>
  <p>Reached by a step:</p>
  <ul class="link-list look-list stage-steps">${reached.map( p => html`<li>${esc( p.program )}: ${esc( either( p.steps ) )}</li>` )}</ul>
  <p class="card-meta">The ${esc( m.title )} metric counts it${esc( when )}.${report ? html` Its report, ${esc( report.name )}, is in ${esc( categoryName( model, report.category ) )}.` : ""}</p>
</div>`;
      } )}
    </div>
    <p class="fine">Notice and Re-engage have no stage view. A first visit is not a step, and Re-engage runs beside the path for people who stopped coming.</p>
    <p class="feature-links"><a class="link-arrow" href="${doc( "docs/reporting.md#2-ministry-by-design" )}">Ministry by Design in the reports</a></p>
  </div>
</section>`;
}

/**
 * Every report, grouped by its category, with what it is for and who can see it.
 */
function reports( model ) {
    const views = model.reportingDataViews.concat( model.syncDataViews );
    const groups = model.categories
        .filter( c => c.parent === "church-reports" )
        .map( category => ( { category, reports: model.reports.filter( r => r.category === category.key ) } ) )
        .filter( g => g.reports.length );

    const card = report => {
        const seen = viewRule( model, report );
        const view = views.find( v => v.key === report.dataView );

        // A report runs for anyone who can see it, even when its data view is locked to other roles (docs/reporting.md
        // section 1.3). A view that takes its rule from the church's top category is open to all staff, so it is no lock.
        const viewSeen = view && viewRule( model, view );
        const isTopCategory = at => model.categories.includes( at ) && !at.parent;
        const viewLock = viewSeen && !isTopCategory( viewSeen.at ) ? viewSeen.rule : null;
        const isLockedApart = viewLock && seen && roles( viewLock ) !== roles( seen.rule );
        const hasOwnLock = ( report.security || [] ).some( r => r.action === "View" );
        return html`<div class="card reveal">
  <h3>${esc( report.name )}</h3>
  <p>${esc( report.description )}</p>
  <p class="card-meta">On the ${esc( view ? view.name : report.dataView )} data view. ${seen ? html`Seen by ${esc( roles( seen.rule ) )}${hasOwnLock ? ", by its own lock" : ""}.` : ""}</p>
  ${isLockedApart ? html`<p class="card-meta">Only ${esc( roles( viewLock ) )} can open its data view. Rock still runs the report for anyone who can see it.</p>` : ""}
</div>`;
    };

    return html`
<section class="section" id="reports">
  <div class="wrap">
    ${sectionHead( { eyebrow: "Reports", title: "Reports a church would really run.", lede: html`${model.reports.length} reports in ${groups.length} categories. Each next-step report names who to invite, and to what. A report takes its security from its category, unless it has its own.` } )}
    ${groups.map( g => {
        const isLocked = ( g.category.security || [] ).some( r => r.action === "View" );
        return html`<div class="report-group">
  <div class="report-group-head reveal"><h3>${esc( g.category.name )}</h3>${isLocked ? html`<span class="tag tag-accent">${esc( lockText( g.category ) )}</span>` : ""}</div>
  <p class="report-group-text reveal">${esc( g.category.description )}</p>
  <div class="card-grid">${g.reports.map( card )}</div>
</div>`;
    } )}
    <p class="feature-links"><a class="link-arrow" href="${doc( "docs/reporting.md#5-reports" )}">Every report's columns</a></p>
  </div>
</section>`;
}

/**
 * The church's top lock, the categories locked tighter, the one item lock, and both category trees.
 */
function locks( model ) {
    const reportRoot = model.categories.find( c => c.key === "church-reports" );
    const rootRule = viewRule( model, reportRoot );
    const locked = model.categories.filter( c => c.parent === reportRoot.key && ( c.security || [] ).some( r => r.action === "View" ) );
    const ownLocks = model.reports.filter( r => ( r.security || [] ).some( s => s.action === "View" ) );
    const workspace = model.categories.filter( c => c.parent === reportRoot.key && ( c.security || [] ).some( r => r.action === "Edit" ) && !c.security.some( r => r.action === "View" ) );
    const viewCategory = category => model.categories.find( c => c.key === category.key.replace( /-reports$/, "-data-views" ) );
    const sameRule = ( a, b ) => a && b && roles( a ) === roles( b );

    return html`
<section class="section section-sand" id="locks">
  <div class="wrap">
    ${sectionHead( { eyebrow: "Security", title: "Locked like a real church.", lede: html`Security sits on the categories, so a view or report made later is locked the day it is made. The church's categories let in only ${esc( rootRule ? roles( rootRule.rule ) : "staff" )}, and deny everyone else, so a person who cannot reach the staff site cannot open one from a link either. ${esc( all( locked.map( c => c.name ) ) )} are locked tighter.` } )}
    <div class="card-grid">
      ${locked.map( c => {
        const view = c.security.find( r => r.action === "View" );
        const edit = c.security.find( r => r.action === "Edit" );
        const viewTree = viewCategory( c );
        const locksViews = viewTree && sameRule( ( viewTree.security || [] ).find( r => r.action === "View" ), view );
        const reportCount = model.reports.filter( r => r.category === c.key ).length;
        const viewCount = locksViews ? model.reportingDataViews.filter( v => v.category === viewTree.key ).length : 0;
        return html`<div class="card reveal">
  <h3>${esc( c.name )}</h3>
  <p>Only ${esc( roles( view ) )} can see ${locksViews ? "its data views and reports" : "its reports"}. ${edit ? html`${esc( roles( edit ) )} can edit them.` : "Only Rock Administration can edit them."}</p>
  <p class="card-meta">${reportCount} ${reportCount === 1 ? "report" : "reports"}${locksViews ? html` and ${viewCount} data ${viewCount === 1 ? "view" : "views"}` : ", on views filed elsewhere"}.</p>
</div>`;
      } )}
    </div>
    ${ownLocks.map( r => {
        const rule = r.security.find( s => s.action === "View" );
        const view = model.reportingDataViews.find( v => v.key === r.dataView );
        const viewRuleOwn = view && ( view.security || [] ).find( s => s.action === "View" );
        const category = model.categories.find( c => c.key === r.category );
        return html`<p class="fine reveal">${esc( r.name )} has its own lock, narrower than ${esc( category ? category.name : r.category )}: only ${esc( roles( rule ) )} can see it.${sameRule( viewRuleOwn, rule ) ? " Its data view carries the same lock, since locking a view alone would not lock the report." : ""}</p>`;
    } )}
    ${workspace.map( c => html`<p class="fine reveal">${esc( c.name )} is where ${esc( roles( c.security.find( r => r.action === "Edit" ) ) )} build their own views and reports.</p>` )}
    <div class="split split-top">
      <div class="panel reveal">
        <p class="panel-title">Data view categories</p>
        <ul class="tree">${tree( model.categories, "church-data-views" )}</ul>
      </div>
      <div class="panel reveal">
        <p class="panel-title">Report categories</p>
        <ul class="tree">${tree( model.categories, "church-reports" )}</ul>
        <p class="fine">Rock's own Foundational Views category is renamed ${esc( ( model.categories.find( c => c.key === "shipped-foundational-views" ) || {} ).name || "Foundational Views (Shipped)" )}, so the church's views stand out.</p>
      </div>
    </div>
    <p class="feature-links"><a class="link-arrow" href="${doc( "docs/reporting.md#6-categories-and-their-security" )}">Categories and their security</a></p>
  </div>
</section>`;
}

/**
 * The Cast people to sign in as, each with the roles that reach reporting and what Rock lets them see.
 */
function castCards( model, r ) {
    const securityRoles = require( path.join( model.root, "configuration/security-roles.json" ) ).roles;
    const rockRole = new Map( securityRoles.map( s => [ s.name, s.rockRole ] ) );
    const securedItems = model.categories
        .filter( c => c.entityType === "Rock.Model.DataView" || c.entityType === "Rock.Model.Report" )
        .concat( model.reportingDataViews, model.reports );
    const reportingRoles = new Set( securedItems.flatMap( item => ( item.security || [] ).flatMap( rule => rule.allow ) ) );
    const rank = name => castOrder.includes( name ) ? castOrder.indexOf( name ) : castOrder.length;
    const names = [ ...new Set( model.reportingAccess.map( a => a.name ) ) ].sort( ( a, b ) => rank( a ) - rank( b ) );

    return html`
<section class="section" id="sign-in-as">
  <div class="wrap">
    ${sectionHead( { eyebrow: "Ask the Cast", title: "Sign in as someone, and see what they see.", lede: html`Each Cast person below can sign in. Each line is a question verify asks Rock's own security check on every verified load, so the answers are Rock's, not ours.` } )}
    <div class="card-grid">
      ${names.map( name => {
        const member = [ ...model.people.values() ].find( m => m.displayName === name );
        // What they can see first, then what they cannot, then the same for edit.
        const order = a => ( a.action === "Edit" ? 2 : 0 ) + ( a.allowed ? 0 : 1 );
        const rows = model.reportingAccess.filter( a => a.name === name ).sort( ( a, b ) => order( a ) - order( b ) );
        const login = member && signInName( member );
        const memberRoles = member ? ( member.securityRoles || [] ).map( s => rockRole.get( s ) || s ).filter( s => reportingRoles.has( s ) ).map( roleName ) : [];
        return html`<div class="card reveal sign-in-card">
  <div class="sign-in-head">${member ? portrait( member, r, "md" ) : ""}<div><h3>${person( model, name )}</h3><p class="fine">${esc( member && member.staffTitle ? member.staffTitle : "Not on staff" )}</p></div></div>
  ${login ? html`<p>Sign in as <code>${esc( login )}</code>.</p>` : ""}
  <p class="card-meta">Roles here: ${esc( memberRoles.length ? all( memberRoles ) : "none" )}.</p>
  <ul class="access-list">
    ${rows.map( a => html`<li class="${a.allowed ? "can" : "cannot"}">${a.allowed ? "Can" : "Cannot"} ${a.action === "Edit" ? "edit" : "see"} ${esc( itemName( model, a ) )}</li>` )}
  </ul>
</div>`;
      } )}
    </div>
    <p class="fine">Every sample login uses the one password you choose at install. See <a href="datasets.html#sign-in">Sign in</a> for the logins to start with.</p>
    <p class="feature-links"><a class="link-arrow" href="${doc( "docs/reporting.md#7-security-walkthrough-with-the-cast" )}">The security walkthrough</a></p>
  </div>
</section>`;
}

module.exports = function reporting( { model, r } ) {
    const sections = [
        { id: "layers", label: "The layers" },
        { id: "ministry-path", label: "The path" },
        { id: "reports", label: "Reports" },
        { id: "locks", label: "Security" },
        { id: "sign-in-as", label: "Ask the Cast" }
    ];

    return {
        file: "reporting.html",
        active: "reporting",
        title: "Reporting",
        description: "The Rock sample data's data views and reports: the layers, the Ministry by Design stages and their metrics, every report and who can see it, and the Cast people to sign in as.",
        body: html`
<section class="page-hero">
  <div class="wrap narrow">
    <p class="eyebrow">Reporting</p>
    <h1 class="display">Data views and reports, <em>built in layers.</em></h1>
    <p class="lede">${model.reportingDataViews.length} data views and ${model.reports.length} reports that teach good Rock practice and follow Ministry by Design. Every dataset loads them, locked the way a real church would lock them. The packages need loader <code>${esc( model.minimumLoaderVersion )}</code> or newer.</p>
  </div>
</section>

<section class="section section-tight">
  <div class="wrap">
    <nav class="feature-index" aria-label="Reporting on this page">
      ${sections.map( s => html`<a href="#${s.id}">${esc( s.label )}</a>` )}
    </nav>
  </div>
</section>
${layers( model )}
${stages( model )}
${reports( model )}
${locks( model )}
${castCards( model, r )}
`
    };
};
