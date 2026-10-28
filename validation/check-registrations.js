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

// Checks configuration/registrations.json against the files it names (docs/registrations.md): every instance's event,
// template, contact, groups, and placements exist; slug patterns make valid, unique slugs; templates ask for what their
// eligibility needs; and every registration feature has an always-open home, as decision D-registrations-2 promises.
// The dates themselves are checked by loader-compat-check, which runs the real planner for install day 2026-10-28.
// Run: node validation/check-registrations.js

const fs = require( "fs" );
const path = require( "path" );

const root = path.join( __dirname, ".." );
const read = file => JSON.parse( fs.readFileSync( path.join( root, file ), "utf8" ) );
const problems = [];

const registrations = read( "configuration/registrations.json" );
const calendar = read( "configuration/calendar.json" );
const accounts = read( "configuration/accounts.json" ).accounts;
const campuses = read( "configuration/campuses.json" ).campuses;
const datasets = new Set( fs.readdirSync( path.join( root, "datasets" ) ).filter( f => f.endsWith( ".json" ) ).map( f => f.replace( /\.json$/, "" ) ) );
const events = new Map( calendar.events.map( e => [ e.key, e ] ) );
const templates = new Map( registrations.templates.map( t => [ t.key, t ] ) );
const groupTypes = new Map( registrations.groupTypes.map( t => [ t.key, t ] ) );
const categories = new Set( registrations.categories.map( c => c.key ) );
const slugPattern = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;

const unique = ( items, what ) => {
    const seen = new Set();
    for ( const item of items ) {
        if ( seen.has( item ) ) {
            problems.push( `${what} ${item} is used twice` );
        }

        seen.add( item );
    }
};

unique( registrations.categories.map( c => c.key ), "category" );
unique( registrations.groupTypes.map( t => t.key ), "group type" );
unique( registrations.templates.map( t => t.key ), "template" );
unique( registrations.instances.map( i => i.key ), "instance rule" );

if ( !accounts.some( a => a.key === registrations.account ) ) {
    problems.push( `account ${registrations.account} is not in configuration/accounts.json` );
}

for ( const type of registrations.groupTypes ) {
    if ( type.roles.filter( r => r.isDefault ).length !== 1 ) {
        problems.push( `group type ${type.key}: needs exactly one default role` );
    }
}

// Each template: its references, and the fields its eligibility needs, since Rock refuses a registrant it cannot check.
for ( const template of registrations.templates ) {
    const name = `template ${template.key}`;
    const fields = template.forms.flatMap( f => f.fields );
    const personFields = new Set( fields.map( f => f.person ).filter( Boolean ) );
    const attributeKeys = new Set( ( template.attributes ?? [] ).map( a => a.key ) );
    if ( !categories.has( template.category ) ) {
        problems.push( `${name}: category ${template.category} is not in 'categories'` );
    }

    if ( template.groupType && !groupTypes.has( template.groupType ) ) {
        problems.push( `${name}: group type ${template.groupType} is not in 'groupTypes'` );
    }

    if ( template.groupMemberRole && !groupTypes.get( template.groupType )?.roles.some( r => r.key === template.groupMemberRole ) ) {
        problems.push( `${name}: role ${template.groupMemberRole} is not a role of ${template.groupType}` );
    }

    for ( const placement of template.placements ?? [] ) {
        if ( !groupTypes.has( placement.groupType ) ) {
            problems.push( `${name}: placement ${placement.key} has group type ${placement.groupType}, which is not in 'groupTypes'` );
        }
    }

    for ( const field of fields.filter( f => f.attribute && !attributeKeys.has( f.attribute ) ) ) {
        problems.push( `${name}: a field asks attribute ${field.attribute}, which its 'attributes' does not hold` );
    }

    for ( const field of fields.filter( f => [ f.person, f.personAttribute, f.attribute ].filter( Boolean ).length !== 1 ) ) {
        problems.push( `${name}: a field must name exactly one of person, personAttribute, and attribute (${JSON.stringify( field )})` );
    }

    if ( !personFields.has( "FirstName" ) || !personFields.has( "LastName" ) ) {
        problems.push( `${name}: the form must ask first and last name` );
    }

    const eligibility = template.eligibility ?? {};
    for ( const [ limit, field ] of [ [ "grades", "Grade" ], [ "ages", "Birthdate" ], [ "gender", "Gender" ] ] ) {
        if ( eligibility[limit] && !personFields.has( field ) ) {
            problems.push( `${name}: eligibility by ${limit} needs a ${field} field` );
        }
    }

    for ( const discount of template.discounts ?? [] ) {
        if ( ( discount.amount > 0 ) === ( discount.percent > 0 ) || discount.percent > 100 ) {
            problems.push( `${name}: discount ${discount.key} needs an amount or a percent up to 100, not both` );
        }
    }

    for ( const fee of template.fees ?? [] ) {
        if ( fee.items.length === 0 ) {
            problems.push( `${name}: fee ${fee.key} has no items` );
        }
    }
}

// The events an instance links to occur more than once a year at a campus when they repeat monthly, weekly, or on several yearly dates.
const isSeveralAYear = event => [ "weekly", "monthly", "seasons" ].includes( event.when.kind )
    || ( event.when.dates?.length ?? 1 ) > 1
    || ( event.when.kind === "step-season" && !event.when.season );
const isPerCampus = event => event.campuses !== "church-wide";
const eventCampuses = event => event.campuses === "all" ? campuses.map( c => c.key ) : event.campuses === "church-wide" ? [ null ] : event.campuses;

// Each instance rule: its references, and slugs and group keys that cannot collide.
const slugShapes = new Map();
const groupKeys = new Map();
for ( const rule of registrations.instances ) {
    const name = `instance ${rule.key}`;
    const template = templates.get( rule.template );
    const event = events.get( rule.event );
    if ( !template ) {
        problems.push( `${name}: template ${rule.template} is not in 'templates'` );
        continue;
    }

    if ( !event ) {
        problems.push( `${name}: event ${rule.event} is not in configuration/calendar.json` );
        continue;
    }

    for ( const campus of eventCampuses( event ).filter( c => c && !campuses.some( k => k.key === c ) ) ) {
        problems.push( `${name}: event ${rule.event} meets at campus ${campus}, which is not in configuration/campuses.json` );
    }

    if ( !calendar.contacts[rule.contact] ) {
        problems.push( `${name}: contact ${rule.contact} is not in configuration/calendar.json 'contacts'` );
    }

    if ( rule.occurrences.length === 0 || rule.occurrences.some( o => o !== "next" && o !== "last" ) ) {
        problems.push( `${name}: occurrences must be "next", "last", or both` );
    }

    if ( ![ "rolling", "days-before" ].includes( rule.opens?.kind ) ) {
        problems.push( `${name}: opens kind must be "rolling" or "days-before"` );
    }

    if ( Boolean( template.setCostOnInstance ) !== ( rule.cost !== undefined ) ) {
        problems.push( `${name}: a cost belongs on the instance exactly when template ${template.key} sets cost on the instance` );
    }

    for ( const dataset of [ ...Object.keys( rule.maxAttendees ?? {} ), ...( rule.placementGroups ?? [] ).flatMap( p => Object.keys( p.count ?? {} ) ) ] ) {
        if ( !datasets.has( dataset ) ) {
            problems.push( `${name}: ${dataset} is not a dataset` );
        }
    }

    // A slug must change every time a new instance does: with the year, the month when the event repeats within a
    // year, and the campus when each campus has its own.
    const needs = [ "{year}", ...( isSeveralAYear( event ) ? [ "{month}" ] : [] ), ...( isPerCampus( event ) ? [ "{campus}" ] : [] ) ];
    for ( const pattern of [ rule.slug, rule.name, rule.linkedGroup?.key, rule.linkedGroup?.name, ...( rule.placementGroups ?? [] ).map( p => p.key ) ].filter( Boolean ) ) {
        for ( const token of needs.filter( t => !pattern.includes( t ) ) ) {
            problems.push( `${name}: "${pattern}" needs ${token}, or two instances would share it` );
        }

        if ( !isPerCampus( event ) && pattern.includes( "{campus}" ) ) {
            problems.push( `${name}: "${pattern}" names {campus}, but ${rule.event} is church-wide` );
        }
    }

    for ( const campus of eventCampuses( event ) ) {
        const slug = rule.slug.replace( "{year}", "2027" ).replace( "{month}", "september" ).replace( "{campus}", campus ?? "" );
        if ( !slugPattern.test( slug ) ) {
            problems.push( `${name}: slug ${slug} does not match Rock's pattern ^[a-z0-9]+(?:-[a-z0-9]+)*$` );
        }
    }

    const shape = rule.slug.replace( /\{year\}|\{month\}|\{campus\}/g, "*" );
    if ( slugShapes.has( shape ) ) {
        problems.push( `${name}: slug ${rule.slug} can make the same slugs as instance ${slugShapes.get( shape )}` );
    }

    slugShapes.set( shape, rule.key );

    if ( rule.linkedGroup && !template.groupType ) {
        problems.push( `${name}: has a linked group, but template ${template.key} has no groupType` );
    }

    for ( const group of rule.placementGroups ?? [] ) {
        if ( !( template.placements ?? [] ).some( p => p.key === group.placement ) ) {
            problems.push( `${name}: placement ${group.placement} is not a placement of template ${template.key}` );
        }

        if ( !group.names && !( group.name?.includes( "{n}" ) && Object.keys( group.count ?? {} ).length > 0 ) ) {
            problems.push( `${name}: placement groups for ${group.placement} need names, or a name with {n} and a count` );
        }
    }

    // A group key two rules share must mean the same group: the same name and type.
    for ( const [ key, groupName, type ] of [
        ...( rule.linkedGroup ? [ [ rule.linkedGroup.key, rule.linkedGroup.name, template.groupType ] ] : [] ),
        ...( rule.placementGroups ?? [] ).map( p => [ p.key, JSON.stringify( p.names ?? [ p.name, p.count ] ), ( template.placements ?? [] ).find( t => t.key === p.placement )?.groupType ] )
    ] ) {
        const other = groupKeys.get( key );
        if ( other && ( other.groupName !== groupName || other.type !== type ) ) {
            problems.push( `${name}: group ${key} is also in instance ${other.rule}, named or typed another way` );
        }

        groupKeys.set( key, { groupName, type, rule: rule.key } );
    }
}

// Decision D-registrations-2: every feature has a home that is open on any install day. A rolling "next" instance is
// always open. So is one whose event is pinned a few days ahead with a window that always covers the install day.
const isAlwaysOpen = rule => {
    const event = events.get( rule.event );
    if ( !rule.occurrences.includes( "next" ) ) {
        return false;
    }

    if ( rule.opens.kind === "rolling" ) {
        return true;
    }

    const pinned = event.when.kind === "pinned" && event.when.direction === "after" && event.when.repeat === "yearly";
    const soonest = -event.when.daysAgo;
    return pinned && rule.opens.days >= soonest + 6 && rule.closesDaysBefore <= soonest;
};

const alwaysOpen = new Map();
for ( const rule of registrations.instances.filter( isAlwaysOpen ) ) {
    alwaysOpen.set( rule.template, [ ...( alwaysOpen.get( rule.template ) ?? [] ), rule ] );
}

const fees = t => t.fees ?? [];
const discounts = t => t.discounts ?? [];
const sharedPlacementGroup = key => registrations.instances.filter( r => r.template === key && isAlwaysOpen( r ) ).flatMap( r => ( r.placementGroups ?? [] ).map( p => p.key ) )
    .some( ( k, i, all ) => all.indexOf( k ) !== i );
const features = [
    [ "Free registration", [ "parents-night-out" ], t => t.cost === 0 && !t.setCostOnInstance && fees( t ).length === 0 ],
    [ "Cost on the template", [ "student-camp" ], t => t.cost > 0 ],
    [ "Cost on the instance", [ "money-matters" ], ( t, rules ) => t.setCostOnInstance && rules.every( r => r.cost > 0 ) ],
    [ "Partial payment", [ "student-camp", "mission-trip", "student-retreat", "womens-retreat" ], t => t.minimumInitialPayment > 0 && t.minimumInitialPayment < t.cost ],
    [ "Pay later", [ "money-matters" ], ( t, rules ) => rules.every( r => r.minimumInitialPayment === 0 ) ],
    [ "Payment plans", [ "student-camp", "mission-trip", "womens-retreat" ], t => ( t.paymentPlan ?? [] ).length > 0 ],
    [ "Full payment or payment plan required", [ "mission-trip" ], t => Boolean( t.fullPaymentOrPlanRequired ) ],
    [ "Payment deadline", [ "student-camp", "mission-trip" ], ( t, rules ) => rules.every( r => r.paymentDaysBefore > 0 ) ],
    [ "Required multiple-choice fee", [ "womens-retreat" ], t => fees( t ).some( f => f.required && f.items.length > 1 ) ],
    [ "Optional multiple-choice fee", [ "student-camp", "student-retreat" ], t => fees( t ).some( f => !f.required && f.items.length > 1 ) ],
    [ "Fee with a quantity", [ "date-night" ], t => fees( t ).some( f => f.allowMultiple ) ],
    [ "Fee item with a usage limit, hidden when none remain", [ "womens-retreat" ], t => fees( t ).some( f => f.hideWhenNoneRemaining && f.items.some( i => i.maximumUsageCount > 0 ) ) ],
    [ "Fee the discount does not apply to", [ "student-camp" ], t => fees( t ).some( f => f.discountApplies === false ) ],
    [ "Percent discount code", [ "womens-retreat" ], t => discounts( t ).some( d => d.percent > 0 && d.percent < 100 && !d.autoApply ) ],
    [ "Amount discount code with limited uses", [ "student-retreat" ], t => discounts( t ).some( d => d.amount > 0 && d.maxUsage > 0 && !d.autoApply ) ],
    [ "100% scholarship code", [ "money-matters" ], t => discounts( t ).some( d => d.percent === 100 ) ],
    [ "Early-bird discount", [ "student-camp", "womens-retreat" ], t => discounts( t ).some( d => d.autoApply && d.endsBefore ) ],
    [ "Discount by number of registrants", [ "student-camp" ], t => discounts( t ).some( d => d.autoApply && d.minRegistrants > 1 ) ],
    [ "Registrant limit", [ "student-camp", "mission-trip", "parents-night-out" ], ( t, rules ) => rules.every( r => Object.keys( r.maxAttendees ?? {} ).length === datasets.size ) ],
    [ "Wait list", [ "mission-trip", "parents-night-out" ], t => t.waitList === true ],
    [ "Several people on one registration, same family", [ "date-night", "student-camp" ], t => t.maxRegistrants > 1 && t.registrantsSameFamily === "Yes" ],
    [ "Grade eligibility", [ "student-camp", "mission-trip" ], t => Boolean( t.eligibility?.grades ) ],
    [ "Age eligibility", [ "parents-night-out" ], t => Boolean( t.eligibility?.ages ) ],
    [ "Gender eligibility", [ "womens-retreat" ], t => Boolean( t.eligibility?.gender ) ],
    [ "Registrar who is not a registrant", [ "student-camp" ], t => t.registrarOption === "PromptForRegistrar" ],
    [ "Placement groups on an instance", [ "student-camp" ], t => ( t.placements ?? [] ).length > 0 ],
    [ "One placement group shared by two instances", [ "student-camp" ], t => sharedPlacementGroup( t.key ) ],
    [ "Several placements per person", [ "student-camp" ], t => ( t.placements ?? [] ).some( p => p.allowMultiple ) ],
    [ "Add to group as Pending", [ "mission-trip" ], ( t, rules ) => t.groupMemberStatus === "Pending" && rules.every( r => r.linkedGroup ) ],
    [ "Registrant attributes", [ "student-camp" ], t => ( t.attributes ?? [] ).length > 0 ],
    [ "Person attribute saved to the person", [ "parents-night-out" ], t => t.forms.some( f => f.fields.some( x => x.personAttribute ) ) ]
];

for ( const [ feature, homes, has ] of features ) {
    for ( const key of homes ) {
        const template = templates.get( key );
        const rules = alwaysOpen.get( key ) ?? [];
        if ( !template || rules.length === 0 ) {
            problems.push( `${feature}: its home ${key} has no always-open instance` );
        }
        else if ( !has( template, rules ) ) {
            problems.push( `${feature}: its home ${key} does not have it` );
        }
    }
}

if ( registrations.registrationAttributes.length === 0 ) {
    problems.push( "every template should ask \"How did you hear?\" as a registration attribute" );
}

// Attribute fills (slice 2): a choice weighs each of its values, and the kinds the loader knows.
const fillKinds = [ "choice", "friend", "contact-name", "contact-phone" ];
for ( const [ owner, attribute ] of [
    ...registrations.registrationAttributes.map( a => [ "registration attribute", a ] ),
    ...registrations.templates.flatMap( t => ( t.attributes ?? [] ).map( a => [ `template ${t.key} attribute`, a ] ) ) ] ) {
    const fill = attribute.fill;
    if ( !fill ) {
        continue;
    }

    if ( !fillKinds.includes( fill.kind ) ) {
        problems.push( `${owner} ${attribute.key}: fill kind ${fill.kind} is not one of ${fillKinds.join( ", " )}` );
    }
    else if ( fill.kind === "choice" && ( attribute.fieldType !== "single-select" || ( fill.weights ?? [] ).length !== attribute.values.length ) ) {
        problems.push( `${owner} ${attribute.key}: a choice fill needs a single-select attribute and one weight per value` );
    }
}

// Registrant rules (slice 2): students come from a Student Path event step or a share, payments are ones the
// template allows and add up to 100, and fees and placements are the template's.
const steps = read( "configuration/steps.json" );
const stepTypes = new Map( steps.programs.flatMap( p => p.stepTypes ).map( t => [ t.key, t ] ) );
const burstDays = [ "opens", "closes", "early-bird", "payment-deadline" ];
const sum = values => Object.values( values ?? {} ).reduce( ( total, value ) => total + value, 0 );
for ( const rule of registrations.instances.filter( r => r.registrants ) ) {
    const name = `instance ${rule.key} registrants`;
    const template = templates.get( rule.template );
    const registrants = rule.registrants;
    const students = registrants.students ?? {};
    if ( students.stepType !== undefined ) {
        if ( stepTypes.get( students.stepType )?.source.kind !== "event" ) {
            problems.push( `${name}: students.stepType ${students.stepType} is not an event step in configuration/steps.json` );
        }
    }
    else if ( !( students.percent > 0 && students.percent <= 100 ) ) {
        problems.push( `${name}: students needs a stepType or a percent from 1 to 100` );
    }

    if ( !template?.eligibility?.grades ) {
        problems.push( `${name}: students register by grade, so template ${rule.template} needs eligibility.grades` );
    }

    if ( registrants.studentLeaders ) {
        const leaders = registrants.studentLeaders;
        const code = discounts( template ).find( d => d.key === leaders.discount );
        if ( stepTypes.get( leaders.stepType )?.source.kind !== "school-years" ) {
            problems.push( `${name}: studentLeaders.stepType ${leaders.stepType} is not a school-years step` );
        }

        if ( !code || code.autoApply || !( code.maxUsage > leaders.keepUnused ) ) {
            problems.push( `${name}: studentLeaders.discount ${leaders.discount} must be a code (not automatic) with more uses than keepUnused` );
        }
    }

    for ( const burst of registrants.timing ?? [] ) {
        if ( !burstDays.includes( burst.after ?? burst.before ) || ( burst.after && burst.before ) || !( burst.days > 0 ) ) {
            problems.push( `${name}: each timing burst needs after or before (${burstDays.join( ", " )}) and days` );
        }
    }

    if ( sum( Object.fromEntries( ( registrants.timing ?? [] ).map( ( b, i ) => [ i, b.percent ] ) ) ) > 100 ) {
        problems.push( `${name}: timing percents add up to more than 100` );
    }

    if ( sum( registrants.payments ) !== 100 ) {
        problems.push( `${name}: payments must add up to 100` );
    }

    if ( registrants.payments?.plan && !( template.paymentPlan ?? [] ).length ) {
        problems.push( `${name}: pays by plan, but template ${template.key} has no payment plans` );
    }

    if ( registrants.payments?.deposit && template.fullPaymentOrPlanRequired ) {
        problems.push( `${name}: pays deposits, but template ${template.key} needs full payment or a plan` );
    }

    for ( const frequency of Object.keys( registrants.planFrequencies ?? {} ) ) {
        if ( !( template.paymentPlan ?? [] ).includes( frequency ) ) {
            problems.push( `${name}: plan frequency ${frequency} is not one template ${template.key} allows` );
        }
    }

    for ( const fee of Object.keys( registrants.fees ?? {} ) ) {
        if ( !fees( template ).some( f => f.key === fee && !f.required ) ) {
            problems.push( `${name}: fee ${fee} is not an optional fee of template ${template.key}` );
        }
    }

    const placements = registrants.placements;
    for ( const key of [ ...Object.keys( placements?.each ?? {} ), ...( placements?.early?.placements ?? [] ) ] ) {
        const placement = ( template.placements ?? [] ).find( p => p.key === key );
        if ( !placement || !( rule.placementGroups ?? [] ).some( g => g.placement === key ) ) {
            problems.push( `${name}: placement ${key} is not a placement of template ${template.key} with groups on this instance` );
        }
        else if ( ( placements.each?.[key] ?? 1 ) > 1 && !placement.allowMultiple ) {
            problems.push( `${name}: placement ${key} gives several groups each, but the template does not allow multiple placements` );
        }
    }

    if ( registrants.waitListPercent !== undefined && ( !template.waitList || !( registrants.waitListPercent >= 0 && registrants.waitListPercent <= 100 ) ) ) {
        problems.push( `${name}: waitListPercent needs a template with a wait list and a share from 0 to 100` );
    }
}

// Rock applies the first automatic discount, in order, that is valid on the day. A discount that needs several
// registrants must come before an early bird, or a family registering early would never get it.
for ( const template of registrations.templates ) {
    const automatic = discounts( template ).filter( d => d.autoApply );
    const earlyBird = automatic.findIndex( d => d.endsBefore );
    const several = automatic.findIndex( d => d.minRegistrants > 1 );
    if ( earlyBird >= 0 && several > earlyBird ) {
        problems.push( `template ${template.key}: discount ${automatic[several].key} needs several registrants, so it must come before the early bird ${automatic[earlyBird].key}` );
    }
}

const openTemplates = [ ...alwaysOpen.keys() ];
console.log( `Registrations: ${registrations.templates.length} templates, ${registrations.instances.length} instance rules, ${openTemplates.length} always open (${openTemplates.join( ", " )}), ${features.length} features checked. ${problems.length} problems.` );
for ( const problem of problems ) {
    console.log( `  ${problem}` );
}

process.exit( problems.length > 0 || openTemplates.length < 7 ? 1 : 0 );
