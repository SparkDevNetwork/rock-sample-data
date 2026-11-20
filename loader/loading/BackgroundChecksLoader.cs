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
    /// <summary>
    /// Adds the small group leader requirement in the configuration step, so it exists in every load.
    /// </summary>
    internal static class LeaderRequirementSetup
    {
        /// <summary>
        /// Adds Rock's Background Check Required requirement to the Small Group type's leader role. Leaders cannot
        /// override it, and it does not stop anyone from being added.
        /// </summary>
        public static void Load( RockDatabase db, BackgroundChecksConfiguration config, AnchorDates dates, string foreignKey )
        {
            var requirement = config?.LeaderRequirement;
            if ( requirement == null )
            {
                return;
            }

            var groupTypeId = db.RequireIdByGuid( "GroupType", requirement.GroupTypeGuid );
            var roleId = Convert.ToInt32( db.Scalar( "SELECT [Id] FROM [GroupTypeRole] WHERE [GroupTypeId] = @TypeId AND [Name] = @Name", P( "@TypeId", groupTypeId ), P( "@Name", requirement.Role ) )
                ?? throw new InvalidOperationException( $"configuration/background-checks.json: the small group type has no {requirement.Role} role." ) );
            var added = dates.Anchor.AddYears( -requirement.AddedYearsAgo );
            db.Execute( @"
IF NOT EXISTS ( SELECT 1 FROM [GroupRequirement] WHERE [Guid] = @Guid )
    INSERT INTO [GroupRequirement] ( [GroupTypeId], [GroupRoleId], [GroupRequirementTypeId], [MustMeetRequirementToAddMember], [AppliesToAgeClassification], [AllowLeadersToOverride], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
    VALUES ( @GroupTypeId, @RoleId, @TypeId, 0, 0, 0, @Guid, @ForeignKey, @Added, @Added )",
                P( "@GroupTypeId", groupTypeId ), P( "@RoleId", roleId ), P( "@TypeId", db.RequireIdByGuid( "GroupRequirementType", config.RequirementTypeGuid ) ),
                P( "@Guid", requirement.Guid ), P( "@ForeignKey", foreignKey ), P( "@Added", added ) );
        }
    }

    /*
        11/19/26 - CLAUDE

        Rock's Checkr provider writes a background check row when staff request one (ForeignId 2, Checkr's candidate key,
        the package, and the request date), and updates it from Checkr's webhooks: the status, the document key, and
        Record Found when the report says consider. It never sets a response date. Checkr's workflow then sets the
        person's Background Checked (True only on a Pass), Background Check Date, and Background Check Result. Checkr keeps
        the report on its own site, which a sample church cannot reach, so the loader draws the report as a PDF and links
        it as the check's response document, the column Rock keeps for a stored report.

        Rock's Calculate Group Requirements job then saves each member's result: the state, and the night each state
        began. The loader writes what the job would have left after running every night, so verify can run the real job
        and see it change nothing but its last-checked time.

        Reason: Decisions 333 and 336. The person, the check, the report, and the requirement agree.
    */

    /// <summary>
    /// Writes every person's latest background check, its report, the person attributes it sets, and the requirement
    /// results for every member a Background Check Required requirement applies to.
    /// </summary>
    internal sealed class BackgroundChecksLoader
    {
        /// <summary>
        /// Rock's Background Check binary file type, which requires view security.
        /// </summary>
        private static readonly Guid BackgroundCheckFileTypeGuid = new Guid( "5C701472-8A6B-4BBE-AEC6-EC833C859F2D" );

        /// <summary>
        /// The ForeignId Rock's Checkr provider marks its checks with, which its request list reads.
        /// </summary>
        private const int CheckrForeignId = 2;

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly AnchorDates _dates;
        private readonly string _foreignKey;
        private readonly string _seed;

        public BackgroundChecksLoader( RockDatabase db, SampleDataSource source, AnchorDates dates, string foreignKey, string seed )
        {
            _db = db;
            _source = source;
            _dates = dates;
            _foreignKey = foreignKey;
            _seed = seed;
        }

        /// <summary>
        /// Plans and writes everything. Returns a line for the load log.
        /// </summary>
        public string Load( PeopleLoader people, ISet<Guid> castPersonGuids )
        {
            var config = _source.BackgroundChecks;
            var requirementTypeId = _db.RequireIdByGuid( "GroupRequirementType", config.RequirementTypeGuid );
            var leaderRequirementId = _db.IdByGuid( "GroupRequirement", config.LeaderRequirement.Guid );
            var members = ReadMembers( requirementTypeId, leaderRequirementId );
            var requests = ReadRequests( config );
            var byId = people.People.Values.ToDictionary( p => p.PersonId );

            var planner = new BackgroundCheckPlanner( config, _dates, _seed );
            var checks = planner.Plan( byId, castPersonGuids, members, requests );

            WriteAttributes( checks.Values );
            WriteChecks( config, checks.Values );
            var states = WriteRequirements( planner, members, byId, checks );

            var finished = checks.Values.Count( c => c.Completed.HasValue );
            return $"  Background checks: {finished:N0} finished ({checks.Values.Count( c => c.Result == BackgroundCheckPlanner.Fail ):N0} failed), {checks.Count - finished:N0} in progress; "
                + $"requirement results: {states.GetValueOrDefault( 0 ):N0} met, {states.GetValueOrDefault( 2 ):N0} expiring, {states.GetValueOrDefault( 1 ):N0} not met";
        }

        /// <summary>
        /// Every member a Background Check Required requirement applies to, the way Rock's job finds them: active and
        /// pending members, not archived, in the requirement's role, of an active group that is not archived.
        /// </summary>
        private List<RequirementMember> ReadMembers( int requirementTypeId, int? leaderRequirementId )
        {
            return _db.Query( @"
SELECT [gr].[Id] AS [RequirementId], [gm].[Id] AS [MemberId], [gm].[PersonId], ISNULL( [gm].[DateTimeAdded], [gm].[CreatedDateTime] ) AS [Joined]
FROM [GroupRequirement] AS [gr]
INNER JOIN [Group] AS [g] ON [g].[Id] = [gr].[GroupId] OR ( [gr].[GroupId] IS NULL AND [g].[GroupTypeId] = [gr].[GroupTypeId] )
INNER JOIN [GroupMember] AS [gm] ON [gm].[GroupId] = [g].[Id]
WHERE [gr].[GroupRequirementTypeId] = @TypeId
    AND [g].[IsActive] = 1 AND [g].[IsArchived] = 0
    AND [gm].[GroupMemberStatus] <> 0 AND [gm].[IsArchived] = 0
    AND ( [gr].[GroupRoleId] IS NULL OR [gm].[GroupRoleId] = [gr].[GroupRoleId] )
ORDER BY [gm].[Id], [gr].[Id]", P( "@TypeId", requirementTypeId ) )
                .Select( r => new RequirementMember
                {
                    GroupRequirementId = Convert.ToInt32( r["RequirementId"] ),
                    GroupMemberId = Convert.ToInt32( r["MemberId"] ),
                    PersonId = Convert.ToInt32( r["PersonId"] ),
                    Joined = ( DateTime ) r["Joined"],
                    Reason = Convert.ToInt32( r["RequirementId"] ) == leaderRequirementId ? CheckReason.Leader : CheckReason.Volunteer
                } )
                .ToList();
        }

        /// <summary>
        /// Active requests in the Background Check status. Rock dates a request's due date from its last status change
        /// (Duration Per Status), so that change is the due date less the status's days.
        /// </summary>
        private List<CheckRequest> ReadRequests( BackgroundChecksConfiguration config )
        {
            return _db.Query( @"
SELECT [cr].[Id], [pa].[PersonId], [cr].[CreatedDateTime], [cr].[DueDate], [cs].[RequestStatusDueDateOffsetInDays] AS [DueInDays]
FROM [ConnectionRequest] AS [cr]
INNER JOIN [ConnectionStatus] AS [cs] ON [cs].[Id] = [cr].[ConnectionStatusId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [cr].[PersonAliasId]
WHERE [cs].[Guid] = @StatusGuid AND [cr].[ConnectionState] = 0
ORDER BY [cr].[Id]", P( "@StatusGuid", config.Requests.StatusGuid ) )
                .Select( r =>
                {
                    var created = ( DateTime ) r["CreatedDateTime"];
                    var since = r["DueDate"] is DateTime due && r["DueInDays"] is int dueInDays ? due.AddDays( -dueInDays ) : created;
                    return new CheckRequest
                    {
                        ConnectionRequestId = Convert.ToInt32( r["Id"] ),
                        PersonId = Convert.ToInt32( r["PersonId"] ),
                        Created = created,
                        StatusSince = since < created ? created : since
                    };
                } )
                .ToList();
        }

        /// <summary>
        /// The person attributes Checkr's workflow sets when a report comes back. A check still in progress sets none.
        /// </summary>
        private void WriteAttributes( IEnumerable<PlannedCheck> checks )
        {
            var ids = _db.Query( @"
SELECT [a].[Key], [a].[Id]
FROM [Attribute] AS [a]
INNER JOIN [EntityType] AS [e] ON [e].[Id] = [a].[EntityTypeId]
WHERE [e].[Name] = 'Rock.Model.Person' AND [a].[Key] IN ( 'BackgroundChecked', 'BackgroundCheckDate', 'BackgroundCheckResult' )" ).ToDictionary( r => ( string ) r["Key"], r => Convert.ToInt32( r["Id"] ) );
            var values = TableFactory.Create( "IsSystem:bool", "AttributeId:int", "EntityId:int", "Value:string", "ValueAsDateTime:date", "ValueAsBoolean:bool", "IsPersistedValueDirty:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            foreach ( var check in checks.Where( c => c.Completed.HasValue ) )
            {
                var day = check.Completed.Value;
                var guid = check.Person.Cast.Guid;
                values.Rows.Add( false, ids["BackgroundChecked"], check.Person.PersonId, check.IsPass ? "True" : "False", DBNull.Value, check.IsPass, true, SeededRandom.StableGuid( $"{guid}:background-checked" ), _foreignKey, day, day );
                values.Rows.Add( false, ids["BackgroundCheckDate"], check.Person.PersonId, day.ToString( "o" ), day, DBNull.Value, true, SeededRandom.StableGuid( $"{guid}:background-check-date" ), _foreignKey, day, day );
                values.Rows.Add( false, ids["BackgroundCheckResult"], check.Person.PersonId, check.Result, DBNull.Value, DBNull.Value, true, SeededRandom.StableGuid( $"{guid}:background-check-result" ), _foreignKey, day, day );
            }

            _db.BulkInsert( "AttributeValue", values, keepIdentity: false );
        }

        /// <summary>
        /// Each check as Rock's Checkr provider leaves it, with a finished check's report as its response document.
        /// </summary>
        private void WriteChecks( BackgroundChecksConfiguration config, IEnumerable<PlannedCheck> checks )
        {
            var organization = OrganizationName();
            var fileType = BinaryFileRows.Type( _db, BackgroundCheckFileTypeGuid );
            var files = new BinaryFileRows( _db, _foreignKey );
            var rows = TableFactory.Create( "PersonAliasId:int", "RequestDate:date", "RecordFound:bool", "ResponseDocumentId:int", "ForeignId:int", "Status:string", "PackageName:string",
                "ResponseId:string", "RequestId:string", "ConnectionRequestId:int", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            foreach ( var check in checks.OrderBy( c => c.Person.PersonId ) )
            {
                var guid = check.Person.Cast.Guid;
                var candidate = CheckrKey( $"{guid}:checkr-candidate" );
                if ( check.Completed.HasValue )
                {
                    var name = PersonName( check.Person );
                    var document = CheckrKey( $"{guid}:checkr-document" );
                    var finished = check.Completed.Value.AddHours( 15 );
                    var pdf = BackgroundCheckReportPdf.Draw( organization, name, document, config.Checkr.PackageTitle, config.Checkr.Searches, check.Requested, check.Completed.Value, check.IsPass );
                    var fileId = files.AddPdf( fileType, BinaryFileRows.PdfFileName( $"Background Check {name}" ), pdf, SeededRandom.StableGuid( $"{guid}:background-check-report" ),
                        SeededRandom.StableGuid( $"{guid}:background-check-report-data" ), finished );
                    rows.Rows.Add( check.Person.AliasId, check.Requested, !check.IsPass, fileId, CheckrForeignId, "ReportCompleted", config.Checkr.PackageName, document, candidate,
                        ( object ) check.ConnectionRequestId ?? DBNull.Value, SeededRandom.StableGuid( $"{guid}:background-check" ), _foreignKey, check.Requested, finished );
                }
                else
                {
                    rows.Rows.Add( check.Person.AliasId, check.Requested, false, DBNull.Value, CheckrForeignId, "ReportCreated", config.Checkr.PackageName, DBNull.Value, candidate,
                        ( object ) check.ConnectionRequestId ?? DBNull.Value, SeededRandom.StableGuid( $"{guid}:background-check" ), _foreignKey, check.Requested, check.Requested );
                }
            }

            files.Write( _db );
            _db.BulkInsert( "BackgroundCheck", rows, keepIdentity: false );
        }

        /// <summary>
        /// Every member's requirement result. Returns how many are in each state.
        /// </summary>
        private Dictionary<int, int> WriteRequirements( BackgroundCheckPlanner planner, List<RequirementMember> members, Dictionary<int, LoadedPerson> people, Dictionary<int, PlannedCheck> checks )
        {
            var today = _dates.Anchor;
            var rows = TableFactory.Create( "GroupMemberId:int", "GroupRequirementId:int", "RequirementMetDateTime:date", "RequirementWarningDateTime:date", "RequirementFailDateTime:date",
                "LastRequirementCheckDateTime:date", "GroupMemberRequirementState:int", "WasManuallyCompleted:bool", "WasOverridden:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var states = new Dictionary<int, int>();
            foreach ( var member in members )
            {
                var person = people[member.PersonId];
                var result = planner.Result( member, person, checks.GetValueOrDefault( member.PersonId ) );
                states[result.State] = states.GetValueOrDefault( result.State ) + 1;
                rows.Rows.Add( member.GroupMemberId, member.GroupRequirementId, ( object ) result.Met ?? DBNull.Value, ( object ) result.Warning ?? DBNull.Value, ( object ) result.Failed ?? DBNull.Value,
                    today, result.State, false, false, SeededRandom.StableGuid( $"{person.Cast.Guid}:requirement:{member.GroupMemberId}:{member.GroupRequirementId}" ), _foreignKey, result.Since, today );
            }

            _db.BulkInsert( "GroupMemberRequirement", rows, keepIdentity: false );
            return states;
        }

        /// <summary>
        /// A key in the form Checkr uses for its candidates and documents: 24 lowercase hex digits.
        /// </summary>
        private static string CheckrKey( string text ) => SeededRandom.StableGuid( text ).ToString( "N" ).Substring( 0, 24 );

        private static string PersonName( LoadedPerson person ) => $"{person.Cast.NickName ?? person.Cast.FirstName} {person.Cast.LastName}";

        private string OrganizationName()
        {
            return _db.Scalar( "SELECT TOP 1 ISNULL( NULLIF( [av].[Value], '' ), [a].[DefaultValue] ) FROM [Attribute] AS [a] LEFT JOIN [AttributeValue] AS [av] ON [av].[AttributeId] = [a].[Id] WHERE [a].[Key] = 'OrganizationName' AND [a].[EntityTypeId] IS NULL" ) as string
                ?? throw new InvalidOperationException( "Rock's OrganizationName global attribute was not found." );
        }
    }
}
