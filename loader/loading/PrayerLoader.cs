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

using System.Text.RegularExpressions;

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /// <summary>
    /// Writes prayer requests and the prayer team's activity: approvals, "Prayed" interactions, flags,
    /// comments, and answers (decision 67). <see cref="PrayerPlanner"/> decides what happened.
    /// </summary>
    internal sealed class PrayerLoader
    {
        private static readonly Guid PrayerEventsChannelGuid = new Guid( "3D49FB99-94D1-4F63-B1A2-30D4FEDE11E9" );
        private static readonly Guid PrayerCommentNoteTypeGuid = new Guid( "0EBABD75-0890-4756-A9EE-62626282BB5D" );

        /// <summary>
        /// Rock's prayer session records the name of the site it runs on as each prayer's summary.
        /// </summary>
        private const string PrayerSessionSiteName = "Rock RMS";

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly AnchorDates _dates;
        private readonly string _foreignKey;
        private readonly string _seed;

        public PrayerLoader( RockDatabase db, SampleDataSource source, AnchorDates dates, string foreignKey, string seed )
        {
            _db = db;
            _source = source;
            _dates = dates;
            _foreignKey = foreignKey;
            _seed = seed;
        }

        /// <summary>
        /// The people on the prayer team, by person Id, once <see cref="Load"/> has run. The Prayer Team
        /// communication list holds them.
        /// </summary>
        public HashSet<int> TeamPersonIds { get; } = new HashSet<int>();

        /// <summary>
        /// Plans and writes every prayer request from <paramref name="historyStart"/> to the anchor.
        /// </summary>
        /// <param name="familySundays">Each family's attended Sundays, from the attendance step.</param>
        /// <param name="gifts">Each giving unit's gifts, from the giving step.</param>
        /// <param name="smallGroupTypeId">The small group type, for who is in a group.</param>
        public void Load( List<LoadedHousehold> households, Dictionary<int, HashSet<DateTime>> familySundays, Dictionary<string, List<RockGift>> gifts, int smallGroupTypeId, DateTime historyStart, ProfileTargets profile )
        {
            var configuration = _source.ReadPrayerConfiguration();
            if ( configuration == null )
            {
                _db.Log.Write( "  Skipped: this package has no configuration/prayer.json." );
                return;
            }

            var text = _source.ReadPrayerText( configuration );
            var categories = configuration.Categories
                .Select( c => new PrayerCategory( c.Key, _db.RequireIdByGuid( "Category", c.Guid ), c.Weight ) )
                .ToList();

            var people = People( households, familySundays, gifts, smallGroupTypeId );
            var planner = new PrayerPlanner( profile, _seed, historyStart, _dates.Anchor );
            var plan = planner.Plan( people, categories, text );
            TeamPersonIds.UnionWith( plan.Team.Select( t => t.Person.PersonId ) );

            // The Cast's own requests come after the generated ones, so the generated rows keep their seeded Guids.
            var castRequests = _source.ReadCastPrayerRequests();
            if ( castRequests.Count > 0 )
            {
                var groupGuids = _source.ReadCastSmallGroupPlans().ToDictionary( g => g.Key, g => g.Guid );
                var groupIds = castRequests.Where( r => r.Group != null ).Select( r => r.Group ).Distinct()
                    .ToDictionary( key => key, key => _db.RequireIdByGuid( "Group", groupGuids[key] ) );
                var entered = castRequests.Select( r => _dates.Resolve( r.Entered ).Value ).ToList();
                var guestCampusId = plan.Admins.FirstOrDefault()?.CampusId ?? people.First().CampusId;
                plan.Requests.AddRange( planner.PlanCast( castRequests, entered, people.ToDictionary( p => p.Guid ), categories.ToDictionary( c => c.Key, c => c.Id ), groupIds, plan, guestCampusId ) );
            }

            Write( plan.Requests );

            var prayers = plan.Requests.Sum( r => r.Prayers.Count );
            var comments = plan.Requests.Sum( r => r.Comments.Count );
            var waiting = plan.Requests.Count( r => !r.IsApproved );
            _db.Log.Write( $"  Prayer requests: {plan.Requests.Count:N0} from {plan.ActiveAdults:N0} active adults, {castRequests.Count} of them the Cast's own ({waiting:N0} waiting for approval, {plan.Requests.Count( r => r.Answer != null ):N0} answered)" );
            _db.Log.Write( $"  Prayer team: {plan.Team.Count:N0} people prayed {prayers:N0} times and left {comments:N0} comments" );
        }

        /// <summary>
        /// Everyone who can ask for or pray: adults in a family (not businesses) who are alive and on an active record.
        /// </summary>
        private List<PrayerPerson> People( List<LoadedHousehold> households, Dictionary<int, HashSet<DateTime>> familySundays, Dictionary<string, List<RockGift>> gifts, int smallGroupTypeId )
        {
            var yearAgo = _dates.Anchor.AddMonths( -12 );
            var groupIds = _db.Query( @"
SELECT [gm].[PersonId], [gm].[GroupId]
FROM [GroupMember] AS [gm]
INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId]
WHERE [g].[GroupTypeId] = @GroupTypeId
    AND [g].[IsActive] = 1
    AND [g].[IsArchived] = 0
    AND [gm].[GroupMemberStatus] = 1
    AND [gm].[IsArchived] = 0", P( "@GroupTypeId", smallGroupTypeId ) )
                .GroupBy( r => ( int ) r["PersonId"] )
                .ToDictionary( g => g.Key, g => g.Select( r => ( int ) r["GroupId"] ).OrderBy( id => id ).ToList() );

            var people = new List<PrayerPerson>();
            foreach ( var household in households.Where( h => h.Cast.Kind != "business" ) )
            {
                var attended = familySundays.TryGetValue( household.FamilyId, out var sundays ) && sundays.Any( s => s >= yearAgo );
                foreach ( var person in household.Members.Where( m => m.IsAdult && m.CanAttend && m.BirthDate.HasValue ) )
                {
                    var gave = person.GivingId != null && gifts.TryGetValue( person.GivingId, out var unitGifts ) && unitGifts.Any( g => g.When >= yearAgo );
                    var groups = groupIds.GetValueOrDefault( person.PersonId ) ?? new List<int>();
                    var roles = person.Cast.SecurityRoles ?? new List<string>();
                    people.Add( new PrayerPerson
                    {
                        PersonId = person.PersonId,
                        Guid = person.Cast.Guid,
                        AliasId = person.AliasId,
                        CampusId = person.CampusId,
                        FirstName = person.Cast.NickName ?? person.Cast.FirstName,
                        LastName = person.Cast.LastName,
                        Email = person.Cast.Email,
                        Age = _dates.AgeOn( person.BirthDate.Value ),
                        IsFemale = person.Cast.Gender == "female",
                        IsActive = attended || gave || groups.Count > 0,
                        IsCastPrayerTeam = roles.Contains( "Prayer Team" ),
                        IsAdmin = roles.Contains( "Prayer Administrators" ),
                        GroupIds = groups
                    } );
                }
            }

            // A person in two households (a shared child grown up) appears once.
            return people.GroupBy( p => p.PersonId ).Select( g => g.First() ).OrderBy( p => p.PersonId ).ToList();
        }

        /// <summary>
        /// Writes the requests, one interaction component for each request that was prayed for, every
        /// prayer as a "Prayed" interaction, and the comments as Prayer Comment notes.
        /// </summary>
        private void Write( List<PlannedPrayerRequest> requests )
        {
            var random = new SeededRandom( $"{_seed}:prayer:guids" );
            var channelId = _db.RequireIdByGuid( "InteractionChannel", PrayerEventsChannelGuid );
            var noteTypeId = _db.RequireIdByGuid( "NoteType", PrayerCommentNoteTypeGuid );
            var nextRequestId = NextId( "PrayerRequest" );
            var nextComponentId = NextId( "InteractionComponent" );
            var nextNoteId = NextId( "Note" );

            var requestRows = TableFactory.Create( "Id:int", "FirstName:string", "LastName:string", "Email:string", "RequestedByPersonAliasId:int", "CategoryId:int", "Text:string", "Answer:string",
                "EnteredDateTime:date", "ExpirationDate:date", "GroupId:int", "AllowComments:bool", "IsUrgent:bool", "IsPublic:bool", "IsActive:bool", "IsApproved:bool", "FlagCount:int", "PrayerCount:int",
                "ApprovedByPersonAliasId:int", "CampusId:int", "ApprovedOnDateTime:date", "ModerationFlags:int", "CreatedDateTime:date", "ModifiedDateTime:date", "CreatedByPersonAliasId:int",
                "ModifiedByPersonAliasId:int", "Guid:guid", "ForeignKey:string" );
            var componentRows = TableFactory.Create( "Id:int", "Name:string", "EntityId:int", "InteractionChannelId:int", "CreatedDateTime:date", "ModifiedDateTime:date", "Guid:guid", "ForeignKey:string" );
            var interactionRows = TableFactory.Create( "InteractionDateTime:date", "Operation:string", "InteractionComponentId:int", "PersonAliasId:int", "InteractionSummary:string", "InteractionDateKey:int",
                "CreatedDateTime:date", "ModifiedDateTime:date", "Guid:guid", "ForeignKey:string" );
            var noteRows = TableFactory.Create( "Id:int", "IsSystem:bool", "NoteTypeId:int", "EntityId:int", "Text:string", "IsAlert:bool", "IsPrivateNote:bool", "ParentNoteId:int", "ApprovalStatus:int",
                "NotificationsSent:bool", "ApprovalsSent:bool", "IsPinned:bool", "CreatedDateTime:date", "ModifiedDateTime:date", "CreatedByPersonAliasId:int", "ModifiedByPersonAliasId:int", "EditedDateTime:date", "EditedByPersonAliasId:int", "Guid:guid", "ForeignKey:string" );

            object Value( object value ) => value ?? DBNull.Value;

            foreach ( var request in requests )
            {
                var requestId = nextRequestId++;
                var lastPrayer = request.Prayers.Count > 0 ? request.Prayers.Max( p => p.When ) : ( DateTime? ) null;
                var modified = new[] { request.Entered, request.ApprovedOn ?? request.Entered, lastPrayer ?? request.Entered }.Max();
                var requesterAlias = ( object ) request.Person?.AliasId;
                requestRows.Rows.Add( requestId, request.FirstName, Value( request.LastName ), Value( request.Email ), Value( requesterAlias ), request.CategoryId, request.Text, Value( request.Answer ),
                    request.Entered, Value( request.Expiration ), Value( request.GroupId ), request.AllowComments, request.IsUrgent, request.IsPublic, request.IsActive, request.IsApproved,
                    request.FlagCount, request.Prayers.Count, Value( request.ApprovedBy?.AliasId ), request.CampusId, Value( request.ApprovedOn ), 0, request.Entered, modified,
                    Value( requesterAlias ), Value( ( object ) request.ApprovedBy?.AliasId ?? requesterAlias ), request.Guid ?? random.NextGuid(), _foreignKey );

                // Rock makes a request's component the first time someone prays for it, named "First Last - MM/dd/yy".
                if ( request.Prayers.Count > 0 )
                {
                    var componentId = nextComponentId++;
                    var firstPrayer = request.Prayers.Min( p => p.When );
                    componentRows.Rows.Add( componentId, $"{request.FirstName} {request.LastName} - {request.Entered:MM/dd/yy}", requestId, channelId, firstPrayer, firstPrayer, random.NextGuid(), _foreignKey );
                    foreach ( var (when, by) in request.Prayers )
                    {
                        interactionRows.Rows.Add( when, "Prayed", componentId, by.AliasId, PrayerSessionSiteName, int.Parse( when.ToString( "yyyyMMdd" ) ), when, when, random.NextGuid(), _foreignKey );
                    }
                }

                int? firstNoteId = null;
                foreach ( var comment in request.Comments.OrderBy( c => c.When ) )
                {
                    var noteId = nextNoteId++;
                    var parentId = comment.IsReply ? firstNoteId : null;
                    firstNoteId ??= noteId;

                    // Notifications and approvals count as sent, so Rock's note jobs send nothing for past comments. Rock's prayer session saves a
                    // comment through NoteClientService, which stamps EditedDateTime and EditedByPersonAliasId on every save, the first included.
                    noteRows.Rows.Add( noteId, false, noteTypeId, requestId, comment.Text, false, false, Value( parentId ), 1, true, true, false,
                        comment.When, comment.When, comment.By.AliasId, comment.By.AliasId, comment.When, comment.By.AliasId, random.NextGuid(), _foreignKey );
                }
            }

            _db.BulkInsert( "PrayerRequest", requestRows, keepIdentity: true );
            _db.BulkInsert( "InteractionComponent", componentRows, keepIdentity: true );
            _db.BulkInsert( "Interaction", interactionRows, keepIdentity: false );
            _db.BulkInsert( "Note", noteRows, keepIdentity: true );
        }

        /*
            10/20/26 - CLAUDE

            Rock Cleanup keeps a daily count of interactions for every channel with daily counts on, and Prayer
            Events is one. Its first run backfills every day from the earliest interaction through yesterday,
            then saves the last day it processed as a system setting. This runs the same INSERT over the same
            range (Rock splits it into 30-day chunks, which gives the same rows) and saves the same setting,
            so Rock's first run finds nothing to do. Rock first lists the enabled channels' components in a
            table variable, for speed on large databases; here a join to the channel picks the same
            components, because the verify tool's write recorder reads only inserts into real tables.

            Reason: Mirrors RockCleanup.UpdateInteractionComponentDailyCounts (see validation/rock-lock.json).
        */
        private const string DailyCountSettingKey = "core_InteractionComponentDailyCount_LastProcessedDate";

        private static readonly Guid AnonymousVisitorGuid = new Guid( "7EBC167B-512D-4683-9D80-98B6BB02E1B9" );

        private static readonly Guid TextFieldTypeGuid = new Guid( "9C204CD0-1233-41C5-818A-C5DA439445AA" );

        /// <summary>
        /// Writes the daily interaction counts Rock Cleanup would, and its last processed date.
        /// </summary>
        public void UpdateDailyCounts()
        {
            var earliest = _db.Scalar( "SELECT MIN( [InteractionDateTime] ) FROM [Interaction]" ) as DateTime?;
            var upperBound = _dates.Anchor.Date;
            if ( earliest == null || earliest.Value.Date >= upperBound )
            {
                return;
            }

            var anonymousVisitorPersonId = _db.IdByGuid( "Person", AnonymousVisitorGuid ) ?? 0;
            _db.Execute( @"
INSERT INTO [InteractionComponentDailyCount]
    ( [InteractionComponentId], [InteractionDate], [Operation], [InteractionDateKey],
      [LoggedInInteractionCount], [AnonymousInteractionCount],
      [LoggedInSessionCount], [AnonymousSessionCount],
      [TotalInteractionCount], [TotalSessionCount],
      [AverageInteractionLength] )
SELECT
    [src].[InteractionComponentId],
    [src].[InteractionDate],
    [src].[Operation],
    CONVERT( INT, CONVERT( VARCHAR(8), [src].[InteractionDate], 112 ) ) AS [InteractionDateKey],
    [src].[LoggedInInteractionCount],
    [src].[AnonymousInteractionCount],
    [src].[LoggedInSessionCount],
    [src].[AnonymousSessionCount],
    [src].[LoggedInInteractionCount] + [src].[AnonymousInteractionCount] AS [TotalInteractionCount],
    [src].[LoggedInSessionCount] + [src].[AnonymousSessionCount] AS [TotalSessionCount],
    [src].[AverageInteractionLength]
FROM (
    SELECT
        [i].[InteractionComponentId],
        CAST( [i].[InteractionDateTime] AS DATE ) AS [InteractionDate],
        ISNULL( [i].[Operation], '' ) AS [Operation],
        SUM( CASE WHEN [pa].[PersonId] IS NOT NULL AND [pa].[PersonId] <> @AnonymousVisitorPersonId THEN 1 ELSE 0 END ) AS [LoggedInInteractionCount],
        SUM( CASE WHEN [pa].[PersonId] IS NULL OR [pa].[PersonId] = @AnonymousVisitorPersonId THEN 1 ELSE 0 END ) AS [AnonymousInteractionCount],
        COUNT( DISTINCT CASE WHEN [pa].[PersonId] IS NOT NULL AND [pa].[PersonId] <> @AnonymousVisitorPersonId THEN [i].[InteractionSessionId] END ) AS [LoggedInSessionCount],
        COUNT( DISTINCT CASE WHEN [pa].[PersonId] IS NULL OR [pa].[PersonId] = @AnonymousVisitorPersonId THEN [i].[InteractionSessionId] END ) AS [AnonymousSessionCount],
        AVG( CAST( [i].[InteractionLength] AS DECIMAL(18, 2) ) ) AS [AverageInteractionLength]
    FROM [InteractionComponent] [ic]
    INNER JOIN [InteractionChannel] [ich] ON [ich].[Id] = [ic].[InteractionChannelId]
                                       AND [ich].[EnableComponentDailyCounts] = 1
    INNER JOIN [Interaction] [i] ON [i].[InteractionComponentId] = [ic].[Id]
                                AND [i].[InteractionDateTime] >= @ChunkLowerBound
                                AND [i].[InteractionDateTime] <  @ChunkUpperBound
    LEFT JOIN [PersonAlias] [pa] ON [pa].[Id] = [i].[PersonAliasId]
    GROUP BY
        [i].[InteractionComponentId],
        CAST( [i].[InteractionDateTime] AS DATE ),
        ISNULL( [i].[Operation], '' )
) AS [src]
WHERE NOT EXISTS (
    SELECT 1
    FROM [InteractionComponentDailyCount] [icdc]
    WHERE [icdc].[InteractionComponentId] = [src].[InteractionComponentId]
      AND [icdc].[InteractionDate]        = [src].[InteractionDate]
      AND [icdc].[Operation]              = [src].[Operation]
);",
                P( "@AnonymousVisitorPersonId", anonymousVisitorPersonId ), P( "@ChunkLowerBound", earliest.Value.Date ), P( "@ChunkUpperBound", upperBound ) );

            // Rock's SystemSettings.SetValue: a text attribute qualified as a system setting, named by SplitCase.
            var lastProcessed = upperBound.AddDays( -1 ).ToString( "yyyy-MM-dd" );
            var name = Regex.Replace( Regex.Replace( DailyCountSettingKey, @"(\P{Ll})(\P{Ll}\p{Ll})", "$1 $2" ), @"(\p{Ll})(\P{Ll})", "$1 $2" );
            _db.Execute( @"
INSERT INTO [Attribute] ( [IsSystem], [FieldTypeId], [EntityTypeQualifierColumn], [EntityTypeQualifierValue], [Key], [Name], [Order], [IsGridColumn], [DefaultValue], [IsMultiValue], [IsRequired], [Guid], [CreatedDateTime], [ModifiedDateTime], [ForeignKey] )
VALUES ( 0, ( SELECT [Id] FROM [FieldType] WHERE [Guid] = @FieldType ), 'SystemSetting', '', @Key, @Name, 0, 0, @Value, 0, 0, @Guid, @Now, @Now, @ForeignKey )",
                P( "@FieldType", TextFieldTypeGuid ), P( "@Key", DailyCountSettingKey ), P( "@Name", name ), P( "@Value", lastProcessed ),
                P( "@Guid", new SeededRandom( $"{_seed}:prayer:daily-count-setting" ).NextGuid() ), P( "@Now", _dates.Anchor ), P( "@ForeignKey", _foreignKey ) );
        }

        private int NextId( string table )
        {
            return Convert.ToInt32( _db.Scalar( $"SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [{table}]" ) );
        }
    }
}
