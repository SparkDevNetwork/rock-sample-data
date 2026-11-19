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
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /*
        11/17/26 - CLAUDE

        Rock 20.0.10 and 21.0.2 store an SMS snippet the same way: a Snippet row of Rock's SMS snippet type, in a
        category of the Snippet entity type. It is shared when it has no owner, and personal when
        OwnerPersonAliasId names the one person who may see it. SMS Conversations lists the shared snippets and the
        person's own, by Order and then Name, and fills in their Lava with the recipient as Person when staff insert
        one. Rock's Snippet Detail block gives a new snippet the next Order and the person saving it as its creator,
        so each row is written that way: shared ones by the church's communications sender, and personal ones by
        their owner, before the first text that uses them.

        Reason: Staff open SMS Conversations to a snippet picker the church already filled (decision 299).
    */

    /// <summary>
    /// Writes the church's SMS snippets from content/communications/snippets.json.
    /// </summary>
    internal sealed class SnippetsLoader
    {
        private static readonly Guid SmsSnippetTypeGuid = new Guid( "D6074803-9405-47E3-974C-E95C9AD05874" );

        /// <summary>
        /// The church made its shared snippets this long before the text history starts.
        /// </summary>
        private const int MadeDaysBeforeHistory = 30;

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly AnchorDates _dates;
        private readonly string _foreignKey;

        public SnippetsLoader( RockDatabase db, SampleDataSource source, AnchorDates dates, string foreignKey )
        {
            _db = db;
            _source = source;
            _dates = dates;
            _foreignKey = foreignKey;
        }

        /// <summary>
        /// Writes every snippet whose owner this load has. Returns them, for the texts that use their words, and the share
        /// of threads in their topics that do.
        /// </summary>
        /// <param name="people">Everyone loaded.</param>
        /// <param name="campuses">The dataset's campuses, with their service times.</param>
        /// <param name="campusPastors">Each campus's campus pastor.</param>
        /// <param name="creatorGuid">The church's communications sender, who made the shared snippets.</param>
        /// <param name="historyStart">The day the text history starts.</param>
        public (List<TextSnippet> Snippets, double ReplyShare) Load( PeopleLoader people, List<EmailCampus> campuses, IReadOnlyDictionary<string, LoadedPerson> campusPastors, Guid creatorGuid, DateTime historyStart )
        {
            var library = _source.ReadSnippetLibrary();
            var snippets = new List<TextSnippet>();
            if ( library == null )
            {
                return (snippets, 0);
            }

            var typeId = _db.RequireIdByGuid( "SnippetType", SmsSnippetTypeGuid );
            var categories = _source.Categories.Categories.ToDictionary( c => c.Key, c => c.Guid, StringComparer.Ordinal );
            var staff = _source.Staff?.Staff ?? new List<StaffMember>();
            var creator = people.People.TryGetValue( creatorGuid, out var sender ) ? sender.AliasId : ( int? ) null;
            var sharedMade = historyStart.Date.AddDays( -MadeDaysBeforeHistory ).AddHours( 10 );
            var order = _db.Scalar( "SELECT MAX( [Order] ) + 1 FROM [Snippet]" ) is int next ? next : 0;

            foreach ( var definition in library.Snippets )
            {
                var copies = definition.CampusGuids == null
                    ? new List<(EmailCampus Campus, Guid Guid)> { (null, definition.Guid.Value) }
                    : campuses.Where( c => definition.CampusGuids.ContainsKey( c.Key ) ).Select( c => (c, definition.CampusGuids[c.Key]) ).ToList();
                foreach ( var (campus, guid) in copies )
                {
                    // A personal snippet loads only with its owner, so a campus pastor's comes only with their campus.
                    var owner = Owner( definition, campus, staff, people, campusPastors );
                    if ( definition.Owner != null && owner == null )
                    {
                        continue;
                    }

                    var member = owner == null ? null : staff.FirstOrDefault( s => s.Person == owner.Cast.Guid );
                    var joined = member == null ? null : _dates.Resolve( member.StartDate );
                    var made = joined.HasValue && joined.Value.Date.AddHours( 10 ) > sharedMade ? joined.Value.Date.AddHours( 10 ) : sharedMade;
                    var tokens = new Dictionary<string, string>
                    {
                        ["{campus}"] = campus?.Name ?? string.Empty,
                        ["{serviceTimes}"] = campus?.ServiceTimes ?? string.Empty,
                        ["{staff}"] = owner == null ? string.Empty : owner.Cast.NickName ?? owner.Cast.FirstName
                    };
                    var text = MessageRenderer.Fill( definition.Text, tokens );
                    var author = owner?.AliasId ?? creator;

                    _db.Execute( @"
INSERT INTO [Snippet] ( [Name], [Description], [SnippetTypeId], [IsActive], [Content], [Order], [OwnerPersonAliasId], [CategoryId],
    [Guid], [CreatedDateTime], [ModifiedDateTime], [CreatedByPersonAliasId], [ModifiedByPersonAliasId], [ForeignKey] )
VALUES ( @Name, @Description, @TypeId, 1, @Content, @Order, @Owner, @CategoryId, @Guid, @Made, @Made, @Author, @Author, @ForeignKey )",
                        P( "@Name", MessageRenderer.Fill( definition.Name, tokens ) ), P( "@Description", MessageRenderer.Fill( definition.Description, tokens ) ),
                        P( "@TypeId", typeId ), P( "@Content", text ), P( "@Order", order++ ), P( "@Owner", owner?.AliasId ),
                        P( "@CategoryId", definition.Category == null ? null : _db.RequireIdByGuid( "Category", categories[definition.Category] ) ),
                        P( "@Guid", guid ), P( "@Made", made ), P( "@Author", author ), P( "@ForeignKey", _foreignKey ) );

                    snippets.Add( new TextSnippet
                    {
                        Key = definition.Key,
                        Text = text,
                        OwnerAliasId = owner?.AliasId,
                        CampusKey = owner == null ? campus?.Key : null,
                        Topics = definition.Topics,
                        Made = made
                    } );
                }
            }

            var personal = snippets.Count( s => s.OwnerAliasId.HasValue );
            _db.Log.Write( $"  SMS snippets: {snippets.Count - personal} shared and {personal} personal" );
            return (snippets, library.ReplyShare);
        }

        /// <summary>
        /// The person a snippet belongs to: a named staff member, the campus's campus pastor, or null for a shared
        /// snippet or an owner this load does not have.
        /// </summary>
        private static LoadedPerson Owner( SnippetDefinition definition, EmailCampus campus, List<StaffMember> staff, PeopleLoader people, IReadOnlyDictionary<string, LoadedPerson> campusPastors )
        {
            if ( definition.Owner == null )
            {
                return null;
            }

            if ( definition.Owner == SnippetLibrary.CampusPastorOwner )
            {
                return campus != null && campusPastors.TryGetValue( campus.Key, out var pastor ) ? pastor : null;
            }

            var member = staff.FirstOrDefault( s => s.Key == definition.Owner );
            return member != null && people.People.TryGetValue( member.Person, out var person ) ? person : null;
        }
    }
}
