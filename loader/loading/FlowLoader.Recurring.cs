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
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /*
        10/30/26 - CLAUDE

        A recurring flow starts a run on each date of its schedule and fills it from its audience data view at
        that moment. Rock cannot run a data view as of a past day, so the loader works out each past run's
        audience from the same rule the view holds, over the weekend service check-ins the attendance part
        wrote. The flow points at the real guarded view, which a later step links once the reports part has
        written it, so Rock's job fills every run after install from the view itself. A paused flow keeps only
        runs that finished before install, since Rock would leave a paused run unsent.

        Reason: decisions D-comm-flows-1 and D-comm-flows-6.
    */

    internal sealed partial class FlowLoader
    {
        private const int Recurring = 1;
        private static readonly Guid WeekendServiceGroupTypeGuid = new Guid( "235BAE2B-5760-4763-AADF-3938F34BA100" );
        private static readonly Guid ActiveRecordStatusGuid = new Guid( "618F906C-C33D-4FA3-8AEF-E58CB7B63F1E" );

        /// <summary>
        /// Points each recurring flow at its guarded audience view, once the reports part has written it. A flow whose
        /// view this load left out is paused, since Rock's job cannot start a run without an audience.
        /// </summary>
        public static void LinkAudiences( RockDatabase db, SampleDataSource source, string seed )
        {
            foreach ( var flow in source.Flows?.Flows.Where( f => f.Trigger == "recurring" ) ?? Enumerable.Empty<FlowDefinition>() )
            {
                var viewGuid = source.Reporting?.DataViews.FirstOrDefault( v => v.Key == flow.Audience?.View )?.Guid;
                var viewId = viewGuid.HasValue ? db.IdByGuid( "DataView", viewGuid.Value ) : null;
                var flowGuid = SeededRandom.StableGuid( $"{seed}:flow:{flow.Key}" );
                var changed = db.Execute( viewId.HasValue
                        ? "UPDATE [CommunicationFlow] SET [TargetAudienceDataViewId] = @ViewId WHERE [Guid] = @Guid"
                        : "UPDATE [CommunicationFlow] SET [IsActive] = 0 WHERE [Guid] = @Guid",
                    P( "@ViewId", ( object ) viewId ?? DBNull.Value ), P( "@Guid", flowGuid ) );
                if ( changed > 0 && !viewId.HasValue )
                {
                    db.Log.Write( $"  {flow.Name} is paused: this load has no {flow.Audience?.View} data view" );
                }
            }
        }

        /// <summary>
        /// Each run date's audience for a recurring flow, by the rule its data view holds, as of that date.
        /// </summary>
        private List<(DateTime Start, List<EmailPerson> People)> RecurringRuns( FlowDefinition flow, FlowContext context, DateTime firstDay )
        {
            var facts = AudienceFacts( context );
            var runs = new List<(DateTime Start, List<EmailPerson> People)>();
            foreach ( var day in RunDates( flow, firstDay, _dates.Anchor.Date.AddDays( -1 ) ) )
            {
                if ( !flow.IsActive && LastSend( flow, day ) >= _dates.Anchor )
                {
                    continue;
                }

                var people = AudienceOn( flow, facts, day ).Where( p => context.ByAlias.ContainsKey( p.AliasId ) )
                    .Select( p => context.ByAlias[p.AliasId] ).OrderBy( p => p.PersonId ).ToList();
                if ( people.Count > 0 )
                {
                    runs.Add( (day, people) );
                }
            }

            return runs;
        }

        /*
            10/30/26 - CLAUDE

            Each rule is the audience view's filters, with Rock's own sliding date ranges. "Last N days" runs from
            N-1 days before through the day itself, "last N weeks" from the start of the week N-1 weeks back (Rock's
            weeks start on Monday), and "last N months" from the first of the month N-1 months back, each up to the
            end of the day. Rock's job fills a run from the view early on its start day, so the day is the moment.
            Every audience view carries the reserved-email guard, so the rule does too. Verify runs the same rule as
            of install day and checks it finds the same people as Rock's own engine running the view.

            Reason: the coordinator's guardrail that the loader's copy of a rule and Rock's view never drift apart.
        */

        /// <summary>
        /// Who a recurring flow's audience view finds on a day, by its rule.
        /// </summary>
        private static IEnumerable<AudiencePerson> AudienceOn( FlowDefinition flow, List<AudiencePerson> facts, DateTime day )
        {
            var end = day.Date.AddDays( 1 );
            DateTime LastDays( int n ) => end.AddDays( -n );
            DateTime LastWeeks( int n ) => end.AddDays( -( ( ( int ) day.DayOfWeek + 6 ) % 7 ) - 1 ).AddDays( 7 ).AddDays( -7 * n );
            DateTime LastMonths( int n ) => new DateTime( day.Year, day.Month, 1 ).AddMonths( 1 - n );
            Func<AudiencePerson, bool> rule = flow.Audience?.Rule switch
            {
                "firstVisitLastWeek" => p => p.IsAdult && p.AcceptsMassEmail && p.FirstVisit.HasValue && p.FirstVisit.Value >= LastDays( 7 ) && p.FirstVisit.Value < end,
                "newlyMissed" => p => p.IsAdult && p.AcceptsMassEmail && p.Count( LastMonths( 18 ), end ) >= 10 && p.Count( LastDays( 90 ), end ) == 0 && p.Count( LastDays( 120 ), end ) >= 1,
                "unbaptizedRegularAttenders" => p => p.IsAdult && p.Count( LastWeeks( 12 ), end ) >= 4 && !( p.Baptized.HasValue && p.Baptized.Value < end ),
                _ => throw new InvalidOperationException( $"configuration/flows.json: flow {flow.Key} has audience rule '{flow.Audience?.Rule}', which the loader does not know." )
            };

            return facts.Where( p => p.IsActive && p.IsReserved && rule( p ) );
        }

        /// <summary>
        /// Who each recurring flow's audience view finds on install day, by the loader's rule, for verify to compare with
        /// Rock's own engine running the view.
        /// </summary>
        internal static Dictionary<Guid, HashSet<int>> AudiencesOnInstallDay( RockDatabase db, SampleDataSource source, DateTime anchor, string foreignKey, string seed )
        {
            var loader = new FlowLoader( db, source, new AnchorDates( anchor ), foreignKey, seed );
            var context = new FlowContext
            {
                ReservedEmailSuffix = "@" + ( source.Flows?.ReservedEmailDomain ?? "example.org" ),
                EmailAddresses = db.Query( "SELECT [Id], [Email] FROM [Person] WHERE [ForeignKey] = @ForeignKey OR [Id] = 1", P( "@ForeignKey", foreignKey ) )
                    .ToDictionary( r => ( int ) r["Id"], r => r["Email"] as string )
            };
            var facts = loader.AudienceFacts( context );
            return ( source.Flows?.Flows ?? new List<FlowDefinition>() )
                .Where( f => f.Trigger == "recurring" && source.Reporting?.DataViews.Any( v => v.Key == f.Audience?.View ) == true )
                .ToDictionary( f => source.Reporting.DataViews.First( v => v.Key == f.Audience.View ).Guid, f => AudienceOn( f, facts, anchor.Date ).Select( p => p.PersonId ).ToHashSet() );
        }

        /// <summary>
        /// The dates a recurring flow's runs start, from <paramref name="from"/> through <paramref name="until"/>.
        /// </summary>
        private List<DateTime> RunDates( FlowDefinition flow, DateTime from, DateTime until )
        {
            var schedule = flow.Schedule;
            var dates = new List<DateTime>();
            switch ( schedule?.Kind )
            {
                case "weekly":
                    var weekday = Weekday( schedule.Weekday );
                    for ( var day = from.AddDays( ( ( int ) weekday - ( int ) from.DayOfWeek + 7 ) % 7 ); day <= until; day = day.AddDays( 7 ) )
                    {
                        dates.Add( day );
                    }

                    break;
                case "monthly":
                    for ( var month = new DateTime( from.Year, from.Month, 1 ); month <= until; month = month.AddMonths( 1 ) )
                    {
                        var first = month.AddDays( ( ( int ) Weekday( schedule.Weekday ) - ( int ) month.DayOfWeek + 7 ) % 7 );
                        var day = first.AddDays( 7 * ( schedule.Week - 1 ) );
                        if ( day >= from && day <= until && day.Month == month.Month )
                        {
                            dates.Add( day );
                        }
                    }

                    break;
                case "beforeBaptismSundays":
                    var baptism = _source.Steps.StepTypes.Single( t => t.Source?.Kind == "baptism" ).Source;
                    for ( var year = from.Year; year <= until.Year + 1; year++ )
                    {
                        dates.AddRange( new[] { SundayCalendar.Easter( year ), baptism.SummerSunday.In( year ) }
                            .Select( d => d.AddDays( -schedule.DaysBefore ) ).Where( d => d >= from && d <= until ) );
                    }

                    dates.Sort();
                    break;
                default:
                    throw new InvalidOperationException( $"configuration/flows.json: flow {flow.Key} has schedule '{schedule?.Kind}', which the loader does not know." );
            }

            return dates;
        }

        /*
            10/30/26 - CLAUDE

            Rock's flow editor saves an unnamed schedule. A weekly or monthly one is a rule with no end, so
            Rock leaves its effective end date empty. The baptism Sundays are specific dates, listed from the
            flow's first run through two years after install, so its effective end date is the last one. Rock's
            schedule save hook writes a ScheduleDate row for each start from two years before to two years after,
            counted from the install day at midnight, as the reports part's schedules do.
        */

        /// <summary>
        /// Writes the recurring flow's schedule and its ScheduleDate rows.
        /// </summary>
        private int WriteSchedule( FlowDefinition flow, DateTime firstRun, DateTime created )
        {
            var time = TimeSpan.Parse( flow.Schedule.Time ?? "09:00", CultureInfo.InvariantCulture );
            var today = _dates.Anchor.Date;
            string rule;
            DateTime? effectiveEnd = null;
            List<DateTime> starts;
            if ( flow.Schedule.Kind == "beforeBaptismSundays" )
            {
                var dates = RunDates( flow, firstRun, today.AddYears( 2 ) );
                rule = dates.Count > 1 ? $"RDATE:{string.Join( ",", dates.Skip( 1 ).Select( d => d.Add( time ).ToString( "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture ) ) )}\r\n" : string.Empty;
                effectiveEnd = dates.Last().Date;
                starts = dates;
            }
            else
            {
                var day = Weekday( flow.Schedule.Weekday ).ToString().Substring( 0, 2 ).ToUpperInvariant();
                rule = flow.Schedule.Kind == "weekly" ? $"RRULE:FREQ=WEEKLY;BYDAY={day}\r\n" : $"RRULE:FREQ=MONTHLY;BYDAY={flow.Schedule.Week}{day}\r\n";
                starts = RunDates( flow, firstRun, today.AddYears( 2 ) );
            }

            var start = firstRun.Add( time );
            var calendar = $"BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTEND:{start.AddMinutes( 1 ):yyyyMMdd'T'HHmmss}\r\nDTSTART:{start:yyyyMMdd'T'HHmmss}\r\n{rule}END:VEVENT\r\nEND:VCALENDAR";
            var scheduleId = Convert.ToInt32( _db.Scalar( @"
INSERT INTO [Schedule] ( [iCalendarContent], [EffectiveStartDate], [EffectiveEndDate], [IsActive], [Order], [AutoInactivateWhenComplete], [IsPublic], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
OUTPUT INSERTED.[Id]
VALUES ( @Calendar, @Start, @End, 1, 0, 0, 0, @Guid, @ForeignKey, @Created, @Created )",
                P( "@Calendar", calendar ), P( "@Start", firstRun.Date ), P( "@End", ( object ) effectiveEnd ?? DBNull.Value ),
                P( "@Guid", SeededRandom.StableGuid( $"{_seed}:flow:{flow.Key}:schedule" ) ), P( "@ForeignKey", _foreignKey ), P( "@Created", created ) ) );

            var scheduleDates = TableFactory.Create( "ScheduleId:int", "StartDateTime:date", "EndDateTime:date", "StartDateKey:int" );
            foreach ( var day in starts )
            {
                var at = day.Add( time );
                if ( at < today.AddYears( 2 ) && at.AddMinutes( 1 ) > today.AddYears( -2 ) )
                {
                    scheduleDates.Rows.Add( scheduleId, at, at.AddMinutes( 1 ), int.Parse( at.ToString( "yyyyMMdd", CultureInfo.InvariantCulture ), CultureInfo.InvariantCulture ) );
                }
            }

            _db.BulkInsert( "ScheduleDate", scheduleDates, keepIdentity: false );
            return scheduleId;
        }

        /// <summary>
        /// The goals that read data views and forms: their settings as Rock's flow editor stores them, and each
        /// person's dates that meet them.
        /// </summary>
        private GoalPlan RecurringGoal( FlowDefinition flow, FlowContext context, int type )
        {
            if ( flow.Goal.Type == "completedForm" )
            {
                var workflowTypeGuid = SeededRandom.StableGuid( $"workflow:{flow.Goal.Workflow}:type" );
                var forms = _db.Query( @"
SELECT [pa].[PersonId], [w].[CompletedDateTime]
FROM [Workflow] AS [w]
INNER JOIN [WorkflowType] AS [wt] ON [wt].[Id] = [w].[WorkflowTypeId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [w].[InitiatorPersonAliasId]
WHERE [wt].[Guid] = @Type AND [w].[CompletedDateTime] IS NOT NULL", P( "@Type", workflowTypeGuid ) )
                    .GroupBy( r => ( int ) r["PersonId"] )
                    .ToDictionary( g => g.Key, g => g.Select( r => ( DateTime ) r["CompletedDateTime"] ).OrderBy( d => d ).ToList() );
                return new GoalPlan
                {
                    TypeValue = type,
                    SettingsJson = JsonSerializer.Serialize( new { ConversionGoalSettings = new { CompletedFormSettings = new { WorkflowTypeGuid = workflowTypeGuid } } } ),
                    Dates = forms,
                    WindowEnd = ( start, days ) => start.AddDays( days )
                };
            }

            // Entered Data View: a person is in "came to worship in the last 30 days" from each check-in on, so each check-in is a date that meets it.
            if ( flow.Goal.Rule != "backInLast30Days" )
            {
                throw new InvalidOperationException( $"configuration/flows.json: flow {flow.Key} has goal rule '{flow.Goal.Rule}', which the loader does not know." );
            }

            var viewGuid = _source.Reporting?.DataViews.FirstOrDefault( v => v.Key == flow.Goal.View )?.Guid
                ?? throw new InvalidOperationException( $"configuration/flows.json: flow {flow.Key} names data view '{flow.Goal.View}', which configuration/reporting.json does not have." );
            var checkIns = AudienceFacts( context ).Where( p => p.CheckIns.Length > 0 ).ToDictionary( p => p.PersonId, p => p.CheckIns.Select( FromDay ).ToList() );
            return new GoalPlan
            {
                TypeValue = type,
                SettingsJson = JsonSerializer.Serialize( new { ConversionGoalSettings = new { EnteredDataViewSettings = new { DataViewGuid = viewGuid } } } ),
                Dates = checkIns,
                WindowEnd = ( start, days ) => start.AddDays( days ),
                PreMet = ( dates, start ) => dates.Any( d => d >= start.AddDays( -29 ) && d < start.AddDays( 1 ) )
            };
        }

        /// <summary>
        /// What the audience rules read about each person: record status, age group, email, first visit, baptism, and
        /// every weekend service check-in. Loaded once.
        /// </summary>
        private List<AudiencePerson> AudienceFacts( FlowContext context )
        {
            if ( context.Audience != null )
            {
                return context.Audience;
            }

            var activeId = _db.RequireIdByGuid( "DefinedValue", ActiveRecordStatusGuid );
            var checkIns = _db.Query( @"
SELECT [pa].[PersonId], CAST( [o].[OccurrenceDate] AS DATE ) AS [Day]
FROM [Attendance] AS [a]
INNER JOIN [AttendanceOccurrence] AS [o] ON [o].[Id] = [a].[OccurrenceId]
INNER JOIN [Group] AS [g] ON [g].[Id] = [o].[GroupId]
INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [a].[PersonAliasId]
WHERE [gt].[Guid] = @Type AND [a].[DidAttend] = 1", P( "@Type", WeekendServiceGroupTypeGuid ) )
                .GroupBy( r => ( int ) r["PersonId"] )
                .ToDictionary( g => g.Key, g => g.Select( r => ToDay( ( DateTime ) r["Day"] ) ).OrderBy( d => d ).ToArray() );
            var baptismType = _source.Steps.StepTypes.Single( t => t.Source?.Kind == "baptism" );
            var baptized = _db.Query( @"
SELECT [pa].[PersonId], MIN( [s].[CompletedDateTime] ) AS [Day]
FROM [Step] AS [s]
INNER JOIN [StepType] AS [st] ON [st].[Id] = [s].[StepTypeId]
INNER JOIN [StepStatus] AS [ss] ON [ss].[Id] = [s].[StepStatusId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [s].[PersonAliasId]
WHERE [st].[Guid] = @Type AND [s].[CompletedDateTime] IS NOT NULL AND [ss].[IsCompleteStatus] = 1
GROUP BY [pa].[PersonId]", P( "@Type", baptismType.Guid ) )
                .ToDictionary( r => ( int ) r["PersonId"], r => ( DateTime ) r["Day"] );

            context.Audience = _db.Query( @"
SELECT [p].[Id], [p].[RecordStatusValueId], [p].[AgeClassification], [p].[EmailPreference], [p].[Email], [pa].[Id] AS [AliasId]
FROM [Person] AS [p]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[PersonId] = [p].[Id] AND [pa].[AliasPersonId] = [p].[Id]
WHERE [p].[ForeignKey] = @ForeignKey OR [p].[Id] = 1", P( "@ForeignKey", _foreignKey ) )
                .Select( r =>
                {
                    var id = ( int ) r["Id"];
                    var email = r["Email"] as string;
                    var days = checkIns.GetValueOrDefault( id ) ?? Array.Empty<int>();
                    return new AudiencePerson
                    {
                        PersonId = id,
                        AliasId = ( int ) r["AliasId"],
                        IsActive = r["RecordStatusValueId"] is int status && status == activeId,
                        IsAdult = Convert.ToInt32( r["AgeClassification"] ) == 1,
                        AcceptsMassEmail = Convert.ToInt32( r["EmailPreference"] ) == 0 && !string.IsNullOrWhiteSpace( email ),
                        IsReserved = IsReserved( id, context ),
                        CheckIns = days,
                        FirstVisit = days.Length > 0 ? FromDay( days[0] ) : null,
                        Baptized = baptized.TryGetValue( id, out var b ) ? b : null
                    };
                } )
                .ToList();
            return context.Audience;
        }

        private static DayOfWeek Weekday( string name ) => ( DayOfWeek ) Enum.Parse( typeof( DayOfWeek ), name, true );

        private static int ToDay( DateTime date ) => ( int ) ( date.Date - DateTime.MinValue ).TotalDays;

        private static DateTime FromDay( int day ) => DateTime.MinValue.AddDays( day );

        /// <summary>
        /// One person as the audience rules see them.
        /// </summary>
        private sealed class AudiencePerson
        {
            public int PersonId { get; set; }

            public int AliasId { get; set; }

            public bool IsActive { get; set; }

            public bool IsAdult { get; set; }

            public bool AcceptsMassEmail { get; set; }

            public bool IsReserved { get; set; }

            /// <summary>
            /// Each weekend service check-in, as a day number, in order.
            /// </summary>
            public int[] CheckIns { get; set; }

            public DateTime? FirstVisit { get; set; }

            public DateTime? Baptized { get; set; }

            /// <summary>
            /// How many weekend service check-ins fall from <paramref name="from"/> up to <paramref name="until"/>.
            /// </summary>
            public int Count( DateTime from, DateTime until )
            {
                var low = Array.BinarySearch( CheckIns, ToDay( from ) );
                var high = Array.BinarySearch( CheckIns, ToDay( until ) );
                low = low < 0 ? ~low : FirstIndex( low );
                high = high < 0 ? ~high : FirstIndex( high );
                return high - low;
            }

            private int FirstIndex( int index )
            {
                while ( index > 0 && CheckIns[index - 1] == CheckIns[index] )
                {
                    index--;
                }

                return index;
            }
        }
    }
}
