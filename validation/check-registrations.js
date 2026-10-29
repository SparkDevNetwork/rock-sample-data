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
// eligibility needs; every registration feature has an always-open home, as decision 190 promises; releases use only the
// Lava the loader renders; and data views are filed and name instances the loader can fill, with eligibility views it replays.
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

// Decision 190: every feature has a home that is open on any install day. A rolling "next" instance is
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

// Attribute fills (slices 2 and 3): a choice weighs each of its values, and the kinds the loader knows.
const fillKinds = [ "choice", "friend", "contact-name", "contact-phone", "same-registration" ];
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

// A person attribute field's fill (slice 3) is a choice among its own values, answered for a share of people.
for ( const template of registrations.templates ) {
    for ( const field of template.forms.flatMap( f => f.fields ).filter( f => f.fill ) ) {
        const fill = field.fill;
        if ( !field.personAttribute || fill.kind !== "choice" || !( fill.values ?? [] ).length || ( fill.weights ?? [] ).length !== fill.values.length || !( fill.percent >= 0 && fill.percent <= 100 ) ) {
            problems.push( `template ${template.key}: a form field's fill must be on a person attribute, a choice with one weight per value, and a percent from 0 to 100` );
        }
    }
}

// Registrant rules (slices 2 and 3): one audience says who registers. Students come from a Student Path event step or
// a share; kids, couples, adults, and volunteers from connection status shares. Payments are ones the template allows
// and add up to 100 on a paid template, and fees, codes, and placements are the template's.
const steps = read( "configuration/steps.json" );
const stepTypes = new Map( steps.programs.flatMap( p => p.stepTypes ).map( t => [ t.key, t ] ) );
const statusNames = new Set( read( "configuration/connection-statuses.json" ).statuses.map( s => s.name ) );
const audiences = [ "students", "kids", "couples", "adults", "volunteers" ];
const burstDays = [ "opens", "closes", "early-bird", "payment-deadline" ];
const sum = values => Object.values( values ?? {} ).reduce( ( total, value ) => total + value, 0 );
const isShare = value => value >= 0 && value <= 100;
const isRange = range => Array.isArray( range ) && range.length === 2 && range[0] <= range[1];
const checkStatuses = ( name, statuses ) => {
    for ( const [ status, percent ] of Object.entries( statuses ?? {} ) ) {
        if ( !statusNames.has( status ) || !isShare( percent ) ) {
            problems.push( `${name}: status ${status} must be a connection status with a share from 0 to 100` );
        }
    }
};
for ( const rule of registrations.instances.filter( r => r.registrants ) ) {
    const name = `instance ${rule.key} registrants`;
    const template = templates.get( rule.template );
    const registrants = rule.registrants;
    const set = audiences.filter( a => registrants[a] );
    if ( set.length !== 1 ) {
        problems.push( `${name}: needs exactly one of ${audiences.join( ", " )}` );
        continue;
    }

    if ( registrants.students ) {
        const students = registrants.students;
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
    }

    if ( registrants.kids ) {
        checkStatuses( name, registrants.kids.statuses );
        if ( !template?.eligibility?.ages ) {
            problems.push( `${name}: kids register by age, so template ${rule.template} needs eligibility.ages` );
        }
    }

    if ( registrants.couples ) {
        checkStatuses( name, registrants.couples.statuses );
        if ( !( template.maxRegistrants >= 2 ) || template.registrantsSameFamily !== "Yes" ) {
            problems.push( `${name}: couples register together, so template ${rule.template} needs two registrants from the same family` );
        }
    }

    if ( registrants.adults ) {
        const adults = registrants.adults;
        checkStatuses( name, adults.statuses );
        const friends = adults.smallGroupFriends;
        if ( !isRange( adults.ages ) || !isShare( adults.couplePercent ?? 0 ) ) {
            problems.push( `${name}: adults need ages as [youngest, oldest] and a couplePercent from 0 to 100` );
        }

        if ( friends && ( !isShare( friends.groupPercent ) || !isRange( friends.size ) || friends.size[0] < 2 || friends.size[1] > template.maxRegistrants ) ) {
            problems.push( `${name}: smallGroupFriends needs a groupPercent and a size from 2 to template ${template.key}'s registrants per registration` );
        }
    }

    if ( registrants.volunteers ) {
        const volunteers = registrants.volunteers;
        checkStatuses( name, volunteers.statuses );
        if ( ![ volunteers.servers, volunteers.groupMembers, volunteers.teenPercent ].every( v => isShare( v ?? 0 ) ) || !isRange( volunteers.teenAges ) ) {
            problems.push( `${name}: volunteers need servers, groupMembers, and teenPercent from 0 to 100, and teenAges as [youngest, oldest]` );
        }
    }

    for ( const [ dataset, scale ] of Object.entries( registrants.scale ?? {} ) ) {
        if ( !datasets.has( dataset ) || !( scale > 0 ) || registrants.students ) {
            problems.push( `${name}: scale ${dataset} must be a dataset with a factor above 0, on an audience other than students` );
        }
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

    const isPaid = template.cost > 0 || template.setCostOnInstance || fees( template ).length > 0;
    if ( isPaid ? sum( registrants.payments ) !== 100 : registrants.payments !== undefined ) {
        problems.push( `${name}: ${isPaid ? "payments must add up to 100" : "a free template has no payments"}` );
    }

    if ( registrants.payments?.plan && !( template.paymentPlan ?? [] ).length ) {
        problems.push( `${name}: pays by plan, but template ${template.key} has no payment plans` );
    }

    if ( registrants.payments?.deposit && template.fullPaymentOrPlanRequired ) {
        problems.push( `${name}: pays deposits, but template ${template.key} needs full payment or a plan` );
    }

    if ( registrants.payments?.["pay-later"] && ( template.setCostOnInstance ? rule.minimumInitialPayment : template.minimumInitialPayment ) !== 0 ) {
        problems.push( `${name}: pays later, but its minimum payment is not 0` );
    }

    for ( const frequency of Object.keys( registrants.planFrequencies ?? {} ) ) {
        if ( !( template.paymentPlan ?? [] ).includes( frequency ) ) {
            problems.push( `${name}: plan frequency ${frequency} is not one template ${template.key} allows` );
        }
    }

    for ( const key of Object.keys( registrants.fees ?? {} ) ) {
        const fee = fees( template ).find( f => f.key === key );
        const weights = registrants.feeWeights?.[key];
        if ( !fee || ( fee.required && !weights ) ) {
            problems.push( `${name}: fee ${key} is not a fee of template ${template.key}, or is required with no feeWeights to choose its item` );
        }
        else if ( weights && weights.length !== fee.items.length ) {
            problems.push( `${name}: feeWeights ${key} needs one weight per item` );
        }
        else if ( registrants.feePerChild?.[key] !== undefined && !fee.allowMultiple ) {
            problems.push( `${name}: fee ${key} is charged per child, so it must allow a quantity` );
        }
    }

    for ( const key of [ ...Object.keys( registrants.feeWeights ?? {} ), ...Object.keys( registrants.feePerChild ?? {} ) ] ) {
        if ( registrants.fees?.[key] === undefined ) {
            problems.push( `${name}: fee ${key} has a rule but no share in fees` );
        }
    }

    for ( const code of registrants.codes ?? [] ) {
        const discount = discounts( template ).find( d => d.key === code.discount );
        if ( !discount || discount.autoApply || !isShare( code.percent ) || ( discount.maxUsage !== undefined && !( discount.maxUsage > ( code.keepUnused ?? 0 ) ) ) ) {
            problems.push( `${name}: code ${code.discount} must be a code (not automatic) of template ${template.key}, with a percent, and more uses than keepUnused` );
        }
    }

    const placements = registrants.placements;
    for ( const key of [ ...Object.keys( placements?.each ?? {} ), ...( placements?.early?.placements ?? [] ), ...Object.keys( placements?.byGrade ?? {} ), ...( placements?.together ?? [] ) ] ) {
        const placement = ( template.placements ?? [] ).find( p => p.key === key );
        if ( !placement || !( rule.placementGroups ?? [] ).some( g => g.placement === key ) ) {
            problems.push( `${name}: placement ${key} is not a placement of template ${template.key} with groups on this instance` );
        }
        else if ( ( placements.each?.[key] ?? 1 ) > 1 && !placement.allowMultiple ) {
            problems.push( `${name}: placement ${key} gives several groups each, but the template does not allow multiple placements` );
        }
        else if ( ( placements.byGrade?.[key] !== undefined || ( placements.together ?? [] ).includes( key ) ) && placements.each?.[key] !== 1 ) {
            problems.push( `${name}: placement ${key} by grade or together gives each registrant one group` );
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

// Releases: every template's release exists, and its Lava uses only what the loader renders (loader/generation/SignatureLava.cs).
const releaseTokens = [ "{{ Registrant.FirstName }}", "{{ Registrant.LastName }}", "{{ Registration.InstanceName }}", "{{ 'Global' | Attribute:'OrganizationName' }}" ];
const releases = new Map( ( registrations.signatureDocuments ?? [] ).map( d => [ d.key, d ] ) );
unique( ( registrations.signatureDocuments ?? [] ).map( d => d.key ), "release" );
for ( const release of releases.values() ) {
    const rest = releaseTokens.reduce( ( text, token ) => text.split( token ).join( "" ), release.lavaTemplate ?? "" );
    if ( rest.includes( "{{" ) || rest.includes( "{%" ) ) {
        problems.push( `release ${release.key}: its lavaTemplate uses Lava the loader cannot render; it may use only ${releaseTokens.join( ", " )}` );
    }

    if ( !/^<h2>[^<]+<\/h2>/.test( release.lavaTemplate ?? "" ) ) {
        problems.push( `release ${release.key}: its lavaTemplate must start with an h2 title, which the PDF shows` );
    }
}

for ( const template of registrations.templates.filter( t => t.signatureDocument ) ) {
    if ( !releases.has( template.signatureDocument ) ) {
        problems.push( `template ${template.key}: release ${template.signatureDocument} is not in 'signatureDocuments'` );
    }
}

// Data views: filed under a church category, registration filters name a planned instance, and a template's
// eligibility view counts the student service check-ins the loader replays (loader/generation/RegistrantPlanner.cs).
const churchCategories = read( "configuration/categories.json" ).categories;
const checkin = read( "configuration/checkin.json" );
const dataViews = new Map( ( registrations.dataViews ?? [] ).map( v => [ v.key, v ] ) );
unique( ( registrations.dataViews ?? [] ).map( v => v.key ), "data view" );
const leaves = filter => filter.component ? [ filter ] : ( filter.all ?? filter.any ?? filter.none ?? [] ).flatMap( leaves );
for ( const view of dataViews.values() ) {
    const category = churchCategories.find( c => c.key === view.category );
    if ( !category || category.parent !== "church-data-views" || category.entityType !== "Rock.Model.DataView" ) {
        problems.push( `data view ${view.key}: category ${view.category} must be a data view child of church-data-views in configuration/categories.json` );
    }

    for ( const leaf of leaves( view.filter ) ) {
        const rule = leaf.registration;
        if ( leaf.component === "Rock.Reporting.DataFilter.Person.InRegistrationInstanceFilter" ) {
            const instance = registrations.instances.find( i => i.key === rule?.instance );
            if ( !instance || !( instance.occurrences ?? [] ).includes( rule.occurrence ) || ![ "registrant", "registrar" ].includes( rule.person ) ) {
                problems.push( `data view ${view.key}: a registration filter must name an instance rule, one of its occurrences, and registrant or registrar` );
            }
            else if ( isPerCampus( events.get( instance.event ) ) ) {
                problems.push( `data view ${view.key}: registration filter ${rule.instance} names a per-campus instance, which has one instance per campus` );
            }
        }
        else if ( rule || !leaf.selection ) {
            problems.push( `data view ${view.key}: filter ${leaf.component} needs a selection, and only a registration filter names an instance` );
        }
    }
}

const studentAreas = new Set( checkin.areas.filter( a => a.groups.some( g => steps.programs.some( p => p.students?.checkInGroups.includes( g ) ) ) ).map( a => a.guid.toUpperCase() ) );
for ( const template of registrations.templates.filter( t => t.eligibility?.dataView ) ) {
    const view = dataViews.get( template.eligibility.dataView );
    if ( !view ) {
        problems.push( `template ${template.key}: eligibility data view ${template.eligibility.dataView} is not in 'dataViews'` );
        continue;
    }

    const attendance = leaves( view.filter ).filter( l => l.component === "Rock.Reporting.DataFilter.Person.GroupTypeAttendanceFilter" );
    const parts = attendance[ 0 ]?.selection.split( "|" ) ?? [];
    const range = ( parts[ 3 ] ?? "" ).split( "," );
    const areas = new Set( ( parts[ 0 ] ?? "" ).split( "," ).map( g => g.toUpperCase() ) );
    if ( view.filter.all === undefined || attendance.length !== 1 || parts[ 1 ] !== "256" || range[ 0 ] !== "Last" || range[ 2 ] !== "Day"
        || areas.size !== studentAreas.size || [ ...areas ].some( a => !studentAreas.has( a ) ) ) {
        problems.push( `template ${template.key}: eligibility data view ${view.key} must require, among all its filters, at least N check-ins in the last N days in exactly the student check-in areas (${[ ...studentAreas ].join( ", " )}), which the loader replays from the Student Path` );
    }

    const schoolGrades = { "c49bd3af-ff94-4a7c-99e1-08503a3c746e": 12, "78f7d773-8244-4995-8bc4-ad6f6a7b7820": 11, "e04e3f62-ef5c-4860-8f32-1c152ca1700a": 10, "2a130e04-3712-427a-8bb0-473eb8ff8924": 9, "d58d70af-3ccc-4d4e-bfaf-2014d8579d60": 8, "3fe728ac-be25-409a-98cb-3cfce5fa063b": 7 };
    const grade = leaves( view.filter ).filter( l => l.component === "Rock.Reporting.DataFilter.Person.GradeFilter" ).map( l => l.selection.split( "|" ) );
    if ( grade.some( g => g[ 0 ] !== "256" || schoolGrades[ ( g[ 1 ] ?? "" ).toLowerCase() ] !== template.eligibility.grades?.[ 0 ] ) || ( grade.length > 0 && template.eligibility.grades?.[ 1 ] !== 12 ) ) {
        problems.push( `template ${template.key}: eligibility data view ${view.key} must count grades from the template's lowest grade up, as the template's own grades do` );
    }

    if ( leaves( view.filter ).some( l => ![ "Rock.Reporting.DataFilter.Person.GroupTypeAttendanceFilter", "Rock.Reporting.DataFilter.Person.GradeFilter" ].includes( l.component ) ) ) {
        problems.push( `template ${template.key}: eligibility data view ${view.key} may use only grade and attendance filters, which the loader replays` );
    }
}

const openTemplates = [ ...alwaysOpen.keys() ];
console.log( `Registrations: ${registrations.templates.length} templates, ${registrations.instances.length} instance rules, ${openTemplates.length} always open (${openTemplates.join( ", " )}), ${features.length} features checked. ${problems.length} problems.` );
for ( const problem of problems ) {
    console.log( `  ${problem}` );
}

process.exit( problems.length > 0 || openTemplates.length < 7 ? 1 : 0 );
