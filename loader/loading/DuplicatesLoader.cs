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

using System.Diagnostics;

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /*
        11/19/26 - CLAUDE

        Rock's Duplicate Finder is empty until its Calculate Person Duplicates job runs at 3 AM. The job only runs
        Rock's duplicate finder procedure, so the load runs that procedure itself, last, after every person, phone,
        and address is in. The job's first night then scores every pair the same and finds nothing new. Full 7 takes
        about 10 seconds. Staff decisions and merge requests come after the procedure, the way staff make them in
        Rock after a night's run, and the procedure keeps them on later runs.

        Reason: The Duplicate Finder shows the Cast's duplicates on day one, and Rock's job changes nothing
        (decisions 310 and 311).
    */

    /// <summary>
    /// Writes cast/duplicates.json: what Rock's merge left for each finished merge, Rock's duplicate finder's pairs,
    /// staff decisions on the Cast's pairs, and the Cast's merge requests.
    /// </summary>
    internal sealed class DuplicatesLoader
    {
        private static readonly Guid PersonEntityTypeGuid = new Guid( "72657ED8-D16E-492E-AC12-144C5E7567E7" );
        private static readonly Guid DemographicHistoryCategoryGuid = new Guid( "51D3EC5A-D079-45ED-909E-B0AB2FD06835" );
        /// <summary>
        /// Rock's Person Merge Request entity set purpose.
        /// </summary>
        internal static readonly Guid MergeRequestPurposeGuid = new Guid( "214EB26F-5493-4540-B2EF-F0887C8FBB9E" );

        /// <summary>
        /// The verb and change type of Rock's Merged History line. They go as parameters: the contract recorder reads the word
        /// MERGE in a command as a SQL MERGE, which the loader never runs.
        /// </summary>
        internal const string MergeVerb = "MERGE";

        internal const string RecordChangeType = "Record";

        /// <summary>
        /// The Calculate Person Duplicates job's CommandTimeout setting, in seconds.
        /// </summary>
        private const int FinderTimeoutSeconds = 1800;

        /// <summary>
        /// The Duplicate Finder lists a pair whose confidence score is above this, its block's Confidence Score Low default.
        /// </summary>
        internal const double FinderListMinimum = 60;

        /// <summary>
        /// The hour the Calculate Person Duplicates job runs (its schedule is 0 0 3 1/1 * ? *).
        /// </summary>
        private const int FinderHour = 3;

        private readonly RockDatabase _db;
        private readonly AnchorDates _dates;
        private readonly string _foreignKey;

        public DuplicatesLoader( RockDatabase db, AnchorDates dates, string foreignKey )
        {
            _db = db;
            _dates = dates;
            _foreignKey = foreignKey;
        }

        /// <summary>
        /// Dates the Crowd's duplicates, writes the finished merges, runs Rock's duplicate finder, then writes the decisions
        /// and merge requests: the Cast's from cast/duplicates.json, and the Crowd's generated duplicates.
        /// </summary>
        public void Load( PeopleLoader people, CastDuplicatesFile duplicates )
        {
            DateCrowdDuplicates( duplicates, people );
            WriteMerges( duplicates.Merges, people );
            RunFinder();
            WriteDecisions( duplicates.Pairs, people );
            WriteMergeRequests( duplicates.MergeRequests, people );

            var listed = Convert.ToInt32( _db.Scalar( "SELECT COUNT(*) FROM [PersonDuplicate] WHERE [ConfidenceScore] > @Minimum AND [IsConfirmedAsNotDuplicate] = 0 AND [IgnoreUntilScoreChanges] = 0", P( "@Minimum", FinderListMinimum ) ) );
            _db.Log.Write( $"  Pairs the Duplicate Finder lists (confidence above {FinderListMinimum:N0}, not decided): {listed:N0}" );
        }

        /*
            11/24/26 - CLAUDE

            The Data Integrity step has dated every record by the time this runs. A Crowd duplicate came from a form after
            its person's record was made, so its record and its family are made on a day between the person's record and
            install. Alisha set a pair aside after its duplicate came, and merged a record after the person's was made:
            Rock's Merged line is one of the person's first records, which their record must come before (decision 383).
            Each decision and merge falls in the rules' window when the records leave room, on a weekday in office hours,
            and a duplicate that must be set aside comes early enough to leave room for it.

            Reason: The Crowd's duplicates, decisions, and merges happen in the order Rock would see them (decision
            398).
        */

        /// <summary>
        /// Dates each Crowd duplicate record after its person's record, and each decision and merge after the records it follows.
        /// </summary>
        private void DateCrowdDuplicates( CastDuplicatesFile duplicates, PeopleLoader people )
        {
            var rules = duplicates.Crowd;
            var pairs = duplicates.Pairs.Where( p => p.IsGenerated ).ToList();
            var merges = duplicates.Merges.Where( m => m.RecordCameLater ).ToList();
            if ( rules == null || pairs.Count + merges.Count == 0 )
            {
                return;
            }

            var created = _db.Query( "SELECT [Id], [CreatedDateTime] FROM [Person] WHERE [ForeignKey] = @ForeignKey", P( "@ForeignKey", _foreignKey ) )
                .ToDictionary( r => ( int ) r["Id"], r => ( DateTime ) r["CreatedDateTime"] );
            var today = _dates.Anchor.Date;
            foreach ( var pair in pairs )
            {
                var personMade = created[people.People[pair.People[0].Guid].PersonId];
                var record = people.People[pair.People[1].Guid];
                var random = new SeededRandom( $"duplicate-dates:{record.Cast.Guid}" );
                var isIgnored = pair.DecisionValue == DuplicateDecision.Ignored;

                // A record to set aside comes before the window's last decision day, so a decision fits after it.
                var made = OfficeHours( random, personMade.Date.AddDays( 1 ), today.AddDays( isIgnored ? -rules.DecidedDaysAgo[0] - 1 : -1 ) )
                    ?? personMade.AddMinutes( 1 );
                _db.Execute( @"
UPDATE [Person] SET [CreatedDateTime] = @Made WHERE [Id] = @PersonId;
UPDATE [Group] SET [CreatedDateTime] = @Made WHERE [Id] = @FamilyId;",
                    P( "@Made", made ), P( "@PersonId", record.PersonId ), P( "@FamilyId", record.FamilyId ) );

                if ( isIgnored )
                {
                    var decided = OfficeHours( random, Later( made.Date.AddDays( 1 ), today.AddDays( -rules.DecidedDaysAgo[1] ) ), today.AddDays( -rules.DecidedDaysAgo[0] ) )
                        ?? made.AddMinutes( 1 );
                    pair.Decided = Relative( decided );
                }
            }

            foreach ( var merge in merges )
            {
                var personMade = created[people.People[merge.Person.Guid].PersonId];
                var random = new SeededRandom( $"duplicate-dates:{merge.Record.Guid}" );
                var merged = OfficeHours( random, Later( personMade.Date.AddDays( 1 ), today.AddDays( -rules.MergedDaysAgo[1] ) ), today.AddDays( -rules.MergedDaysAgo[0] ) )
                    ?? OfficeHours( random, personMade.Date.AddDays( 1 ), today.AddDays( -1 ) )
                    ?? personMade.AddMinutes( 1 );
                merge.Merged = Relative( merged );
            }

            _db.Log.Write( $"  Crowd duplicate dates: {pairs.Count:N0} records made after their people, {pairs.Count( p => p.DecisionValue == DuplicateDecision.Ignored ):N0} set aside after them, {merges.Count:N0} merges after their people" );
        }

        private static DateTime Later( DateTime a, DateTime b ) => a > b ? a : b;

        /// <summary>
        /// A moment in office hours, 8:30 to 4:30, on a day from the first to the last, on a weekday when the range has one. Null when the range is empty.
        /// </summary>
        private static DateTime? OfficeHours( SeededRandom random, DateTime first, DateTime last )
        {
            if ( first.Date > last.Date )
            {
                return null;
            }

            var day = first.Date.AddDays( random.Between( 0, ( int ) ( last.Date - first.Date ).TotalDays ) );
            var weekdays = Enumerable.Range( -2, 5 ).Select( d => day.AddDays( d ) )
                .Where( d => d >= first.Date && d <= last.Date && d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday )
                .OrderBy( d => Math.Abs( ( d - day ).TotalDays ) )
                .ToList();
            return ( weekdays.Count > 0 ? weekdays[0] : day ).AddMinutes( random.Between( 8 * 60 + 30, 16 * 60 + 30 ) );
        }

        /// <summary>
        /// A moment as a date that counts back from install day.
        /// </summary>
        private RelativeDate Relative( DateTime at ) => new RelativeDate { DaysAgo = ( int ) ( _dates.Anchor.Date - at.Date ).TotalDays, Time = at.ToString( "HH:mm", System.Globalization.CultureInfo.InvariantCulture ) };

        /*
            11/19/26 - CLAUDE

            Rock's Person Merge block writes a Merged line in the kept person's History, then runs spCrm_PersonMerge,
            which points the old record's aliases at the kept person and saves the old record's last name as a previous
            name when it differs. Then the old Person row is gone. The merge also deletes the old record's family
            memberships and phones, so nothing else is left.

            Reason: A merge already made looks the way Rock's merge leaves it (decision 312).
        */

        /// <summary>
        /// Writes what Rock's merge left for each finished merge: the old record's alias, the Merged History line, and a previous name.
        /// </summary>
        private void WriteMerges( List<CastMerge> merges, PeopleLoader people )
        {
            if ( merges.Count == 0 )
            {
                return;
            }

            var personTypeId = _db.RequireIdByGuid( "EntityType", PersonEntityTypeGuid );
            var categoryId = _db.RequireIdByGuid( "Category", DemographicHistoryCategoryGuid );
            foreach ( var merge in merges )
            {
                var person = people.People[merge.Person.Guid];
                var oldId = people.MergedRecordIds[merge.Record.Guid];
                var mergedBy = people.People[merge.MergedBy.Guid].AliasId;
                var when = _dates.Resolve( merge.Merged ).Value;

                _db.Execute( @"
INSERT INTO [PersonAlias] ( [PersonId], [AliasPersonId], [AliasPersonGuid], [Guid], [ForeignKey] )
VALUES ( @PersonId, @OldId, @OldGuid, NEWID(), @ForeignKey );

INSERT INTO [History] ( [IsSystem], [CategoryId], [EntityTypeId], [EntityId], [Verb], [ChangeType], [ValueName], [IsSensitive], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime], [CreatedByPersonAliasId], [ModifiedByPersonAliasId] )
VALUES ( 0, @CategoryId, @PersonTypeId, @PersonId, @Verb, @ChangeType, @ValueName, 0, NEWID(), @ForeignKey, @When, @When, @MergedBy, @MergedBy );

DECLARE @LastName NVARCHAR(50) = ( SELECT LTRIM( RTRIM( [LastName] ) ) FROM [Person] WHERE [Id] = @PersonId );
IF @OldLastName <> @LastName AND NOT EXISTS (
    SELECT 1 FROM [PersonPreviousName] AS [pn] INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [pn].[PersonAliasId]
    WHERE [pa].[PersonId] = @PersonId AND LTRIM( RTRIM( [pn].[LastName] ) ) = @OldLastName )
BEGIN
    INSERT INTO [PersonPreviousName] ( [PersonAliasId], [LastName], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
    VALUES ( @AliasId, @OldLastName, NEWID(), @ForeignKey, @When, @When );
END",
                    P( "@PersonId", person.PersonId ), P( "@AliasId", person.AliasId ), P( "@OldId", oldId ), P( "@OldGuid", merge.Record.Guid ), P( "@OldLastName", merge.Record.LastName.Trim() ),
                    P( "@CategoryId", categoryId ), P( "@PersonTypeId", personTypeId ), P( "@Verb", MergeVerb ), P( "@ChangeType", RecordChangeType ), P( "@ValueName", $"{merge.Record.FullName} [ID: {oldId}]" ),
                    P( "@MergedBy", mergedBy ), P( "@When", when ), P( "@ForeignKey", _foreignKey ) );
            }

            var crowdMerges = merges.Count( m => m.RecordCameLater );
            var named = merges.Where( m => !m.RecordCameLater ).Select( m => $"{m.Record.FullName} into {m.Person.Name}" ).Concat( crowdMerges > 0 ? new[] { $"{crowdMerges:N0} Crowd record{( crowdMerges == 1 ? string.Empty : "s" )}" } : Array.Empty<string>() );
            _db.Log.Write( $"  Merges already made: {merges.Count:N0} ({string.Join( ", ", named )})" );
        }

        /// <summary>
        /// Runs Rock's duplicate finder procedure, as the Calculate Person Duplicates job does, and marks its pairs as this dataset's.
        /// </summary>
        private void RunFinder()
        {
            var timer = Stopwatch.StartNew();
            using ( var command = _db.CreateCommand( "EXEC [dbo].[spCrm_PersonDuplicateFinder]" ) )
            {
                command.CommandTimeout = FinderTimeoutSeconds;
                command.ExecuteNonQuery();
            }

            var pairs = _db.Execute( "UPDATE [PersonDuplicate] SET [ForeignKey] = @ForeignKey WHERE [ForeignKey] IS NULL", P( "@ForeignKey", _foreignKey ) );
            _db.Log.Write( $"  Rock's duplicate finder: {pairs:N0} pairs scored ({timer.ElapsedMilliseconds:N0} ms)" );
        }

        /*
            11/19/26 - CLAUDE

            Not Duplicate and Ignore each save the pair, so Rock stamps who clicked. The job's later runs reset every
            score and rewrite ModifiedDateTime, but they keep both flags and the modifier, and the pair's created date
            stays the night the job first found it. So a decided pair was found at 3 AM on or before the decision.

            Reason: A pair staff decided on reads the way Rock leaves it after more nights (decision 311).
        */

        /// <summary>
        /// Writes each Cast pair's decision onto the pair Rock's duplicate finder scored. A pair the finder did not score is
        /// skipped here, and the Cast expectations report it.
        /// </summary>
        private void WriteDecisions( List<CastDuplicatePair> pairs, PeopleLoader people )
        {
            var decided = 0;
            foreach ( var pair in pairs.Where( p => p.DecisionValue != DuplicateDecision.Open ) )
            {
                var when = _dates.Resolve( pair.Decided ).Value;
                var found = when.Date.AddHours( FinderHour );
                if ( found > when )
                {
                    found = found.AddDays( -1 );
                }

                decided += _db.Execute( @"
UPDATE [PersonDuplicate]
SET [IsConfirmedAsNotDuplicate] = @IsNotDuplicate, [IgnoreUntilScoreChanges] = @IsIgnored, [ModifiedByPersonAliasId] = @DecidedBy, [CreatedDateTime] = @Found
WHERE ( [PersonAliasId] = @First AND [DuplicatePersonAliasId] = @Second ) OR ( [PersonAliasId] = @Second AND [DuplicatePersonAliasId] = @First )",
                    P( "@IsNotDuplicate", pair.DecisionValue == DuplicateDecision.NotADuplicate ), P( "@IsIgnored", pair.DecisionValue == DuplicateDecision.Ignored ),
                    P( "@DecidedBy", people.People[pair.DecidedBy.Guid].AliasId ), P( "@Found", found ),
                    P( "@First", people.People[pair.People[0].Guid].AliasId ), P( "@Second", people.People[pair.People[1].Guid].AliasId ) );
            }

            _db.Log.Write( $"  Duplicate decisions: {decided:N0} pairs marked Not Duplicate or ignored" );
        }

        /*
            11/19/26 - CLAUDE

            A person who may view the Merge People page but not merge selects people in a grid and clicks Merge. The grid
            saves them as an entity set that expires in 5 minutes, and the merge page turns it into a Person Merge Request
            that never expires. Then the requester saves a note and the Notify Me When Merged box. The grid bulk-inserts the
            items, which stamps them with the requester and the time.

            Reason: Rock's Merge Requests page lists the request the way Rock saved it (decision 311).
        */

        /// <summary>
        /// Writes each Cast merge request as Rock's Person Merge Request entity set.
        /// </summary>
        private void WriteMergeRequests( List<CastMergeRequest> requests, PeopleLoader people )
        {
            if ( requests.Count == 0 )
            {
                return;
            }

            var personTypeId = _db.RequireIdByGuid( "EntityType", PersonEntityTypeGuid );
            var purposeId = _db.RequireIdByGuid( "DefinedValue", MergeRequestPurposeGuid );
            foreach ( var request in requests )
            {
                var requester = people.People[request.Requester.Guid].AliasId;
                var requested = _dates.Resolve( request.Requested ).Value;
                var setId = Convert.ToInt32( _db.Scalar( @"
INSERT INTO [EntitySet] ( [EntityTypeId], [Order], [EntitySetPurposeValueId], [Note], [AdditionalSettingsJson], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime], [CreatedByPersonAliasId], [ModifiedByPersonAliasId] )
VALUES ( @PersonTypeId, 0, @PurposeId, @Note, @Settings, @Guid, @ForeignKey, @Requested, @NoteSaved, @Requester, @Requester );
SELECT CAST( SCOPE_IDENTITY() AS INT );",
                    P( "@PersonTypeId", personTypeId ), P( "@PurposeId", purposeId ), P( "@Note", request.Note ),
                    P( "@Settings", $"{{\"EntitySetAdditionalSettings\":{{\"ShouldNotifyAfterMerge\":{( request.NotifyAfterMerge ? "true" : "false" )}}}}}" ),
                    P( "@Guid", request.Guid ), P( "@ForeignKey", _foreignKey ), P( "@Requested", requested ), P( "@NoteSaved", requested.AddSeconds( 75 ) ), P( "@Requester", requester ) ) );

                foreach ( var person in request.People )
                {
                    _db.Execute( @"
INSERT INTO [EntitySetItem] ( [EntitySetId], [Order], [EntityId], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime], [CreatedByPersonAliasId], [ModifiedByPersonAliasId] )
VALUES ( @SetId, 0, @PersonId, NEWID(), @ForeignKey, @Requested, @Requested, @Requester, @Requester )",
                        P( "@SetId", setId ), P( "@PersonId", people.People[person.Guid].PersonId ), P( "@ForeignKey", _foreignKey ), P( "@Requested", requested ), P( "@Requester", requester ) );
                }
            }

            _db.Log.Write( $"  Merge requests: {requests.Count:N0}" );
        }
    }
}
