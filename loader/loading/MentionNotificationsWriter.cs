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

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /// <summary>
    /// One saved note with the staff members it mentions, for the notification messages Rock makes from it.
    /// </summary>
    internal sealed class MentionedNote
    {
        public int NoteId { get; set; }

        public Guid NoteGuid { get; set; }

        public string NoteTypeName { get; set; }

        /// <summary>
        /// The person the note is about.
        /// </summary>
        public int PersonId { get; set; }

        public int AuthorAliasId { get; set; }

        public DateTime Written { get; set; }

        /// <summary>
        /// The primary alias of each staff member the note mentions.
        /// </summary>
        public List<int> MentionedAliasIds { get; set; } = new List<int>();
    }

    /// <summary>
    /// Writes the notification messages Rock's Note Mention component makes when a saved note mentions someone (decision
    /// 307), so each staff member's bell counts their recent unread mentions and opens the note on the person's profile.
    /// </summary>
    internal sealed class MentionNotificationsWriter
    {
        /// <summary>
        /// Mentions from this many days before install day get a message. Rock's message expires 90 days after the note, and
        /// Rock Cleanup deletes an expired message, so none may expire on install day.
        /// </summary>
        public const int WindowDays = 89;

        /// <summary>
        /// Messages from notes this many days before install day, or less, are still unread.
        /// </summary>
        public const int UnreadDays = 3;

        /// <summary>
        /// Rock's NoteMention sets ExpireDateTime this many days after MessageDateTime.
        /// </summary>
        public const int ExpireDays = 90;

        public const string NoteMentionComponent = "Rock.Core.NotificationMessageTypes.NoteMention";

        public static readonly Guid NoteMentionComponentGuid = new Guid( "54711081-5E34-40C5-8403-2B3F320979BB" );

        /// <summary>
        /// The key Rock's NoteMention gives its message type.
        /// </summary>
        public const string MessageTypeKey = "mention";

        /// <summary>
        /// Rock's person profile page, where the Notes block saves a note about a person.
        /// </summary>
        public static readonly Guid PersonProfilePageGuid = new Guid( "08DBD8A5-2C35-4146-B4A8-0F7652348B25" );

        private readonly RockDatabase _db;
        private readonly AnchorDates _dates;
        private readonly string _foreignKey;
        private readonly string _seed;

        public MentionNotificationsWriter( RockDatabase db, AnchorDates dates, string foreignKey, string seed )
        {
            _db = db;
            _dates = dates;
            _foreignKey = foreignKey;
            _seed = seed;
        }

        /*
            11/18/26 - CLAUDE

            Rock makes these messages only from its Obsidian Notes block (NoteClientService), not the WebForms
            Notes block a new Rock 20 or 21 still has on the person profile. The rows here are the ones the
            Obsidian block saves for a note on the person profile page: the author as title, a Key of
            "note-{Id}", and the page, its PersonId parameter, and the note's anchor, from which Rock builds
            the link the bell opens. Rock saves the message right after the note. A message keeps no audit
            columns, so opening one only marks it read.

            Reason: The bell and the notification list behave as if Rock made the messages (decision 307).
        */

        /// <summary>
        /// Writes a message for each staff member each recent note mentions, other than its author. Returns a line for the log.
        /// </summary>
        public string Write( IReadOnlyList<MentionedNote> notes )
        {
            var unreadFrom = _dates.Anchor.Date.AddDays( -UnreadDays );
            var messages = notes
                .Where( n => n.Written >= _dates.Anchor.Date.AddDays( -WindowDays ) && n.Written < _dates.Anchor.Date )
                .SelectMany( n => n.MentionedAliasIds.Distinct().Where( a => a != n.AuthorAliasId ).Select( a => (Note: n, AliasId: a) ) )
                .ToList();
            if ( messages.Count == 0 )
            {
                return "Mention notifications: none (no note in the last 89 days mentions a staff member)";
            }

            var componentId = RockComponents.Register( _db, NoteMentionComponent, NoteMentionComponentGuid, _foreignKey );
            var firstMention = notes.Where( n => n.MentionedAliasIds.Any( a => a != n.AuthorAliasId ) ).Min( n => n.Written );
            var typeId = MessageTypeId( componentId, firstMention );
            var pageId = _db.RequireIdByGuid( "Page", PersonProfilePageGuid );

            // Rock's Person.FullName: nick name, last name, and suffix.
            var people = _db.Query( $@"
SELECT [p].[Id], [p].[PrimaryAliasId], [p].[NickName] + ' ' + [p].[LastName] + ISNULL( ' ' + [dv].[Value], '' ) AS [FullName]
FROM [Person] AS [p]
LEFT JOIN [DefinedValue] AS [dv] ON [dv].[Id] = [p].[SuffixValueId]
WHERE {PeopleLoader.DatasetPersonFilter( "p" )}", P( "@ForeignKey", _foreignKey ) );
            var byPerson = people.ToDictionary( r => ( int ) r["Id"], r => ( string ) r["FullName"] );
            var byAlias = people.Where( r => r["PrimaryAliasId"] is int ).ToDictionary( r => ( int ) r["PrimaryAliasId"], r => ( string ) r["FullName"] );

            var table = TableFactory.Create( "NotificationMessageTypeId:int", "Title:string", "Description:string", "Key:string", "MessageDateTime:date", "ExpireDateTime:date",
                "Count:int", "PersonAliasId:int", "IsRead:bool", "ComponentDataJson:string", "Guid:guid", "ForeignKey:string" );
            var unread = 0;
            foreach ( var (note, aliasId) in messages )
            {
                var isUnread = note.Written >= unreadFrom;
                unread += isUnread ? 1 : 0;
                var data = string.Format( CultureInfo.InvariantCulture,
                    "{{\"NoteId\":{0},\"EntityId\":{1},\"PersonAliasId\":{2},\"SourcePageId\":{3},\"SourcePageParameters\":{{\"PersonId\":\"{1}\"}},\"SourcePageAnchor\":\"NoteRef-{4:N}\"}}",
                    note.NoteId, note.PersonId, note.AuthorAliasId, pageId, note.NoteGuid );
                table.Rows.Add( typeId, byAlias[note.AuthorAliasId], $"You were mentioned in a {note.NoteTypeName} about {byPerson[note.PersonId]}.", $"note-{note.NoteId}",
                    note.Written, note.Written.AddDays( ExpireDays ), 1, aliasId, !isUnread, data, SeededRandom.StableGuid( $"{_seed}:mention:{note.NoteGuid}:{aliasId}" ), _foreignKey );
            }

            _db.BulkInsert( "NotificationMessage", table, keepIdentity: false );
            return $"Mention notifications: {messages.Count:N0} for {messages.Select( m => m.AliasId ).Distinct().Count()} staff members, {unread:N0} unread from the last {UnreadDays} days";
        }

        /// <summary>
        /// The Note Mention message type, which Rock makes the first time a note mentions someone: web and mobile, keyed
        /// "mention". A Rock that already made it keeps its own.
        /// </summary>
        private int MessageTypeId( int componentId, DateTime firstMention )
        {
            if ( _db.Scalar( "SELECT [Id] FROM [NotificationMessageType] WHERE [EntityTypeId] = @ComponentId AND [Key] = @Key", P( "@ComponentId", componentId ), P( "@Key", MessageTypeKey ) ) is int id )
            {
                return id;
            }

            return ( int ) _db.Scalar( @"
INSERT INTO [NotificationMessageType] ( [EntityTypeId], [Key], [IsDeletedOnRead], [IsWebSupported], [IsMobileApplicationSupported], [IsTvApplicationSupported],
    [Guid], [CreatedDateTime], [ModifiedDateTime], [ForeignKey] )
OUTPUT INSERTED.[Id]
VALUES ( @ComponentId, @Key, 0, 1, 1, 0, @Guid, @Made, @Made, @ForeignKey )",
                P( "@ComponentId", componentId ), P( "@Key", MessageTypeKey ), P( "@Guid", SeededRandom.StableGuid( $"{_seed}:mention-type" ) ),
                P( "@Made", firstMention ), P( "@ForeignKey", _foreignKey ) );
        }
    }
}
