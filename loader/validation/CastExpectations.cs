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
using RockSampleData.Loader.Generation;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Validation
{
    /// <summary>
    /// Checks the Cast givers whose giving patterns say what Rock should show (<c>expect</c> in their household
    /// file) against Rock's values in the loaded database: the frequency label, the journey stage, and alerts. Also
    /// checks the Cast people whose own <c>expect</c> names achievements: earned, in progress, or not earned.
    /// </summary>
    /// <remarks>
    /// Documentation, demos, and tests lean on these givers ("the Deckers give monthly"). Generator changes
    /// and Rock version changes can quietly break those stories, so every load checks them.
    /// </remarks>
    internal static class CastExpectations
    {
        private static readonly string[] FrequencyNames = { null, "weekly", "bi-weekly", "monthly", "quarterly", "erratic", "undetermined" };
        private static readonly string[] StageNames = { "non-giver", "new", "consistent", "occasional", "lapsed", "former" };

        /// <summary>
        /// Prints each expectation's result and returns how many failed.
        /// </summary>
        public static int Check( RockDatabase db, SampleDataSource source )
        {
            var (checks, failures) = Evaluate( db, source );
            Console.WriteLine( failures.Count == 0
                ? $"  Cast expectations: all {checks} hold"
                : $"  WARNING: {failures.Count} of {checks} Cast expectations do not hold" );
            foreach ( var failure in failures )
            {
                Console.WriteLine( $"    {failure}" );
            }

            return failures.Count;
        }

        /// <summary>
        /// Returns how many expectations were checked, and a line for each one that does not hold.
        /// </summary>
        public static (int Checks, List<string> Failures) Evaluate( RockDatabase db, SampleDataSource source )
        {
            var alertTypeKeys = source.GivingAlertTypes.AlertTypes.ToDictionary( t => t.Guid, t => t.Key );
            var checks = 0;
            var failures = new List<string>();

            foreach ( var household in source.ReadHouseholds() )
            {
                foreach ( var pattern in household.Giving.Where( g => g.Expect != null ) )
                {
                    var giver = household.Members.First( m => m.Key == pattern.Person );
                    var person = db.Query( "SELECT [Id], [GivingId] FROM [Person] WHERE [Guid] = @Guid", P( "@Guid", giver.Guid ) ).FirstOrDefault();
                    var name = $"{giver.NickName ?? giver.FirstName} {giver.LastName}";
                    if ( person == null )
                    {
                        failures.Add( $"{name}: not in the database" );
                        continue;
                    }

                    var values = db.Query( @"
SELECT [a].[Key], [av].[Value]
FROM [AttributeValue] AS [av]
INNER JOIN [Attribute] AS [a] ON [a].[Id] = [av].[AttributeId]
WHERE [av].[EntityId] = @PersonId AND [a].[Key] IN ( 'FrequencyLabel', 'CurrentJourneyGivingStage' )
    AND [a].[EntityTypeId] = ( SELECT [Id] FROM [EntityType] WHERE [Name] = 'Rock.Model.Person' )", P( "@PersonId", person["Id"] ) )
                        .ToDictionary( r => ( string ) r["Key"], r => ( string ) r["Value"] );

                    if ( pattern.Expect.Frequency != null )
                    {
                        checks++;
                        var actual = values.TryGetValue( "FrequencyLabel", out var label ) && int.TryParse( label, out var index ) ? FrequencyNames.ElementAtOrDefault( index ) : null;
                        if ( actual != pattern.Expect.Frequency )
                        {
                            failures.Add( $"{name}: expected frequency {pattern.Expect.Frequency}, Rock shows {actual ?? "none"}" );
                        }
                    }

                    if ( pattern.Expect.JourneyStage != null )
                    {
                        checks++;
                        var actual = values.TryGetValue( "CurrentJourneyGivingStage", out var stage ) && int.TryParse( stage, out var index ) ? StageNames.ElementAtOrDefault( index ) : null;
                        if ( actual != pattern.Expect.JourneyStage )
                        {
                            failures.Add( $"{name}: expected journey stage {pattern.Expect.JourneyStage}, Rock shows {actual ?? "none"}" );
                        }
                    }

                    if ( pattern.Expect.Alerts.Count > 0 )
                    {
                        var raised = db.Query( @"
SELECT DISTINCT [t].[Guid]
FROM [FinancialTransactionAlert] AS [fa]
INNER JOIN [FinancialTransactionAlertType] AS [t] ON [t].[Id] = [fa].[AlertTypeId]
WHERE [fa].[GivingId] = @GivingId", P( "@GivingId", person["GivingId"] ) )
                            .Select( r => alertTypeKeys.GetValueOrDefault( ( Guid ) r["Guid"] ) )
                            .ToHashSet();

                        foreach ( var alert in pattern.Expect.Alerts )
                        {
                            checks++;
                            if ( !raised.Contains( alert ) )
                            {
                                failures.Add( $"{name}: expected a {alert} alert, Rock has none" );
                            }
                        }
                    }
                }
            }

            checks += CheckAchievements( db, source, failures );

            checks += CheckBusinesses( db, source, failures );

            checks += CheckPrayerRequests( db, source, failures );

            return (checks, failures);
        }

        /*
            10/21/26 - CLAUDE

            Each Cast prayer request must show the state its file names, because demos and docs point at them
            ("Peggy Carter's request has a reply"). A current request must still be in Rock's prayer session on
            the install day, which a daysAgo date gives back: the entry date plus its days ago. The check reads
            only the database and the Cast file, so it also works on a database loaded days earlier.

            Reason: The always-current requests keep every dataset's prayer list full, so a load must prove it.
        */
        private const int MinimumCurrentCastPrayerRequests = 8;

        /// <summary>
        /// Checks every Cast prayer request's state, prayers, comments, answer, group, and requester, and that
        /// enough of them are in Rock's prayer session on the install day. Returns how many checks ran.
        /// </summary>
        private static int CheckPrayerRequests( RockDatabase db, SampleDataSource source, List<string> failures )
        {
            var requests = source.ReadCastPrayerRequests();
            if ( requests.Count == 0 )
            {
                return 0;
            }

            var configuration = source.ReadPrayerConfiguration();
            var categoryGuids = configuration.Categories.ToDictionary( c => c.Key, c => c.Guid );
            var groupGuids = source.ReadCastSmallGroupPlans().ToDictionary( g => g.Key, g => g.Guid );
            var checks = 0;
            var inSession = 0;

            foreach ( var request in requests )
            {
                checks++;
                var name = $"Prayer request {request.Key}";
                var row = db.Query( @"
SELECT [pr].[IsApproved], [pr].[IsActive], [pr].[IsUrgent], [pr].[IsPublic], [pr].[AllowComments], ISNULL( [pr].[FlagCount], 0 ) AS [FlagCount], ISNULL( [pr].[PrayerCount], 0 ) AS [PrayerCount],
    [pr].[Answer], [pr].[EnteredDateTime], [pr].[ExpirationDate], [c].[Guid] AS [CategoryGuid], [g].[Guid] AS [GroupGuid], [p].[Guid] AS [RequesterGuid], [pr].[FirstName],
    ( SELECT COUNT( * ) FROM [InteractionComponent] AS [ic] INNER JOIN [Interaction] AS [i] ON [i].[InteractionComponentId] = [ic].[Id]
        WHERE [ic].[EntityId] = [pr].[Id] AND [ic].[InteractionChannelId] = ( SELECT [Id] FROM [InteractionChannel] WHERE [Guid] = '3D49FB99-94D1-4F63-B1A2-30D4FEDE11E9' ) ) AS [Prayed],
    ( SELECT COUNT( * ) FROM [Note] AS [n] WHERE [n].[EntityId] = [pr].[Id] AND [n].[NoteTypeId] = ( SELECT [Id] FROM [NoteType] WHERE [Guid] = '0EBABD75-0890-4756-A9EE-62626282BB5D' ) ) AS [Comments],
    ( SELECT COUNT( * ) FROM [Note] AS [n] WHERE [n].[EntityId] = [pr].[Id] AND [n].[ParentNoteId] IS NOT NULL AND [n].[NoteTypeId] = ( SELECT [Id] FROM [NoteType] WHERE [Guid] = '0EBABD75-0890-4756-A9EE-62626282BB5D' ) ) AS [Replies]
FROM [PrayerRequest] AS [pr]
LEFT JOIN [Category] AS [c] ON [c].[Id] = [pr].[CategoryId]
LEFT JOIN [Group] AS [g] ON [g].[Id] = [pr].[GroupId]
LEFT JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [pr].[RequestedByPersonAliasId]
LEFT JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
WHERE [pr].[Guid] = @Guid", P( "@Guid", request.Guid ) ).FirstOrDefault();

                if ( row == null )
                {
                    failures.Add( $"{name}: not in the database" );
                    continue;
                }

                var problems = new List<string>();
                var isApproved = ( bool? ) Nullable( row["IsApproved"] ) == true;
                var isActive = ( bool? ) Nullable( row["IsActive"] ) == true;
                var flagCount = Convert.ToInt32( row["FlagCount"] );
                var expiration = ( DateTime? ) Nullable( row["ExpirationDate"] );
                var answer = Nullable( row["Answer"] ) as string;

                // The install day, when the entry date is days ago; otherwise today, for long-past requests.
                var installDay = request.Entered?.DaysAgo is int daysAgo ? ( ( DateTime ) row["EnteredDateTime"] ).Date.AddDays( daysAgo ) : DateTime.Today;
                var isInSession = isApproved && isActive && expiration.HasValue && expiration.Value >= installDay;

                var stateHolds = request.State switch
                {
                    "current" => isInSession && flagCount == 0,
                    "waiting" => !isApproved && flagCount == 0,
                    "flagged" => !isApproved && flagCount > 0,
                    "expired" or "answered" => isApproved && expiration.HasValue && expiration.Value < installDay,
                    _ => false
                };
                if ( !stateHolds )
                {
                    problems.Add( $"is not {request.State} (approved {isApproved}, active {isActive}, flags {flagCount}, expires {expiration:yyyy-MM-dd})" );
                }

                if ( request.State == "current" && isInSession )
                {
                    inSession++;
                }

                var prayed = Convert.ToInt32( row["Prayed"] );
                if ( prayed != request.Prayers && request.State != "waiting" || prayed != Convert.ToInt32( row["PrayerCount"] ) )
                {
                    problems.Add( $"has {prayed} prayers and a prayer count of {row["PrayerCount"]}, expected {request.Prayers}" );
                }

                if ( ( request.State == "answered" ) != !string.IsNullOrEmpty( answer ) )
                {
                    problems.Add( request.State == "answered" ? "has no answer" : "has an answer" );
                }

                if ( ( bool? ) Nullable( row["IsUrgent"] ) != request.IsUrgent || ( bool? ) Nullable( row["IsPublic"] ) != !request.IsPrivate )
                {
                    problems.Add( "has the wrong urgent or private setting" );
                }

                if ( Nullable( row["CategoryGuid"] ) as Guid? != categoryGuids.GetValueOrDefault( request.Category ) )
                {
                    problems.Add( $"is not in category {request.Category}" );
                }

                var expectedGroup = request.Group == null ? ( Guid? ) null : groupGuids.GetValueOrDefault( request.Group );
                if ( Nullable( row["GroupGuid"] ) as Guid? != expectedGroup )
                {
                    problems.Add( request.Group == null ? "is shared with a group" : $"is not shared with {request.Group}" );
                }

                var requesterGuid = Nullable( row["RequesterGuid"] ) as Guid?;
                if ( requesterGuid != request.Requester || ( request.Guest != null && ( string ) row["FirstName"] != request.Guest.FirstName ) )
                {
                    problems.Add( request.Guest != null ? "is not from the guest" : "is not from its requester" );
                }

                var replies = request.Comments.Count( c => c.By == "requester" );
                if ( Convert.ToInt32( row["Comments"] ) != request.Comments.Count || Convert.ToInt32( row["Replies"] ) != replies )
                {
                    problems.Add( $"has {row["Comments"]} comments ({row["Replies"]} replies), expected {request.Comments.Count} ({replies})" );
                }

                if ( problems.Count > 0 )
                {
                    failures.Add( $"{name}: {string.Join( "; ", problems )}" );
                }
            }

            checks++;
            if ( inSession < MinimumCurrentCastPrayerRequests )
            {
                failures.Add( $"Prayer: {inSession} Cast requests are in the prayer session on the install day, fewer than {MinimumCurrentCastPrayerRequests}" );
            }

            return checks;
        }

        /// <summary>
        /// Turns a database null into a C# null.
        /// </summary>
        private static object Nullable( object value ) => value is DBNull ? null : value;

        /*
            10/19/26 - CLAUDE

            Every load also checks the businesses, Cast and generated alike: each campus with families
            has its minimum number of businesses, and every business has a contact and a gift in the
            last 12 months. "The last 12 months" counts back from the newest gift in the database, so
            a later --check-cast run on the same database gets the same answer.

            Reason: A church keeps a business record only because the business gives.
        */

        /// <summary>
        /// Checks the businesses in the loaded database. Returns how many were checked and adds each failure.
        /// </summary>
        private static int CheckBusinesses( RockDatabase db, SampleDataSource source, List<string> failures )
        {
            var mainCampusGuid = source.Campuses.Campuses.Single( c => c.RockDefault ).Guid;
            var businesses = db.Query( @"
DECLARE @BusinessTypeId INT = ( SELECT [Id] FROM [DefinedValue] WHERE [Guid] = 'BF64ADD3-E70A-44CE-9C4B-E76BBED37550' );
DECLARE @OwnerRoleId INT = ( SELECT [Id] FROM [GroupTypeRole] WHERE [Guid] = '7BC6C12E-0CD1-4DFD-8D5B-1B35AE714C42' );
DECLARE @ContactRoleId INT = ( SELECT [Id] FROM [GroupTypeRole] WHERE [Guid] = '102E6AF5-62C2-4767-B473-C9C228D75FB6' );
DECLARE @YearAgo DATETIME = DATEADD( DAY, -365, ( SELECT MAX( [TransactionDateTime] ) FROM [FinancialTransaction] ) );

SELECT
    [p].[LastName],
    [p].[PrimaryCampusId],
    ( SELECT COUNT( * )
        FROM [GroupMember] AS [owner]
        INNER JOIN [GroupMember] AS [contact] ON [contact].[GroupId] = [owner].[GroupId] AND [contact].[GroupRoleId] = @ContactRoleId
        WHERE [owner].[PersonId] = [p].[Id] AND [owner].[GroupRoleId] = @OwnerRoleId ) AS [Contacts],
    ( SELECT COUNT( * )
        FROM [FinancialTransaction] AS [ft]
        INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [ft].[AuthorizedPersonAliasId]
        WHERE [pa].[PersonId] = [p].[Id] AND [ft].[TransactionDateTime] >= @YearAgo ) AS [RecentGifts]
FROM [Person] AS [p]
WHERE [p].[RecordTypeValueId] = @BusinessTypeId" );

            foreach ( var business in businesses )
            {
                if ( ( int ) business["Contacts"] == 0 )
                {
                    failures.Add( $"{business["LastName"]}: has no business contact" );
                }

                if ( ( int ) business["RecentGifts"] == 0 )
                {
                    failures.Add( $"{business["LastName"]}: has no gift in the last 12 months" );
                }
            }

            // Every campus that has families has its minimum number of businesses.
            var campuses = db.Query( @"
SELECT [c].[Id], [c].[Name], [c].[Guid]
FROM [Campus] AS [c]
WHERE EXISTS (
    SELECT 1 FROM [Person] AS [p]
    WHERE [p].[PrimaryCampusId] = [c].[Id] AND [p].[RecordTypeValueId] <> ( SELECT [Id] FROM [DefinedValue] WHERE [Guid] = 'BF64ADD3-E70A-44CE-9C4B-E76BBED37550' ) )" );
            foreach ( var campus in campuses )
            {
                var minimum = ( Guid ) campus["Guid"] == mainCampusGuid ? BusinessGenerator.MainCampusMinimum : BusinessGenerator.CampusMinimum;
                var count = businesses.Count( b => b["PrimaryCampusId"] is int id && id == ( int ) campus["Id"] );
                if ( count < minimum )
                {
                    failures.Add( $"{campus["Name"]}: has {count} businesses; it needs at least {minimum}" );
                }
            }

            return ( businesses.Count * 2 ) + campuses.Count;
        }

        /// <summary>
        /// Checks each Cast person's achievement expectations against their attempts in the loaded database. Returns
        /// how many were checked and adds each failure.
        /// </summary>
        private static int CheckAchievements( RockDatabase db, SampleDataSource source, List<string> failures )
        {
            var typeKeys = source.Achievements.AchievementTypes.ToDictionary( t => t.Guid, t => t.Key );
            var checks = 0;

            foreach ( var person in source.ReadHouseholds().SelectMany( h => h.Members ).Where( m => m.Expect?.Achievements != null ) )
            {
                var name = $"{person.NickName ?? person.FirstName} {person.LastName}";
                var attempts = db.Query( @"
SELECT [t].[Guid], [a].[IsSuccessful], [a].[IsClosed]
FROM [AchievementAttempt] AS [a]
INNER JOIN [AchievementType] AS [t] ON [t].[Id] = [a].[AchievementTypeId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [a].[AchieverEntityId]
INNER JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
WHERE [p].[Guid] = @Guid", P( "@Guid", person.Guid ) )
                    .Where( r => typeKeys.ContainsKey( ( Guid ) r["Guid"] ) )
                    .Select( r => (Key: typeKeys[( Guid ) r["Guid"]], IsSuccessful: ( bool ) r["IsSuccessful"], IsClosed: ( bool ) r["IsClosed"]) )
                    .ToList();

                var earned = attempts.Where( a => a.IsSuccessful ).Select( a => a.Key ).ToHashSet();
                var inProgress = attempts.Where( a => !a.IsSuccessful && !a.IsClosed ).Select( a => a.Key ).ToHashSet();
                var expect = person.Expect.Achievements;

                foreach ( var key in expect.Earned )
                {
                    checks++;
                    if ( !earned.Contains( key ) )
                    {
                        failures.Add( $"{name}: expected {key} earned, Rock shows it {( inProgress.Contains( key ) ? "in progress" : "not earned" )}" );
                    }
                }

                foreach ( var key in expect.InProgress )
                {
                    checks++;
                    if ( !inProgress.Contains( key ) )
                    {
                        failures.Add( $"{name}: expected {key} in progress, Rock shows it {( earned.Contains( key ) ? "earned" : "not started" )}" );
                    }
                }

                foreach ( var key in expect.NotEarned )
                {
                    checks++;
                    if ( earned.Contains( key ) )
                    {
                        failures.Add( $"{name}: expected {key} not earned, Rock shows it earned" );
                    }
                }
            }

            return checks;
        }
    }
}
