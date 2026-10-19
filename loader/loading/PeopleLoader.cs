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
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /*
        10/15/26 - CLAUDE

        People are bulk-copied with Ids assigned here (current maximum Id + 1, and up),
        so every foreign key is known before anything is written and no lookups are needed
        during the copy. Bulk copy skips Rock's save hooks, so this loader also fills in
        what those hooks normally set: birth date and key, age and age bracket, giving Id
        and giving leader, primary family and campus, and the phone's formatted forms.

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
            var lookups = new Lookups( _db, _source );

            var nextGroupId = NextId( "Group" );
            var nextPersonId = NextId( "Person" );
            var nextAliasId = NextId( "PersonAlias" );
            var nextMemberId = NextId( "GroupMember" );
            var nextLocationId = NextId( "Location" );
            var nextGroupLocationId = NextId( "GroupLocation" );
            var nextPhoneId = NextId( "PhoneNumber" );

            var groups = NewTable( "Id:int", "IsSystem:bool", "GroupTypeId:int", "CampusId:int", "Name:string", "IsSecurityRole:bool", "IsActive:bool", "Order:int", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var persons = NewTable( "Id:int", "IsSystem:bool", "RecordTypeValueId:int", "RecordStatusValueId:int", "RecordStatusReasonValueId:int", "ConnectionStatusValueId:int", "IsDeceased:bool",
                "FirstName:string", "NickName:string", "MiddleName:string", "LastName:string", "BirthDay:int", "BirthMonth:int", "BirthYear:int", "BirthDate:date", "BirthDateKey:int", "Age:int", "AgeBracket:int",
                "Gender:int", "MaritalStatusValueId:int", "AnniversaryDate:date", "GraduationYear:int", "GivingGroupId:int", "GivingId:string", "GivingLeaderId:int", "Email:string", "IsEmailActive:bool",
                "EmailPreference:int", "CommunicationPreference:int", "AgeClassification:int", "PrimaryFamilyId:int", "PrimaryCampusId:int", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var aliases = NewTable( "Id:int", "PersonId:int", "AliasPersonId:int", "AliasPersonGuid:guid", "Guid:guid", "ForeignKey:string" );
            var members = NewTable( "Id:int", "IsSystem:bool", "GroupId:int", "GroupTypeId:int", "PersonId:int", "GroupRoleId:int", "GroupMemberStatus:int", "DateTimeAdded:date", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var locations = NewTable( "Id:int", "Street1:string", "Street2:string", "City:string", "State:string", "PostalCode:string", "Country:string", "IsActive:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var geo = NewTable( "Id:int", "Latitude:double", "Longitude:double" );
            var groupLocations = NewTable( "Id:int", "GroupId:int", "LocationId:int", "GroupLocationTypeValueId:int", "IsMailingLocation:bool", "IsMappedLocation:bool", "Order:int", "IsOverflowLocation:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var phones = NewTable( "Id:int", "IsSystem:bool", "PersonId:int", "CountryCode:string", "Number:string", "NumberFormatted:string", "FullNumber:string", "NumberTypeValueId:int", "IsMessagingEnabled:bool", "IsUnlisted:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            var homeLocationIds = new Dictionary<string, int>();

            // Pass 1: assign every family and person an Id, so shared members can point across households.
            foreach ( var household in households )
            {
                FamilyIds[household.Key] = nextGroupId++;
                var campusId = _campusIds[CampusKeyFor( household )];
                foreach ( var person in household.Members )
                {
                    People[person.Guid] = new LoadedPerson { PersonId = nextPersonId++, AliasId = nextAliasId++, FamilyId = FamilyIds[household.Key], CampusId = campusId, Cast = person };
                }
            }

            // Pass 2: build the rows.
            foreach ( var household in households )
            {
                var familyId = FamilyIds[household.Key];
                var familyCampusId = _campusIds[CampusKeyFor( household )];
                groups.Rows.Add( familyId, false, familyTypeId, familyCampusId, household.Name, false, true, 0, household.Guid, _foreignKey, _now, _now );

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

                    loaded.BirthDate = birthDate;
                    loaded.GraduationYear = grade.HasValue && !isAdult ? _dates.GraduationYear( grade.Value ) : null;
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
                        grade.HasValue && !isAdult ? _dates.GraduationYear( grade.Value ) : ( object ) Value( _dates.Resolve( person.Graduation )?.Year ),
                        Value( givingGroupId ),
                        loaded.GivingId,
                        givingLeaderId,
                        person.Email ?? ( object ) DBNull.Value,
                        person.EmailIsActive,
                        person.EmailDoNotEmail ? 2 : 0,
                        1,
                        isAdult ? 1 : 2,
                        familyId, familyCampusId,
                        person.Guid, _foreignKey, _now, _now );

                    aliases.Rows.Add( loaded.AliasId, loaded.PersonId, loaded.PersonId, person.Guid, Guid.NewGuid(), _foreignKey );
                    members.Rows.Add( nextMemberId++, false, familyId, familyTypeId, loaded.PersonId, isAdult ? adultRoleId : childRoleId, 1, _now, Guid.NewGuid(), _foreignKey, _now, _now );

                    AddPhone( phones, ref nextPhoneId, loaded.PersonId, person.Phones?.Home, lookups.Id( HomePhoneGuid ), false );
                    AddPhone( phones, ref nextPhoneId, loaded.PersonId, person.Phones?.Mobile, lookups.Id( MobilePhoneGuid ), true );
                    AddPhone( phones, ref nextPhoneId, loaded.PersonId, person.Phones?.Work, lookups.Id( WorkPhoneGuid ), false );
                }

                // People who also belong to this family (for example a child of divorced parents).
                foreach ( var shared in household.SharedMembers )
                {
                    var loaded = People[shared.Guid];
                    var role = loaded.Cast.FamilyRole == "child" ? childRoleId : adultRoleId;
                    members.Rows.Add( nextMemberId++, false, familyId, familyTypeId, loaded.PersonId, role, 1, _now, Guid.NewGuid(), _foreignKey, _now, _now );
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

            // The household view later steps use. Shared children appear in every household they belong to.
            foreach ( var household in households )
            {
                var loadedHousehold = new LoadedHousehold { Cast = household, FamilyId = FamilyIds[household.Key], CampusKey = CampusKeyFor( household ), HomeLocationId = homeLocationIds.TryGetValue( household.Key, out var home ) ? home : null };
                loadedHousehold.Members.AddRange( household.Members.Select( m => People[m.Guid] ) );
                loadedHousehold.Members.AddRange( household.SharedMembers.Select( s => People[s.Guid] ) );
                Households.Add( loadedHousehold );
            }

            _db.BulkInsert( "Group", groups, keepIdentity: true );
            _db.BulkInsert( "Person", persons, keepIdentity: true );
            _db.BulkInsert( "PersonAlias", aliases, keepIdentity: true );
            _db.BulkInsert( "GroupMember", members, keepIdentity: true );
            _db.BulkInsert( "Location", locations, keepIdentity: true );
            _db.BulkInsert( "GroupLocation", groupLocations, keepIdentity: true );
            _db.BulkInsert( "PhoneNumber", phones, keepIdentity: true );
            SetGeoPoints( geo );

            Console.WriteLine( $"  Families: {groups.Rows.Count}, people: {persons.Rows.Count}, family members: {members.Rows.Count}, addresses: {locations.Rows.Count}, phones: {phones.Rows.Count}" );
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

        public CastPerson Cast { get; set; }
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
