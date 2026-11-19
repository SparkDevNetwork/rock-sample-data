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
    /// Checks the Cast's reminders in configuration/reminders.json (issue 22): each one is on its owner, about its person
    /// or group, with its note and repeat, and due, upcoming, or complete as its story says on the load's install day.
    /// Each owner's ReminderCount, which Rock's bell shows, is what Rock's Process Reminders job would set.
    /// </summary>
    internal static class CastReminderExpectations
    {
        /// <summary>
        /// Returns how many expectations were checked and adds each failure.
        /// </summary>
        /// <param name="anchor">The load's install day, which every reminder date counts from.</param>
        public static int Check( RockDatabase db, SampleDataSource source, CastPersonGuids guids, DateTime anchor, List<string> failures )
        {
            var config = source.Reminders;
            if ( config == null )
            {
                return 0;
            }

            var tomorrow = anchor.Date.AddDays( 1 );
            var checks = 0;
            foreach ( var cast in config.Cast )
            {
                checks++;
                var name = $"{cast.OwnerName}'s reminder about {cast.AboutName}";
                var row = db.Query( @"
SELECT [r].[IsComplete], [r].[ReminderDate], [r].[Note], [r].[RenewPeriodDays], [r].[RenewCurrentCount], [rt].[Guid] AS [TypeGuid], [owner].[Guid] AS [OwnerGuid],
    [about].[Guid] AS [AboutGuid], CASE WHEN [ea].[AliasPersonId] = [ea].[PersonId] THEN 1 ELSE 0 END AS [IsPrimaryAlias], [g].[Guid] AS [GroupGuid]
FROM [Reminder] AS [r]
INNER JOIN [ReminderType] AS [rt] ON [rt].[Id] = [r].[ReminderTypeId]
INNER JOIN [PersonAlias] AS [oa] ON [oa].[Id] = [r].[PersonAliasId]
INNER JOIN [Person] AS [owner] ON [owner].[Id] = [oa].[PersonId]
LEFT JOIN [PersonAlias] AS [ea] ON [ea].[Id] = [r].[EntityId] AND [rt].[Guid] = @PersonType
LEFT JOIN [Person] AS [about] ON [about].[Id] = [ea].[PersonId]
LEFT JOIN [Group] AS [g] ON [g].[Id] = [r].[EntityId] AND [rt].[Guid] = @GroupType
WHERE [r].[Guid] = @Guid",
                    P( "@Guid", cast.Guid ), P( "@PersonType", config.PersonReminderType ), P( "@GroupType", config.GroupReminderType ) ).FirstOrDefault();
                if ( row == null )
                {
                    failures.Add( $"{name}: not in the database" );
                    continue;
                }

                var problems = new List<string>();
                if ( ( Guid ) row["OwnerGuid"] != guids.Rock( cast.Owner ) )
                {
                    problems.Add( "it belongs to someone else" );
                }

                if ( cast.Person.HasValue && ( ( Guid ) row["TypeGuid"] != config.PersonReminderType || row["AboutGuid"] as Guid? != guids.Rock( cast.Person.Value ) || Convert.ToInt32( row["IsPrimaryAlias"] ) != 1 ) )
                {
                    problems.Add( "it is not a Personal Reminder on the person's primary alias" );
                }

                if ( cast.Group.HasValue && ( ( Guid ) row["TypeGuid"] != config.GroupReminderType || row["GroupGuid"] as Guid? != cast.Group.Value ) )
                {
                    problems.Add( "it is not a Group Reminder on the group" );
                }

                if ( ( string ) row["Note"] != cast.Note )
                {
                    problems.Add( "its note differs" );
                }

                var isComplete = ( bool ) row["IsComplete"];
                var date = ( DateTime ) row["ReminderDate"];
                var shows = isComplete ? "complete" : date < tomorrow ? "due" : "upcoming";
                if ( shows != cast.Expect )
                {
                    problems.Add( $"expected {cast.Expect}, Rock shows it {shows} on {date:yyyy-MM-dd}" );
                }

                if ( cast.Repeat != null && ( row["RenewPeriodDays"] as int? != cast.Repeat.EveryDays || row["RenewCurrentCount"] as int? != cast.Repeat.Completed ) )
                {
                    problems.Add( $"expected to repeat every {cast.Repeat.EveryDays} days after {cast.Repeat.Completed} completions" );
                }

                if ( problems.Count > 0 )
                {
                    failures.Add( $"{name}: {string.Join( "; ", problems )}" );
                }
            }

            // Rock's bell shows each owner's ReminderCount, and the Process Reminders job sets it to their reminders due.
            foreach ( var owner in config.Cast.GroupBy( c => c.Owner ) )
            {
                checks++;
                var counts = db.Query( @"
SELECT [p].[ReminderCount],
    ( SELECT COUNT(*)
        FROM [Reminder] AS [r]
        INNER JOIN [ReminderType] AS [rt] ON [rt].[Id] = [r].[ReminderTypeId]
        INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [r].[PersonAliasId]
        WHERE [pa].[PersonId] = [p].[Id] AND [rt].[IsActive] = 1 AND [r].[IsComplete] = 0 AND [r].[ReminderDate] < @Tomorrow ) AS [Due]
FROM [Person] AS [p]
WHERE [p].[Guid] = @Guid", P( "@Guid", guids.Value( owner.Key ) ), P( "@Tomorrow", tomorrow ) ).FirstOrDefault();
                var name = owner.First().OwnerName;
                if ( counts == null )
                {
                    failures.Add( $"{name}: not in the database" );
                    continue;
                }

                var shown = counts["ReminderCount"] as int? ?? 0;
                var due = Convert.ToInt32( counts["Due"] );
                if ( shown != due || ( owner.Any( c => c.Expect == "due" ) && due == 0 ) )
                {
                    failures.Add( $"{name}: Rock's bell counts {shown} reminders due, where Rock's Process Reminders job counts {due}" );
                }
            }

            return checks;
        }
    }
}
