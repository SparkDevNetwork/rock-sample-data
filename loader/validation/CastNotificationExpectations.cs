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

using System.Text.RegularExpressions;

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Loading;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Validation
{
    /// <summary>
    /// Checks that each recent mention in a Cast note left its staff member the notification message Rock's Note Mention
    /// component makes (decision 307): one per note and person, unread when the note is from the last three days,
    /// and opening the note on the person's profile.
    /// </summary>
    internal static class CastNotificationExpectations
    {
        private static readonly Regex Token = new Regex( @"\{@([a-z]+)\}", RegexOptions.Compiled );

        /// <summary>
        /// Returns how many expectations were checked, and adds a line for each one that does not hold. A load without Rock's
        /// DataEncryptionKey writes mentions as plain names, which Rock never reads as mentions, so it has none to check.
        /// </summary>
        public static int Check( RockDatabase db, SampleDataSource source, CastPersonGuids guids, List<string> failures )
        {
            var staff = source.Notes?.Staff.ToDictionary( s => s.Key );
            if ( staff == null || db.Scalar( "SELECT TOP 1 1 FROM [Note] WHERE [ForeignKey] LIKE 'SampleData:%' AND [Text] LIKE '%class=\"mention\"%'" ) == null )
            {
                return 0;
            }

            var notes = source.ReadCastNotes().SelectMany( f => f.Notes.SelectMany( n => new[] { (n.Key, n.Guid, n.Author, n.Written, n.Text) }
                .Concat( n.Replies.Select( r => (n.Key, r.Guid, r.Author, r.Written, r.Text) ) ) ) );
            var checks = 0;
            foreach ( var (key, guid, author, written, text) in notes.Where( n => n.Written?.DaysAgo <= MentionNotificationsWriter.WindowDays ) )
            {
                foreach ( var mentioned in Token.Matches( text ).Cast<Match>().Select( m => m.Groups[1].Value ).Distinct().Where( k => k != author ) )
                {
                    checks++;
                    var person = staff[mentioned];
                    var isUnread = written.DaysAgo <= MentionNotificationsWriter.UnreadDays;
                    var message = db.Query( @"
SELECT [nm].[IsRead], [nm].[ComponentDataJson]
FROM [NotificationMessage] AS [nm]
INNER JOIN [NotificationMessageType] AS [nmt] ON [nmt].[Id] = [nm].[NotificationMessageTypeId]
INNER JOIN [EntityType] AS [et] ON [et].[Id] = [nmt].[EntityTypeId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [nm].[PersonAliasId]
INNER JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
INNER JOIN [Note] AS [n] ON [nm].[Key] = 'note-' + CAST( [n].[Id] AS nvarchar( 20 ) )
WHERE [n].[Guid] = @Note AND [p].[Guid] = @Person AND [et].[Name] = @Component AND [nmt].[Key] = @TypeKey AND [nmt].[IsWebSupported] = 1",
                        P( "@Note", guid ), P( "@Person", guids.Value( person.Guid ) ), P( "@Component", MentionNotificationsWriter.NoteMentionComponent ),
                        P( "@TypeKey", MentionNotificationsWriter.MessageTypeKey ) ).ToList();
                    var expected = $"{( isUnread ? "an unread" : "a read" )} mention from {staff[author].Name}'s note {key}";
                    if ( message.Count != 1 )
                    {
                        failures.Add( $"{person.Name}: expected {expected} in the bell's list, Rock shows {message.Count}" );
                    }
                    else if ( ( bool ) message[0]["IsRead"] == isUnread )
                    {
                        failures.Add( $"{person.Name}: expected {expected}, Rock shows it {( isUnread ? "read" : "unread" )}" );
                    }
                    else if ( !( ( string ) message[0]["ComponentDataJson"] ).Contains( $"\"SourcePageAnchor\":\"NoteRef-{guid:N}\"" ) )
                    {
                        failures.Add( $"{person.Name}: expected the mention from note {key} to open that note, Rock's message points elsewhere" );
                    }
                }
            }

            return checks;
        }
    }
}
