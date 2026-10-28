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
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /*
        10/15/26 - CLAUDE

        People are bulk-copied with Ids assigned here (current maximum Id + 1, and up),
        so every foreign key is known before anything is written and no lookups are needed
        during the copy. Bulk copy skips Rock's save hooks, so this loader also fills in
        what those hooks normally set: birth date and key, age and age bracket, age
        classification, primary alias, giving Id and giving leader, primary family and
        campus, the family salutations, and the phone's formatted forms. It also adds the
        rows Rock adds for every person: an alternate Id search key, the names' metaphones,
        and a Known Relationship group the person owns.

        Reason: Bulk insert is what makes the install fast.
    */

    /// <summary>
    /// Loads the Cast: family groups, people, aliases, family memberships, home addresses, and phones.
    /// </summary>
    internal sealed class PeopleLoader
    {
        private static readonly Guid FamilyGroupTypeGuid = new Guid( "790E3215-3B10-442B-AF69-616C0DCB998E" );
        private static readonly Guid AdultRoleGuid = new Guid( "2639F9A5-2AAE-4E48-A8C3-4FFE86681E42" );
        private static readonly Guid ChildRoleGuid = new Guid( "C8B1814F-6AA7-4055-B2D7-48FE20429CB9" );
        private static readonly Guid PersonRecordTypeGuid = new Guid( "36CF10D6-C695-413D-8E7C-4546EFEF385E" );
        private static readonly Guid BusinessRecordTypeGuid = new Guid( "BF64ADD3-E70A-44CE-9C4B-E76BBED37550" );
        private static readonly Guid ActiveStatusGuid = new Guid( "618F906C-C33D-4FA3-8AEF-E58CB7B63F1E" );
        private static readonly Guid InactiveStatusGuid = new Guid( "1DAD99D5-41A9-4865-8366-F269902B80A4" );
        private static readonly Guid PendingStatusGuid = new Guid( "283999EC-7346-42E3-B807-BCE9B2BABB49" );
        private static readonly Guid DeceasedReasonGuid = new Guid( "05D35BC4-5816-4210-965F-1BF44F35A16A" );
        private static readonly Guid MarriedGuid = new Guid( "5FE5A540-7D9F-433E-B47E-4229D1472248" );
        private static readonly Guid HomeLocationTypeGuid = new Guid( "8C52E53C-2A66-435A-AE6E-5EE307D9A0DC" );
        private static readonly Guid WorkLocationTypeGuid = new Guid( "E071472A-F805-4FC4-917A-D5E3C095C35C" );
        private static readonly Guid HomePhoneGuid = new Guid( "AA8732FB-2CEA-4C76-8D6D-6AAA2C6A4303" );
        private static readonly Guid MobilePhoneGuid = new Guid( "407E7E45-7B2E-4FCD-9605-ECB1339F2453" );
        private static readonly Guid WorkPhoneGuid = new Guid( "2CC66D5A-F61C-4B74-9AF9-590A9847C13C" );
        private static readonly Guid KnownRelationshipGroupTypeGuid = new Guid( "E0C5A0E2-B7B3-4EF4-820D-BBF7F9A374EF" );
        private static readonly Guid KnownRelationshipOwnerRoleGuid = new Guid( "7BC6C12E-0CD1-4DFD-8D5B-1B35AE714C42" );
        private static readonly Guid AlternateIdSearchKeyGuid = new Guid( "AD77CF28-E42B-44C2-8D5C-F6A0A6EE8022" );

        /// <summary>
        /// The admin person Rock's first migration inserts. Its Guid differs between installs, so it is known by its Id.
        /// </summary>
        internal const int AdminPersonId = 1;

        /// <summary>
        /// The Person columns the admin person keeps from Rock: its identity, its system flag, its alias, when Rock made
        /// it, and its empty ForeignKey, so nothing that removes sample data by ForeignKey can ever remove the site's admin.
        /// </summary>
        private static readonly HashSet<string> AdminKeptColumns = new HashSet<string>( StringComparer.Ordinal )
        {
            "Id", "Guid", "IsSystem", "PrimaryAliasId", "PrimaryAliasGuid", "CreatedDateTime", "ForeignKey"
        };

        /// <summary>
        /// A SQL condition for this dataset's people, for the Person table under this alias: every person the load added
        /// (marked by @ForeignKey), and Rock's admin person, which the load turned into a Cast person (decision 177).
        /// </summary>
        internal static string DatasetPersonFilter( string alias )
        {
            return $"( [{alias}].[ForeignKey] = @ForeignKey OR [{alias}].[Id] = {AdminPersonId} )";
        }

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly AnchorDates _dates;
        private readonly string _foreignKey;
        private readonly Dictionary<string, int> _campusIds;
        private readonly string _mainCampusKey;
        private readonly DateTime _now = DateTime.Now;

        public PeopleLoader( RockDatabase db, SampleDataSource source, AnchorDates dates, string foreignKey, Dictionary<string, int> campusIds, string mainCampusKey )
        {
            _db = db;
            _source = source;
            _dates = dates;
            _foreignKey = foreignKey;
            _campusIds = campusIds;
            _mainCampusKey = mainCampusKey;
        }

        /// <summary>
        /// The loaded people, keyed by Cast Guid, for later steps (giving, attendance).
        /// </summary>
        public Dictionary<Guid, LoadedPerson> People { get; } = new Dictionary<Guid, LoadedPerson>();

        /// <summary>
        /// The loaded family groups, keyed by household key.
        /// </summary>
        public Dictionary<string, int> FamilyIds { get; } = new Dictionary<string, int>();

        /// <summary>
        /// The loaded households, in load order.
        /// </summary>
        public List<LoadedHousehold> Households { get; } = new List<LoadedHousehold>();


        /// <summary>
        /// Builds every row in memory, then bulk-copies each table in foreign key order.
        /// </summary>
        public void Load( List<CastHousehold> households )
        {
            var familyTypeId = _db.RequireIdByGuid( "GroupType", FamilyGroupTypeGuid );
            var adultRoleId = _db.RequireIdByGuid( "GroupTypeRole", AdultRoleGuid );
            var childRoleId = _db.RequireIdByGuid( "GroupTypeRole", ChildRoleGuid );
            var relationshipTypeId = _db.RequireIdByGuid( "GroupType", KnownRelationshipGroupTypeGuid );
            var relationshipOwnerRoleId = _db.RequireIdByGuid( "GroupTypeRole", KnownRelationshipOwnerRoleGuid );
            var relationshipGroupName = ( string ) _db.Scalar( "SELECT [Name] FROM [GroupType] WHERE [Id] = @Id", P( "@Id", relationshipTypeId ) );
            var lookups = new Lookups( _db, _source );
            var admin = ReadAdmin( familyTypeId, relationshipTypeId, relationshipOwnerRoleId );

            var nextGroupId = NextId( "Group" );
            var nextPersonId = NextId( "Person" );
            var nextAliasId = NextId( "PersonAlias" );
            var nextMemberId = NextId( "GroupMember" );
            var nextLocationId = NextId( "Location" );
            var nextGroupLocationId = NextId( "GroupLocation" );
            var nextPhoneId = NextId( "PhoneNumber" );

            var groups = NewTable( "Id:int", "IsSystem:bool", "GroupTypeId:int", "CampusId:int", "Name:string", "IsSecurityRole:bool", "IsActive:bool", "Order:int", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date",
                "GroupSalutation:string", "GroupSalutationFull:string", "InactiveDateTime:date" );
            var persons = NewTable( "Id:int", "IsSystem:bool", "RecordTypeValueId:int", "RecordStatusValueId:int", "RecordStatusReasonValueId:int", "ConnectionStatusValueId:int", "IsDeceased:bool",
                "FirstName:string", "NickName:string", "MiddleName:string", "LastName:string", "BirthDay:int", "BirthMonth:int", "BirthYear:int", "BirthDate:date", "BirthDateKey:int", "Age:int", "AgeBracket:int",
                "Gender:int", "MaritalStatusValueId:int", "AnniversaryDate:date", "GraduationYear:int", "GivingGroupId:int", "GivingId:string", "GivingLeaderId:int", "Email:string", "IsEmailActive:bool",
                "EmailPreference:int", "CommunicationPreference:int", "AgeClassification:int", "PrimaryFamilyId:int", "PrimaryCampusId:int", "PrimaryAliasId:int", "PrimaryAliasGuid:guid",
                "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var aliases = NewTable( "Id:int", "PersonId:int", "AliasPersonId:int", "AliasPersonGuid:guid", "Guid:guid", "ForeignKey:string" );
            var members = NewTable( "Id:int", "IsSystem:bool", "GroupId:int", "GroupTypeId:int", "PersonId:int", "GroupRoleId:int", "GroupMemberStatus:int", "DateTimeAdded:date", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var locations = NewTable( "Id:int", "Street1:string", "Street2:string", "City:string", "State:string", "PostalCode:string", "Country:string", "IsActive:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var geo = NewTable( "Id:int", "Latitude:double", "Longitude:double" );
            var groupLocations = NewTable( "Id:int", "GroupId:int", "LocationId:int", "GroupLocationTypeValueId:int", "IsMailingLocation:bool", "IsMappedLocation:bool", "Order:int", "IsOverflowLocation:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var phones = NewTable( "Id:int", "IsSystem:bool", "PersonId:int", "CountryCode:string", "Number:string", "NumberFormatted:string", "FullNumber:string", "NumberTypeValueId:int", "IsMessagingEnabled:bool", "IsUnlisted:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            var homeLocationIds = new Dictionary<string, int>();
            var familyRows = new Dictionary<int, DataRow>();
            var salutationMembers = new Dictionary<int, List<SalutationMember>>();

            // Pass 1: assign every family and person an Id, so shared members can point across households.
            foreach ( var household in households )
            {
                FamilyIds[household.Key] = nextGroupId++;
                var campusId = _campusIds[CampusKeyFor( household )];
                foreach ( var person in household.Members )
                {
                    People[person.Guid] = person.ReplacesAdmin && admin != null
                        ? new LoadedPerson { PersonId = AdminPersonId, AliasId = admin.AliasId, AliasGuid = admin.AliasGuid, KnownRelationshipGroupId = admin.KnownRelationshipGroupId, IsRockAdmin = true, FamilyId = FamilyIds[household.Key], CampusId = campusId, Cast = person }
                        : new LoadedPerson { PersonId = nextPersonId++, AliasId = nextAliasId++, AliasGuid = Guid.NewGuid(), FamilyId = FamilyIds[household.Key], CampusId = campusId, Cast = person };
                }
            }

            // Pass 2: build the rows.
            foreach ( var household in households )
            {
                var familyId = FamilyIds[household.Key];
                var familyCampusId = _campusIds[CampusKeyFor( household )];
                familyRows[familyId] = groups.Rows.Add( familyId, false, familyTypeId, familyCampusId, household.Name, false, true, 0, household.Guid, _foreignKey, _now, _now );
                var familySalutationMembers = salutationMembers[familyId] = new List<SalutationMember>();

                var givingLeader = GivingLeader( household );
                foreach ( var person in household.Members )
                {
                    var loaded = People[person.Guid];
                    var isAdult = person.FamilyRole != "child";
                    var isBusiness = person.RecordType == "business";
                    DateTime? birthDate = person.Birth == null ? null : _dates.BirthDate( person.Birth );
                    int? age = birthDate.HasValue ? _dates.AgeOn( birthDate.Value ) : null;
                    var grade = person.Grade ?? DerivedGrade( person, age );
                    var givesWithFamily = isAdult && GivesWithFamily( household, person );
                    int? givingGroupId = givesWithFamily ? familyId : null;
                    var givingLeaderId = givesWithFamily && givingLeader != null ? People[givingLeader.Guid].PersonId : loaded.PersonId;
                    loaded.GivingId = givingGroupId.HasValue ? $"G{givingGroupId}" : $"P{loaded.PersonId}";

                    /*
                        10/22/26 - CLAUDE

                        A grade is derived only for ages 5 to 17, so a child who just graduated (Jasmine Washington)
                        had no graduation year and never checked in. Now a child whose file sets a graduation keeps
                        that year, and attendance checks them in by grade until they graduate.

                        Reason: Check-in and the Student Path follow a recent graduate's real school years (decision 118).
                    */
                    /*
                        10/23/26 - CLAUDE

                        An adult keeps their file's graduation year too. Attendance reads it only when it falls inside
                        the history window, as it does for the generated adults aged 18 to 20, whose graduation the
                        Crowd sets: they check in as students until they graduate. The Cast's adult graduations are
                        decades old, so nothing changes for the Cast.

                        Reason: Recent graduates get their student years (decision 148).
                    */
                    var fileGraduationYear = _dates.Resolve( person.Graduation )?.Year;
                    loaded.BirthDate = birthDate;
                    loaded.GraduationYear = isAdult ? fileGraduationYear : grade.HasValue ? _dates.GraduationYear( grade.Value ) : fileGraduationYear;
                    loaded.IsAdult = isAdult;
                    loaded.CanAttend = !isBusiness && !person.IsDeceased && person.RecordStatus != "inactive";

                    persons.Rows.Add(
                        loaded.PersonId, false,
                        lookups.RecordType( isBusiness ? BusinessRecordTypeGuid : PersonRecordTypeGuid ),
                        lookups.RecordStatus( person.RecordStatus ),
                        person.IsDeceased ? lookups.Id( DeceasedReasonGuid ) : ( object ) DBNull.Value,
                        lookups.ConnectionStatus( person.ConnectionStatus ),
                        person.IsDeceased,
                        person.FirstName, person.NickName ?? person.FirstName, person.MiddleName ?? ( object ) DBNull.Value, person.LastName,
                        Value( birthDate?.Day ), Value( birthDate?.Month ), Value( birthDate?.Year ), Value( birthDate ),
                        birthDate.HasValue ? ( object ) int.Parse( birthDate.Value.ToString( "yyyyMMdd" ) ) : DBNull.Value,
                        Value( age ), AnchorDates.AgeBracket( age ),
                        person.Gender == "male" ? 1 : person.Gender == "female" ? 2 : 0,
                        Value( lookups.MaritalStatus( person.MaritalStatus ) ),
                        Value( _dates.Resolve( person.Anniversary ) ),
                        Value( isAdult ? fileGraduationYear : loaded.GraduationYear ),
                        Value( givingGroupId ),
                        loaded.GivingId,
                        givingLeaderId,
                        person.Email ?? ( object ) DBNull.Value,
                        person.EmailIsActive,
                        person.EmailDoNotEmail ? 2 : 0,
                        person.PrefersText ? 2 : 1,
                        AgeClassification( person, isAdult, isBusiness, age ),
                        familyId, familyCampusId,
                        loaded.AliasId, loaded.AliasGuid,
                        person.Guid, _foreignKey, _now, _now );

                    if ( !loaded.IsRockAdmin )
                    {
                        aliases.Rows.Add( loaded.AliasId, loaded.PersonId, loaded.PersonId, person.Guid, loaded.AliasGuid, _foreignKey );
                    }

                    members.Rows.Add( nextMemberId++, false, familyId, familyTypeId, loaded.PersonId, isAdult ? adultRoleId : childRoleId, 1, _now, Guid.NewGuid(), _foreignKey, _now, _now );
                    familySalutationMembers.Add( SalutationMemberFor( person, isAdult, age ) );

                    AddPhone( phones, ref nextPhoneId, loaded.PersonId, person.Phones?.Home, lookups.Id( HomePhoneGuid ), false );
                    AddPhone( phones, ref nextPhoneId, loaded.PersonId, person.Phones?.Mobile, lookups.Id( MobilePhoneGuid ), true );
                    AddPhone( phones, ref nextPhoneId, loaded.PersonId, person.Phones?.Work, lookups.Id( WorkPhoneGuid ), false );
                }

                // People who also belong to this family (for example a child of divorced parents).
                foreach ( var shared in household.SharedMembers )
                {
                    var loaded = People[shared.Guid];
                    var isSharedAdult = loaded.Cast.FamilyRole != "child";
                    members.Rows.Add( nextMemberId++, false, familyId, familyTypeId, loaded.PersonId, isSharedAdult ? adultRoleId : childRoleId, 1, _now, Guid.NewGuid(), _foreignKey, _now, _now );
                    familySalutationMembers.Add( SalutationMemberFor( loaded.Cast, isSharedAdult, loaded.Cast.Birth == null ? null : _dates.AgeOn( _dates.BirthDate( loaded.Cast.Birth ) ) ) );
                }

                if ( household.Address != null )
                {
                    var address = household.Address;
                    var locationId = nextLocationId++;
                    locations.Rows.Add( locationId, address.Street1, address.Street2 ?? ( object ) DBNull.Value, address.City, address.State, address.PostalCode, "US", true, Guid.NewGuid(), _foreignKey, _now, _now );
                    if ( address.Latitude.HasValue && address.Longitude.HasValue )
                    {
                        geo.Rows.Add( locationId, address.Latitude.Value, address.Longitude.Value );
                    }

                    var isHome = address.Type == "home";
                    if ( isHome )
                    {
                        homeLocationIds[household.Key] = locationId;
                    }

                    // A business's address is its Work location, the one Rock's Business Detail shows and edits.
                    // Like a home, it is the mailing and mapped address.
                    var isWork = address.Type == "work";
                    var isMailing = isHome || isWork;
                    var locationTypeId = lookups.Id( isWork ? WorkLocationTypeGuid : HomeLocationTypeGuid );
                    groupLocations.Rows.Add( nextGroupLocationId++, familyId, locationId, locationTypeId, isMailing, isMailing, 0, false, Guid.NewGuid(), _foreignKey, _now, _now );
                }
            }

            // Rock Cleanup marks a family inactive when none of its members has a record status other than
            // Inactive (PersonCleanup in Rock/Jobs/RockCleanup.cs), and Rock dates it then. So does the loader.
            foreach ( var household in households )
            {
                var familyId = FamilyIds[household.Key];
                var family = familyRows[familyId];
                family["GroupSalutation"] = FamilySalutation.Calculate( household.Name, salutationMembers[familyId], includeChildren: false );
                family["GroupSalutationFull"] = FamilySalutation.Calculate( household.Name, salutationMembers[familyId], includeChildren: true );

                var isActive = household.Members.Concat( household.SharedMembers.Select( s => People[s.Guid].Cast ) ).Any( p => p.RecordStatus != "inactive" );
                if ( !isActive )
                {
                    family["IsActive"] = false;
                    family["InactiveDateTime"] = _now;
                }
            }

            var relationshipGroupCount = AddKnownRelationshipGroups( groups, members, ref nextGroupId, ref nextMemberId, relationshipTypeId, relationshipOwnerRoleId, relationshipGroupName );
            var searchKeys = AlternateIdSearchKeys( lookups.Id( AlternateIdSearchKeyGuid ) );
            var metaphones = Metaphones();

            // The household view later steps use. Shared children appear in every household they belong to.
            foreach ( var household in households )
            {
                var loadedHousehold = new LoadedHousehold { Cast = household, FamilyId = FamilyIds[household.Key], CampusKey = CampusKeyFor( household ), HomeLocationId = homeLocationIds.TryGetValue( household.Key, out var home ) ? home : null };
                loadedHousehold.Members.AddRange( household.Members.Select( m => People[m.Guid] ) );
                loadedHousehold.Members.AddRange( household.SharedMembers.Select( s => People[s.Guid] ) );
                Households.Add( loadedHousehold );
            }

            // The admin person's row updates Person 1 after the other people are in, since its giving leader may be one of them.
            var adminRow = persons.Rows.Cast<DataRow>().SingleOrDefault( r => ( int ) r["Id"] == AdminPersonId && admin != null );
            var adminValues = adminRow?.Table.Columns.Cast<DataColumn>().ToDictionary( c => c.ColumnName, c => adminRow[c] );
            if ( adminRow != null )
            {
                persons.Rows.Remove( adminRow );
            }

            _db.BulkInsert( "Group", groups, keepIdentity: true );
            _db.BulkInsert( "Person", persons, keepIdentity: true );
            if ( adminValues != null )
            {
                UpdateAdmin( adminValues );
            }

            _db.BulkInsert( "PersonAlias", aliases, keepIdentity: true );
            _db.BulkInsert( "GroupMember", members, keepIdentity: true );
            if ( adminValues != null )
            {
                RemoveAdminFamilies( admin );
            }

            _db.BulkInsert( "Location", locations, keepIdentity: true );
            _db.BulkInsert( "GroupLocation", groupLocations, keepIdentity: true );
            _db.BulkInsert( "PhoneNumber", phones, keepIdentity: true );
            _db.BulkInsert( "PersonSearchKey", searchKeys, keepIdentity: false );
            var newMetaphoneNames = AddNewMetaphones( metaphones );
            SetGeoPoints( geo );

            var adminText = adminValues == null ? "" : $" (and {adminValues["NickName"]} {adminValues["LastName"]} in Rock's admin person)";
            _db.Log.Write( $"  Families: {households.Count}, people: {persons.Rows.Count}{adminText}, family members: {members.Rows.Count - relationshipGroupCount}, addresses: {locations.Rows.Count}, phones: {phones.Rows.Count}, "
                + $"known relationship groups: {relationshipGroupCount}, new metaphone names: {newMetaphoneNames}" );
        }

        /// <summary>
        /// The campus key for a household: the Cast's "main" slot is the main campus; anything else names a campus.
        /// </summary>
        private string CampusKeyFor( CastHousehold household )
        {
            return household.CampusSlot == "main" || string.IsNullOrEmpty( household.CampusSlot ) ? _mainCampusKey : household.CampusSlot;
        }

        /// <summary>
        /// Rock's giving leader for a family: among the adults who give with the family, the first
        /// by gender (male first), then the oldest.
        /// </summary>
        private CastPerson GivingLeader( CastHousehold household )
        {
            return household.Members
                .Where( p => p.FamilyRole != "child" && GivesWithFamily( household, p ) )
                .OrderBy( p => p.Gender == "male" ? 0 : p.Gender == "female" ? 1 : 2 )
                .ThenByDescending( p => p.Birth?.Age ?? 0 )
                .FirstOrDefault();
        }

        /*
            10/16/26 - CLAUDE

            Rock treats people who share a GivingGroupId as one giving unit (GivingId G{family}).
            Most couples give together. A couple can give separately (AdultsGiveSeparately), and any
            other adult living with a married couple, such as an adult child at home, always gives on
            their own (GivingId P{person}). A single adult who heads the family gives as the family.

            Reason: Rock's giving analytics, statements, and alerts are all per giving unit.
        */

        /// <summary>
        /// True when this adult is part of the family's giving unit.
        /// </summary>
        private static bool GivesWithFamily( CastHousehold household, CastPerson person )
        {
            if ( person.FamilyRole == "child" )
            {
                return false;
            }

            var hasCouple = household.Members.Count( m => m.FamilyRole != "child" && IsMarried( m ) ) >= 2;
            if ( !hasCouple )
            {
                return true;
            }

            return IsMarried( person ) && !household.AdultsGiveSeparately;
        }

        /// <summary>
        /// True for a married person. Cast files say "married"; the generated crowd says "Married".
        /// </summary>
        private static bool IsMarried( CastPerson person )
        {
            return string.Equals( person.MaritalStatus, "married", StringComparison.OrdinalIgnoreCase );
        }

        /// <summary>
        /// Rock's age classification (PersonService.UpdatePersonAgeClassifications): Adult (1) when the
        /// person is 18 or older or an adult in any family, otherwise Child (2). Rock's update skips the
        /// deceased, so they keep the family role's classification. Its nightly pass also skips businesses;
        /// a business holds the Adult role, so it is Adult either way.
        /// </summary>
        private static int AgeClassification( CastPerson person, bool isAdult, bool isBusiness, int? age )
        {
            var isClassifiedByRock = !isBusiness && !person.IsDeceased;
            var isAdultByRock = isAdult || age >= 18;
            return ( isClassifiedByRock ? isAdultByRock : isAdult ) ? 1 : 2;
        }

        /// <summary>
        /// What Rock's family salutation reads about one family member.
        /// </summary>
        private static SalutationMember SalutationMemberFor( CastPerson person, bool isAdult, int? age )
        {
            return new SalutationMember
            {
                NickName = person.NickName ?? person.FirstName,
                LastName = person.LastName,
                Gender = person.Gender == "male" ? 1 : person.Gender == "female" ? 2 : 0,
                Age = age,
                IsAdult = isAdult,
                IsActiveRecord = person.RecordStatus != "inactive" && person.RecordStatus != "pending",
                IsDeceased = person.IsDeceased
            };
        }

        /*
            10/19/26 - CLAUDE

            Rock Cleanup gives every person who owns no Known Relationship group a new one, named for the
            group type, with the person as its Owner (AddMissingRelationshipGroups in Rock/Jobs/RockCleanup.cs).
            Rock's relationship screens and the "known relationships" badge read it. The loader adds the
            same rows for every person, including the deceased, who would have had one while they were alive.

            Reason: Bulk copy skips what Rock adds for each person.
        */

        /// <summary>
        /// Adds one Known Relationship group per person, with the person as Owner, except the admin person, who
        /// owns one from Rock's install. Returns how many.
        /// </summary>
        private int AddKnownRelationshipGroups( DataTable groups, DataTable members, ref int nextGroupId, ref int nextMemberId, int groupTypeId, int ownerRoleId, string groupName )
        {
            var count = 0;
            foreach ( var person in People.Values.Where( p => p.KnownRelationshipGroupId == 0 ) )
            {
                count++;
                var groupId = nextGroupId++;
                person.KnownRelationshipGroupId = groupId;
                groups.Rows.Add( groupId, false, groupTypeId, DBNull.Value, groupName, false, true, 0, Guid.NewGuid(), _foreignKey, _now, _now, DBNull.Value, DBNull.Value );
                members.Rows.Add( nextMemberId++, false, groupId, groupTypeId, person.PersonId, ownerRoleId, 1, _now, Guid.NewGuid(), _foreignKey, _now, _now );
            }

            return count;
        }

        /*
            10/19/26 - CLAUDE

            Rock's person save hook gives every new person an alternate Id search key on their primary alias
            (Rock/Model/CRM/Person/Person.SaveHook.cs), made by PersonSearchKeyService.GenerateRandomAlternateId:
            the first 14 hex digits of a new Guid, as 7, a dash, and 7. Check-in and person search look people
            up by it. Each value must be unique among alternate Ids, so the ones already in Rock are read first.

            Reason: Bulk copy skips what Rock adds for each person.
        */

        /// <summary>
        /// One alternate Id search key per person.
        /// </summary>
        private DataTable AlternateIdSearchKeys( int alternateIdTypeId )
        {
            var taken = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
            foreach ( var row in _db.Query( "SELECT [SearchValue] FROM [PersonSearchKey] WHERE [SearchTypeValueId] = @TypeId AND [SearchValue] IS NOT NULL", P( "@TypeId", alternateIdTypeId ) ) )
            {
                taken.Add( ( string ) row["SearchValue"] );
            }

            var keys = NewTable( "PersonAliasId:int", "SearchTypeValueId:int", "SearchValue:string", "IsValuePrivate:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            foreach ( var person in People.Values )
            {
                string value;
                do
                {
                    var random = Guid.NewGuid().ToString( "N" );
                    value = $"{random.Substring( 0, 7 )}-{random.Substring( 7, 7 )}";
                }
                while ( !taken.Add( value ) );

                keys.Rows.Add( person.AliasId, alternateIdTypeId, value, false, Guid.NewGuid(), _foreignKey, _now, _now );
            }

            return keys;
        }

        /*
            10/19/26 - CLAUDE

            Rock's person save hook queues SaveMetaphoneTransaction (Rock/Transactions/SaveMetaphoneTransaction.cs),
            which adds each new first, nick, and last name to the Metaphone table with its two Double Metaphone
            keys. Rock's duplicate finder and sounds-like person search read them. The table is keyed by name
            and shared by everyone, so only names Rock does not have yet are added. Rock asks SQL Server whether
            a name is there, so the database's collation decides which names match: it treats "Strauß" and
            "Strauss" as one name, which no .NET string comparer does the same way. So SQL Server makes that
            choice here too. Metaphone has no ForeignKey column, so these rows are not marked as sample data;
            they hold only names, and Rock keeps them after a person is deleted too.

            Reason: Bulk copy skips what Rock adds for each person.
        */

        /// <summary>
        /// The candidate metaphone rows, one per distinct name in person order. SQL Server decides which are new.
        /// </summary>
        private DataTable Metaphones()
        {
            var seen = new HashSet<string>( StringComparer.Ordinal );
            var metaphones = NewTable( "Order:int", "Name:string", "Metaphone1:string", "Metaphone2:string" );
            foreach ( var person in People.Values.Select( p => p.Cast ) )
            {
                foreach ( var name in new[] { person.FirstName, person.NickName ?? person.FirstName, person.LastName } )
                {
                    if ( string.IsNullOrWhiteSpace( name ) || !seen.Add( name.Trim() ) )
                    {
                        continue;
                    }

                    string primary = null;
                    string alternate = null;
                    DoubleMetaphone.doubleMetaphone( name.Trim(), ref primary, ref alternate );
                    metaphones.Rows.Add( metaphones.Rows.Count, name.Trim(), primary ?? ( object ) DBNull.Value, alternate ?? ( object ) DBNull.Value );
                }
            }

            return metaphones;
        }

        /// <summary>
        /// Adds the names Rock does not have yet, and returns how many. When the collation treats two new names
        /// as one, the first person's spelling is kept, as Rock keeps the first one saved.
        /// </summary>
        private int AddNewMetaphones( DataTable metaphones )
        {
            _db.Execute( "CREATE TABLE #Metaphone ( [Order] INT PRIMARY KEY, [Name] NVARCHAR(50) COLLATE DATABASE_DEFAULT, [Metaphone1] NVARCHAR(4) COLLATE DATABASE_DEFAULT, [Metaphone2] NVARCHAR(4) COLLATE DATABASE_DEFAULT )" );
            _db.BulkInsert( "#Metaphone", metaphones, keepIdentity: false );
            return _db.Execute( @"
INSERT INTO [Metaphone] ( [Name], [Metaphone1], [Metaphone2] )
SELECT [n].[Name], [n].[Metaphone1], [n].[Metaphone2]
FROM ( SELECT [Name], [Metaphone1], [Metaphone2], ROW_NUMBER() OVER ( PARTITION BY [Name] ORDER BY [Order] ) AS [Rank] FROM #Metaphone ) AS [n]
WHERE [n].[Rank] = 1 AND NOT EXISTS ( SELECT 1 FROM [Metaphone] AS [m] WHERE [m].[Name] = [n].[Name] );
DROP TABLE #Metaphone;" );
        }

        /// <summary>
        /// Estimates a school grade from age when the Cast does not give one (age 5 is kindergarten).
        /// </summary>
        private static int? DerivedGrade( CastPerson person, int? age )
        {
            if ( person.FamilyRole != "child" || age == null || age < 5 || age > 17 )
            {
                return null;
            }

            return Math.Min( 12, age.Value - 5 );
        }

        /// <summary>
        /// Adds one phone row when the number is present.
        /// </summary>
        private void AddPhone( DataTable phones, ref int nextId, int personId, string number, int typeId, bool isMobile )
        {
            if ( string.IsNullOrEmpty( number ) )
            {
                return;
            }

            var formatted = number.Length == 10 ? $"({number.Substring( 0, 3 )}) {number.Substring( 3, 3 )}-{number.Substring( 6 )}" : number;
            phones.Rows.Add( nextId++, false, personId, "1", number, formatted, $"1{number}", typeId, isMobile, false, Guid.NewGuid(), _foreignKey, _now, _now );
        }

        /// <summary>
        /// Sets home coordinates in one statement. Bulk copy cannot write geography values directly,
        /// so the coordinates go to a temp table first.
        /// </summary>
        private void SetGeoPoints( DataTable geo )
        {
            if ( geo.Rows.Count == 0 )
            {
                return;
            }

            _db.Execute( "CREATE TABLE #LocationGeo ( [Id] INT PRIMARY KEY, [Latitude] FLOAT, [Longitude] FLOAT )" );
            _db.BulkInsert( "#LocationGeo", geo, keepIdentity: false );
            _db.Execute( @"
UPDATE [l]
SET [l].[GeoPoint] = geography::Point( [g].[Latitude], [g].[Longitude], 4326 ), [l].[GeocodedDateTime] = GETDATE()
FROM [Location] AS [l]
INNER JOIN #LocationGeo AS [g] ON [g].[Id] = [l].[Id];
DROP TABLE #LocationGeo;" );
        }

        /*
            10/27/26 - CLAUDE

            A new Rock has one admin person (Person Id 1) with the admin login, in Rock Administrators, alone in a
            family named "Admin". People sign in as admin, so the Cast person marked replacesAdmin (Alisha Marble)
            loads into that person instead of a new one: the loader updates Person 1 with everything the Cast gives
            her, moves her into her Cast family, and deletes the empty Admin family. Person 1 keeps Rock's Id, Guid,
            primary alias, system flag, login, roles, Known Relationship group, and empty ForeignKey, so every
            audit column and alias Rock wrote at install stays valid, and nothing that removes sample data by
            ForeignKey can ever remove the site's admin. Her new rows (memberships, notes she writes, connector
            rows) carry the ForeignKey as usual. Queries and invariants that mean "this dataset's people" use
            DatasetPersonFilter, which counts Person 1 in. A database with no Person 1 loads her as a new person.

            Reason: Signing in as admin signs in as a Cast person with real data (decision 177).
        */

        /// <summary>
        /// Rock's admin person: its primary alias, the Known Relationship group it owns, and its families. Null when
        /// the database has no Person 1.
        /// </summary>
        private AdminPerson ReadAdmin( int familyTypeId, int relationshipTypeId, int relationshipOwnerRoleId )
        {
            var row = _db.Query( @"
SELECT [pa].[Id], [pa].[Guid]
FROM [PersonAlias] AS [pa]
WHERE [pa].[PersonId] = @Id AND [pa].[AliasPersonId] = @Id", P( "@Id", AdminPersonId ) ).FirstOrDefault();
            if ( row == null )
            {
                return null;
            }

            var relationshipGroupId = _db.Scalar( @"
SELECT TOP 1 [GroupId]
FROM [GroupMember]
WHERE [PersonId] = @Id AND [GroupTypeId] = @TypeId AND [GroupRoleId] = @OwnerRoleId
ORDER BY [GroupId]", P( "@Id", AdminPersonId ), P( "@TypeId", relationshipTypeId ), P( "@OwnerRoleId", relationshipOwnerRoleId ) );

            var familyIds = _db.Query( "SELECT [GroupId] FROM [GroupMember] WHERE [PersonId] = @Id AND [GroupTypeId] = @TypeId", P( "@Id", AdminPersonId ), P( "@TypeId", familyTypeId ) )
                .Select( r => Convert.ToInt32( r["GroupId"] ) )
                .ToList();

            return new AdminPerson
            {
                AliasId = Convert.ToInt32( row["Id"] ),
                AliasGuid = ( Guid ) row["Guid"],
                KnownRelationshipGroupId = relationshipGroupId == null ? 0 : Convert.ToInt32( relationshipGroupId ),
                FamilyTypeId = familyTypeId,
                FamilyIds = familyIds
            };
        }

        /// <summary>
        /// Writes the admin person's built row over Person 1, keeping the columns Rock owns.
        /// </summary>
        private void UpdateAdmin( Dictionary<string, object> values )
        {
            var columns = values.Keys.Where( c => !AdminKeptColumns.Contains( c ) ).ToList();
            var parameters = columns.Select( c => P( $"@{c}", values[c] ) ).ToList();
            parameters.Add( P( "@Id", AdminPersonId ) );
            _db.Execute( $"UPDATE [Person] SET {string.Join( ", ", columns.Select( c => $"[{c}] = @{c}" ) )} WHERE [Id] = @Id", parameters.ToArray() );
        }

        /// <summary>
        /// Takes the admin person out of the families Rock put it in, and deletes those families once they are empty.
        /// </summary>
        private void RemoveAdminFamilies( AdminPerson admin )
        {
            if ( admin.FamilyIds.Count == 0 )
            {
                return;
            }

            var ids = string.Join( ", ", admin.FamilyIds );
            _db.Execute( $"DELETE FROM [GroupMember] WHERE [PersonId] = @Id AND [GroupId] IN ( {ids} )", P( "@Id", AdminPersonId ) );
            _db.Execute( $"DELETE FROM [Group] WHERE [Id] IN ( {ids} ) AND [GroupTypeId] = @TypeId AND NOT EXISTS ( SELECT 1 FROM [GroupMember] AS [gm] WHERE [gm].[GroupId] = [Group].[Id] )",
                P( "@TypeId", admin.FamilyTypeId ) );
        }

        /// <summary>
        /// The next free Id in a table.
        /// </summary>
        private int NextId( string table )
        {
            return Convert.ToInt32( _db.Scalar( $"SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [{table}]" ) );
        }

        private static DataTable NewTable( params string[] columns )
        {
            return TableFactory.Create( columns );
        }

        /// <summary>
        /// Turns a null into DBNull for a DataTable cell.
        /// </summary>
        private static object Value<T>( T? value ) where T : struct
        {
            return value.HasValue ? value.Value : DBNull.Value;
        }

        /// <summary>
        /// Defined value lookups, cached for the life of the load.
        /// </summary>
        private sealed class Lookups
        {
            private readonly RockDatabase _db;
            private readonly SampleDataSource _source;
            private readonly Dictionary<Guid, int> _ids = new Dictionary<Guid, int>();
            private readonly Dictionary<string, int?> _maritalByName = new Dictionary<string, int?>( StringComparer.OrdinalIgnoreCase );

            public Lookups( RockDatabase db, SampleDataSource source )
            {
                _db = db;
                _source = source;
            }

            public int Id( Guid guid )
            {
                if ( !_ids.TryGetValue( guid, out var id ) )
                {
                    id = _db.RequireIdByGuid( "DefinedValue", guid );
                    _ids[guid] = id;
                }

                return id;
            }

            public int RecordType( Guid guid ) => Id( guid );

            public int RecordStatus( string status ) => Id( status switch
            {
                "inactive" => InactiveStatusGuid,
                "pending" => PendingStatusGuid,
                _ => ActiveStatusGuid
            } );

            public int ConnectionStatus( string name )
            {
                var status = _source.ConnectionStatuses.Statuses.Single( s => string.Equals( s.Name, name, StringComparison.OrdinalIgnoreCase ) );
                return Id( status.Guid );
            }

            /// <summary>
            /// Finds a marital status by name in the same defined type as Married. Unknown names return null.
            /// </summary>
            public int? MaritalStatus( string name )
            {
                if ( string.IsNullOrEmpty( name ) || name == "unknown" )
                {
                    return null;
                }

                if ( !_maritalByName.TryGetValue( name, out var id ) )
                {
                    var found = _db.Scalar( "SELECT TOP 1 [Id] FROM [DefinedValue] WHERE [Value] = @Name AND [DefinedTypeId] = ( SELECT [DefinedTypeId] FROM [DefinedValue] WHERE [Guid] = @Married )",
                        P( "@Name", name ), P( "@Married", MarriedGuid ) );
                    id = found == null ? null : Convert.ToInt32( found );
                    _maritalByName[name] = id;
                }

                return id;
            }
        }
    }

    /// <summary>
    /// A loaded person: the Rock Ids and facts later steps need.
    /// </summary>
    internal sealed class LoadedPerson
    {
        public int PersonId { get; set; }

        public int AliasId { get; set; }

        /// <summary>
        /// The primary alias's Guid, which Rock also keeps on the person (PrimaryAliasGuid).
        /// </summary>
        public Guid AliasGuid { get; set; }

        /// <summary>
        /// The Known Relationship group the person owns. Business contacts are added to it.
        /// </summary>
        public int KnownRelationshipGroupId { get; set; }

        public int FamilyId { get; set; }

        public int CampusId { get; set; }

        /// <summary>
        /// Rock's giving unit: G{family} when the person gives with their family, otherwise P{person}.
        /// </summary>
        public string GivingId { get; set; }

        public DateTime? BirthDate { get; set; }

        public int? GraduationYear { get; set; }

        public bool IsAdult { get; set; }

        /// <summary>
        /// False for businesses, the deceased, and inactive records. Only these people attend.
        /// </summary>
        public bool CanAttend { get; set; }

        /// <summary>
        /// True for the Cast person loaded into Rock's admin person (Person Id 1) instead of a new person.
        /// </summary>
        public bool IsRockAdmin { get; set; }

        public CastPerson Cast { get; set; }
    }

    /// <summary>
    /// What the loader reads about Rock's admin person before it becomes a Cast person.
    /// </summary>
    internal sealed class AdminPerson
    {
        public int AliasId { get; set; }

        public Guid AliasGuid { get; set; }

        /// <summary>
        /// The Known Relationship group the admin owns, or 0 when it owns none.
        /// </summary>
        public int KnownRelationshipGroupId { get; set; }

        public int FamilyTypeId { get; set; }

        /// <summary>
        /// The families Rock put the admin in. A new Rock has one, named "Admin".
        /// </summary>
        public List<int> FamilyIds { get; set; }
    }

    /// <summary>
    /// A loaded household: its family group, campus, and members (including shared children).
    /// </summary>
    internal sealed class LoadedHousehold
    {
        public CastHousehold Cast { get; set; }

        public int FamilyId { get; set; }

        public string CampusKey { get; set; }

        /// <summary>
        /// The Location Id of the family's home address, or null when it has none. Small groups meet here.
        /// </summary>
        public int? HomeLocationId { get; set; }

        public List<LoadedPerson> Members { get; } = new List<LoadedPerson>();
    }
}
