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

using Microsoft.Data.SqlClient;

using RockSampleData.Loader.Generation;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /// <summary>
    /// The weekly metrics Rock counts with SQL: each run counts the rows dated on the run's day and the seven days before,
    /// as Rock's SQL does, and on the run's own day only the rows that came before the run. Baptisms, which runs monthly, is here too.
    /// </summary>
    internal sealed partial class RockMetricsLoader
    {
        private const string WeekendTotalKey = "total-weekend-attendance";

        private static readonly Guid PersonRecordTypeGuid = new Guid( "36CF10D6-C695-413D-8E7C-4546EFEF385E" );
        private static readonly Guid PersonEntityTypeGuid = new Guid( "72657ED8-D16E-492E-AC12-144C5E7567E7" );
        private static readonly Guid CampusEntityTypeGuid = new Guid( "00096BED-9587-415E-8AD4-4E076AE8FBF0" );
        private static readonly Guid ScheduleEntityTypeGuid = new Guid( "0B2C38A7-D79C-4F85-9757-F1B045D32C8A" );
        private static readonly Guid ServingAreaPurposeGuid = new Guid( "36A554CE-7815-41B9-A435-93F3D52A2828" );
        private static readonly Guid PrayerChannelGuid = new Guid( "3D49FB99-94D1-4F63-B1A2-30D4FEDE11E9" );
        private static readonly Guid PersonAnalyticsCategoryGuid = new Guid( "C1524D2E-3E8F-3D83-45F8-526B749D79F0" );
        private static readonly Guid BaptismDateGuid = new Guid( "D42763FA-28E9-4A55-A25A-48998D7D7FEF" );

        /// <summary>
        /// The measurement classifications Rock's Total Weekend Attendance adds up: children, students, volunteers, and adults.
        /// </summary>
        private static readonly Guid[] WeekendPartGuids =
        {
            new Guid( "9B16A979-48B1-4180-B44F-57FCD38A103A" ),
            new Guid( "8EC797E4-7DCE-4A70-B1E8-9B21192476C3" ),
            new Guid( "6A2621BF-E600-428A-94C2-CCB79645FA27" ),
            new Guid( "66C649EF-7569-4CE2-8EA0-FB9851F9A598" )
        };

        /// <summary>
        /// The check-in areas each of Rock's attendance metrics names.
        /// </summary>
        private enum AttendanceArea
        {
            Children,
            Students,
            Volunteers
        }

        /// <summary>
        /// A group a metric value is for, such as a campus, or a campus and a schedule.
        /// </summary>
        private readonly record struct Group( int? First, int? Second );

        /// <summary>
        /// An amount of rows on one day for one group. A late row came after the run's time of day, so on the run's own day
        /// Rock's job had not seen it yet.
        /// </summary>
        private readonly record struct DayAmount( DateTime Day, bool IsLate, Group Group, decimal Amount );

        /// <summary>
        /// Rock's three attendance metrics: check-ins that attended, by campus and schedule. Children and students are the
        /// weekend areas Rock's SQL names, and volunteers are every serving area.
        /// </summary>
        private List<MetricValueRow> Attendance( RockMetric metric, AttendanceArea area )
        {
            var filter = area switch
            {
                AttendanceArea.Children => "[gt].[AttendanceCountsAsWeekendService] = 1 AND [gt].[Guid] IN ( 'CADB2D12-7836-44BC-8EEA-3C6AB22FD5E8', 'E3C8F7D6-5CEB-43BB-802F-66C3E734049E' )",
                AttendanceArea.Students => "[gt].[AttendanceCountsAsWeekendService] = 1 AND [gt].[Guid] IN ( '7A17235B-69AD-439B-BAB0-1A0A472DB96F', '9A88743B-F336-4404-B877-2A623689195D' )",
                _ => "[gt].[GroupTypePurposeValueId] = ( SELECT [Id] FROM [DefinedValue] WHERE [Guid] = @ServingArea )"
            };

            return Weekly( metric, $@"
SELECT CAST( [a].[StartDateTime] AS date ) AS [Day],
    CAST( CASE WHEN CAST( [a].[StartDateTime] AS time ) > @RunTime THEN 1 ELSE 0 END AS bit ) AS [IsLate],
    [a].[CampusId] AS [First], [o].[ScheduleId] AS [Second], COUNT(*) AS [Amount]
FROM [Attendance] AS [a]
INNER JOIN [AttendanceOccurrence] AS [o] ON [o].[Id] = [a].[OccurrenceId]
INNER JOIN [Group] AS [g] ON [g].[Id] = [o].[GroupId]
INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId]
WHERE [a].[DidAttend] = 1 AND [a].[StartDateTime] >= @From AND [a].[StartDateTime] < @To AND {filter}
GROUP BY CAST( [a].[StartDateTime] AS date ), CASE WHEN CAST( [a].[StartDateTime] AS time ) > @RunTime THEN 1 ELSE 0 END, [a].[CampusId], [o].[ScheduleId]",
                null, g => new[] { g.First, g.Second }, P( "@ServingArea", ServingAreaPurposeGuid ) );
        }

        /// <summary>
        /// Rock's Prayer Requests: active requests made in the window, by the request's campus. Rock's SQL groups by every
        /// campus any request has, so a week with none at a campus still gets a zero.
        /// </summary>
        private List<MetricValueRow> PrayerRequests( RockMetric metric )
        {
            var groups = _db.Query( "SELECT DISTINCT [CampusId] FROM [PrayerRequest]" ).Select( r => new Group( r["CampusId"] as int?, null ) ).ToList();
            return Weekly( metric, @"
SELECT CAST( [CreatedDateTime] AS date ) AS [Day],
    CAST( CASE WHEN CAST( [CreatedDateTime] AS time ) > @RunTime THEN 1 ELSE 0 END AS bit ) AS [IsLate],
    [CampusId] AS [First], NULL AS [Second], COUNT(*) AS [Amount]
FROM [PrayerRequest]
WHERE [IsActive] = 1 AND [CreatedDateTime] >= @From AND [CreatedDateTime] < @To
GROUP BY CAST( [CreatedDateTime] AS date ), CASE WHEN CAST( [CreatedDateTime] AS time ) > @RunTime THEN 1 ELSE 0 END, [CampusId]",
                groups, g => new[] { g.First } );
        }

        /// <summary>
        /// Rock's Prayers: each time someone prayed for a request, by the campus of the person who prayed. Rock's SQL
        /// groups by every campus of anyone with an interaction on a component whose entity is a prayer request.
        /// </summary>
        private List<MetricValueRow> Prayers( RockMetric metric )
        {
            const string joins = @"
FROM [Interaction] AS [i]
INNER JOIN [InteractionComponent] AS [ic] ON [ic].[Id] = [i].[InteractionComponentId]
INNER JOIN [InteractionChannel] AS [ichan] ON [ichan].[Id] = [ic].[InteractionChannelId]
INNER JOIN [PrayerRequest] AS [pr] ON [pr].[Id] = [ic].[EntityId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [i].[PersonAliasId]
INNER JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]";
            var groups = _db.Query( $"SELECT DISTINCT [p].[PrimaryCampusId] {joins}" ).Select( r => new Group( r["PrimaryCampusId"] as int?, null ) ).ToList();
            return Weekly( metric, $@"
SELECT CAST( [i].[InteractionDateTime] AS date ) AS [Day],
    CAST( CASE WHEN CAST( [i].[InteractionDateTime] AS time ) > @RunTime THEN 1 ELSE 0 END AS bit ) AS [IsLate],
    [p].[PrimaryCampusId] AS [First], NULL AS [Second], COUNT(*) AS [Amount]
{joins}
WHERE [ichan].[Guid] = @PrayerChannel AND [i].[Operation] = 'Prayed' AND [i].[InteractionDateTime] >= @From AND [i].[InteractionDateTime] < @To
GROUP BY CAST( [i].[InteractionDateTime] AS date ), CASE WHEN CAST( [i].[InteractionDateTime] AS time ) > @RunTime THEN 1 ELSE 0 END, [p].[PrimaryCampusId]",
                groups, g => new[] { g.First }, P( "@PrayerChannel", PrayerChannelGuid ) );
        }

        /// <summary>
        /// Rock's Giving: gifts from people (not businesses) to the tithe, the active, tax-deductible top-level accounts
        /// that are open on the run's day and their tax-deductible child accounts, by the account's campus.
        /// </summary>
        private List<MetricValueRow> Giving( RockMetric metric )
        {
            if ( metric.Runs.Count == 0 )
            {
                return new List<MetricValueRow>();
            }

            var accounts = _db.Query( "SELECT [Id], [ParentAccountId], [IsTaxDeductible], [IsActive], [StartDate], [EndDate], [CampusId] FROM [FinancialAccount]" )
                .Select( r => (Id: ( int ) r["Id"], Parent: r["ParentAccountId"] as int?, IsTaxDeductible: ( bool ) r["IsTaxDeductible"], IsActive: ( bool ) r["IsActive"],
                    Start: ( r["StartDate"] as DateTime? )?.Date, End: ( r["EndDate"] as DateTime? )?.Date, Campus: r["CampusId"] as int?) )
                .ToList();
            var campusOf = accounts.ToDictionary( a => a.Id, a => a.Campus );

            // Rock's SQL picks the tithe accounts on the run's day, and then counts their whole tree.
            HashSet<int> TitheAccounts( DateTime day )
            {
                var tree = accounts.Where( a => a.Parent == null && a.IsTaxDeductible && a.IsActive
                    && ( a.Start != null && a.End != null && day >= a.Start && day <= a.End || a.End != null && day < a.End || a.Start != null && day > a.Start || a.Start == null && a.End == null ) )
                    .Select( a => a.Id ).ToHashSet();
                for ( var added = true; added; )
                {
                    var children = accounts.Where( a => a.Parent != null && tree.Contains( a.Parent.Value ) && !tree.Contains( a.Id ) ).Select( a => a.Id ).ToList();
                    tree.UnionWith( children );
                    added = children.Count > 0;
                }

                return accounts.Where( a => tree.Contains( a.Id ) && a.IsTaxDeductible ).Select( a => a.Id ).ToHashSet();
            }

            var (from, to) = Range( metric );
            var gifts = _db.Query( @"
SELECT [ft].[TransactionDateKey] AS [DateKey],
    CAST( CASE WHEN CAST( [ft].[TransactionDateTime] AS time ) > @RunTime THEN 1 ELSE 0 END AS bit ) AS [IsLate],
    [ftd].[AccountId], SUM( [ftd].[Amount] ) AS [Amount]
FROM [Person] AS [p]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[PersonId] = [p].[Id]
INNER JOIN [FinancialTransaction] AS [ft] ON [ft].[AuthorizedPersonAliasId] = [pa].[Id]
INNER JOIN [FinancialTransactionDetail] AS [ftd] ON [ftd].[TransactionId] = [ft].[Id]
INNER JOIN [FinancialAccount] AS [fa] ON [fa].[Id] = [ftd].[AccountId]
INNER JOIN [AnalyticsSourceDate] AS [asd] ON [asd].[DateKey] = [ft].[TransactionDateKey]
WHERE [fa].[IsTaxDeductible] = 1
    AND [p].[RecordTypeValueId] = ( SELECT [Id] FROM [DefinedValue] WHERE [Guid] = @PersonRecordType )
    AND [ft].[TransactionDateKey] BETWEEN @FromKey AND @ToKey
GROUP BY [ft].[TransactionDateKey], CASE WHEN CAST( [ft].[TransactionDateTime] AS time ) > @RunTime THEN 1 ELSE 0 END, [ftd].[AccountId]",
                P( "@RunTime", metric.Runs[0].TimeOfDay ), P( "@PersonRecordType", PersonRecordTypeGuid ), P( "@FromKey", DateKey( from ) ), P( "@ToKey", DateKey( to.AddDays( -1 ) ) ) )
                .Select( r => (Day: DateTime.ParseExact( r["DateKey"].ToString(), "yyyyMMdd", CultureInfo.InvariantCulture ), IsLate: ( bool ) r["IsLate"], Account: ( int ) r["AccountId"], Amount: ( decimal ) r["Amount"]) )
                .ToList();

            var random = new SeededRandom( $"{_seed}:rock-metric-runs:{metric.Key}" );
            var values = new List<MetricValueRow>();
            foreach ( var run in metric.Runs )
            {
                var saved = SavedAfter( run, random );
                var tithe = TitheAccounts( run.Date );
                var first = run.Date.AddDays( -7 );
                foreach ( var campus in gifts.Where( g => g.Day >= first && g.Day <= run.Date && !( g.Day == run.Date && g.IsLate ) && tithe.Contains( g.Account ) )
                    .GroupBy( g => new Group( campusOf[g.Account], null ) ) )
                {
                    values.Add( new MetricValueRow( run, saved, Stored( campus.Sum( g => g.Amount ) ), new[] { campus.Key.First } ) );
                }
            }

            return values;
        }

        /*
            11/23/26 - CLAUDE

            Rock's eRA job writes a history line each time a person enters eRA and each time they exit, on the day it sets
            their eRA Start Date or End Date. So each line is a win or a loss at the runs that saw it, which reads back
            every entry and exit, not only each person's latest. Rock's shipped Weekly eRA Losses SQL counts people whose
            Currently an eRA value is false, and the job never saves false: it deletes the value when a family exits. So
            the load saves the church's SQL for that metric, which counts people whose eRA ended in the week and who are no
            longer an eRA, and the history counts the exit lines the same way.

            Reason: Every exit shows on the chart, and Rock's own runs after install continue the same line.
        */

        /// <summary>
        /// Weekly eRA Wins: people whose eRA began in the window and who were an eRA at the run, by their campus.
        /// </summary>
        private List<MetricValueRow> EraWins( RockMetric metric ) => EraChanges( metric, "ENTERED" );

        /// <summary>
        /// Weekly eRA Losses, as the church's SQL counts it: people whose eRA ended in the window and who were no longer an
        /// eRA at the run, by their campus.
        /// </summary>
        private List<MetricValueRow> EraLosses( RockMetric metric ) => EraChanges( metric, "EXITED" );

        /// <summary>
        /// The people Rock's eRA job moved in or out of eRA in each run's window, from its history lines. Rock's SQL groups
        /// by the campus of every person with an attribute value.
        /// </summary>
        private List<MetricValueRow> EraChanges( RockMetric metric, string verb )
        {
            return Weekly( metric, @"
SELECT CAST( [h].[CreatedDateTime] AS date ) AS [Day],
    CAST( CASE WHEN CAST( [h].[CreatedDateTime] AS time ) > @RunTime THEN 1 ELSE 0 END AS bit ) AS [IsLate],
    [p].[PrimaryCampusId] AS [First], NULL AS [Second], COUNT(*) AS [Amount]
FROM [History] AS [h]
INNER JOIN [Person] AS [p] ON [p].[Id] = [h].[EntityId]
WHERE [h].[CategoryId] = ( SELECT [Id] FROM [Category] WHERE [Guid] = @PersonAnalytics )
    AND [h].[EntityTypeId] = ( SELECT [Id] FROM [EntityType] WHERE [Guid] = @PersonEntity )
    AND [h].[Verb] = @Verb AND [h].[CreatedDateTime] >= @From AND [h].[CreatedDateTime] < @To
GROUP BY CAST( [h].[CreatedDateTime] AS date ), CASE WHEN CAST( [h].[CreatedDateTime] AS time ) > @RunTime THEN 1 ELSE 0 END, [p].[PrimaryCampusId]",
                PersonAttributeGroups(), g => new[] { g.First }, P( "@PersonAnalytics", PersonAnalyticsCategoryGuid ), P( "@PersonEntity", PersonEntityTypeGuid ), P( "@Verb", verb ) );
        }

        /*
            11/23/26 - CLAUDE

            Rock runs Baptisms on the 1st of each month at 4 AM, and its shipped SQL counts the month that has just begun,
            up to the day before the month's last day, so nearly every value it saves is zero. The church's SQL, which the
            load saves in place of Rock's, counts the whole month before the run, by campus. The loader counts each run's
            month the same way, from the Baptism Date values the load wrote.

            Reason: Jon's answer to alpha.19 plan question 6. Each value on the 1st is the month that just ended, and
            Rock's own runs after install continue the same line.
        */

        /// <summary>
        /// Baptisms, as the church's SQL counts it: people whose Baptism Date is in the month before the run, by campus.
        /// It groups by every campus Rock's eRA SQL does, so a month with none at a campus is a zero.
        /// </summary>
        private List<MetricValueRow> Baptisms( RockMetric metric )
        {
            if ( metric.Runs.Count == 0 )
            {
                return new List<MetricValueRow>();
            }

            var rows = _db.Query( @"
SELECT CAST( [av].[ValueAsDateTime] AS date ) AS [Day], [p].[PrimaryCampusId], COUNT(*) AS [Count]
FROM [Person] AS [p]
INNER JOIN [AttributeValue] AS [av] ON [av].[EntityId] = [p].[Id]
WHERE [av].[AttributeId] = ( SELECT [Id] FROM [Attribute] WHERE [Guid] = @BaptismDate )
    AND [av].[ValueAsDateTime] >= @From AND [av].[ValueAsDateTime] < @To
GROUP BY CAST( [av].[ValueAsDateTime] AS date ), [p].[PrimaryCampusId]",
                P( "@BaptismDate", BaptismDateGuid ), P( "@From", MonthBefore( metric.Runs[0] ) ), P( "@To", MonthBefore( metric.Runs[metric.Runs.Count - 1] ).AddMonths( 1 ) ) )
                .Select( r => (Day: ( DateTime ) r["Day"], Campus: r["PrimaryCampusId"] as int?, Count: ( int ) r["Count"]) )
                .ToList();

            var groups = PersonAttributeGroups();
            var random = new SeededRandom( $"{_seed}:rock-metric-runs:{metric.Key}" );
            var values = new List<MetricValueRow>();
            foreach ( var run in metric.Runs )
            {
                var saved = SavedAfter( run, random );
                var first = MonthBefore( run );
                var counts = rows.Where( r => r.Day >= first && r.Day < first.AddMonths( 1 ) ).GroupBy( r => r.Campus ).ToDictionary( g => new Group( g.Key, null ), g => g.Sum( r => r.Count ) );
                values.AddRange( groups.Select( g => new MetricValueRow( run, saved, counts.GetValueOrDefault( g ), new[] { g.First } ) ) );
            }

            return values;
        }

        /// <summary>
        /// The first day of the month before a run's month.
        /// </summary>
        private static DateTime MonthBefore( DateTime run ) => new DateTime( run.Year, run.Month, 1 ).AddMonths( -1 );

        /// <summary>
        /// The campuses Rock's eRA and Baptisms SQL group by: those of every person whose Id is the entity of an attribute value.
        /// </summary>
        private List<Group> PersonAttributeGroups()
        {
            return _db.Query( "SELECT DISTINCT [p].[PrimaryCampusId] FROM [Person] AS [p] WHERE EXISTS ( SELECT 1 FROM [AttributeValue] AS [av] WHERE [av].[EntityId] = [p].[Id] )" )
                .Select( r => new Group( r["PrimaryCampusId"] as int?, null ) ).ToList();
        }

        /*
            11/23/26 - CLAUDE

            Rock 21's Total Weekend Attendance adds up the values of every metric whose measurement is children's,
            students', volunteer, or adult attendance, by campus and schedule. Rock's job runs it last on Monday, after the
            other weekend metrics. Rock's shipped SQL reads values dated from seven days before the run, which holds that
            Monday's values and the previous Monday's, so it counts each week's children, students, and volunteers twice.
            The load saves the church's SQL for it, which starts the window six days before the run: that Monday's values
            and the adult counts staff typed on Sunday. The loader adds up the values it just worked out the same way.

            Reason: The chart shows each weekend's real total, and Rock's own runs after install continue the same line.
        */

        /// <summary>
        /// Total Weekend Attendance, as the church's SQL counts it, from the values of the other weekend metrics.
        /// </summary>
        private List<MetricValueRow> WeekendTotal( RockMetric metric, List<RockMetric> all )
        {
            var classifications = WeekendPartGuids.Select( g => _db.IdByGuid( "DefinedValue", g ) ).Where( id => id != null ).ToHashSet();
            var campusType = _db.RequireIdByGuid( "EntityType", CampusEntityTypeGuid );
            var scheduleType = _db.RequireIdByGuid( "EntityType", ScheduleEntityTypeGuid );

            // A value the job saves is there at its run, since the parts run first. One staff type is there once they save it.
            var parts = all.Where( m => m != metric && m.MeasurementClassificationValueId != null && classifications.Contains( m.MeasurementClassificationValueId ) )
                .SelectMany( m =>
                {
                    var campus = m.Partitions.FindIndex( p => p.EntityTypeId == campusType );
                    var schedule = m.Partitions.FindIndex( p => p.EntityTypeId == scheduleType );
                    return m.Values.Select( v => (Day: v.At.Date, There: m.Runs.Count > 0 ? v.At : v.Saved, v.Value,
                        Group: new Group( campus >= 0 && campus < v.Entities.Length ? v.Entities[campus] : null, schedule >= 0 && schedule < v.Entities.Length ? v.Entities[schedule] : null )) );
                } )
                .ToList();

            var random = new SeededRandom( $"{_seed}:rock-metric-runs:{metric.Key}" );
            var values = new List<MetricValueRow>();
            foreach ( var run in metric.Runs )
            {
                var saved = SavedAfter( run, random );
                var first = run.Date.AddDays( -6 );
                foreach ( var group in parts.Where( p => p.Day >= first && p.Day <= run.Date && p.There <= run ).GroupBy( p => p.Group ) )
                {
                    values.Add( new MetricValueRow( run, saved, group.Sum( p => p.Value ), new[] { group.Key.First, group.Key.Second } ) );
                }
            }

            return values;
        }

        /// <summary>
        /// Counts a weekly metric from rows grouped by day: each run adds up its window, by group. With
        /// <paramref name="groups"/>, as Rock's GROUP BY ALL does, every group gets a value, zero when it had no rows.
        /// The query gets @From, @To, and @RunTime, and returns Day, IsLate, First, Second, and Amount.
        /// </summary>
        private List<MetricValueRow> Weekly( RockMetric metric, string sql, IReadOnlyCollection<Group> groups, Func<Group, int?[]> entities, params SqlParameter[] parameters )
        {
            if ( metric.Runs.Count == 0 )
            {
                return new List<MetricValueRow>();
            }

            var (from, to) = Range( metric );
            var rows = _db.Query( sql, parameters.Concat( new[] { P( "@From", from ), P( "@To", to ), P( "@RunTime", metric.Runs[0].TimeOfDay ) } ).ToArray() )
                .Select( r => new DayAmount( ( DateTime ) r["Day"], ( bool ) r["IsLate"], new Group( r["First"] as int?, r["Second"] as int? ), Convert.ToDecimal( r["Amount"] ) ) )
                .ToList();

            var random = new SeededRandom( $"{_seed}:rock-metric-runs:{metric.Key}" );
            var values = new List<MetricValueRow>();
            foreach ( var run in metric.Runs )
            {
                var saved = SavedAfter( run, random );
                var first = run.Date.AddDays( -7 );
                var sums = rows.Where( r => r.Day >= first && r.Day <= run.Date && !( r.Day == run.Date && r.IsLate ) )
                    .GroupBy( r => r.Group )
                    .ToDictionary( g => g.Key, g => g.Sum( r => r.Amount ) );
                foreach ( var group in groups ?? sums.Keys )
                {
                    values.Add( new MetricValueRow( run, saved, Stored( sums.GetValueOrDefault( group ) ), entities( group ) ) );
                }
            }

            return values;
        }

        /// <summary>
        /// The days a metric's runs read: from seven days before the first run's day to the end of the last run's day.
        /// </summary>
        private static (DateTime From, DateTime To) Range( RockMetric metric ) => (metric.Runs[0].Date.AddDays( -7 ), metric.Runs[metric.Runs.Count - 1].Date.AddDays( 1 ));

        private static int DateKey( DateTime day ) => int.Parse( day.ToString( "yyyyMMdd" ) );
    }
}
