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
        (decisions D-duplicates-1 and D-duplicates-2).
    */

    /// <summary>
    /// Writes cast/duplicates.json: Rock's duplicate finder's pairs, staff decisions on the Cast's pairs, and the Cast's merge requests.
    /// </summary>
    internal sealed class DuplicatesLoader
    {
        private static readonly Guid PersonEntityTypeGuid = new Guid( "72657ED8-D16E-492E-AC12-144C5E7567E7" );
        /// <summary>
        /// Rock's Person Merge Request entity set purpose.
        /// </summary>
        internal static readonly Guid MergeRequestPurposeGuid = new Guid( "214EB26F-5493-4540-B2EF-F0887C8FBB9E" );

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
        private readonly SampleDataSource _source;
        private readonly AnchorDates _dates;
        private readonly string _foreignKey;

        public DuplicatesLoader( RockDatabase db, SampleDataSource source, AnchorDates dates, string foreignKey )
        {
            _db = db;
            _source = source;
            _dates = dates;
            _foreignKey = foreignKey;
        }

        /// <summary>
        /// Runs Rock's duplicate finder, then writes the Cast's decisions and merge requests.
        /// </summary>
        public void Load( PeopleLoader people )
        {
            var duplicates = _source.ReadCastDuplicates();
            RunFinder();
            WriteDecisions( duplicates.Pairs, people );
            WriteMergeRequests( duplicates.MergeRequests, people );

            var listed = Convert.ToInt32( _db.Scalar( "SELECT COUNT(*) FROM [PersonDuplicate] WHERE [ConfidenceScore] > @Minimum AND [IsConfirmedAsNotDuplicate] = 0 AND [IgnoreUntilScoreChanges] = 0", P( "@Minimum", FinderListMinimum ) ) );
            _db.Log.Write( $"  Pairs the Duplicate Finder lists (confidence above {FinderListMinimum:N0}, not decided): {listed:N0}" );
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

            Reason: A pair staff decided on reads the way Rock leaves it after more nights (decision D-duplicates-2).
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

            _db.Log.Write( $"  Cast duplicate decisions: {decided:N0} pairs marked Not Duplicate or ignored" );
        }

        /*
            11/19/26 - CLAUDE

            A person who may view the Merge People page but not merge selects people in a grid and clicks Merge. The grid
            saves them as an entity set that expires in 5 minutes, and the merge page turns it into a Person Merge Request
            that never expires. Then the requester saves a note and the Notify Me When Merged box. The grid bulk-inserts the
            items, which stamps them with the requester and the time.

            Reason: Rock's Merge Requests page lists the request the way Rock saved it (decision D-duplicates-2).
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
