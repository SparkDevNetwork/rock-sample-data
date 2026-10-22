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

using System.Text.Json;
using System.Text.Json.Nodes;

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /*
        10/21/26 - CLAUDE

        The campaign's dates count back from install day, so every install is the same distance into the
        campaign. Pledges go to the campaign's parent fund. Rock's Pledge Analytics and contribution
        statements count gifts to its campus accounts toward those pledges, so the statement template
        includes child accounts.

        Rock ships the WebForms Pledge Entry block, and a Rock job later swaps it for the Obsidian block with
        a new block Guid. The loader finds the block on each pledge page by either block type, so it works
        before and after the swap. Both block types use the same setting keys.

        Reason: Pledge screens work on install (decisions 97, 98, and 100).
    */

    /// <summary>
    /// Sets the building campaign's dates and description on its fund, and points Rock's two pledge pages and
    /// default contribution statement at it.
    /// </summary>
    internal sealed class CampaignLoader
    {
        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly AnchorDates _dates;
        private readonly string _foreignKey;

        public CampaignLoader( RockDatabase db, SampleDataSource source, AnchorDates dates, string foreignKey )
        {
            _db = db;
            _source = source;
            _dates = dates;
            _foreignKey = foreignKey;
        }

        /// <summary>
        /// Sets up the campaign. Run after the configuration step has added the campaign's fund.
        /// </summary>
        public void Load()
        {
            var campaign = _source.Campaign;
            if ( campaign == null )
            {
                _db.Log.Write( "  Campaign skipped (this package has no configuration/campaign.json)" );
                return;
            }

            var fund = _source.Accounts.Accounts.FirstOrDefault( a => a.Name == campaign.Fund )
                ?? throw new InvalidOperationException( $"configuration/campaign.json names the fund '{campaign.Fund}', which is not in configuration/accounts.json." );
            var fundId = _db.RequireIdByGuid( "FinancialAccount", fund.Guid );

            var installSunday = _dates.Anchor.AddDays( -( int ) _dates.Anchor.DayOfWeek );
            var start = installSunday.AddDays( -7 * campaign.CommitmentSundayWeeksBeforeInstall );
            var end = start.AddYears( campaign.LengthYears ).AddDays( -1 );

            _db.Execute( "UPDATE [FinancialAccount] SET [StartDate] = @Start, [EndDate] = @End WHERE [Id] = @Id OR [ParentAccountId] = @Id",
                P( "@Start", start ), P( "@End", end ), P( "@Id", fundId ) );
            _db.Execute( "UPDATE [FinancialAccount] SET [Description] = @Description, [PublicDescription] = @Description WHERE [Id] = @Id",
                P( "@Description", campaign.Description ), P( "@Id", fundId ) );

            LoadPledgeEntry( campaign.PledgeEntry, fund.Guid, start, end );
            LoadStatementTemplate( campaign.StatementTemplateGuid, fundId );

            _db.Log.Write( $"  Campaign: {campaign.Name}, {start:yyyy-MM-dd} to {end:yyyy-MM-dd}" );
        }

        /// <summary>
        /// Points the Pledge Entry block on each pledge page at the campaign's fund and dates.
        /// </summary>
        private void LoadPledgeEntry( CampaignPledgeEntry pledgeEntry, Guid fundGuid, DateTime start, DateTime end )
        {
            var values = new Dictionary<string, string>
            {
                ["Account"] = fundGuid.ToString(),
                ["PledgeDateRange"] = $"{start:s},{end:s}",
                ["ShowPledgeFrequency"] = pledgeEntry.ShowPledgeFrequency ? "True" : "False"
            };

            var typeNames = string.Join( ", ", pledgeEntry.BlockTypeGuids.Select( ( _, i ) => $"@Type{i}" ) );
            foreach ( var pageGuid in pledgeEntry.PageGuids )
            {
                var blocks = _db.Query( $@"
SELECT [b].[Id], [b].[BlockTypeId]
FROM [Block] AS [b]
INNER JOIN [Page] AS [p] ON [p].[Id] = [b].[PageId]
INNER JOIN [BlockType] AS [bt] ON [bt].[Id] = [b].[BlockTypeId]
WHERE [p].[Guid] = @PageGuid AND [bt].[Guid] IN ( {typeNames} )",
                    pledgeEntry.BlockTypeGuids.Select( ( guid, i ) => P( $"@Type{i}", guid ) ).Append( P( "@PageGuid", pageGuid ) ).ToArray() );
                if ( blocks.Count == 0 )
                {
                    throw new InvalidOperationException( $"Rock's page {pageGuid} has no Pledge Entry block of type {string.Join( " or ", pledgeEntry.BlockTypeGuids )}." );
                }

                foreach ( var block in blocks )
                {
                    SetBlockValues( block, values );
                }
            }
        }

        /// <summary>
        /// Saves setting values on one block, by the setting keys its block type has.
        /// </summary>
        private void SetBlockValues( Dictionary<string, object> block, Dictionary<string, string> values )
        {
            foreach ( var value in values )
            {
                var attributeId = _db.Scalar( @"
SELECT TOP 1 [a].[Id]
FROM [Attribute] AS [a]
INNER JOIN [EntityType] AS [e] ON [e].[Id] = [a].[EntityTypeId]
WHERE [e].[Name] = 'Rock.Model.Block' AND [a].[Key] = @Key
    AND [a].[EntityTypeQualifierColumn] = 'BlockTypeId' AND [a].[EntityTypeQualifierValue] = @BlockTypeId",
                    P( "@Key", value.Key ), P( "@BlockTypeId", block["BlockTypeId"].ToString() ) )
                    ?? throw new InvalidOperationException( $"Rock's Pledge Entry block has no '{value.Key}' setting." );

                _db.Execute( @"
IF NOT EXISTS ( SELECT 1 FROM [AttributeValue] WHERE [AttributeId] = @AttributeId AND [EntityId] = @EntityId )
    INSERT INTO [AttributeValue] ( [IsSystem], [AttributeId], [EntityId], [Value], [IsPersistedValueDirty], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
    VALUES ( 0, @AttributeId, @EntityId, @Value, 1, NEWID(), @ForeignKey, GETDATE(), GETDATE() )
ELSE
    UPDATE [AttributeValue] SET [Value] = @Value, [IsPersistedValueDirty] = 1 WHERE [AttributeId] = @AttributeId AND [EntityId] = @EntityId",
                    P( "@AttributeId", attributeId ), P( "@EntityId", block["Id"] ), P( "@Value", value.Value ), P( "@ForeignKey", _foreignKey ) );
            }
        }

        /// <summary>
        /// Shows the campaign's pledges on the contribution statement, with gifts to its campus accounts counted.
        /// </summary>
        private void LoadStatementTemplate( Guid templateGuid, int fundId )
        {
            var json = _db.Scalar( "SELECT [ReportSettingsJson] FROM [FinancialStatementTemplate] WHERE [Guid] = @Guid", P( "@Guid", templateGuid ) ) as string
                ?? throw new InvalidOperationException( $"Rock has no contribution statement template {templateGuid}." );

            var settings = JsonNode.Parse( json ).AsObject();
            var pledges = settings["PledgeSettings"] as JsonObject ?? new JsonObject();
            pledges["AccountIds"] = new JsonArray( fundId );
            pledges["IncludeGiftsToChildAccounts"] = true;
            settings["PledgeSettings"] = pledges;

            _db.Execute( "UPDATE [FinancialStatementTemplate] SET [ReportSettingsJson] = @Json WHERE [Guid] = @Guid",
                P( "@Json", settings.ToJsonString( new JsonSerializerOptions { WriteIndented = true } ) ), P( "@Guid", templateGuid ) );
        }
    }
}
