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
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /// <summary>
    /// Sets up small groups in Rock from configuration/small-groups.json: the group type's
    /// capacity rule, purpose, and history; the Host role; the Child Care Provided, Meeting Style,
    /// and Meeting Link attributes; the topics; the inactive reasons; and the parent groups, which
    /// replace Rock's starter sections.
    /// Like <see cref="ConfigurationLoader"/>, every step finds existing rows by Guid first.
    /// </summary>
    internal sealed class SmallGroupConfigurationLoader
    {
        private static readonly Guid GroupEntityTypeGuid = new Guid( "9BBFDA11-0D22-40D5-902F-60ADFBC88987" );
        private static readonly Guid GroupTypePurposeTypeGuid = new Guid( "B23F1E45-BC26-4E82-BEB3-9B191FE5CCC3" );
        private static readonly Guid SmallGroupTopicTypeGuid = new Guid( "D4111631-6B42-1CBD-4019-427D6BC6F475" );
        private static readonly Guid TopicAttributeGuid = new Guid( "04BFAC9B-6D1A-E089-446A-B2C604C76764" );
        private static readonly Guid InactiveReasonTypeGuid = new Guid( "EB5D9839-F770-4E22-8B56-0B09397307D9" );
        private static readonly Guid MeetingLocationTypeGuid = new Guid( "96D540F5-071D-4BBD-9906-28F0A64D39C4" );
        private static readonly Guid BooleanFieldTypeGuid = new Guid( "1EDAFDED-DFE6-4334-B019-6EECBA89E05A" );
        private static readonly Guid SingleSelectFieldTypeGuid = new Guid( "7525C4CB-EE6B-41D4-9B64-A08048D5A5C0" );
        private static readonly Guid UrlLinkFieldTypeGuid = new Guid( "C0D0D7E2-C3B0-4004-ABEA-4BBFAD10D5D2" );
        private const string InactiveReasonGroupTypeFilterKey = "core_InactiveReasonsGroupTypeFilter";

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly string _foreignKey;
        private readonly Dictionary<string, int> _campusIds;

        public SmallGroupConfigurationLoader( RockDatabase db, SampleDataSource source, string foreignKey, Dictionary<string, int> campusIds )
        {
            _db = db;
            _source = source;
            _foreignKey = foreignKey;
            _campusIds = campusIds;
        }

        /// <summary>
        /// Runs every small group setup step and returns the Rock Ids the small group loader needs.
        /// </summary>
        public SmallGroupSetup Load()
        {
            var rules = _source.SmallGroups;
            var setup = new SmallGroupSetup
            {
                GroupTypeId = _db.RequireIdByGuid( "GroupType", rules.GroupType.Guid ),
                MeetingLocationTypeId = _db.RequireIdByGuid( "DefinedValue", MeetingLocationTypeGuid ),
                TopicAttributeId = _db.RequireIdByGuid( "Attribute", TopicAttributeGuid )
            };
            setup.GroupTypeName = ( string ) _db.Scalar( "SELECT [Name] FROM [GroupType] WHERE [Id] = @Id", P( "@Id", setup.GroupTypeId ) );
            setup.MeetingLocationTypeName = ( string ) _db.Scalar( "SELECT [Value] FROM [DefinedValue] WHERE [Id] = @Id", P( "@Id", setup.MeetingLocationTypeId ) );

            LoadGroupType( rules, setup );
            LoadRoles( rules, setup );
            LoadAttributes( rules, setup );
            LoadTopics( rules, setup );
            LoadInactiveReasons( rules, setup );
            LoadParents( rules, setup );
            LoadGroupFinder( rules );
            return setup;
        }

        /// <summary>
        /// Turns on Show Image on Rock's Group Finder page, so each card shows the group's photo. A Rock
        /// without that block is left alone.
        /// </summary>
        private void LoadGroupFinder( SmallGroupRules rules )
        {
            var finder = rules.GroupFinder;
            var blockId = finder == null ? null : _db.Scalar( "SELECT [Id] FROM [Block] WHERE [Guid] = @Guid", P( "@Guid", finder.BlockGuid ) );
            var attributeId = finder == null ? null : _db.Scalar( "SELECT [Id] FROM [Attribute] WHERE [Guid] = @Guid", P( "@Guid", finder.ShowImageAttributeGuid ) );
            if ( blockId == null || attributeId == null )
            {
                return;
            }

            UpsertAttributeValue( attributeId, blockId, "True" );
            _db.Log.Write( "  Group Finder: shows group photos" );
        }

        /// <summary>
        /// Sets an attribute value, adding it when the entity has none yet.
        /// </summary>
        private void UpsertAttributeValue( object attributeId, object entityId, string value )
        {
            _db.Execute( @"
IF NOT EXISTS ( SELECT 1 FROM [AttributeValue] WHERE [AttributeId] = @AttributeId AND [EntityId] = @EntityId )
    INSERT INTO [AttributeValue] ( [IsSystem], [AttributeId], [EntityId], [Value], [IsPersistedValueDirty], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
    VALUES ( 0, @AttributeId, @EntityId, @Value, 1, NEWID(), @ForeignKey, GETDATE(), GETDATE() )
ELSE
    UPDATE [AttributeValue] SET [Value] = @Value, [IsPersistedValueDirty] = 1 WHERE [AttributeId] = @AttributeId AND [EntityId] = @EntityId",
                P( "@AttributeId", attributeId ), P( "@EntityId", entityId ), P( "@Value", value ), P( "@ForeignKey", _foreignKey ) );
        }

        /// <summary>
        /// Sets the capacity rule and purpose, turns on inactive reasons, and confirms group history is on.
        /// </summary>
        private void LoadGroupType( SmallGroupRules rules, SmallGroupSetup setup )
        {
            var purposeTypeId = _db.RequireIdByGuid( "DefinedType", GroupTypePurposeTypeGuid );
            var purposeId = ConfigurationLoader.UpsertDefinedValue( _db, _foreignKey, purposeTypeId, rules.GroupType.Purpose.Guid, rules.GroupType.Purpose.Value, null, 0 );
            var capacityRule = rules.GroupType.CapacityRule switch
            {
                "hard" => 1,
                "soft" => 2,
                _ => 0
            };

            var historyWasOn = Convert.ToBoolean( _db.Scalar( "SELECT [EnableGroupHistory] FROM [GroupType] WHERE [Id] = @Id", P( "@Id", setup.GroupTypeId ) ) );
            _db.Execute( "UPDATE [GroupType] SET [GroupCapacityRule] = @Rule, [GroupTypePurposeValueId] = @Purpose, [EnableInactiveReason] = 1, [EnableGroupHistory] = 1 WHERE [Id] = @Id",
                P( "@Rule", capacityRule ), P( "@Purpose", purposeId ), P( "@Id", setup.GroupTypeId ) );
            _db.Log.Write( $"  {setup.GroupTypeName}: capacity rule {rules.GroupType.CapacityRule}, purpose {rules.GroupType.Purpose.Value}, group history {( historyWasOn ? "already on" : "turned on" )}" );
        }

        /// <summary>
        /// Adds the Host role (copying the Member role's permissions) and reads the role Ids.
        /// </summary>
        private void LoadRoles( SmallGroupRules rules, SmallGroupSetup setup )
        {
            var host = rules.Roles.Host;
            if ( _db.IdByGuid( "GroupTypeRole", host.Guid ) == null )
            {
                var memberColumns = "[IsSystem], [GroupTypeId], [MaxCount], [MinCount], [IsLeader], [CanView], [CanEdit], [ReceiveRequirementsNotifications], [CanManageMembers], [IsExcludedFromPeerNetwork], [IsCheckInAllowed], [ChatRole], [CanTakeAttendance], [IsPublic]";
                _db.Execute( $@"
INSERT INTO [GroupTypeRole] ( [Name], [Description], [Order], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime], {memberColumns} )
SELECT @Name, @Description, ( SELECT MAX( [Order] ) + 1 FROM [GroupTypeRole] WHERE [GroupTypeId] = [m].[GroupTypeId] ), @Guid, @ForeignKey, GETDATE(), GETDATE(), {memberColumns.Replace( "[", "[m].[" )}
FROM [GroupTypeRole] AS [m]
WHERE [m].[Guid] = @MemberGuid",
                    P( "@Name", host.Name ), P( "@Description", host.Description ), P( "@Guid", host.Guid ), P( "@ForeignKey", _foreignKey ), P( "@MemberGuid", rules.Roles.Member.Guid ) );
            }

            foreach ( var (role, definition) in new[] { (SmallGroupRole.Leader, rules.Roles.Leader), (SmallGroupRole.Host, host), (SmallGroupRole.Member, rules.Roles.Member) } )
            {
                var row = _db.Query( "SELECT [Id], [Name], [IsLeader] FROM [GroupTypeRole] WHERE [Guid] = @Guid", P( "@Guid", definition.Guid ) ).Single();
                setup.Roles[role] = new SmallGroupRoleInfo( Convert.ToInt32( row["Id"] ), ( string ) row["Name"], Convert.ToBoolean( row["IsLeader"] ) );
            }

            _db.Log.Write( $"  Roles: {string.Join( ", ", setup.Roles.Values.Select( r => r.Name ) )}" );
        }

        /// <summary>
        /// Adds the Child Care Provided, Meeting Style, and Meeting Link group attributes for the small group type.
        /// </summary>
        private void LoadAttributes( SmallGroupRules rules, SmallGroupSetup setup )
        {
            var attributes = rules.Attributes;
            setup.ChildCareAttributeId = EnsureAttribute( setup, attributes.ChildCareProvided, BooleanFieldTypeGuid, 1, "False",
                new Dictionary<string, string> { ["truetext"] = "Yes", ["falsetext"] = "No" } );
            setup.MeetingStyleAttributeId = EnsureAttribute( setup, attributes.MeetingStyle, SingleSelectFieldTypeGuid, 2, attributes.MeetingStyle.Values[0],
                new Dictionary<string, string> { ["values"] = string.Join( ",", attributes.MeetingStyle.Values ), ["fieldtype"] = "ddl" } );
            setup.MeetingLinkAttributeId = EnsureAttribute( setup, attributes.MeetingLink, UrlLinkFieldTypeGuid, 3, string.Empty, new Dictionary<string, string>() );
            _db.Log.Write( $"  Attributes: {attributes.ChildCareProvided.Name}, {attributes.MeetingStyle.Name}, {attributes.MeetingLink.Name}" );
        }

        /// <summary>
        /// Adds a group attribute qualified to the small group type, with its qualifiers. Returns its Id.
        /// </summary>
        private int EnsureAttribute( SmallGroupSetup setup, SmallGroupAttribute attribute, Guid fieldTypeGuid, int order, string defaultValue, Dictionary<string, string> qualifiers )
        {
            var id = _db.IdByGuid( "Attribute", attribute.Guid );
            if ( id != null )
            {
                return id.Value;
            }

            id = Convert.ToInt32( _db.Scalar( @"
INSERT INTO [Attribute] ( [IsSystem], [FieldTypeId], [EntityTypeId], [EntityTypeQualifierColumn], [EntityTypeQualifierValue], [Key], [Name], [Description], [Order], [IsGridColumn], [DefaultValue], [IsMultiValue], [IsRequired],
    [AllowSearch], [IsIndexEnabled], [IsAnalytic], [IsAnalyticHistory], [IsActive], [EnableHistory], [ShowOnBulk], [IsPublic], [IsDefaultPersistedValueDirty], [IsSuppressHistoryLogging], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
OUTPUT INSERTED.[Id]
VALUES ( 0, ( SELECT [Id] FROM [FieldType] WHERE [Guid] = @FieldType ), ( SELECT [Id] FROM [EntityType] WHERE [Guid] = @EntityType ), 'GroupTypeId', @GroupTypeId, @Key, @Name, @Description, @Order, 0, @Default, 0, 0,
    0, 0, 0, 0, 1, 0, 0, 1, 1, 0, @Guid, @ForeignKey, GETDATE(), GETDATE() )",
                P( "@FieldType", fieldTypeGuid ),
                P( "@EntityType", GroupEntityTypeGuid ),
                P( "@GroupTypeId", setup.GroupTypeId.ToString() ),
                P( "@Key", attribute.Key ),
                P( "@Name", attribute.Name ),
                P( "@Description", attribute.Description ),
                P( "@Order", order ),
                P( "@Default", defaultValue ),
                P( "@Guid", attribute.Guid ),
                P( "@ForeignKey", _foreignKey ) ) );

            foreach ( var qualifier in qualifiers )
            {
                _db.Execute( "INSERT INTO [AttributeQualifier] ( [IsSystem], [AttributeId], [Key], [Value], [Guid], [ForeignKey] ) VALUES ( 0, @AttributeId, @Key, @Value, NEWID(), @ForeignKey )",
                    P( "@AttributeId", id ), P( "@Key", qualifier.Key ), P( "@Value", qualifier.Value ), P( "@ForeignKey", _foreignKey ) );
            }

            return id.Value;
        }

        /// <summary>
        /// Replaces the Small Group Topic values (which the Topic attribute lists) with the configured topics.
        /// </summary>
        private void LoadTopics( SmallGroupRules rules, SmallGroupSetup setup )
        {
            var typeId = _db.RequireIdByGuid( "DefinedType", SmallGroupTopicTypeGuid );
            var keep = string.Join( ",", rules.Topics.Select( t => $"'{t.Guid}'" ) );
            var removed = _db.Execute( $"DELETE FROM [DefinedValue] WHERE [DefinedTypeId] = @TypeId AND [Guid] NOT IN ( {keep} )", P( "@TypeId", typeId ) );

            var order = 0;
            foreach ( var topic in rules.Topics )
            {
                ConfigurationLoader.UpsertDefinedValue( _db, _foreignKey, typeId, topic.Guid, topic.Value, null, order++ );
                setup.TopicGuids[topic.Value] = topic.Guid;
            }

            _db.Log.Write( $"  Topics: {rules.Topics.Count}{( removed > 0 ? $" (replaced {removed} existing)" : string.Empty )}" );
        }

        /// <summary>
        /// Adds the inactive reasons and limits each to the small group type.
        /// </summary>
        private void LoadInactiveReasons( SmallGroupRules rules, SmallGroupSetup setup )
        {
            var typeId = _db.RequireIdByGuid( "DefinedType", InactiveReasonTypeGuid );
            var filterAttribute = _db.Scalar( "SELECT [Id] FROM [Attribute] WHERE [Key] = @Key AND [EntityTypeQualifierColumn] = 'DefinedTypeId' AND [EntityTypeQualifierValue] = @TypeId",
                P( "@Key", InactiveReasonGroupTypeFilterKey ), P( "@TypeId", typeId.ToString() ) );
            var groupTypeGuid = rules.GroupType.Guid.ToString().ToLowerInvariant();

            var order = 0;
            foreach ( var reason in rules.InactiveReasons )
            {
                var id = ConfigurationLoader.UpsertDefinedValue( _db, _foreignKey, typeId, reason.Guid, reason.Value, null, order++ );
                setup.InactiveReasonIds[reason.Value] = id;
                if ( filterAttribute != null )
                {
                    UpsertAttributeValue( filterAttribute, id, groupTypeGuid );
                }
            }

            _db.Log.Write( $"  Inactive reasons: {string.Join( ", ", rules.InactiveReasons.Select( r => r.Value ) )}" );
        }

        /// <summary>
        /// Removes Rock's starter sections, then adds the "Small Groups" parent and one parent
        /// per campus in this dataset beneath it.
        /// </summary>
        private void LoadParents( SmallGroupRules rules, SmallGroupSetup setup )
        {
            var parents = rules.Parents;
            RemoveStarterSections( parents.RemoveStarterSections );
            var sectionTypeId = _db.RequireIdByGuid( "GroupType", rules.GroupType.SectionGuid );
            setup.RootParentId = EnsureParent( parents.Root.Guid, parents.Root.Name, sectionTypeId, null, null );

            foreach ( var campus in _source.Campuses.Campuses.Where( c => _campusIds.ContainsKey( c.Key ) ) )
            {
                var name = parents.CampusNameFormat.Replace( "{campus}", campus.Name );
                setup.CampusParentIds[campus.Key] = EnsureParent( parents.Campuses[campus.Key], name, sectionTypeId, setup.RootParentId, _campusIds[campus.Key] );
            }

            _db.Log.Write( $"  Parent groups: {parents.Root.Name} and {setup.CampusParentIds.Count} campus group(s)" );
        }

        /// <summary>
        /// Deletes the starter sections that are still empty. A section with child groups or
        /// members is left alone, since something in this database is using it.
        /// </summary>
        private void RemoveStarterSections( List<NamedGuid> sections )
        {
            var removed = new List<string>();
            foreach ( var section in sections )
            {
                var deleted = _db.Execute( @"
DELETE FROM [Group]
WHERE [Guid] = @Guid
    AND NOT EXISTS ( SELECT 1 FROM [Group] AS [c] WHERE [c].[ParentGroupId] = [Group].[Id] )
    AND NOT EXISTS ( SELECT 1 FROM [GroupMember] AS [m] WHERE [m].[GroupId] = [Group].[Id] )",
                    P( "@Guid", section.Guid ) );
                if ( deleted > 0 )
                {
                    removed.Add( section.Name );
                }
            }

            if ( removed.Count > 0 )
            {
                _db.Log.Write( $"  Removed starter sections: {string.Join( ", ", removed )}" );
            }
        }

        private int EnsureParent( Guid guid, string name, int groupTypeId, int? parentId, int? campusId )
        {
            return _db.IdByGuid( "Group", guid ) ?? Convert.ToInt32( _db.Scalar( @"
INSERT INTO [Group] ( [IsSystem], [ParentGroupId], [GroupTypeId], [CampusId], [Name], [IsSecurityRole], [IsActive], [IsPublic], [Order], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
OUTPUT INSERTED.[Id]
VALUES ( 0, @ParentId, @GroupTypeId, @CampusId, @Name, 0, 1, 1, 0, @Guid, @ForeignKey, GETDATE(), GETDATE() )",
                P( "@ParentId", parentId ), P( "@GroupTypeId", groupTypeId ), P( "@CampusId", campusId ), P( "@Name", name ), P( "@Guid", guid ), P( "@ForeignKey", _foreignKey ) ) );
        }
    }

    /// <summary>
    /// The Rock Ids and names the small group loader needs, filled by <see cref="SmallGroupConfigurationLoader"/>.
    /// </summary>
    internal sealed class SmallGroupSetup
    {
        public int GroupTypeId { get; set; }

        public string GroupTypeName { get; set; }

        public Dictionary<SmallGroupRole, SmallGroupRoleInfo> Roles { get; } = new Dictionary<SmallGroupRole, SmallGroupRoleInfo>();

        public int TopicAttributeId { get; set; }

        /// <summary>
        /// Topic defined value Guids by topic value.
        /// </summary>
        public Dictionary<string, Guid> TopicGuids { get; } = new Dictionary<string, Guid>();

        public int ChildCareAttributeId { get; set; }

        public int MeetingStyleAttributeId { get; set; }

        public int MeetingLinkAttributeId { get; set; }

        /// <summary>
        /// Inactive reason defined value Ids by reason value.
        /// </summary>
        public Dictionary<string, int> InactiveReasonIds { get; } = new Dictionary<string, int>();

        public int RootParentId { get; set; }

        /// <summary>
        /// Each campus's "{campus} Small Groups" parent group Id, by campus key.
        /// </summary>
        public Dictionary<string, int> CampusParentIds { get; } = new Dictionary<string, int>();

        public int MeetingLocationTypeId { get; set; }

        public string MeetingLocationTypeName { get; set; }
    }

    /// <summary>
    /// A small group role's Rock Id, name, and whether it is a leader role.
    /// </summary>
    internal sealed record SmallGroupRoleInfo( int Id, string Name, bool IsLeader );
}
