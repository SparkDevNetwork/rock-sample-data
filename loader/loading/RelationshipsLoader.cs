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

using System.Data;

using RockSampleData.Loader.Database;

namespace RockSampleData.Loader.Loading
{
    /*
        10/19/26 - CLAUDE

        Rock keeps known relationships as one Known Relationships group per person, owned by that
        person. A business contact is two rows, as Rock's PersonService.AddContactToBusiness writes
        them: the contact in the business's group with the Business Contact role, and the business in
        the contact's group with the Business role. The people step already gave every person their
        group, the way Rock Cleanup does, so these rows go into those groups.

        Reason: Business Detail lists contacts, and a person's Known Relationships shows the business.
    */

    /// <summary>
    /// Loads the "business" relationships: each business's contacts, both ways.
    /// </summary>
    internal sealed class RelationshipsLoader
    {
        private static readonly Guid KnownRelationshipsGroupTypeGuid = new Guid( "E0C5A0E2-B7B3-4EF4-820D-BBF7F9A374EF" );
        private static readonly Guid BusinessContactRoleGuid = new Guid( "102E6AF5-62C2-4767-B473-C9C228D75FB6" );
        private static readonly Guid BusinessRoleGuid = new Guid( "7FC58BB2-7C1E-4C5C-B2B3-4738258A0BE0" );

        private readonly RockDatabase _db;
        private readonly string _foreignKey;
        private readonly DateTime _now = DateTime.Now;

        public RelationshipsLoader( RockDatabase db, string foreignKey )
        {
            _db = db;
            _foreignKey = foreignKey;
        }

        /// <summary>
        /// Writes every business contact found on the loaded people. Returns the number of contacts.
        /// </summary>
        public int Load( IReadOnlyDictionary<Guid, LoadedPerson> people )
        {
            var contacts = people.Values
                .SelectMany( p => p.Cast.Relationships.Where( r => r.Type == "business" ).Select( r => (Contact: p, Business: Business( people, p, r.With )) ) )
                .ToList();
            if ( contacts.Count == 0 )
            {
                return 0;
            }

            var groupTypeId = _db.RequireIdByGuid( "GroupType", KnownRelationshipsGroupTypeGuid );
            var contactRoleId = _db.RequireIdByGuid( "GroupTypeRole", BusinessContactRoleGuid );
            var businessRoleId = _db.RequireIdByGuid( "GroupTypeRole", BusinessRoleGuid );

            var nextMemberId = NextId( "GroupMember" );
            var members = TableFactory.Create( "Id:int", "IsSystem:bool", "GroupId:int", "GroupTypeId:int", "PersonId:int", "GroupRoleId:int", "GroupMemberStatus:int", "DateTimeAdded:date", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            foreach ( var (contact, business) in contacts )
            {
                members.Rows.Add( nextMemberId++, false, business.KnownRelationshipGroupId, groupTypeId, contact.PersonId, contactRoleId, 1, _now, Guid.NewGuid(), _foreignKey, _now, _now );
                members.Rows.Add( nextMemberId++, false, contact.KnownRelationshipGroupId, groupTypeId, business.PersonId, businessRoleId, 1, _now, Guid.NewGuid(), _foreignKey, _now, _now );
            }

            _db.BulkInsert( "GroupMember", members, keepIdentity: true );
            return contacts.Count;
        }

        /// <summary>
        /// The business a "business" relationship names, which must be a loaded business record.
        /// </summary>
        private static LoadedPerson Business( IReadOnlyDictionary<Guid, LoadedPerson> people, LoadedPerson contact, Guid businessGuid )
        {
            if ( !people.TryGetValue( businessGuid, out var business ) || business.Cast.RecordType != "business" )
            {
                throw new InvalidOperationException( $"{contact.Cast.Key} is a contact of {businessGuid}, which is not a loaded business." );
            }

            return business;
        }

        /// <summary>
        /// The next free Id in a table.
        /// </summary>
        private int NextId( string table )
        {
            return Convert.ToInt32( _db.Scalar( $"SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [{table}]" ) );
        }
    }
}
