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

using System.Globalization;

using RockSampleData.Loader.Model;

namespace RockSampleData.Loader.Generation
{
    /*
        10/28/26 - CLAUDE

        Every registration date is a rule counted from the install day, through the calendar's own occurrences, so a
        registration always names the day its event page shows. An event's dates at one campus form a list: one per
        occurrence, or one per start for a monthly occurrence with no end. "Next" is the first whose registration closes
        on or after the install day, and "last" is the one before it.

        A rolling instance opens the day after the previous date's registration closed, so its event always has one
        open. That is how camp, the mission trip, Parents' Night Out, Money Matters, Date Night, and the Women's
        Retreat stay open on any install day.

        Reason: The registrations are open, closed, or not yet open the same way on every install day (decision 190).
    */

    /// <summary>
    /// Plans every registration instance for one load from registrations.json and the calendar's planned events:
    /// which occurrence, its dates, its slug, and its linked and placement groups. Reads no database.
    /// </summary>
    internal sealed class RegistrationPlanner
    {
        /// <summary>
        /// How far ahead a monthly occurrence's starts are listed. The calendar plans a year or so ahead.
        /// </summary>
        private const int ListYearsAhead = 2;

        private readonly RegistrationsConfiguration _registrations;
        private readonly string _dataset;
        private readonly DateTime _anchor;
        private readonly IReadOnlyDictionary<string, string> _campusNames;

        /// <param name="registrations">The contents of registrations.json.</param>
        /// <param name="dataset">The dataset key, for limits and group counts.</param>
        /// <param name="anchor">The install day.</param>
        /// <param name="campusNames">Each of the dataset's campuses' names, by key.</param>
        public RegistrationPlanner( RegistrationsConfiguration registrations, string dataset, DateTime anchor, IReadOnlyDictionary<string, string> campusNames )
        {
            _registrations = registrations;
            _dataset = dataset;
            _anchor = anchor.Date;
            _campusNames = campusNames;
        }

        /// <summary>
        /// Plans every instance, in registrations.json order, then by campus and date.
        /// </summary>
        /// <param name="calendar">The calendar's planned events for this dataset.</param>
        public RegistrationPlan Plan( IReadOnlyList<PlannedEvent> calendar )
        {
            var templates = _registrations.Templates.ToDictionary( t => t.Key );
            var plan = new RegistrationPlan();
            foreach ( var rule in _registrations.Instances )
            {
                var template = templates.TryGetValue( rule.Template, out var found ) ? found
                    : throw new InvalidDataException( $"configuration/registrations.json: instance {rule.Key} names template {rule.Template}, which 'templates' does not hold." );
                var item = calendar.FirstOrDefault( e => e.Event.Key == rule.Event );
                if ( item == null )
                {
                    continue;
                }

                foreach ( var occurrences in item.Occurrences.GroupBy( o => o.CampusKey ) )
                {
                    var dates = EventDates( occurrences );
                    var next = dates.FindIndex( d => Closes( d, rule ) >= _anchor );
                    foreach ( var pick in rule.Occurrences )
                    {
                        var index = pick == RegistrationOccurrencePick.Next ? next : ( next < 0 ? dates.Count : next ) - 1;
                        if ( index < 0 )
                        {
                            throw new InvalidDataException( $"configuration/registrations.json: instance {rule.Key} needs the {pick.ToString().ToLowerInvariant()} {rule.Event} occurrence, which the calendar does not plan. Raise monthsAhead in configuration/calendar.json." );
                        }

                        plan.Instances.Add( Instance( rule, template, pick, dates, index, occurrences.Key, plan ) );
                    }
                }
            }

            foreach ( var template in _registrations.Templates )
            {
                var latest = plan.Instances.Where( i => i.Template == template ).Select( i => i.EventDay ).DefaultIfEmpty().Max();
                foreach ( var discount in template.Discounts.Where( d => d.EndsBefore != null && latest != default ) )
                {
                    plan.DiscountEnds[(template.Key, discount.Key)] = LastOnOrBefore( discount.EndsBefore, latest );
                }
            }

            return plan;
        }

        /// <summary>
        /// One campus's dates of an event, oldest first: each occurrence's first start, or every start of a monthly
        /// occurrence with no end.
        /// </summary>
        private List<EventDate> EventDates( IEnumerable<PlannedOccurrence> occurrences )
        {
            return occurrences
                .SelectMany( o => o.Schedule.IsOngoing
                    ? o.Schedule.Starts( _anchor.AddYears( ListYearsAhead ) ).Select( s => new EventDate( s, o ) )
                    : new[] { new EventDate( o.Schedule.FirstStart, o ) } )
                .OrderBy( d => d.Start )
                .ToList();
        }

        private PlannedRegistrationInstance Instance( RegistrationInstanceRule rule, RegistrationTemplateDefinition template, RegistrationOccurrencePick pick, List<EventDate> dates, int index, string campusKey, RegistrationPlan plan )
        {
            var date = dates[index];
            var day = date.Start.Date;
            var instance = new PlannedRegistrationInstance
            {
                Rule = rule,
                Template = template,
                Pick = pick,
                Occurrence = date.Occurrence,
                CampusKey = campusKey,
                EventStart = date.Start,
                Opens = Opens( rule, dates, index ),
                Closes = Closes( date, rule ),
                PaymentDeadline = rule.PaymentDaysBefore is { } paymentDays ? day.AddDays( -paymentDays ) : null,
                SendReminder = rule.ReminderDaysBefore is { } reminderDays ? day.AddDays( -reminderDays ).AddHours( 9 ) : null,
                Name = Fill( rule.Name, day, campusKey, isSlug: false ),
                PublicName = Fill( rule.PublicName ?? rule.Name, day, campusKey, isSlug: false ),
                Slug = Fill( rule.Slug, day, campusKey, isSlug: true ),
                MaxAttendees = rule.MaxAttendees.TryGetValue( _dataset, out var limit ) ? limit : null
            };

            var key = $"{rule.Key}:{campusKey ?? "church-wide"}:{day:yyyy-MM-dd}";
            instance.Guid = SeededRandom.StableGuid( $"registration-instance:{key}" );
            instance.LinkageGuid = SeededRandom.StableGuid( $"registration-linkage:{key}" );

            if ( rule.LinkedGroup != null )
            {
                var groupType = template.GroupType ?? throw new InvalidDataException( $"configuration/registrations.json: instance {rule.Key} has a linked group, but template {template.Key} has no groupType." );
                instance.LinkedGroup = Group( plan, Fill( rule.LinkedGroup.Key, day, campusKey, isSlug: true ), Fill( rule.LinkedGroup.Name, day, campusKey, isSlug: false ), groupType, campusKey, instance );
            }

            foreach ( var placementRule in rule.PlacementGroups )
            {
                var placement = template.Placements.FirstOrDefault( p => p.Key == placementRule.Placement )
                    ?? throw new InvalidDataException( $"configuration/registrations.json: instance {rule.Key} has groups for placement {placementRule.Placement}, which template {template.Key} does not hold." );
                var names = placementRule.Names
                    ?? Enumerable.Range( 1, placementRule.Count.TryGetValue( _dataset, out var count ) ? count : 0 ).Select( n => placementRule.Name.Replace( "{n}", n.ToString( CultureInfo.InvariantCulture ) ) ).ToList();
                var groupKey = Fill( placementRule.Key, day, campusKey, isSlug: true );
                for ( var n = 0; n < names.Count; n++ )
                {
                    var group = Group( plan, $"{groupKey}-{n + 1}", Fill( names[n], day, campusKey, isSlug: false ), placement.GroupType, campusKey, instance );
                    instance.PlacementGroups.Add( new PlannedPlacementGroup( placement, group, n ) );
                }
            }

            return instance;
        }

        /// <summary>
        /// The group with this key, made the first time an instance names it. Instances that name the same key share it.
        /// </summary>
        private static PlannedRegistrationGroup Group( RegistrationPlan plan, string key, string name, string groupType, string campusKey, PlannedRegistrationInstance instance )
        {
            if ( plan.Groups.TryGetValue( key, out var shared ) )
            {
                if ( shared.Name != name || shared.GroupType != groupType || shared.CampusKey != campusKey )
                {
                    throw new InvalidDataException( $"configuration/registrations.json: group {key} is named, typed, or placed two different ways." );
                }

                return shared;
            }

            var group = new PlannedRegistrationGroup
            {
                Key = key,
                Name = name,
                GroupType = groupType,
                CampusKey = campusKey,
                Guid = SeededRandom.StableGuid( $"registration-group:{key}" ),
                FirstInstance = instance
            };
            plan.Groups.Add( key, group );
            return group;
        }

        /// <summary>
        /// The day registration opens: the day after the previous date's registration closed, or a number of days before the event.
        /// </summary>
        private DateTime Opens( RegistrationInstanceRule rule, List<EventDate> dates, int index )
        {
            var day = dates[index].Start.Date;
            switch ( rule.Opens?.Kind )
            {
                case RegistrationOpenRule.Rolling:
                    // The calendar starts at the history window, so the oldest date has no date before it. It opens a year before it closes.
                    return index > 0 ? Closes( dates[index - 1], rule ).AddDays( 1 ) : Closes( dates[index], rule ).AddYears( -1 ).AddDays( 1 );

                case RegistrationOpenRule.DaysBefore:
                    return day.AddDays( -rule.Opens.Days );

                default:
                    throw new InvalidDataException( $"configuration/registrations.json: instance {rule.Key} needs 'opens'." );
            }
        }

        /// <summary>
        /// The last day of registration: a number of days before the event.
        /// </summary>
        private static DateTime Closes( EventDate date, RegistrationInstanceRule rule ) => date.Start.Date.AddDays( -rule.ClosesDaysBefore );

        /// <summary>
        /// The last month and day on or before a day, such as April 30 before a June camp.
        /// </summary>
        internal static DateTime LastOnOrBefore( MonthDay monthDay, DateTime day )
        {
            var date = new DateTime( day.Year, monthDay.Month, monthDay.Day );
            return date <= day ? date : date.AddYears( -1 );
        }

        /// <summary>
        /// A name or slug with {year}, {month}, and {campus} filled in. A slug uses the lowercase month and the campus key.
        /// </summary>
        private string Fill( string pattern, DateTime day, string campusKey, bool isSlug )
        {
            var month = day.ToString( "MMMM", CultureInfo.InvariantCulture );
            var campus = campusKey == null ? string.Empty : isSlug ? campusKey : _campusNames[campusKey];
            return pattern
                .Replace( "{year}", day.Year.ToString( CultureInfo.InvariantCulture ) )
                .Replace( "{month}", isSlug ? month.ToLowerInvariant() : month )
                .Replace( "{campus}", campus );
        }

        /// <summary>
        /// One date of an event at one campus, and the occurrence it belongs to.
        /// </summary>
        private sealed record EventDate( DateTime Start, PlannedOccurrence Occurrence );
    }

    /// <summary>
    /// Every planned instance and the groups they link to.
    /// </summary>
    internal sealed class RegistrationPlan
    {
        public List<PlannedRegistrationInstance> Instances { get; } = new List<PlannedRegistrationInstance>();

        /// <summary>
        /// Linked and placement groups by key, in the order instances first name them.
        /// </summary>
        public Dictionary<string, PlannedRegistrationGroup> Groups { get; } = new Dictionary<string, PlannedRegistrationGroup>();

        /// <summary>
        /// The end day of each early-bird discount, by template key and discount key.
        /// </summary>
        public Dictionary<(string Template, string Discount), DateTime> DiscountEnds { get; } = new Dictionary<(string Template, string Discount), DateTime>();
    }

    /// <summary>
    /// One registration instance: its occurrence, dates, linkage, and groups.
    /// </summary>
    internal sealed class PlannedRegistrationInstance
    {
        public RegistrationInstanceRule Rule { get; set; }

        public RegistrationTemplateDefinition Template { get; set; }

        /// <summary>
        /// Whether this is the rule's next or last instance.
        /// </summary>
        public RegistrationOccurrencePick Pick { get; set; }

        /// <summary>
        /// The calendar occurrence the linkage points at.
        /// </summary>
        public PlannedOccurrence Occurrence { get; set; }

        /// <summary>
        /// The campus, or null for a church-wide instance.
        /// </summary>
        public string CampusKey { get; set; }

        /// <summary>
        /// When the event starts: the occurrence's first start, or one start of a monthly occurrence.
        /// </summary>
        public DateTime EventStart { get; set; }

        public DateTime EventDay => EventStart.Date;

        /// <summary>
        /// The first day of registration.
        /// </summary>
        public DateTime Opens { get; set; }

        /// <summary>
        /// The last day of registration.
        /// </summary>
        public DateTime Closes { get; set; }

        public DateTime? PaymentDeadline { get; set; }

        public DateTime? SendReminder { get; set; }

        public string Name { get; set; }

        public string PublicName { get; set; }

        public string Slug { get; set; }

        public int? MaxAttendees { get; set; }

        public Guid Guid { get; set; }

        public Guid LinkageGuid { get; set; }

        public PlannedRegistrationGroup LinkedGroup { get; set; }

        public List<PlannedPlacementGroup> PlacementGroups { get; } = new List<PlannedPlacementGroup>();

        /// <summary>
        /// True when registration is open on a day.
        /// </summary>
        public bool IsOpenOn( DateTime day ) => Opens <= day.Date && day.Date <= Closes;
    }

    /// <summary>
    /// A linked or placement group.
    /// </summary>
    internal sealed class PlannedRegistrationGroup
    {
        public string Key { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// The groupTypes key.
        /// </summary>
        public string GroupType { get; set; }

        public string CampusKey { get; set; }

        public Guid Guid { get; set; }

        /// <summary>
        /// The first instance that names the group, whose dates the group's audit dates follow.
        /// </summary>
        public PlannedRegistrationInstance FirstInstance { get; set; }
    }

    /// <summary>
    /// One placement group of an instance, in its placement's order.
    /// </summary>
    internal sealed record PlannedPlacementGroup( RegistrationPlacementDefinition Placement, PlannedRegistrationGroup Group, int Order );
}
