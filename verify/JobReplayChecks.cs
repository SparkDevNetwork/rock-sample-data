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

namespace RockSampleData.Verify
{
    /*
        11/19/26 - CLAUDE

        The loader replays two of Rock's jobs: Family Analytics (eRA) and Calculate Peer Network. Each job is Rock's
        stored procedures plus a few lines of C#, and the procedures read the clock themselves. So the checks run the
        procedures Rock installed, unchanged, on the loaded database, inside a transaction they roll back, and compare
        what the procedures would change. Family Analytics' C# moves a family in or out of eRA only for a row its
        procedure returns as entering or exiting, so those rows stand for the job's own changes. The procedures count
        from today, so like Rock Cleanup the checks are judged only when today is the loads' anchor.

        Reason: Rock's own first run of each job changes nothing the load wrote (decisions 324 and 329).
    */

    /// <summary>
    /// Runs Rock's own Family Analytics and Calculate Peer Network procedures on a load and reports anything they would change.
    /// </summary>
    internal static class JobReplayChecks
    {
        private const string FamilyAnalyticsJobGuid = "623F4751-C654-FEB7-45B7-59685B1F60AE";
        private const string PeerNetworkJobGuid = "D3172560-0E8C-4E69-A477-56ABC018FEEF";

        /// <summary>
        /// Currently an eRA, eRA Start Date, eRA End Date, and the seven values the count procedures write.
        /// </summary>
        private static readonly string[] FamilyAnalyticsAttributeGuids =
        {
            "CE5739C5-2156-E2AB-48E5-1337C38B935E", "A106610C-A7A1-469E-4097-9DE6400FDFC2", "4711D67E-7526-9582-4A8E-1CD7BBE1B3A2",
            "EE5EC76A-D4B9-56B5-4B48-29627D945F10", "02F64263-E290-399E-4487-FC236F4DE81F", "AC11EF53-AE55-79A0-4CAD-43721750E988", "57700E8F-ED11-D787-415A-04DDF411BB10",
            "AB12B3B0-55B8-D6A5-4C1F-DB9CCB2C4342", "5F4C6462-018E-D19C-4AB0-9843CB21C57E", "45A1E978-DC5B-CFA1-4AF4-EA098A24C914"
        };

        /// <summary>
        /// Rock's Family Analytics job, as it would run on install day: the job is on with Set Visit Dates off, its eRA
        /// procedure moves no family in or out, and none of its three procedures changes a value.
        /// </summary>
        public static CheckResult FamilyAnalytics( SqlServer sql, string database, DateTime anchor )
        {
            if ( sql.Today() != anchor.Date || DateTime.Today != anchor.Date )
            {
                return NotJudged( "family-analytics", "Family Analytics", anchor );
            }

            var problems = new List<string>();
            var job = sql.Query( database, $@"
SELECT [j].[IsActive], [v].[Value] AS [SetVisitDates]
FROM [ServiceJob] AS [j]
LEFT JOIN [Attribute] AS [a] ON [a].[EntityTypeQualifierColumn] = 'Class' AND [a].[EntityTypeQualifierValue] = [j].[Class] AND [a].[Key] = 'SetVisitDates'
LEFT JOIN [AttributeValue] AS [v] ON [v].[AttributeId] = [a].[Id] AND [v].[EntityId] = [j].[Id]
WHERE [j].[Guid] = '{FamilyAnalyticsJobGuid}'" ).FirstOrDefault();
            if ( job == null || !( bool ) job["IsActive"] || !string.Equals( job["SetVisitDates"] as string, "False", StringComparison.OrdinalIgnoreCase ) )
            {
                problems.Add( "Rock's Family Analytics job must be on with Set Visit Dates off (decision 325)." );
            }

            var guids = string.Join( ", ", FamilyAnalyticsAttributeGuids.Select( g => $"'{g}'" ) );
            var rows = sql.Query( database, $@"
SET NOCOUNT ON;
DECLARE @Attributes TABLE ( [Id] INT PRIMARY KEY );
INSERT INTO @Attributes SELECT [Id] FROM [Attribute] WHERE [Guid] IN ( {guids} );
DECLARE @Before TABLE ( [AttributeId] INT, [EntityId] INT, [Value] NVARCHAR(MAX), [ValueAsDateTime] DATETIME, [ValueAsNumeric] DECIMAL(38,10), PRIMARY KEY ( [AttributeId], [EntityId] ) );
DECLARE @After TABLE ( [AttributeId] INT, [EntityId] INT, [Value] NVARCHAR(MAX), [ValueAsDateTime] DATETIME, [ValueAsNumeric] DECIMAL(38,10), PRIMARY KEY ( [AttributeId], [EntityId] ) );
DECLARE @Era TABLE ( [FamilyId] INT, [EntryGiftShort] INT, [EntryGiftLong] INT, [ExitGift] INT, [EntryAttendance] INT, [ExitAttendanceShort] INT, [ExitAttendanceLong] INT, [IsEra] BIT );

INSERT INTO @Before SELECT [AttributeId], [EntityId], [Value], [ValueAsDateTime], [ValueAsNumeric] FROM [AttributeValue] WHERE [AttributeId] IN ( SELECT [Id] FROM @Attributes );
BEGIN TRANSACTION;
INSERT INTO @Era EXEC [dbo].[spCrm_FamilyAnalyticsEraDataset];
EXEC [dbo].[spCrm_FamilyAnalyticsGiving];
EXEC [dbo].[spCrm_FamilyAnalyticsAttendance];
INSERT INTO @After SELECT [AttributeId], [EntityId], [Value], [ValueAsDateTime], [ValueAsNumeric] FROM [AttributeValue] WHERE [AttributeId] IN ( SELECT [Id] FROM @Attributes );
ROLLBACK TRANSACTION;

SELECT 'enter' AS [Kind], [g].[Name] AS [What], NULL AS [Before], NULL AS [After]
FROM @Era AS [e] INNER JOIN [Group] AS [g] ON [g].[Id] = [e].[FamilyId]
WHERE [e].[IsEra] = 0
UNION ALL
SELECT 'exit', [g].[Name], NULL, NULL
FROM @Era AS [e] INNER JOIN [Group] AS [g] ON [g].[Id] = [e].[FamilyId]
WHERE [e].[IsEra] = 1 AND [e].[ExitGift] < 1 AND [e].[ExitAttendanceShort] < 1 AND [e].[ExitAttendanceLong] < 8
UNION ALL
SELECT 'value', CONCAT( [p].[NickName], ' ', [p].[LastName], ': ', [a].[Name] ), [b].[Value], [n].[Value]
FROM @Before AS [b]
FULL OUTER JOIN @After AS [n] ON [n].[AttributeId] = [b].[AttributeId] AND [n].[EntityId] = [b].[EntityId]
INNER JOIN [Attribute] AS [a] ON [a].[Id] = COALESCE( [b].[AttributeId], [n].[AttributeId] )
LEFT JOIN [Person] AS [p] ON [p].[Id] = COALESCE( [b].[EntityId], [n].[EntityId] )
WHERE [b].[EntityId] IS NULL OR [n].[EntityId] IS NULL
    OR ISNULL( [b].[Value], '' ) <> ISNULL( [n].[Value], '' )
    OR ISNULL( [b].[ValueAsDateTime], '1900-01-01' ) <> ISNULL( [n].[ValueAsDateTime], '1900-01-01' )
    OR ISNULL( [b].[ValueAsNumeric], -1 ) <> ISNULL( [n].[ValueAsNumeric], -1 );" );

            var entering = rows.Where( r => ( string ) r["Kind"] == "enter" ).ToList();
            var exiting = rows.Where( r => ( string ) r["Kind"] == "exit" ).ToList();
            var values = rows.Where( r => ( string ) r["Kind"] == "value" ).ToList();
            problems.AddRange( entering.Take( 10 ).Select( r => $"Rock's job would move {r["What"]} into eRA." ) );
            problems.AddRange( exiting.Take( 10 ).Select( r => $"Rock's job would move {r["What"]} out of eRA." ) );
            problems.AddRange( values.Take( 10 ).Select( r => $"Rock's procedures would change {r["What"]} from \"{r["Before"] ?? "nothing"}\" to \"{r["After"] ?? "nothing"}\"." ) );
            if ( entering.Count + exiting.Count + values.Count > 30 )
            {
                problems.Add( $"In all: {entering.Count} families in, {exiting.Count} out, and {values.Count} values changed." );
            }

            return CheckResult.From( "family-analytics", problems.Count == 0
                ? "Rock's Family Analytics job, run on install day, moves no family in or out of eRA and changes no value"
                : "Rock's Family Analytics job would change eRA or its counts on its first run", problems );
        }

        /// <summary>
        /// Rock's Calculate Peer Network job, as it would run on install day: the job is on, and its two procedures add,
        /// remove, and rescore no row the load wrote.
        /// </summary>
        public static CheckResult PeerNetwork( SqlServer sql, string database, DateTime anchor )
        {
            if ( sql.Today() != anchor.Date || DateTime.Today != anchor.Date )
            {
                return NotJudged( "peer-network", "Calculate Peer Network", anchor );
            }

            var problems = new List<string>();
            if ( !Convert.ToBoolean( sql.Scalar( database, $"SELECT [IsActive] FROM [ServiceJob] WHERE [Guid] = '{PeerNetworkJobGuid}'" ) ?? false ) )
            {
                problems.Add( "Rock's Calculate Peer Network job must stay on (decision 329)." );
            }

            var rows = sql.Query( database, @"
SET NOCOUNT ON;
DECLARE @Before TABLE ( [Type] INT, [Source] INT, [Target] INT, [Related] INT, [Class] INT, [StartDate] DATE, [EndDate] DATE, [Score] DECIMAL(8,1), [Caption] NVARCHAR(MAX) );
DECLARE @After TABLE ( [Type] INT, [Source] INT, [Target] INT, [Related] INT, [Class] INT, [StartDate] DATE, [EndDate] DATE, [Score] DECIMAL(8,1), [Caption] NVARCHAR(MAX) );
INSERT INTO @Before SELECT [RelationshipTypeValueId], [SourcePersonId], [TargetPersonId], [RelatedEntityId], [ClassificationEntityId], [RelationshipStartDate], [RelationshipEndDate], [RelationshipScore], [Caption] FROM [PeerNetwork];
BEGIN TRANSACTION;
EXEC [dbo].[spPeerNetwork_UpdateFollowing];
EXEC [dbo].[spPeerNetwork_UpdateGroupConnections];
INSERT INTO @After SELECT [RelationshipTypeValueId], [SourcePersonId], [TargetPersonId], [RelatedEntityId], [ClassificationEntityId], [RelationshipStartDate], [RelationshipEndDate], [RelationshipScore], [Caption] FROM [PeerNetwork];
ROLLBACK TRANSACTION;

WITH [Removed] AS ( SELECT * FROM @Before EXCEPT SELECT * FROM @After ), [Added] AS ( SELECT * FROM @After EXCEPT SELECT * FROM @Before )
SELECT 'was' AS [Kind], [x].*, CONCAT( [s].[NickName], ' ', [s].[LastName] ) AS [SourceName], CONCAT( [t].[NickName], ' ', [t].[LastName] ) AS [TargetName], ( SELECT COUNT(*) FROM @Before ) AS [Rows]
FROM [Removed] AS [x] LEFT JOIN [Person] AS [s] ON [s].[Id] = [x].[Source] LEFT JOIN [Person] AS [t] ON [t].[Id] = [x].[Target]
UNION ALL
SELECT 'becomes', [x].*, CONCAT( [s].[NickName], ' ', [s].[LastName] ), CONCAT( [t].[NickName], ' ', [t].[LastName] ), ( SELECT COUNT(*) FROM @Before )
FROM [Added] AS [x] LEFT JOIN [Person] AS [s] ON [s].[Id] = [x].[Source] LEFT JOIN [Person] AS [t] ON [t].[Id] = [x].[Target]
UNION ALL
SELECT 'total', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, ( SELECT COUNT(*) FROM @Before );" );

            var total = rows.Where( r => ( string ) r["Kind"] == "total" ).Select( r => Convert.ToInt32( r["Rows"] ) ).FirstOrDefault();
            var changed = rows.Where( r => ( string ) r["Kind"] != "total" ).ToList();
            string Describe( Dictionary<string, object> r ) => $"{r["SourceName"]} and {r["TargetName"]}{( r["Caption"] is string c ? $" in {c}" : " (a follow)" )}: score {r["Score"]}, from {r["StartDate"]:yyyy-MM-dd}{( r["EndDate"] is DateTime end ? $" to {end:yyyy-MM-dd}" : string.Empty )}";
            problems.AddRange( changed.Take( 20 ).Select( r => $"{( ( string ) r["Kind"] == "was" ? "Rock's job would change or remove" : "Rock's job would make" )} {Describe( r )}." ) );
            if ( changed.Count > 20 )
            {
                problems.Add( $"In all: {changed.Count( r => ( string ) r["Kind"] == "was" )} of {total} rows would change or go, and {changed.Count( r => ( string ) r["Kind"] == "becomes" )} rows would take their place or be added." );
            }

            return CheckResult.From( "peer-network", problems.Count == 0
                ? $"Rock's Calculate Peer Network job, run on install day, keeps all {total:N0} peer network rows as the load wrote them"
                : "Rock's Calculate Peer Network job would change the peer network on its first run", problems );
        }

        private static CheckResult NotJudged( string name, string job, DateTime anchor )
        {
            return CheckResult.From( name, $"{job} was not judged: today's date is not the loads' anchor", new List<string>
            {
                $"The loads were anchored to {anchor:yyyy-MM-dd}, but today is {DateTime.Today:yyyy-MM-dd}. Rock's procedures count from today, so run it again on one day."
            } );
        }
    }
}
