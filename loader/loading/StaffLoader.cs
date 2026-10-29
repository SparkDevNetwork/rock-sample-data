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
    /// <summary>
    /// The Staff person attributes (decision 201): the attributes in the Staff category, their security, the
    /// Attribute Values block that shows them on the person profile, and each staff member's values.
    /// </summary>
    internal sealed class StaffLoader
    {
        private static readonly Guid PersonEntityTypeGuid = new Guid( "72657ED8-D16E-492E-AC12-144C5E7567E7" );

        /// <summary>
        /// Rock's field types, by the name staff.json uses.
        /// </summary>
        private static readonly Dictionary<string, Guid> FieldTypes = new Dictionary<string, Guid>
        {
            ["image"] = new Guid( "97F8157D-A8C8-4AB3-96A2-9CB2A9049E6D" ),
            ["text"] = new Guid( "9C204CD0-1233-41C5-818A-C5DA439445AA" ),
            ["single-select"] = new Guid( "7525C4CB-EE6B-41D4-9B64-A08048D5A5C0" ),
            ["campus"] = new Guid( "1B71FEF4-201F-4D53-8C60-2DF21F1985ED" ),
            ["memo"] = new Guid( "C28C7BF3-A552-4D77-9408-DEDCF760CED0" ),
            ["person"] = new Guid( "E4EAB7B2-0B76-429B-AFE4-AD86D7428C70" ),
            ["date"] = new Guid( "6B6AA175-4758-453F-8D83-FCD8044B5F36" )
        };

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly AnchorDates _dates;
        private readonly string _foreignKey;
        private readonly Dictionary<string, int> _attributeIds = new Dictionary<string, int>( StringComparer.Ordinal );

        public StaffLoader( RockDatabase db, SampleDataSource source, AnchorDates dates, string foreignKey )
        {
            _db = db;
            _source = source;
            _dates = dates;
            _foreignKey = foreignKey;
        }

        /// <summary>
        /// True when the package has staff attributes. A package from before them has none, and nothing loads.
        /// </summary>
        public bool HasAttributes => _source.Staff?.Category != null && _source.Staff.Attributes.Count > 0;

        #region Setup

        /// <summary>
        /// Adds the staff attributes to the Staff category, with their security, and the block that shows them.
        /// Runs after the church's categories load. Returns a summary for the log.
        /// </summary>
        public string LoadSetup()
        {
            var staff = _source.Staff;
            var category = _source.Categories?.Categories.FirstOrDefault( c => c.Key == staff.Category )
                ?? throw new InvalidDataException( $"configuration/staff.json names category '{staff.Category}', which configuration/categories.json does not have." );
            var categoryId = _db.RequireIdByGuid( "Category", category.Guid );
            var personEntityTypeId = _db.RequireIdByGuid( "EntityType", PersonEntityTypeGuid );
            var attributeEntityTypeId = Convert.ToInt32( _db.Scalar( "SELECT [Id] FROM [EntityType] WHERE [Name] = 'Rock.Model.Attribute'" ) );
            var now = DateTime.Now;

            foreach ( var attribute in staff.Attributes.OrderBy( a => a.Order ) )
            {
                if ( !FieldTypes.TryGetValue( attribute.FieldType, out var fieldTypeGuid ) )
                {
                    throw new InvalidDataException( $"configuration/staff.json: attribute {attribute.Key} has field type '{attribute.FieldType}', which the loader does not know." );
                }

                // Public attributes are the ones a website staff page shows, so they are available to public Lava and APIs.
                var isPublic = attribute.Viewers == null;
                var id = Convert.ToInt32( _db.Scalar( @"
INSERT INTO [Attribute] ( [IsSystem], [FieldTypeId], [EntityTypeId], [EntityTypeQualifierColumn], [EntityTypeQualifierValue], [Key], [Name], [Description], [Order], [IsGridColumn], [DefaultValue], [IsMultiValue], [IsRequired],
    [AllowSearch], [IsIndexEnabled], [IsAnalytic], [IsAnalyticHistory], [IsActive], [EnableHistory], [ShowOnBulk], [IsPublic], [IsDefaultPersistedValueDirty], [IsSuppressHistoryLogging], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
OUTPUT INSERTED.[Id]
VALUES ( 0, ( SELECT [Id] FROM [FieldType] WHERE [Guid] = @FieldType ), @EntityTypeId, '', '', @Key, @Name, @Description, @Order, 0, '', 0, 0,
    0, 0, 0, 0, 1, 0, 0, @IsPublic, 1, 0, @Guid, @ForeignKey, @Now, @Now )",
                    P( "@FieldType", fieldTypeGuid ), P( "@EntityTypeId", personEntityTypeId ), P( "@Key", attribute.Key ), P( "@Name", attribute.Name ),
                    P( "@Description", attribute.Description ), P( "@Order", attribute.Order ), P( "@IsPublic", isPublic ), P( "@Guid", attribute.Guid ),
                    P( "@ForeignKey", _foreignKey ), P( "@Now", now ) ) );
                _attributeIds[attribute.Key] = id;

                foreach ( var qualifier in attribute.Qualifiers )
                {
                    _db.Execute( "INSERT INTO [AttributeQualifier] ( [IsSystem], [AttributeId], [Key], [Value], [Guid], [ForeignKey] ) VALUES ( 0, @AttributeId, @Key, @Value, NEWID(), @ForeignKey )",
                        P( "@AttributeId", id ), P( "@Key", qualifier.Key ), P( "@Value", qualifier.Value ), P( "@ForeignKey", _foreignKey ) );
                }

                _db.Execute( "INSERT INTO [AttributeCategory] ( [AttributeId], [CategoryId] ) VALUES ( @AttributeId, @CategoryId )", P( "@AttributeId", id ), P( "@CategoryId", categoryId ) );

                if ( attribute.Viewers != null )
                {
                    AddSecurity( attributeEntityTypeId, id, "View", attribute.Viewers, now );
                }

                AddSecurity( attributeEntityTypeId, id, "Edit", attribute.Editors, now );
            }

            AddBlock( staff.Block, category.Guid, now );
            return $"{staff.Attributes.Count} attributes in {category.Name}, and the {staff.Block.Name} block on the Extended Attributes page";
        }

        /// <summary>
        /// Allows each role in order, then denies all users, as Rock's security editor writes them.
        /// </summary>
        private void AddSecurity( int attributeEntityTypeId, int attributeId, string action, List<string> roleKeys, DateTime now )
        {
            var order = 0;
            foreach ( var roleKey in roleKeys )
            {
                if ( !_source.Staff.Roles.TryGetValue( roleKey, out var roleGuid ) )
                {
                    throw new InvalidDataException( $"configuration/staff.json: an attribute's security names role '{roleKey}', which 'roles' does not list." );
                }

                InsertAuth( attributeEntityTypeId, attributeId, order++, action, "A", 0, _db.RequireIdByGuid( "Group", roleGuid ), now );
            }

            // SpecialRole 1 is Rock's "All Users".
            InsertAuth( attributeEntityTypeId, attributeId, order, action, "D", 1, null, now );
        }

        private void InsertAuth( int entityTypeId, int entityId, int order, string action, string allowOrDeny, int specialRole, int? groupId, DateTime now )
        {
            _db.Execute( @"
INSERT INTO [Auth] ( [EntityTypeId], [EntityId], [Order], [Action], [AllowOrDeny], [SpecialRole], [GroupId], [Guid], [CreatedDateTime], [ModifiedDateTime], [ForeignKey] )
VALUES ( @EntityTypeId, @EntityId, @Order, @Action, @AllowOrDeny, @SpecialRole, @GroupId, NEWID(), @Now, @Now, @ForeignKey )",
                P( "@EntityTypeId", entityTypeId ), P( "@EntityId", entityId ), P( "@Order", order ), P( "@Action", action ), P( "@AllowOrDeny", allowOrDeny ),
                P( "@SpecialRole", specialRole ), P( "@GroupId", groupId ), P( "@Now", now ), P( "@ForeignKey", _foreignKey ) );
        }

        /*
            10/28/26 - CLAUDE

            The person profile shows only the attribute categories its Attribute Values blocks name, so a new
            category needs its own block. It goes on Rock's Extended Attributes page after Employment and
            Education, the way an admin adds one in Rock: the Attribute Values block type with its Category
            setting. That block type keeps its Guid whether it is the WebForms block (Rock 20) or the Obsidian
            one (Rock 21), so the same rows work on both.

            Reason: Without the block, no Rock screen shows the staff attributes.
        */

        /// <summary>
        /// Adds the Attribute Values block that shows the category, and sets its Category setting.
        /// </summary>
        private void AddBlock( StaffBlock block, Guid categoryGuid, DateTime now )
        {
            var pageId = _db.RequireIdByGuid( "Page", block.Page );
            var blockTypeId = _db.RequireIdByGuid( "BlockType", block.BlockType );
            var blockId = Convert.ToInt32( _db.Scalar( @"
INSERT INTO [Block] ( [IsSystem], [PageId], [BlockTypeId], [Zone], [Order], [Name], [OutputCacheDuration], [Guid], [CreatedDateTime], [ModifiedDateTime], [ForeignKey] )
OUTPUT INSERTED.[Id]
VALUES ( 0, @PageId, @BlockTypeId, @Zone, @Order, @Name, 0, @Guid, @Now, @Now, @ForeignKey )",
                P( "@PageId", pageId ), P( "@BlockTypeId", blockTypeId ), P( "@Zone", block.Zone ), P( "@Order", block.Order ), P( "@Name", block.Name ),
                P( "@Guid", block.Guid ), P( "@Now", now ), P( "@ForeignKey", _foreignKey ) ) );

            var settingId = _db.Scalar( @"
SELECT TOP 1 [Id] FROM [Attribute]
WHERE [EntityTypeId] = ( SELECT [Id] FROM [EntityType] WHERE [Name] = 'Rock.Model.Block' )
    AND [EntityTypeQualifierColumn] = 'BlockTypeId' AND [EntityTypeQualifierValue] = @BlockTypeId AND [Key] = 'Category'",
                P( "@BlockTypeId", blockTypeId.ToString() ) )
                ?? throw new InvalidDataException( "Rock's Attribute Values block type has no Category setting." );

            _db.Execute( @"
INSERT INTO [AttributeValue] ( [IsSystem], [AttributeId], [EntityId], [Value], [IsPersistedValueDirty], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
VALUES ( 0, @AttributeId, @EntityId, @Value, 1, NEWID(), @ForeignKey, @Now, @Now )",
                P( "@AttributeId", settingId ), P( "@EntityId", blockId ), P( "@Value", categoryGuid.ToString() ), P( "@ForeignKey", _foreignKey ), P( "@Now", now ) );
        }

        #endregion Setup

        #region Values

        /// <summary>
        /// Writes each loaded staff member's values. A staff member whose campus is not in the dataset is not
        /// loaded and gets none. Staff Photo comes from the photos step. Returns how many values.
        /// </summary>
        public int LoadValues( PeopleLoader people, IReadOnlyDictionary<string, Guid> campusGuids )
        {
            var staff = _source.Staff;
            var byKey = staff.Staff.ToDictionary( s => s.Key, StringComparer.Ordinal );
            var values = TableFactory.Create( "IsSystem:bool", "AttributeId:int", "EntityId:int", "Value:string", "ValueAsNumeric:decimal", "ValueAsDateTime:date", "ValueAsBoolean:bool",
                "ValueAsPersonId:int", "IsPersistedValueDirty:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            foreach ( var member in staff.Staff )
            {
                if ( !people.People.TryGetValue( member.Person, out var person ) )
                {
                    continue;
                }

                var since = _dates.Resolve( member.StartDate ) ?? _dates.Anchor;
                void Add( string key, string value, int? personId = null )
                {
                    if ( string.IsNullOrEmpty( value ) || !_attributeIds.TryGetValue( key, out var attributeId ) )
                    {
                        return;
                    }

                    var (numeric, dateTime, boolean) = ValueAs( value );
                    values.Rows.Add( false, attributeId, person.PersonId, value, ( object ) numeric ?? DBNull.Value, ( object ) dateTime ?? DBNull.Value, ( object ) boolean ?? DBNull.Value,
                        ( object ) personId ?? DBNull.Value, true, Guid.NewGuid(), _foreignKey, since, since );
                }

                Add( "StaffTitle", member.Title );
                Add( "StaffDepartment", member.Department );
                Add( "StaffCampus", member.Campus != null && campusGuids.TryGetValue( member.Campus, out var campusGuid ) ? campusGuid.ToString() : null );
                Add( "StaffBio", member.Bio );
                Add( "StaffFavoriteQuote", member.Quote );
                Add( "StaffStartDate", since.ToString( "o" ) );
                if ( member.Supervisor != null && byKey.TryGetValue( member.Supervisor, out var supervisor ) && people.People.TryGetValue( supervisor.Person, out var boss ) )
                {
                    Add( "StaffSupervisor", boss.AliasGuid.ToString(), boss.PersonId );
                }
            }

            _db.BulkInsert( "AttributeValue", values, keepIdentity: false );
            return values.Rows.Count;
        }

        /// <summary>
        /// The ValueAs columns Rock's save hook would set for a value (AttributeValue.UpdateValueAsProperties): a
        /// date, a number for a short value, and a boolean for a value of up to five characters.
        /// </summary>
        private static (decimal? Numeric, DateTime? DateTime, bool? Boolean) ValueAs( string value )
        {
            DateTime? dateTime = DateTime.TryParse( value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed ) ? parsed : null;
            decimal? numeric = value.Length <= 24 && decimal.TryParse( value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var number ) ? number : null;
            bool? boolean = value.Length > 0 && value.Length <= 5 ? new[] { "1", "y", "t", "yes", "true" }.Contains( value.ToLowerInvariant() ) : null;
            return (numeric, dateTime, boolean);
        }

        #endregion Values

        #region Org chart

        /// <summary>
        /// True when the package has the org chart (decision 202).
        /// </summary>
        public bool HasOrgChart => _source.Staff?.OrgChart != null && _source.Staff.OrgChart.Units.Count > 0;

        /// <summary>
        /// Adds the org chart units Rock does not ship, under their parents, in Rock's Organization Unit group type.
        /// The units Rock ships are already there. Returns a summary for the log.
        /// </summary>
        public string LoadOrgChartSetup()
        {
            var chart = _source.Staff.OrgChart;
            var groupTypeId = _db.RequireIdByGuid( "GroupType", chart.GroupType );
            var added = new List<string>();
            foreach ( var unit in chart.Units.Where( u => !u.RockShips ) )
            {
                var parent = chart.Units.FirstOrDefault( u => u.Key == unit.Parent )
                    ?? throw new InvalidDataException( $"configuration/staff.json: org chart unit {unit.Key} has parent '{unit.Parent}', which is not a unit." );
                if ( !parent.RockShips && chart.Units.IndexOf( parent ) > chart.Units.IndexOf( unit ) )
                {
                    throw new InvalidDataException( $"configuration/staff.json: org chart unit {unit.Key} comes before its parent {parent.Key}." );
                }

                _db.Execute( @"
INSERT INTO [Group] ( [IsSystem], [ParentGroupId], [GroupTypeId], [Name], [Description], [IsSecurityRole], [IsActive], [IsPublic], [Order], [SchedulingMustMeetRequirements], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
VALUES ( 0, @ParentId, @GroupTypeId, @Name, @Description, 0, 1, 1, @Order, 0, @Guid, @ForeignKey, GETDATE(), GETDATE() )",
                    P( "@ParentId", _db.RequireIdByGuid( "Group", parent.Guid ) ), P( "@GroupTypeId", groupTypeId ), P( "@Name", unit.Name ), P( "@Description", unit.Description ),
                    P( "@Order", unit.Order ), P( "@Guid", unit.Guid ), P( "@ForeignKey", _foreignKey ) );
                added.Add( unit.Name );
            }

            return $"{chart.Units.Count} units, adding {string.Join( " and ", added )}";
        }

        /// <summary>
        /// Adds each unit's leader in the Leader role and its staff in the Staff role, as active members. A staff
        /// member whose campus is not in the dataset is not loaded and is left out. Returns how many members.
        /// </summary>
        public int LoadOrgChartMembers( PeopleLoader people )
        {
            var chart = _source.Staff.OrgChart;
            var staff = _source.Staff.Staff.ToDictionary( s => s.Key, StringComparer.Ordinal );
            var groupTypeId = _db.RequireIdByGuid( "GroupType", chart.GroupType );
            var leaderRoleId = _db.RequireIdByGuid( "GroupTypeRole", chart.LeaderRole );
            var staffRoleId = _db.RequireIdByGuid( "GroupTypeRole", chart.StaffRole );
            var now = DateTime.Now;
            var count = 0;

            foreach ( var unit in chart.Units )
            {
                var groupId = _db.RequireIdByGuid( "Group", unit.Guid );
                var members = new[] { (Key: unit.Leader, RoleId: leaderRoleId) }.Concat( unit.Staff.Select( k => (Key: k, RoleId: staffRoleId) ) );
                foreach ( var (key, roleId) in members.Where( m => m.Key != null ) )
                {
                    if ( !staff.TryGetValue( key, out var member ) )
                    {
                        throw new InvalidDataException( $"configuration/staff.json: org chart unit {unit.Key} names '{key}', who is not staff." );
                    }

                    if ( !people.People.TryGetValue( member.Person, out var person ) )
                    {
                        continue;
                    }

                    var since = _dates.Resolve( member.StartDate ) ?? now;
                    _db.Execute( @"
INSERT INTO [GroupMember] ( [IsSystem], [GroupId], [GroupTypeId], [PersonId], [GroupRoleId], [GroupMemberStatus], [DateTimeAdded], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
VALUES ( 0, @GroupId, @GroupTypeId, @PersonId, @GroupRoleId, 1, @Since, NEWID(), @ForeignKey, @Since, @Since )",
                        P( "@GroupId", groupId ), P( "@GroupTypeId", groupTypeId ), P( "@PersonId", person.PersonId ), P( "@GroupRoleId", roleId ),
                        P( "@Since", since ), P( "@ForeignKey", _foreignKey ) );
                    count++;
                }
            }

            return count;
        }

        #endregion Org chart
    }
}
