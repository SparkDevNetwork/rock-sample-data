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
    /// Checks the Cast's assessment stories against the loaded database: each expected result and pending request, each
    /// story's leaning, and each legacy DISC result exactly as its file has it.
    /// </summary>
    internal static class CastAssessmentExpectations
    {
        /// <summary>
        /// The person attribute that shows each type's result.
        /// </summary>
        private static readonly Dictionary<string, string> ResultKeys = new Dictionary<string, string>
        {
            ["disc"] = "PersonalityType",
            ["spiritual-gifts"] = "core_DominantGifts",
            ["conflict-profile"] = "core_ConflictThemeSolving",
            ["emotional-intelligence"] = "core_EQSelfAwareness",
            ["motivators"] = "core_MotivatorsTop5Motivators"
        };

        private static readonly Guid SpiritualGiftsTypeGuid = new Guid( "9D9628F0-7FC5-411E-B9DF-740AA17689A0" );

        /// <summary>
        /// Returns how many expectations were checked, and adds a line for each one that does not hold.
        /// </summary>
        public static int Check( RockDatabase db, SampleDataSource source, CastPersonGuids guids, List<string> failures )
        {
            var config = source.Assessments;
            if ( config == null )
            {
                return 0;
            }

            var typeKeys = config.Types.ToDictionary( t => t.Guid, t => t.Key );
            var gifts = db.Query( @"
SELECT [dv].[Value], [dv].[Guid]
FROM [DefinedValue] AS [dv]
INNER JOIN [DefinedType] AS [dt] ON [dt].[Id] = [dv].[DefinedTypeId]
WHERE [dt].[Guid] = @Guid", P( "@Guid", SpiritualGiftsTypeGuid ) )
                .ToDictionary( r => ( ( string ) r["Value"] ).ToLowerInvariant().Replace( ' ', '-' ), r => ( Guid ) r["Guid"] );
            var checks = 0;

            foreach ( var person in source.ReadHouseholds().SelectMany( h => h.Members ).Where( m => m.Expect?.Assessments != null || m.Assessments.Count > 0 ) )
            {
                var name = $"{person.NickName ?? person.FirstName} {person.LastName}";
                var rows = db.Query( @"
SELECT [at].[Guid] AS [TypeGuid], [a].[Status], [a].[RequesterPersonAliasId], [a].[CompletedDateTime], [a].[AssessmentResultData]
FROM [Assessment] AS [a]
INNER JOIN [AssessmentType] AS [at] ON [at].[Id] = [a].[AssessmentTypeId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [a].[PersonAliasId]
INNER JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
WHERE [p].[Guid] = @Guid
ORDER BY [a].[Id]", P( "@Guid", guids.Value( person.Guid ) ) )
                    .Where( r => typeKeys.ContainsKey( ( Guid ) r["TypeGuid"] ) )
                    .Select( r => (Type: typeKeys[( Guid ) r["TypeGuid"]], IsComplete: Convert.ToInt32( r["Status"] ) == 1, HasRequester: r["RequesterPersonAliasId"] is int, Completed: r["CompletedDateTime"] as DateTime?, Answers: r["AssessmentResultData"] as string) )
                    .ToList();
                var values = db.Query( @"
SELECT [a].[Key], [av].[Value]
FROM [AttributeValue] AS [av]
INNER JOIN [Attribute] AS [a] ON [a].[Id] = [av].[AttributeId]
INNER JOIN [EntityType] AS [et] ON [et].[Id] = [a].[EntityTypeId] AND [et].[Name] = 'Rock.Model.Person'
INNER JOIN [Person] AS [p] ON [p].[Id] = [av].[EntityId]
WHERE [p].[Guid] = @Guid", P( "@Guid", guids.Value( person.Guid ) ) )
                    .GroupBy( r => ( string ) r["Key"] )
                    .ToDictionary( g => g.Key, g => g.First()["Value"] as string );

                foreach ( var type in person.Expect?.Assessments?.Complete ?? new List<string>() )
                {
                    checks++;
                    if ( !rows.Any( r => r.Type == type && r.IsComplete ) || !values.TryGetValue( ResultKeys[type], out var shown ) || string.IsNullOrEmpty( shown ) )
                    {
                        failures.Add( $"{name}: expected a {type} result, Rock shows {( rows.Any( r => r.Type == type ) ? "only a request" : "none" )}" );
                    }
                }

                foreach ( var type in person.Expect?.Assessments?.Pending ?? new List<string>() )
                {
                    checks++;
                    var latest = rows.LastOrDefault( r => r.Type == type );
                    if ( latest.Type == null || latest.IsComplete || !latest.HasRequester )
                    {
                        failures.Add( $"{name}: expected a pending {type} request with a requester, Rock shows {( latest.Type == null ? "none" : latest.IsComplete ? "it complete" : "no requester" )}" );
                    }
                }

                foreach ( var (type, story) in person.Assessments )
                {
                    if ( type == "spiritual-gifts" && story.Leaning.Count > 0 )
                    {
                        checks++;
                        var dominant = values.TryGetValue( "core_DominantGifts", out var shown ) ? shown.Split( ',' ).Select( g => Guid.TryParse( g, out var guid ) ? guid : Guid.Empty ).ToHashSet() : new HashSet<Guid>();
                        var missing = story.Leaning.Where( g => !gifts.TryGetValue( g, out var guid ) || !dominant.Contains( guid ) ).ToList();
                        if ( missing.Count > 0 )
                        {
                            failures.Add( $"{name}: expected {string.Join( ", ", missing )} among the dominant gifts, Rock shows other gifts" );
                        }
                    }

                    foreach ( var legacy in story.Takes.Where( t => t.Legacy != null ).Select( t => t.Legacy ) )
                    {
                        checks++;
                        var expected = new[] { "D", "I", "S", "C" }.SelectMany( l => new[] { ("Adaptive" + l, legacy.Adaptive[l].ToString()), ("Natural" + l, legacy.Natural[l].ToString()) } )
                            .Append( ("PersonalityType", legacy.PersonalityType) );
                        var wrong = expected.Where( e => !values.TryGetValue( e.Item1, out var value ) || value != e.Item2 ).Select( e => $"{e.Item1} {( values.TryGetValue( e.Item1, out var value ) ? value : "empty" )} (expected {e.Item2})" ).ToList();
                        if ( !rows.Any( r => r.Type == type && r.IsComplete && r.Answers == null ) )
                        {
                            wrong.Add( "no completed DISC without answers" );
                        }

                        if ( wrong.Count > 0 )
                        {
                            failures.Add( $"{name}: expected the legacy DISC result from the household file, Rock shows {string.Join( ", ", wrong )}" );
                        }
                    }
                }
            }

            return checks;
        }
    }
}
