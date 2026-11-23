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

    /*
        11/23/26 - CLAUDE

        Every relationship type now loads, the way Rock's GroupMemberService.CreateKnownRelationship writes one: the
        other person in the owner's group with the line's role, and the owner in the other person's group with that
        role's inverse, which Rock keeps in the role's InverseRelationship attribute. A Cast line states a pair once,
        and the validator refuses a pair stated on both people. A pair stated twice in an older package still writes
        each row once.

        Reason: Six Cast lines never loaded, so no kiosk showed a child a parent in another family may check in
        (decision D-checkin-config-1).
    */

    /// <summary>
    /// One known relationship: <see cref="With"/> holds <see cref="Type"/>'s role in the owner's group.
    /// </summary>
    internal sealed record KnownRelationship( LoadedPerson Owner, string Type, LoadedPerson With );

    /// <summary>
    /// Loads known relationships, both halves of each.
    /// </summary>
    internal sealed class RelationshipsLoader
    {
        private static readonly Guid KnownRelationshipsGroupTypeGuid = new Guid( "E0C5A0E2-B7B3-4EF4-820D-BBF7F9A374EF" );
        private static readonly Guid BusinessContact = new Guid( "102E6AF5-62C2-4767-B473-C9C228D75FB6" );
        private static readonly Guid Business = new Guid( "7FC58BB2-7C1E-4C5C-B2B3-4738258A0BE0" );
        private static readonly Guid CanCheckIn = new Guid( "DC8E5C35-F37C-4B49-A5C6-DF3D94FC808F" );
        private static readonly Guid AllowCheckInBy = new Guid( "FF9869F1-BC56-4410-8A12-CAFC32C62257" );
        private static readonly Guid Parent = new Guid( "6F3FADC4-6320-4B54-9CF6-02EF9586A660" );
        private static readonly Guid Child = new Guid( "F87DF00F-E86D-4771-A3AE-DBF79B78CF5D" );
        private static readonly Guid Grandparent = new Guid( "567DA89F-3C43-443D-A988-C05BC516EF28" );
        private static readonly Guid Grandchild = new Guid( "C1A393B2-519D-4E46-A551-E48C36BCAC06" );
        private static readonly Guid StepParent = new Guid( "D14827EF-5D43-442D-8134-DEB58AAC93C5" );
        private static readonly Guid StepChild = new Guid( "EFD2D6D1-A407-4EFB-9086-5DF1F19B7D93" );

        /// <summary>
        /// Each Cast type: the other person's role in the owner's group, and the owner's role in theirs. The Cast
        /// schema and validator list the same types.
        /// </summary>
        internal static readonly IReadOnlyDictionary<string, (Guid Role, Guid Inverse)> Roles = new Dictionary<string, (Guid, Guid)>
        {
            ["business"] = (Business, BusinessContact),
            ["can-check-in"] = (CanCheckIn, AllowCheckInBy),
            ["allow-check-in-by"] = (AllowCheckInBy, CanCheckIn),
            ["parent"] = (Parent, Child),
            ["child"] = (Child, Parent),
            ["grandparent"] = (Grandparent, Grandchild),
            ["grandchild"] = (Grandchild, Grandparent),
            ["step-parent"] = (StepParent, StepChild),
            ["step-child"] = (StepChild, StepParent)
        };

        private readonly RockDatabase _db;
        private readonly string _foreignKey;
        private readonly DateTime _now = DateTime.Now;

        public RelationshipsLoader( RockDatabase db, string foreignKey )
        {
            _db = db;
            _foreignKey = foreignKey;
        }

        /// <summary>
        /// The relationships the loaded people's household files state: the Cast's, and each generated business contact's.
        /// </summary>
        public static List<KnownRelationship> Stated( IReadOnlyDictionary<Guid, LoadedPerson> people )
        {
            return people.Values
                .SelectMany( p => p.Cast.Relationships.Select( r => new KnownRelationship( p, r.Type, Other( people, p, r.Type, r.With ) ) ) )
                .ToList();
        }

        /// <summary>
        /// Writes each relationship's two rows. Returns how many relationships were written.
        /// </summary>
        public int Load( IReadOnlyCollection<KnownRelationship> relationships )
        {
            if ( relationships.Count == 0 )
            {
                return 0;
            }

            var groupTypeId = _db.RequireIdByGuid( "GroupType", KnownRelationshipsGroupTypeGuid );
            var roleIds = Roles.Values.SelectMany( r => new[] { r.Role, r.Inverse } ).Distinct().ToDictionary( g => g, g => _db.RequireIdByGuid( "GroupTypeRole", g ) );

            var nextMemberId = Convert.ToInt32( _db.Scalar( "SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [GroupMember]" ) );
            var members = TableFactory.Create( "Id:int", "IsSystem:bool", "GroupId:int", "GroupTypeId:int", "PersonId:int", "GroupRoleId:int", "GroupMemberStatus:int", "DateTimeAdded:date", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var written = new HashSet<(int Group, int Person, int Role)>();
            void Add( LoadedPerson owner, LoadedPerson member, Guid role )
            {
                var roleId = roleIds[role];
                if ( written.Add( (owner.KnownRelationshipGroupId, member.PersonId, roleId) ) )
                {
                    members.Rows.Add( nextMemberId++, false, owner.KnownRelationshipGroupId, groupTypeId, member.PersonId, roleId, 1, _now, Guid.NewGuid(), _foreignKey, _now, _now );
                }
            }

            foreach ( var relationship in relationships )
            {
                if ( !Roles.TryGetValue( relationship.Type, out var roles ) )
                {
                    throw new InvalidDataException( $"{relationship.Owner.Cast.Key} has a \"{relationship.Type}\" relationship, a type the loader does not map: {string.Join( ", ", Roles.Keys )}." );
                }

                Add( relationship.Owner, relationship.With, roles.Role );
                Add( relationship.With, relationship.Owner, roles.Inverse );
            }

            _db.BulkInsert( "GroupMember", members, keepIdentity: true );
            return relationships.Count;
        }

        /// <summary>
        /// The person a relationship names. A business relationship must name a loaded business record.
        /// </summary>
        private static LoadedPerson Other( IReadOnlyDictionary<Guid, LoadedPerson> people, LoadedPerson owner, string type, Guid with )
        {
            if ( !people.TryGetValue( with, out var other ) )
            {
                throw new InvalidOperationException( $"{owner.Cast.Key} has a \"{type}\" relationship with {with}, who is not loaded." );
            }

            if ( type == "business" && other.Cast.RecordType != "business" )
            {
                throw new InvalidOperationException( $"{owner.Cast.Key} is a contact of {with}, which is not a loaded business." );
            }

            return other;
        }
    }
}
