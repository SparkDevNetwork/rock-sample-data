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

using RockSampleData.Loader.Generation;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /*
        11/23/26 - CLAUDE

        Total Adult Attendance and Salvations have no SQL in Rock. Staff type them into Rock's Service Metrics Entry
        block, on its Weekly Metrics page, for each campus and service, under the weekend's Sunday, which the block saves
        as the value's date. They finish on Sunday afternoon, after the last service, so Monday's Total Weekend
        Attendance run finds the adult counts. The adult count is how many adults the load has in that service. A
        salvation is a decision the steps record: a decision step on a Sunday counts at the service the person came to
        that day, and a Baptism step marked Baptized Here, for someone with no decision step, counts on the last Sunday
        they came to a service in the weeks before the baptism. Staff type a zero for a service with no salvations.

        Reason: Issue 55 and MT-2. The two values fit the loaded attendance and steps, as a staff member's would.
    */

    /// <summary>
    /// The two weekly metrics staff type into Rock's Service Metrics Entry block.
    /// </summary>
    internal sealed partial class RockMetricsLoader
    {
        /// <summary>
        /// Rock's Weekly Service Check-in Area, where the load records each adult in a worship service.
        /// </summary>
        private static readonly Guid WorshipAreaGuid = new Guid( "235BAE2B-5760-4763-AADF-3938F34BA100" );

        /// <summary>
        /// One service a staff member typed numbers for: its Sunday, campus, and schedule.
        /// </summary>
        private readonly record struct ServiceEntry( DateTime Sunday, int? Campus, int? Schedule );

        /// <summary>
        /// The adults in each service in the history window, read once for both metrics.
        /// </summary>
        private Dictionary<ServiceEntry, int> _adultsByService;

        /// <summary>
        /// Rock's Total Adult Attendance: the adults in each service, typed in on Sunday.
        /// </summary>
        private List<MetricValueRow> AdultAttendance( RockMetric metric )
        {
            return Entered( metric, ReadAdults( metric ) );
        }

        /// <summary>
        /// Rock's Salvations: the decisions made at each service, typed in with that service's adult count.
        /// </summary>
        private List<MetricValueRow> Salvations( RockMetric metric )
        {
            var services = ReadAdults( metric );
            var first = _installed.Date.AddDays( -7 * _configuration.Metrics.First( m => m.Key == metric.Key ).Weeks );
            var decisions = Decisions().Where( d => d.Key.Sunday > first ).ToDictionary( d => d.Key, d => d.Value );
            var counts = services.Keys.Union( decisions.Keys ).ToDictionary( s => s, s => decisions.GetValueOrDefault( s ) );
            return Entered( metric, counts );
        }

        /// <summary>
        /// The values staff typed for each service: dated the Sunday, saved that afternoon, never after install.
        /// </summary>
        private List<MetricValueRow> Entered( RockMetric metric, Dictionary<ServiceEntry, int> counts )
        {
            var campusType = _db.RequireIdByGuid( "EntityType", CampusEntityTypeGuid );
            var enteredAt = TimeSpan.Parse( _configuration.ServiceEntry.EnteredAt );
            var values = new List<MetricValueRow>();
            foreach ( var (service, count) in counts.OrderBy( c => c.Key.Sunday ) )
            {
                // Both values of a service are saved together, so their time is the service's, not the metric's.
                var random = new SeededRandom( $"{_seed}:service-entry:{service.Sunday:yyyyMMdd}:{service.Campus}:{service.Schedule}" );
                var saved = service.Sunday.Add( enteredAt ).AddMinutes( random.Between( 0, 90 ) ).AddSeconds( random.Between( 0, 59 ) );
                if ( saved > _installed )
                {
                    continue;
                }

                var entities = metric.Partitions.Select( p => p.EntityTypeId == campusType ? service.Campus : service.Schedule ).ToArray();
                values.Add( new MetricValueRow( service.Sunday, saved, count, entities ) );
            }

            return values;
        }

        /// <summary>
        /// How many adults the load has in each worship service, by Sunday, campus, and schedule, from the first Sunday of
        /// the history window to install.
        /// </summary>
        private Dictionary<ServiceEntry, int> ReadAdults( RockMetric metric )
        {
            if ( _adultsByService != null )
            {
                return _adultsByService;
            }

            var weeks = _configuration.Metrics.First( m => m.Key == metric.Key ).Weeks;
            _adultsByService = _db.Query( @"
SELECT [o].[SundayDate], [a].[CampusId], [o].[ScheduleId], COUNT(*) AS [Count]
FROM [Attendance] AS [a]
INNER JOIN [AttendanceOccurrence] AS [o] ON [o].[Id] = [a].[OccurrenceId]
INNER JOIN [Group] AS [g] ON [g].[Id] = [o].[GroupId]
INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId]
WHERE [gt].[Guid] = @WorshipArea AND [a].[DidAttend] = 1 AND [o].[SundayDate] > @From AND [o].[SundayDate] <= @To
GROUP BY [o].[SundayDate], [a].[CampusId], [o].[ScheduleId]",
                P( "@WorshipArea", WorshipAreaGuid ), P( "@From", _installed.Date.AddDays( -7 * weeks ) ), P( "@To", _installed.Date ) )
                .ToDictionary( r => new ServiceEntry( ( DateTime ) r["SundayDate"], r["CampusId"] as int?, r["ScheduleId"] as int? ), r => ( int ) r["Count"] );
            return _adultsByService;
        }

        /// <summary>
        /// The decisions made at each service: decision steps completed on a Sunday, at the service the person came to that
        /// day, and the decision before each Baptized Here baptism of someone with no decision step.
        /// </summary>
        private Dictionary<ServiceEntry, int> Decisions()
        {
            var settings = _configuration.ServiceEntry;
            var stepTypes = _source.Steps?.Programs.SelectMany( p => p.StepTypes ).ToList() ?? new List<Model.StepTypeDefinition>();
            var decisionType = stepTypes.FirstOrDefault( t => t.Key == settings.DecisionStepType );
            var baptismType = stepTypes.FirstOrDefault( t => t.Key == settings.BaptismStepType );
            if ( decisionType == null || baptismType == null )
            {
                return new Dictionary<ServiceEntry, int>();
            }

            var decisionTypeId = _db.IdByGuid( "StepType", decisionType.Guid );
            var steps = _db.Query( @"
SELECT [pa].[PersonId], [s].[StepTypeId], CAST( [s].[CompletedDateTime] AS date ) AS [Day],
    CASE WHEN EXISTS ( SELECT 1 FROM [AttributeValue] AS [av] INNER JOIN [Attribute] AS [at] ON [at].[Id] = [av].[AttributeId]
        WHERE [at].[Key] = 'BaptizedHere' AND [at].[EntityTypeQualifierColumn] = 'StepTypeId' AND [at].[EntityTypeQualifierValue] = CAST( [s].[StepTypeId] AS NVARCHAR( 200 ) )
            AND [av].[EntityId] = [s].[Id] AND [av].[Value] = 'True' ) THEN 1 ELSE 0 END AS [IsBaptizedHere]
FROM [Step] AS [s]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [s].[PersonAliasId]
WHERE [s].[CompletedDateTime] IS NOT NULL AND [s].[StepTypeId] IN ( SELECT [Id] FROM [StepType] WHERE [Guid] IN ( @DecisionType, @BaptismType ) )",
                P( "@DecisionType", decisionType.Guid ), P( "@BaptismType", baptismType.Guid ) )
                .Select( r => (Person: ( int ) r["PersonId"], IsDecision: ( int ) r["StepTypeId"] == decisionTypeId, Day: ( DateTime ) r["Day"],
                    IsBaptizedHere: ( int ) r["IsBaptizedHere"] == 1) )
                .ToList();

            // Each Sunday service the people with these steps came to: worship, or a weekend check-in area, never serving.
            var visits = _db.Query( @"
SELECT [pa].[PersonId], [a].[StartDateTime], [o].[SundayDate], [a].[CampusId], [o].[ScheduleId]
FROM [Attendance] AS [a]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [a].[PersonAliasId]
INNER JOIN [AttendanceOccurrence] AS [o] ON [o].[Id] = [a].[OccurrenceId]
INNER JOIN [Group] AS [g] ON [g].[Id] = [o].[GroupId]
INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId]
WHERE [a].[DidAttend] = 1 AND [gt].[AttendanceCountsAsWeekendService] = 1 AND CAST( [a].[StartDateTime] AS date ) = [o].[SundayDate]
    AND ISNULL( [gt].[GroupTypePurposeValueId], 0 ) <> ISNULL( ( SELECT [Id] FROM [DefinedValue] WHERE [Guid] = @ServingArea ), -1 )
    AND [pa].[PersonId] IN ( SELECT [spa].[PersonId] FROM [Step] AS [s] INNER JOIN [PersonAlias] AS [spa] ON [spa].[Id] = [s].[PersonAliasId]
        WHERE [s].[StepTypeId] IN ( SELECT [Id] FROM [StepType] WHERE [Guid] IN ( @DecisionType, @BaptismType ) ) )",
                P( "@ServingArea", ServingAreaPurposeGuid ), P( "@DecisionType", decisionType.Guid ), P( "@BaptismType", baptismType.Guid ) )
                .Select( r => (Person: ( int ) r["PersonId"], Start: ( DateTime ) r["StartDateTime"], Service: new ServiceEntry( ( DateTime ) r["SundayDate"], r["CampusId"] as int?, r["ScheduleId"] as int? )) )
                .ToLookup( v => v.Person );

            var decided = steps.Where( s => s.IsDecision ).Select( s => s.Person ).ToHashSet();
            var services = new List<ServiceEntry>();
            foreach ( var step in steps )
            {
                var visit = step.IsDecision
                    ? visits[step.Person].Where( v => v.Service.Sunday == step.Day ).OrderBy( v => v.Start ).FirstOrDefault()
                    : step.IsBaptizedHere && !decided.Contains( step.Person )
                        ? visits[step.Person].Where( v => v.Service.Sunday >= step.Day.AddDays( -settings.MaximumDaysBeforeBaptism ) && v.Service.Sunday <= step.Day.AddDays( -settings.MinimumDaysBeforeBaptism ) )
                            .OrderByDescending( v => v.Start ).FirstOrDefault()
                        : default;
                if ( visit.Person != 0 )
                {
                    services.Add( visit.Service );
                }
            }

            return services.GroupBy( s => s ).ToDictionary( g => g.Key, g => g.Count() );
        }
    }
}
