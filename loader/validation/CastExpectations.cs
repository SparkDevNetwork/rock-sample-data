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
using RockSampleData.Loader.Loading;
using RockSampleData.Loader.Model;

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
        public static int Check( RockDatabase db, SampleDataSource source, IReadOnlyCollection<SampleDataPart> parts )
        {
            var (checks, failures) = Evaluate( db, source, parts );
            db.Log.Write( failures.Count == 0
                ? $"  Cast expectations: all {checks} hold"
                : $"  WARNING: {failures.Count} of {checks} Cast expectations do not hold" );
            foreach ( var failure in failures )
            {
                db.Log.Write( $"    {failure}" );
            }

            return failures.Count;
        }

        /// <summary>
        /// Returns how many expectations were checked, and a line for each one that does not hold.
        /// </summary>
        /// <param name="parts">The parts that loaded. Expectations of other parts are not checked.</param>
        public static (int Checks, List<string> Failures) Evaluate( RockDatabase db, SampleDataSource source, IReadOnlyCollection<SampleDataPart> parts )
        {
            var alertTypeKeys = source.GivingAlertTypes.AlertTypes.ToDictionary( t => t.Guid, t => t.Key );
            var guids = new CastPersonGuids( db, source );
            var checks = 0;
            var failures = new List<string>();

            // Each part's expectations are checked only when that part loaded.
            var givingHouseholds = parts.Contains( SampleDataPart.Giving ) ? source.ReadHouseholds() : new List<CastHousehold>();
            foreach ( var household in givingHouseholds )
            {
                foreach ( var pattern in household.Giving.Where( g => g.Expect != null ) )
                {
                    var giver = household.Members.First( m => m.Key == pattern.Person );
                    var person = db.Query( "SELECT [Id], [GivingId] FROM [Person] WHERE [Guid] = @Guid", P( "@Guid", guids.Value( giver.Guid ) ) ).FirstOrDefault();
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

            checks += parts.Contains( SampleDataPart.Attendance ) ? CheckAchievements( db, source, guids, failures ) : 0;

            checks += parts.Contains( SampleDataPart.Steps ) ? CheckSteps( db, source, guids, failures ) : 0;

            checks += CheckBusinesses( db, source, parts.Contains( SampleDataPart.Giving ), failures );

            checks += parts.Contains( SampleDataPart.Prayer ) ? CheckPrayerRequests( db, source, failures ) : 0;

            checks += parts.Contains( SampleDataPart.Giving ) ? CheckCampaign( db, source, guids, failures ) : 0;

            checks += parts.Contains( SampleDataPart.Communications ) ? CheckPhoneNumbers( db, source, guids, failures ) : 0;

            // Signing in as admin shows Alisha Marble's open requests (decision 175).
            checks += parts.Contains( SampleDataPart.Serving ) ? CheckStaffConnectors( db, source, guids, failures ) : 0;

            checks += parts.Contains( SampleDataPart.Communications ) ? CheckUnsubscribes( db, source, guids, failures ) : 0;

            checks += parts.Contains( SampleDataPart.Communications ) ? CheckThreads( db, source, guids, failures ) : 0;

            checks += parts.Contains( SampleDataPart.Communications ) ? CheckStaffEmails( db, source, guids, failures ) : 0;

            checks += parts.Contains( SampleDataPart.Notes ) ? CheckProfileNotes( db, source, failures ) : 0;

            return (checks, failures);
        }

        /// <summary>
        /// Checks that each Cast text thread shows in SMS Conversations the way its file says: every text the person sent
        /// and every staff text, and the thread unread, read, or stopped.
        /// </summary>
        private static int CheckThreads( RockDatabase db, SampleDataSource source, CastPersonGuids guids, List<string> failures )
        {
            var numbers = source.Communications?.PhoneNumbers.Numbers.ToDictionary( n => n.Key, n => n.Guid ) ?? new Dictionary<string, Guid>();
            var checks = 0;
            foreach ( var thread in source.ReadCastThreads() )
            {
                checks++;
                var row = db.Query( @"
SELECT
    ( SELECT COUNT(*) FROM [CommunicationResponse] AS [r] WHERE [r].[FromPersonAliasId] = [p].[PrimaryAliasId] AND [r].[RelatedSmsFromSystemPhoneNumberId] = [spn].[Id] ) AS [FromPerson],
    ( SELECT COUNT(*) FROM [CommunicationResponse] AS [r] WHERE [r].[FromPersonAliasId] = [p].[PrimaryAliasId] AND [r].[RelatedSmsFromSystemPhoneNumberId] = [spn].[Id] AND [r].[IsRead] = 0 ) AS [Unread],
    ( SELECT COUNT(*) FROM [CommunicationRecipient] AS [cr] INNER JOIN [Communication] AS [c] ON [c].[Id] = [cr].[CommunicationId]
        WHERE [cr].[PersonAliasId] = [p].[PrimaryAliasId] AND [c].[SmsFromSystemPhoneNumberId] = [spn].[Id] AND [cr].[Status] = 1 AND [c].[IsBulkCommunication] = 0 ) AS [FromStaff],
    ( SELECT COUNT(*) FROM [CommunicationRecipient] AS [cr] WHERE [cr].[PersonAliasId] = [p].[PrimaryAliasId] AND [cr].[UnsubscribeLevel] = 1 ) AS [Stops]
FROM [Person] AS [p]
CROSS JOIN [SystemPhoneNumber] AS [spn]
WHERE [p].[Guid] = @Person AND [spn].[Guid] = @Number", P( "@Person", guids.Value( thread.Person ) ), P( "@Number", numbers.GetValueOrDefault( thread.Number ) ) ).FirstOrDefault();
                var fromPerson = thread.Messages.Count( m => m.From == "person" );
                var fromStaff = thread.Messages.Count( m => m.From == "staff" );
                var name = $"Text thread {thread.Key}";
                if ( row == null )
                {
                    failures.Add( $"{name}: expected the person and number, Rock shows neither" );
                }
                else if ( Convert.ToInt32( row["FromPerson"] ) != fromPerson || Convert.ToInt32( row["FromStaff"] ) != fromStaff )
                {
                    failures.Add( $"{name}: expected {fromPerson} texts from the person and {fromStaff} from staff, Rock shows {row["FromPerson"]} and {row["FromStaff"]}" );
                }
                else if ( thread.State == "unread" && Convert.ToInt32( row["Unread"] ) == 0 )
                {
                    failures.Add( $"{name}: expected unread texts, Rock shows the thread read" );
                }
                else if ( thread.State != "unread" && Convert.ToInt32( row["Unread"] ) > 0 )
                {
                    failures.Add( $"{name}: expected the thread read, Rock shows {row["Unread"]} unread" );
                }
                else if ( thread.State == "stopped" && Convert.ToInt32( row["Stops"] ) == 0 )
                {
                    failures.Add( $"{name}: expected the STOP to mark a text as unsubscribed, Rock shows none" );
                }
            }

            return checks;
        }

        /// <summary>
        /// Checks that each Cast staff email shows in the person's Communication History: sent by the staff member, with its
        /// subject, delivered, on its day.
        /// </summary>
        private static int CheckStaffEmails( RockDatabase db, SampleDataSource source, CastPersonGuids guids, List<string> failures )
        {
            var staff = source.Notes?.Staff.ToDictionary( s => s.Key, s => s.Guid ) ?? new Dictionary<string, Guid>();
            var checks = 0;
            foreach ( var email in source.ReadCastStaffEmails() )
            {
                checks++;
                var count = Convert.ToInt32( db.Scalar( @"
SELECT COUNT(*)
FROM [CommunicationRecipient] AS [cr]
INNER JOIN [Communication] AS [c] ON [c].[Id] = [cr].[CommunicationId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [cr].[PersonAliasId]
INNER JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
INNER JOIN [PersonAlias] AS [sa] ON [sa].[Id] = [c].[SenderPersonAliasId]
INNER JOIN [Person] AS [s] ON [s].[Id] = [sa].[PersonId]
WHERE [p].[Guid] = @Person AND [s].[Guid] = @Staff AND [c].[CommunicationType] = 1 AND [c].[Subject] = @Subject AND [cr].[Status] IN ( 1, 4 )
    AND DATEDIFF( DAY, [cr].[SendDateTime], GETDATE() ) BETWEEN @DaysAgo - 1 AND @DaysAgo + 1",
                    P( "@Person", guids.Value( email.To ) ), P( "@Staff", guids.Value( staff.GetValueOrDefault( email.From ) ) ), P( "@Subject", email.Subject ), P( "@DaysAgo", email.DaysAgo ) ) );
                if ( count != 1 )
                {
                    failures.Add( $"Staff email {email.Key}: expected one delivered email from {email.From} about {days( email.DaysAgo )} with its subject, Rock shows {count}" );
                }
            }

            return checks;

            static string days( int count ) => count == 1 ? "1 day ago" : $"{count} days ago";
        }

        /*
            10/27/26 - CLAUDE

            Rock's legacy sample data put notes on Ted Decker and others, but they sat in a household
            field the loader never read, so the best-known profiles showed no notes. The person profile's
            Notes block (TimeLine) shows a note only when its type is a Person note type with no qualifier
            and, when the block names note types, one of those. Checking each Cast note against
            that rule catches both a note that never loaded and a type the profile would hide.

            Reason: Every Cast note shows on its person's profile.
        */

        /// <summary>
        /// Checks that each Cast note is on its person, with a type the person profile's Notes block shows.
        /// </summary>
        private static int CheckProfileNotes( RockDatabase db, SampleDataSource source, List<string> failures )
        {
            var shownTypes = db.Query( @"
SELECT [nt].[Guid]
FROM [NoteType] AS [nt]
WHERE [nt].[EntityTypeId] = ( SELECT [Id] FROM [EntityType] WHERE [Name] = 'Rock.Model.Person' )
    AND ISNULL( [nt].[EntityTypeQualifierColumn], '' ) = '' AND ISNULL( [nt].[EntityTypeQualifierValue], '' ) = ''" )
                .Select( r => ( Guid ) r["Guid"] )
                .ToHashSet();

            // The block's Note Types setting: empty shows every type above, a list shows only those types.
            var blockSetting = db.Query( @"
SELECT COALESCE( [av].[Value], [a].[DefaultValue] ) AS [NoteTypes]
FROM [Block] AS [b]
INNER JOIN [Attribute] AS [a] ON [a].[EntityTypeQualifierColumn] = 'BlockTypeId' AND [a].[EntityTypeQualifierValue] = CAST( [b].[BlockTypeId] AS nvarchar( 20 ) ) AND [a].[Key] = 'NoteTypes'
    AND [a].[EntityTypeId] = ( SELECT [Id] FROM [EntityType] WHERE [Name] = 'Rock.Model.Block' )
LEFT JOIN [AttributeValue] AS [av] ON [av].[AttributeId] = [a].[Id] AND [av].[EntityId] = [b].[Id]
WHERE [b].[Guid] = '0B2B550C-B0C9-420E-9CF3-BEC8979108F2'" ).FirstOrDefault();
            if ( blockSetting == null )
            {
                failures.Add( "Person profile: the Notes block (TimeLine) or its Note Types setting is missing" );
                return 1;
            }

            var listed = ( blockSetting["NoteTypes"] as string ?? string.Empty )
                .Split( new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries )
                .Select( v => Guid.TryParse( v.Trim(), out var g ) ? g : Guid.Empty )
                .ToHashSet();
            if ( listed.Count > 0 )
            {
                shownTypes.IntersectWith( listed );
            }

            var people = source.ReadHouseholds().ToDictionary( h => h.Key, h => h.Members.ToDictionary( m => m.Key ) );
            var checks = 0;
            foreach ( var file in source.ReadCastNotes() )
            {
                foreach ( var note in file.Notes )
                {
                    checks++;
                    var person = people[file.Household][note.About];
                    var type = db.Query( @"
SELECT [nt].[Guid], [nt].[Name]
FROM [Note] AS [n]
INNER JOIN [NoteType] AS [nt] ON [nt].[Id] = [n].[NoteTypeId]
INNER JOIN [Person] AS [p] ON [p].[Id] = [n].[EntityId]
WHERE [n].[Guid] = @Note AND [p].[Guid] = @Person", P( "@Note", note.Guid ), P( "@Person", person.Guid ) ).FirstOrDefault();
                    var name = $"{person.NickName ?? person.FirstName} {person.LastName}";
                    if ( type == null )
                    {
                        failures.Add( $"{name}: expected note {note.Key} on their profile, Rock has no such note on them" );
                    }
                    else if ( !shownTypes.Contains( ( Guid ) type["Guid"] ) )
                    {
                        failures.Add( $"{name}: note {note.Key} is a {type["Name"]}, which the profile's Notes block does not show" );
                    }
                }
            }

            return checks;
        }

        /// <summary>
        /// Checks each scripted Cast email unsubscribe: one email recipient row at the right level, and the person left the list,
        /// or chose No mass emails, the way Rock's email preference page records it.
        /// </summary>
        private static int CheckUnsubscribes( RockDatabase db, SampleDataSource source, CastPersonGuids guids, List<string> failures )
        {
            var checks = 0;
            foreach ( var unsubscribe in source.Communications?.CastUnsubscribes ?? new List<CastUnsubscribe>() )
            {
                checks++;
                var level = unsubscribe.List == null ? 2 : 3;
                var row = db.Query( @"
SELECT TOP 1 [cr].[UnsubscribeLevel], [p].[EmailPreference],
    ( SELECT TOP 1 [gm].[GroupMemberStatus] FROM [GroupMember] AS [gm] WHERE [gm].[GroupId] = [c].[ListGroupId] AND [gm].[PersonId] = [p].[Id] ) AS [ListStatus]
FROM [CommunicationRecipient] AS [cr]
INNER JOIN [Communication] AS [c] ON [c].[Id] = [cr].[CommunicationId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [cr].[PersonAliasId]
INNER JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
WHERE [p].[Guid] = @Guid AND [cr].[UnsubscribeDateTime] IS NOT NULL AND [c].[CommunicationType] = 1
ORDER BY [cr].[UnsubscribeDateTime] DESC", P( "@Guid", guids.Value( unsubscribe.Person ) ) ).FirstOrDefault();
                var name = unsubscribe.Person.ToString();
                if ( row == null )
                {
                    failures.Add( $"Unsubscribe {name}: expected an unsubscribe, Rock shows none" );
                }
                else if ( Convert.ToInt32( row["UnsubscribeLevel"] ) != level )
                {
                    failures.Add( $"Unsubscribe {name}: expected level {level}, Rock shows {row["UnsubscribeLevel"]}" );
                }
                else if ( level == 3 && !( Nullable( row["ListStatus"] ) is int status && status == 0 ) )
                {
                    failures.Add( $"Unsubscribe {name}: expected them Inactive in the {unsubscribe.List} list" );
                }
                else if ( level == 2 && Convert.ToInt32( row["EmailPreference"] ) != 1 )
                {
                    failures.Add( $"Unsubscribe {name}: expected No mass emails, Rock shows email preference {row["EmailPreference"]}" );
                }
            }

            return checks;
        }

        /// <summary>
        /// Checks that each Cast staff connector with a minimum connects at least that many active requests.
        /// </summary>
        private static int CheckStaffConnectors( RockDatabase db, SampleDataSource source, CastPersonGuids guids, List<string> failures )
        {
            var checks = 0;
            foreach ( var person in source.ReadHouseholds().SelectMany( h => h.Members ).Where( m => m.Connects?.Minimum != null ) )
            {
                checks++;
                var count = Convert.ToInt32( db.Scalar( @"
SELECT COUNT(*)
FROM [ConnectionRequest] AS [cr]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [cr].[ConnectorPersonAliasId]
INNER JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
WHERE [p].[Guid] = @Guid AND [cr].[ConnectionState] = 0", P( "@Guid", guids.Value( person.Guid ) ) ) );
                if ( count < person.Connects.Minimum.Value )
                {
                    failures.Add( $"{person.NickName ?? person.FirstName} {person.LastName}: expected to connect at least {person.Connects.Minimum} active requests, Rock shows {count}" );
                }
            }

            return checks;
        }

        /// <summary>
        /// Checks that each Cast staff member's personal texting number is active and assigned to them, so
        /// SMS Conversations shows it as theirs.
        /// </summary>
        private static int CheckPhoneNumbers( RockDatabase db, SampleDataSource source, CastPersonGuids guids, List<string> failures )
        {
            var checks = 0;
            foreach ( var number in source.Communications?.PhoneNumbers.Numbers ?? new List<SystemPhoneNumberDefinition>() )
            {
                checks++;
                var row = db.Query( @"
SELECT [spn].[IsActive], [p].[Guid] AS [PersonGuid]
FROM [SystemPhoneNumber] AS [spn]
LEFT JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [spn].[AssignedToPersonAliasId]
LEFT JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
WHERE [spn].[Guid] = @Guid", P( "@Guid", number.Guid ) ).FirstOrDefault();
                var assignedTo = row == null ? null : Nullable( row["PersonGuid"] ) as Guid?;
                if ( row == null || !( bool ) row["IsActive"] )
                {
                    failures.Add( $"Phone number {number.Name}: expected an active system phone number, Rock shows {( row == null ? "none" : "it inactive" )}" );
                }
                else if ( assignedTo != ( number.AssignedTo.HasValue ? guids.Rock( number.AssignedTo.Value ) : ( Guid? ) null ) )
                {
                    failures.Add( $"Phone number {number.Name}: expected it assigned to {number.AssignedTo?.ToString() ?? "nobody"}, Rock shows {assignedTo?.ToString() ?? "nobody"}" );
                }
            }

            return checks;
        }

        /// <summary>
        /// Checks each Cast campaign entry: the pledge is there with its total, and the pledge is in the state its
        /// story says on install day (complete, ahead, on pace, behind, stopped, not started, or gifts only).
        /// </summary>
        private static int CheckCampaign( RockDatabase db, SampleDataSource source, CastPersonGuids guids, List<string> failures )
        {
            var campaign = source.Campaign;
            if ( campaign == null )
            {
                return 0;
            }

            var fundGuid = source.Accounts.Accounts.First( a => a.Name == campaign.Fund ).Guid;
            var fund = db.Query( "SELECT [Id], [StartDate] FROM [FinancialAccount] WHERE [Guid] = @Guid", P( "@Guid", fundGuid ) ).FirstOrDefault();
            if ( fund == null || fund["StartDate"] is DBNull )
            {
                failures.Add( $"The {campaign.Fund} fund is missing or has no dates" );
                return 1;
            }

            // Install day's Sunday. The load's anchor is not stored, but the campaign started a set number of weeks before it.
            var today = ( ( DateTime ) fund["StartDate"] ).AddDays( 7 * campaign.CommitmentSundayWeeksBeforeInstall );
            var checks = 0;

            foreach ( var household in source.ReadHouseholds() )
            {
                foreach ( var entry in household.Campaign )
                {
                    checks++;
                    var giver = household.Members.First( m => m.Key == entry.Person );
                    var name = $"{giver.NickName ?? giver.FirstName ?? household.Name} {giver.LastName}".Trim();
                    var person = db.Query( "SELECT [Id], [GivingId] FROM [Person] WHERE [Guid] = @Guid", P( "@Guid", guids.Value( giver.Guid ) ) ).FirstOrDefault();
                    if ( person == null )
                    {
                        failures.Add( $"{name}: not in the database" );
                        continue;
                    }

                    var pledge = db.Query( @"
SELECT [fp].[TotalAmount], [fp].[StartDate], [fp].[EndDate]
FROM [FinancialPledge] AS [fp]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [fp].[PersonAliasId]
WHERE [pa].[PersonId] = @PersonId AND [fp].[AccountId] = @FundId", P( "@PersonId", person["Id"] ), P( "@FundId", fund["Id"] ) ).FirstOrDefault();
                    var gifts = db.Query( @"
SELECT SUM( [ftd].[Amount] ) AS [Given], MAX( [ft].[TransactionDateTime] ) AS [LastGift]
FROM [FinancialTransactionDetail] AS [ftd]
INNER JOIN [FinancialAccount] AS [fa] ON [fa].[Id] = [ftd].[AccountId]
INNER JOIN [FinancialTransaction] AS [ft] ON [ft].[Id] = [ftd].[TransactionId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [ft].[AuthorizedPersonAliasId]
INNER JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
WHERE ( [fa].[Id] = @FundId OR [fa].[ParentAccountId] = @FundId ) AND [p].[GivingId] = @GivingId", P( "@FundId", fund["Id"] ), P( "@GivingId", person["GivingId"] ) ).Single();

                    if ( ( pledge == null ) != ( entry.Pledge == null ) || ( pledge != null && ( decimal ) pledge["TotalAmount"] != entry.Pledge ) )
                    {
                        failures.Add( $"{name}: expected {( entry.Pledge == null ? "no pledge" : $"a ${entry.Pledge:N0} pledge" )}, the load has {( pledge == null ? "none" : $"${pledge["TotalAmount"]:N0}" )}" );
                        continue;
                    }

                    var given = gifts["Given"] is decimal sum ? sum : 0;
                    var lastGift = gifts["LastGift"] as DateTime?;
                    var state = pledge == null
                        ? PledgeProgress.State( null, null, today, today, given, lastGift, today )
                        : PledgeProgress.State( entry.Pledge, entry.Frequency, ( DateTime ) pledge["StartDate"], ( DateTime ) pledge["EndDate"], given, lastGift, today );
                    if ( state != entry.ExpectedState )
                    {
                        failures.Add( $"{name}: expected the pledge to be {entry.ExpectedState}, the load shows {state} (${given:N0}{( entry.Pledge == null ? string.Empty : $" of ${entry.Pledge:N0}" )})" );
                    }
                }
            }

            return checks;
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
        private static int CheckBusinesses( RockDatabase db, SampleDataSource source, bool isGivingLoaded, List<string> failures )
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

                if ( isGivingLoaded && ( int ) business["RecentGifts"] == 0 )
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

            return ( businesses.Count * ( isGivingLoaded ? 2 : 1 ) ) + campuses.Count;
        }

        /// <summary>
        /// Checks each Cast person's step expectations against their steps and program completions in the loaded
        /// database. Returns how many were checked and adds each failure.
        /// </summary>
        private static int CheckSteps( RockDatabase db, SampleDataSource source, CastPersonGuids guids, List<string> failures )
        {
            if ( source.Steps == null )
            {
                return 0;
            }

            var typeKeys = source.Steps.StepTypes.ToDictionary( t => t.Guid, t => t.Key );
            var checks = 0;

            foreach ( var person in source.ReadHouseholds().SelectMany( h => h.Members ).Where( m => m.Expect?.Steps != null ) )
            {
                var name = $"{person.NickName ?? person.FirstName} {person.LastName}";
                var steps = db.Query( @"
SELECT [st].[Guid], [ss].[IsCompleteStatus]
FROM [Step] AS [s]
INNER JOIN [StepType] AS [st] ON [st].[Id] = [s].[StepTypeId]
INNER JOIN [StepStatus] AS [ss] ON [ss].[Id] = [s].[StepStatusId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [s].[PersonAliasId]
INNER JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
WHERE [p].[Guid] = @Guid", P( "@Guid", guids.Value( person.Guid ) ) )
                    .Where( r => typeKeys.ContainsKey( ( Guid ) r["Guid"] ) )
                    .Select( r => (Key: typeKeys[( Guid ) r["Guid"]], IsComplete: ( bool ) r["IsCompleteStatus"]) )
                    .ToList();
                var expect = person.Expect.Steps;

                foreach ( var key in expect.Complete )
                {
                    checks++;
                    if ( !steps.Any( s => s.Key == key && s.IsComplete ) )
                    {
                        failures.Add( $"{name}: expected a Complete {key} step, Rock shows {( steps.Any( s => s.Key == key ) ? "it In Progress" : "none" )}" );
                    }
                }

                foreach ( var key in expect.InProgress )
                {
                    checks++;
                    if ( !steps.Any( s => s.Key == key && !s.IsComplete ) )
                    {
                        failures.Add( $"{name}: expected an In Progress {key} step, Rock shows {( steps.Any( s => s.Key == key ) ? "it Complete" : "none" )}" );
                    }
                }

                if ( expect.ProgramCompletions != null )
                {
                    checks++;
                    var programKeys = source.Steps.Programs.ToDictionary( p => p.Guid, p => p.Key );
                    var completed = db.Query( @"
SELECT DISTINCT [sp].[Guid]
FROM [StepProgramCompletion] AS [c]
INNER JOIN [StepProgram] AS [sp] ON [sp].[Id] = [c].[StepProgramId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [c].[PersonAliasId]
INNER JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
WHERE [p].[Guid] = @Guid", P( "@Guid", guids.Value( person.Guid ) ) )
                        .Select( r => ( Guid ) r["Guid"] )
                        .Where( programKeys.ContainsKey )
                        .Select( g => programKeys[g] )
                        .OrderBy( k => k )
                        .ToList();
                    var expected = expect.ProgramCompletions.OrderBy( k => k ).ToList();
                    if ( !completed.SequenceEqual( expected ) )
                    {
                        string Names( List<string> keys ) => keys.Count == 0 ? "none" : string.Join( ", ", keys );
                        failures.Add( $"{name}: expected program completions for {Names( expected )}, Rock shows {Names( completed )}" );
                    }
                }
            }

            return checks;
        }

        /// <summary>
        /// Checks each Cast person's achievement expectations against their attempts in the loaded database. Returns
        /// how many were checked and adds each failure.
        /// </summary>
        private static int CheckAchievements( RockDatabase db, SampleDataSource source, CastPersonGuids guids, List<string> failures )
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
WHERE [p].[Guid] = @Guid", P( "@Guid", guids.Value( person.Guid ) ) )
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

    /// <summary>
    /// A Cast person's Guid in Rock. It is the Guid in their household file, except for the person who replaces
    /// Rock's admin (decision 174), who keeps the Guid Rock gave Person 1 at install.
    /// </summary>
    internal sealed class CastPersonGuids
    {
        private readonly Guid _adminCastGuid;
        private readonly Guid? _adminRockGuid;

        public CastPersonGuids( RockDatabase db, SampleDataSource source )
        {
            _adminCastGuid = source.ReadHouseholds().SelectMany( h => h.Members ).FirstOrDefault( m => m.ReplacesAdmin )?.Guid ?? Guid.Empty;
            _adminRockGuid = db.Scalar( "SELECT [Guid] FROM [Person] WHERE [Id] = @Id", P( "@Id", PeopleLoader.AdminPersonId ) ) as Guid?;
        }

        /// <summary>
        /// The Rock Guid for a Cast Guid.
        /// </summary>
        public Guid Rock( Guid castGuid )
        {
            return castGuid == _adminCastGuid && _adminRockGuid.HasValue ? _adminRockGuid.Value : castGuid;
        }

        /// <summary>
        /// The Rock Guid for a Cast Guid, as a SQL parameter value. The admin's Guid goes as text, which SQL Server
        /// compares the same way: it differs on every install, so the contract recorder, which keeps every Guid
        /// parameter that is in the trusted base, must not take it for a Rock Guid the loader depends on.
        /// </summary>
        public object Value( Guid castGuid )
        {
            var guid = Rock( castGuid );
            return guid == castGuid ? guid : ( object ) guid.ToString();
        }
    }
}
