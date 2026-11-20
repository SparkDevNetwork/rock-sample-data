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

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Validation
{
    /// <summary>
    /// Checks the Cast's eRA stories against the loaded database (decision 327): the flag, the start or end date, the
    /// eRA step, and the history line each story's state leaves.
    /// </summary>
    internal static class CastEraExpectations
    {
        private static readonly Guid EraStepTypeGuid = new Guid( "E57468BE-15BF-48B6-AAB2-F8E2B02720F3" );

        /// <summary>
        /// Returns how many expectations were checked, and adds a line for each one that does not hold.
        /// </summary>
        /// <param name="installDay">The load's install day, which the stories' days count back from.</param>
        public static int Check( RockDatabase db, SampleDataSource source, CastPersonGuids guids, DateTime installDay, List<string> failures )
        {
            var checks = 0;
            foreach ( var person in source.ReadHouseholds().SelectMany( h => h.Members ).Where( m => m.Expect?.Era != null ) )
            {
                checks++;
                var name = $"{person.NickName ?? person.FirstName} {person.LastName}";
                var problem = Problem( db, guids.Value( person.Guid ), person.Expect.Era, installDay );
                if ( problem != null )
                {
                    failures.Add( $"{name}: {problem}" );
                }
            }

            return checks;
        }

        /// <summary>
        /// What is wrong with one person's eRA, or null.
        /// </summary>
        private static string Problem( RockDatabase db, object personGuid, CastEraExpect expect, DateTime installDay )
        {
            var values = db.Query( @"
SELECT [a].[Key], [av].[Value], [av].[ValueAsDateTime]
FROM [AttributeValue] AS [av]
INNER JOIN [Attribute] AS [a] ON [a].[Id] = [av].[AttributeId]
INNER JOIN [Person] AS [p] ON [p].[Id] = [av].[EntityId]
WHERE [p].[Guid] = @Guid AND [a].[Key] IN ( 'core_CurrentlyAnEra', 'core_EraStartDate', 'core_EraEndDate' )
    AND [a].[EntityTypeId] = ( SELECT [Id] FROM [EntityType] WHERE [Name] = 'Rock.Model.Person' )", P( "@Guid", personGuid ) )
                .ToDictionary( r => ( string ) r["Key"], r => (Value: r["Value"] as string, Date: r["ValueAsDateTime"] as DateTime?) );
            var steps = db.Query( @"
SELECT [s].[StartDateTime], [s].[CompletedDateTime], [ss].[IsCompleteStatus]
FROM [Step] AS [s]
INNER JOIN [StepType] AS [st] ON [st].[Id] = [s].[StepTypeId]
INNER JOIN [StepStatus] AS [ss] ON [ss].[Id] = [s].[StepStatusId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [s].[PersonAliasId]
INNER JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
WHERE [p].[Guid] = @Guid AND [st].[Guid] = @StepType", P( "@Guid", personGuid ), P( "@StepType", EraStepTypeGuid ) )
                .Select( r => (Start: ( DateTime ) r["StartDateTime"], Completed: r["CompletedDateTime"] as DateTime?, IsComplete: ( bool ) r["IsCompleteStatus"]) )
                .ToList();
            var history = db.Query( @"
SELECT [h].[Verb], [h].[CreatedDateTime]
FROM [History] AS [h]
INNER JOIN [Person] AS [p] ON [p].[Id] = [h].[EntityId]
INNER JOIN [Category] AS [c] ON [c].[Id] = [h].[CategoryId]
WHERE [p].[Guid] = @Guid AND [c].[Guid] = 'C1524D2E-3E8F-3D83-45F8-526B749D79F0' AND [h].[Caption] = 'eRA'", P( "@Guid", personGuid ) )
                .Select( r => (Verb: ( string ) r["Verb"], At: ( DateTime ) r["CreatedDateTime"]) )
                .ToList();

            var isEra = values.TryGetValue( "core_CurrentlyAnEra", out var flag ) && string.Equals( flag.Value, "True", StringComparison.OrdinalIgnoreCase );
            var start = values.TryGetValue( "core_EraStartDate", out var s ) ? s.Date : null;
            var end = values.TryGetValue( "core_EraEndDate", out var e ) ? e.Date : null;
            string Days( DateTime date ) => $"{( installDay.Date - date.Date ).TotalDays:0} days ago";

            switch ( expect.Status )
            {
                case "current":
                    if ( !isEra )
                    {
                        return end.HasValue ? $"expected a current eRA, Rock shows one that ended {Days( end.Value )}" : "expected a current eRA, Rock shows none";
                    }

                    if ( !start.HasValue )
                    {
                        return "expected a current eRA with a start date, Rock shows no eRA Start Date";
                    }

                    if ( end.HasValue )
                    {
                        return $"expected a current eRA, Rock also shows an eRA End Date {Days( end.Value )}";
                    }

                    if ( !Within( installDay, start.Value, expect.StartedDaysAgo ) )
                    {
                        return $"expected an eRA that started {Range( expect.StartedDaysAgo )}, Rock shows it started {Days( start.Value )}";
                    }

                    var open = steps.Where( x => !x.IsComplete ).ToList();
                    if ( open.Count != 1 || open[0].Start.Date != start.Value.Date )
                    {
                        return $"expected one In Progress eRA step from its start date, Rock shows {open.Count}{( open.Count == 1 ? $" from {open[0].Start:yyyy-MM-dd}" : string.Empty )}";
                    }

                    return history.Any( h => h.Verb == "ENTERED" && h.At.Date == start.Value.Date ) ? null : "expected an eRA Entered history line on its start date, Rock shows none";

                case "former":
                    if ( isEra )
                    {
                        return $"expected a former eRA, Rock shows a current one since {Days( start ?? installDay )}";
                    }

                    if ( !end.HasValue )
                    {
                        return "expected a former eRA, Rock shows no eRA End Date";
                    }

                    if ( !Within( installDay, end.Value, expect.EndedDaysAgo ) )
                    {
                        return $"expected an eRA that ended {Range( expect.EndedDaysAgo )}, Rock shows it ended {Days( end.Value )}";
                    }

                    if ( steps.Any( x => !x.IsComplete ) || !steps.Any( x => x.IsComplete && x.Completed?.Date == end.Value.Date ) )
                    {
                        return "expected its eRA step Complete on its end date and none In Progress, Rock shows otherwise";
                    }

                    return history.Any( h => h.Verb == "EXITED" && h.At.Date == end.Value.Date ) ? null : "expected an eRA Exited history line on its end date, Rock shows none";

                case "never":
                    return isEra || start.HasValue || end.HasValue || steps.Count > 0 || history.Count > 0
                        ? $"expected never an eRA, Rock shows {( isEra ? "a current eRA" : steps.Count > 0 ? "an eRA step" : "eRA dates or history" )}"
                        : null;

                default:
                    return $"has eRA status \"{expect.Status}\", which is not current, former, or never";
            }
        }

        private static bool Within( DateTime installDay, DateTime date, CastDaysAgoRange range )
        {
            var daysAgo = ( installDay.Date - date.Date ).TotalDays;
            return range == null || ( ( range.Min == null || daysAgo >= range.Min ) && ( range.Max == null || daysAgo <= range.Max ) );
        }

        private static string Range( CastDaysAgoRange range )
        {
            return range?.Min != null && range.Max != null ? $"{range.Min} to {range.Max} days ago"
                : range?.Min != null ? $"at least {range.Min} days ago"
                : range?.Max != null ? $"at most {range.Max} days ago"
                : "any time";
        }
    }
}
