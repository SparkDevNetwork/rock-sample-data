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
    /// Sets up requests to serve from configuration/connections.json: Rock's Involvement connection
    /// type, its request statuses and activity types, and one opportunity per serving team, offered at
    /// every campus and placing people on that campus's team.
    /// Like <see cref="ConfigurationLoader"/>, every step finds existing rows by Guid first.
    /// </summary>
    internal sealed class ConnectionConfigurationLoader
    {
        private static readonly Guid GlobalConnectorGroupGuid = new Guid( "7A2AFC10-BCFA-4CCF-9B6C-C2367D6BE7BE" );

        /// <summary>
        /// Rock's DueDateCalculationMode values (Rock.Enums.Connection), by name.
        /// </summary>
        private static readonly Dictionary<string, int> DueDateCalculationModes = new Dictionary<string, int>
        {
            ["FixedDaysFromStartTypeLevel"] = 0,
            ["FixedDaysFromStartOpportunityLevel"] = 1,
            ["DurationPerStatus"] = 2
        };

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly string _foreignKey;
        private readonly Dictionary<string, int> _campusIds;
        private readonly ServingSetup _serving;

        public ConnectionConfigurationLoader( RockDatabase db, SampleDataSource source, string foreignKey, Dictionary<string, int> campusIds, ServingSetup serving )
        {
            _db = db;
            _source = source;
            _foreignKey = foreignKey;
            _campusIds = campusIds;
            _serving = serving;
        }

        /// <summary>
        /// Runs every connection setup step and returns the Rock Ids later steps need.
        /// </summary>
        public ConnectionSetup Load()
        {
            var config = _source.Connections;
            var setup = new ConnectionSetup { ConnectionTypeId = _db.RequireIdByGuid( "ConnectionType", config.ConnectionType.Guid ) };

            LoadConnectionType( config.ConnectionType, setup.ConnectionTypeId );
            LoadRequestStatuses( config.RequestStatuses, setup );
            LoadActivityTypes( config.ActivityTypes, setup );
            LoadOpportunities( config, setup );
            LoadOpportunityAttributes( config, setup );
            return setup;
        }

        /// <summary>
        /// Updates the option lists of Rock's Involvement opportunity attributes and sets each opportunity's
        /// values, so the Serve page's search filters find every opportunity.
        /// </summary>
        private void LoadOpportunityAttributes( ConnectionsConfiguration config, ConnectionSetup setup )
        {
            var attributeIds = new Dictionary<string, int>();
            foreach ( var key in config.AttributeOptions.Keys.Concat( config.Opportunities.SelectMany( o => o.Attributes.Keys ) ).Distinct() )
            {
                var id = _db.Scalar( @"
SELECT TOP 1 [a].[Id]
FROM [Attribute] AS [a]
INNER JOIN [EntityType] AS [e] ON [e].[Id] = [a].[EntityTypeId]
WHERE [e].[Name] = 'Rock.Model.ConnectionOpportunity' AND [a].[Key] = @Key
    AND [a].[EntityTypeQualifierColumn] = 'ConnectionTypeId' AND [a].[EntityTypeQualifierValue] = @TypeId",
                    P( "@Key", key ), P( "@TypeId", setup.ConnectionTypeId.ToString() ) );
                attributeIds[key] = id == null ? throw new InvalidOperationException( $"Rock has no '{key}' opportunity attribute on the Involvement connection type." ) : Convert.ToInt32( id );
            }

            foreach ( var option in config.AttributeOptions )
            {
                _db.Execute( "UPDATE [AttributeQualifier] SET [Value] = @Value WHERE [AttributeId] = @AttributeId AND [Key] = 'values'",
                    P( "@Value", option.Value ), P( "@AttributeId", attributeIds[option.Key] ) );
            }

            foreach ( var opportunity in config.Opportunities )
            {
                foreach ( var value in opportunity.Attributes )
                {
                    _db.Execute( @"
IF NOT EXISTS ( SELECT 1 FROM [AttributeValue] WHERE [AttributeId] = @AttributeId AND [EntityId] = @EntityId )
    INSERT INTO [AttributeValue] ( [IsSystem], [AttributeId], [EntityId], [Value], [IsPersistedValueDirty], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
    VALUES ( 0, @AttributeId, @EntityId, @Value, 1, NEWID(), @ForeignKey, GETDATE(), GETDATE() )
ELSE
    UPDATE [AttributeValue] SET [Value] = @Value, [IsPersistedValueDirty] = 1 WHERE [AttributeId] = @AttributeId AND [EntityId] = @EntityId",
                        P( "@AttributeId", attributeIds[value.Key] ), P( "@EntityId", setup.OpportunityIds[opportunity.Team] ), P( "@Value", value.Value ), P( "@ForeignKey", _foreignKey ) );
                }
            }

            _db.Log.Write( $"  Opportunity attributes: {string.Join( ", ", attributeIds.Keys )}" );
        }

        private void LoadConnectionType( ConnectionTypeDefinition type, int id )
        {
            _db.Execute( "UPDATE [ConnectionType] SET [Name] = @Name, [Description] = @Description, [IconCssClass] = @Icon, [RequiresPlacementGroupToConnect] = @RequiresPlacement, [IsActive] = 1 WHERE [Id] = @Id",
                P( "@Name", type.Name ), P( "@Description", type.Description ), P( "@Icon", type.IconCssClass ), P( "@RequiresPlacement", type.RequiresPlacementGroupToConnect ), P( "@Id", id ) );

            if ( type.DueDateCalculationMode != null )
            {
                _db.Execute( "UPDATE [ConnectionType] SET [DueDateCalculationMode] = @Mode WHERE [Id] = @Id", P( "@Mode", DueDateCalculationModes[type.DueDateCalculationMode] ), P( "@Id", id ) );
            }

            _db.Log.Write( $"  Connection type: {type.Name}" );
        }

        /// <summary>
        /// Updates Rock's two request statuses and adds the onboarding ones, in board order.
        /// </summary>
        private void LoadRequestStatuses( List<ConnectionRequestStatusDefinition> statuses, ConnectionSetup setup )
        {
            var order = 0;
            foreach ( var status in statuses )
            {
                var parameters = new[]
                {
                    P( "@Name", status.Name ), P( "@Description", status.Description ), P( "@TypeId", setup.ConnectionTypeId ), P( "@IsDefault", status.IsDefault ),
                    P( "@IsCritical", status.IsCritical ), P( "@Color", status.HighlightColor ), P( "@Order", order++ ), P( "@Guid", status.Guid ), P( "@ForeignKey", _foreignKey ),
                    P( "@DueInDays", ( object ) status.DueInDays ?? DBNull.Value ), P( "@DueSoonInDays", ( object ) status.DueSoonInDays ?? DBNull.Value )
                };
                var id = _db.IdByGuid( "ConnectionStatus", status.Guid );
                if ( id != null )
                {
                    _db.Execute( @"
UPDATE [ConnectionStatus]
SET [Name] = @Name, [Description] = @Description, [IsDefault] = @IsDefault, [IsCritical] = @IsCritical, [HighlightColor] = @Color, [Order] = @Order, [IsActive] = 1,
    [RequestStatusDueDateOffsetInDays] = ISNULL( @DueInDays, [RequestStatusDueDateOffsetInDays] ), [RequestStatusDueSoonOffsetInDays] = ISNULL( @DueSoonInDays, [RequestStatusDueSoonOffsetInDays] )
WHERE [Guid] = @Guid", parameters );
                }
                else
                {
                    id = Convert.ToInt32( _db.Scalar( @"
INSERT INTO [ConnectionStatus] ( [Name], [Description], [ConnectionTypeId], [IsCritical], [IsDefault], [IsActive], [AutoInactivateState], [Order], [HighlightColor],
    [RequestStatusDueDateOffsetInDays], [RequestStatusDueSoonOffsetInDays], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
OUTPUT INSERTED.[Id]
VALUES ( @Name, @Description, @TypeId, @IsCritical, @IsDefault, 1, 0, @Order, @Color, @DueInDays, @DueSoonInDays, @Guid, @ForeignKey, GETDATE(), GETDATE() )", parameters ) );
                }

                setup.RequestStatusIds[status.Name] = id.Value;
            }

            _db.Log.Write( $"  Request statuses: {string.Join( ", ", statuses.Select( s => s.Name ) )}" );
        }

        private void LoadActivityTypes( List<NamedGuid> activityTypes, ConnectionSetup setup )
        {
            foreach ( var activityType in activityTypes )
            {
                setup.ActivityTypeIds[activityType.Name] = _db.IdByGuid( "ConnectionActivityType", activityType.Guid ) ?? Convert.ToInt32( _db.Scalar( @"
INSERT INTO [ConnectionActivityType] ( [Name], [ConnectionTypeId], [IsActive], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
OUTPUT INSERTED.[Id]
VALUES ( @Name, @TypeId, 1, @Guid, @ForeignKey, GETDATE(), GETDATE() )",
                    P( "@Name", activityType.Name ), P( "@TypeId", setup.ConnectionTypeId ), P( "@Guid", activityType.Guid ), P( "@ForeignKey", _foreignKey ) ) );
            }

            _db.Log.Write( $"  Activity types added: {string.Join( ", ", activityTypes.Select( a => a.Name ) )}" );
        }

        /// <summary>
        /// Adds or updates each opportunity, offers it at every campus, links it to each campus's team, and
        /// sets the role and status a connected request places people with.
        /// </summary>
        private void LoadOpportunities( ConnectionsConfiguration config, ConnectionSetup setup )
        {
            var teams = _source.Serving.Teams.ToDictionary( t => t.Key );
            var placementRoleId = config.Placement.Role == "leader" ? _serving.LeaderRoleId : _serving.MemberRoleId;
            var placementStatus = config.Placement.Status switch
            {
                "Inactive" => 0,
                "Active" => 1,
                "Pending" => 2,
                _ => throw new InvalidOperationException( $"connections.json placement status '{config.Placement.Status}' is not Active, Inactive, or Pending." )
            };
            var globalConnectorGroupId = _db.IdByGuid( "Group", GlobalConnectorGroupGuid );

            var order = 0;
            foreach ( var opportunity in config.Opportunities )
            {
                var team = teams.TryGetValue( opportunity.Team, out var match ) ? match : throw new InvalidOperationException( $"connections.json opportunity '{opportunity.PublicName}' names team '{opportunity.Team}', which serving.json does not have." );
                var id = UpsertOpportunity( opportunity, team.Name, setup.ConnectionTypeId, order++ );
                setup.OpportunityIds[opportunity.Team] = id;

                foreach ( var campus in _campusIds )
                {
                    InsertIfMissing( "ConnectionOpportunityCampus", "[ConnectionOpportunityId] = @OpportunityId AND [CampusId] = @OtherId",
                        "[ConnectionOpportunityId], [CampusId]", id, campus.Value );
                    InsertIfMissing( "ConnectionOpportunityGroup", "[ConnectionOpportunityId] = @OpportunityId AND [GroupId] = @OtherId",
                        "[ConnectionOpportunityId], [GroupId]", id, _serving.TeamGroupIds[(campus.Key, team.Key)] );
                }

                // Rock connects its shipped opportunities through the Global Connector Group; the new ones match.
                if ( globalConnectorGroupId != null )
                {
                    InsertIfMissing( "ConnectionOpportunityConnectorGroup", "[ConnectionOpportunityId] = @OpportunityId AND [CampusId] IS NULL AND [ConnectorGroupId] = @OtherId",
                        "[ConnectionOpportunityId], [ConnectorGroupId]", id, globalConnectorGroupId.Value );
                }

                _db.Execute( @"
IF EXISTS ( SELECT 1 FROM [ConnectionOpportunityGroupConfig] WHERE [ConnectionOpportunityId] = @OpportunityId AND [GroupTypeId] = @GroupTypeId )
    UPDATE [ConnectionOpportunityGroupConfig] SET [GroupMemberRoleId] = @RoleId, [GroupMemberStatus] = @Status, [UseAllGroupsOfType] = 0
    WHERE [ConnectionOpportunityId] = @OpportunityId AND [GroupTypeId] = @GroupTypeId
ELSE
    INSERT INTO [ConnectionOpportunityGroupConfig] ( [ConnectionOpportunityId], [GroupTypeId], [GroupMemberRoleId], [GroupMemberStatus], [UseAllGroupsOfType], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
    VALUES ( @OpportunityId, @GroupTypeId, @RoleId, @Status, 0, NEWID(), @ForeignKey, GETDATE(), GETDATE() )",
                    P( "@OpportunityId", id ), P( "@GroupTypeId", _serving.GroupTypeId ), P( "@RoleId", placementRoleId ), P( "@Status", placementStatus ), P( "@ForeignKey", _foreignKey ) );
            }

            _db.Log.Write( $"  Opportunities: {string.Join( ", ", config.Opportunities.Select( o => o.PublicName ) )}, each at {_campusIds.Count} campus(es)" );
        }

        private int UpsertOpportunity( ConnectionOpportunityDefinition opportunity, string name, int connectionTypeId, int order )
        {
            var parameters = new[]
            {
                P( "@Name", name ), P( "@PublicName", opportunity.PublicName ), P( "@Summary", opportunity.Summary ), P( "@Description", opportunity.Description ),
                P( "@Icon", opportunity.IconCssClass ), P( "@TypeId", connectionTypeId ), P( "@Order", order ), P( "@Guid", opportunity.Guid ), P( "@ForeignKey", _foreignKey )
            };
            var id = _db.IdByGuid( "ConnectionOpportunity", opportunity.Guid );
            if ( id != null )
            {
                _db.Execute( @"
UPDATE [ConnectionOpportunity]
SET [Name] = @Name, [PublicName] = @PublicName, [Summary] = @Summary, [Description] = @Description, [IconCssClass] = @Icon, [ConnectionTypeId] = @TypeId, [Order] = @Order, [IsActive] = 1
WHERE [Guid] = @Guid", parameters );
                return id.Value;
            }

            return Convert.ToInt32( _db.Scalar( @"
INSERT INTO [ConnectionOpportunity] ( [Name], [PublicName], [Summary], [Description], [IconCssClass], [ConnectionTypeId], [IsActive], [Order], [ShowStatusOnTransfer], [ShowConnectButton], [ShowCampusOnTransfer], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
OUTPUT INSERTED.[Id]
VALUES ( @Name, @PublicName, @Summary, @Description, @Icon, @TypeId, 1, @Order, 1, 1, 0, @Guid, @ForeignKey, GETDATE(), GETDATE() )", parameters ) );
        }

        /// <summary>
        /// Adds a link row between an opportunity and one other record when it is not there yet.
        /// </summary>
        private void InsertIfMissing( string table, string match, string columns, int opportunityId, int otherId )
        {
            _db.Execute( $@"
IF NOT EXISTS ( SELECT 1 FROM [{table}] WHERE {match} )
    INSERT INTO [{table}] ( {columns}, [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
    VALUES ( @OpportunityId, @OtherId, NEWID(), @ForeignKey, GETDATE(), GETDATE() )",
                P( "@OpportunityId", opportunityId ), P( "@OtherId", otherId ), P( "@ForeignKey", _foreignKey ) );
        }
    }

    /// <summary>
    /// The Rock Ids later steps need about connections, filled by <see cref="ConnectionConfigurationLoader"/>.
    /// </summary>
    internal sealed class ConnectionSetup
    {
        public int ConnectionTypeId { get; set; }

        /// <summary>
        /// Request status Ids by status name.
        /// </summary>
        public Dictionary<string, int> RequestStatusIds { get; } = new Dictionary<string, int>();

        /// <summary>
        /// The added activity type Ids by name.
        /// </summary>
        public Dictionary<string, int> ActivityTypeIds { get; } = new Dictionary<string, int>();

        /// <summary>
        /// Opportunity Ids by team key.
        /// </summary>
        public Dictionary<string, int> OpportunityIds { get; } = new Dictionary<string, int>();
    }
}
