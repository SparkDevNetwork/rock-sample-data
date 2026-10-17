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

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Validation
{
    /// <summary>
    /// Checks the Cast givers whose giving patterns say what Rock should show (<c>expect</c> in their household
    /// file) against Rock's values in the loaded database: the frequency label, the journey stage, and alerts.
    /// </summary>
    /// <remarks>
    /// Documentation, demos, and tests lean on these givers ("the Deckers give monthly"). Generator changes
    /// and Rock version changes can quietly break those stories, so every load checks them.
    /// </remarks>
    internal static class CastExpectations
    {
        private static readonly string[] FrequencyNames = { null, "weekly", "bi-weekly", "monthly", "quarterly", "erratic", "undetermined" };
        private static readonly string[] StageNames = { "non-giver", "new", "consistent", "occasional", "lapsed", "former" };

        /// <summary>
        /// Prints each expectation's result and returns how many failed.
        /// </summary>
        public static int Check( RockDatabase db, SampleDataSource source )
        {
            var alertTypeKeys = source.GivingAlertTypes.AlertTypes.ToDictionary( t => t.Guid, t => t.Key );
            var checks = 0;
            var failures = new List<string>();

            foreach ( var household in source.ReadHouseholds() )
            {
                foreach ( var pattern in household.Giving.Where( g => g.Expect != null ) )
                {
                    var giver = household.Members.First( m => m.Key == pattern.Person );
                    var person = db.Query( "SELECT [Id], [GivingId] FROM [Person] WHERE [Guid] = @Guid", P( "@Guid", giver.Guid ) ).FirstOrDefault();
                    var name = $"{giver.NickName ?? giver.FirstName} {giver.LastName}";
                    if ( person == null )
                    {
                        failures.Add( $"{name}: not in the database" );
                        continue;
                    }

                    var values = db.Query( @"
SELECT [a].[Key], [av].[Value]
FROM [AttributeValue] AS [av]
INNER JOIN [Attribute] AS [a] ON [a].[Id] = [av].[AttributeId]
WHERE [av].[EntityId] = @PersonId AND [a].[Key] IN ( 'FrequencyLabel', 'CurrentJourneyGivingStage' )
    AND [a].[EntityTypeId] = ( SELECT [Id] FROM [EntityType] WHERE [Name] = 'Rock.Model.Person' )", P( "@PersonId", person["Id"] ) )
                        .ToDictionary( r => ( string ) r["Key"], r => ( string ) r["Value"] );

                    if ( pattern.Expect.Frequency != null )
                    {
                        checks++;
                        var actual = values.TryGetValue( "FrequencyLabel", out var label ) && int.TryParse( label, out var index ) ? FrequencyNames.ElementAtOrDefault( index ) : null;
                        if ( actual != pattern.Expect.Frequency )
                        {
                            failures.Add( $"{name}: expected frequency {pattern.Expect.Frequency}, Rock shows {actual ?? "none"}" );
                        }
                    }

                    if ( pattern.Expect.JourneyStage != null )
                    {
                        checks++;
                        var actual = values.TryGetValue( "CurrentJourneyGivingStage", out var stage ) && int.TryParse( stage, out var index ) ? StageNames.ElementAtOrDefault( index ) : null;
                        if ( actual != pattern.Expect.JourneyStage )
                        {
                            failures.Add( $"{name}: expected journey stage {pattern.Expect.JourneyStage}, Rock shows {actual ?? "none"}" );
                        }
                    }

                    if ( pattern.Expect.Alerts.Count > 0 )
                    {
                        var raised = db.Query( @"
SELECT DISTINCT [t].[Guid]
FROM [FinancialTransactionAlert] AS [fa]
INNER JOIN [FinancialTransactionAlertType] AS [t] ON [t].[Id] = [fa].[AlertTypeId]
WHERE [fa].[GivingId] = @GivingId", P( "@GivingId", person["GivingId"] ) )
                            .Select( r => alertTypeKeys.GetValueOrDefault( ( Guid ) r["Guid"] ) )
                            .ToHashSet();

                        foreach ( var alert in pattern.Expect.Alerts )
                        {
                            checks++;
                            if ( !raised.Contains( alert ) )
                            {
                                failures.Add( $"{name}: expected a {alert} alert, Rock has none" );
                            }
                        }
                    }
                }
            }

            Console.WriteLine( failures.Count == 0
                ? $"  Cast expectations: all {checks} hold"
                : $"  WARNING: {failures.Count} of {checks} Cast expectations do not hold" );
            foreach ( var failure in failures )
            {
                Console.WriteLine( $"    {failure}" );
            }

            return failures.Count;
        }
    }
}
