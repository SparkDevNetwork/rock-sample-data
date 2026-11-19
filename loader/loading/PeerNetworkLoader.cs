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

using System.Globalization;

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /// <summary>
    /// Turns on Rock's Peer Network for the group types in configuration/peer-network.json, writes the follows, and fills
    /// the PeerNetwork table as Rock's Calculate Peer Network job's last two monthly runs would have left it
    /// (decisions D-peers-1 to D-peers-4).
    /// </summary>
    /// <remarks>
    /// It reads the groups, memberships, and notes from the rows the earlier steps wrote, so it always runs, and it shows
    /// whatever parts loaded, as Rock's job would.
    /// </remarks>
    internal sealed class PeerNetworkLoader
    {
        private static readonly Guid GroupConnectionTypeGuid = new Guid( "CB51DC46-FBDB-43DA-B7F3-60E7C6E70F40" );
        private static readonly Guid FollowingTypeGuid = new Guid( "84E0360E-0828-E5A5-4BCC-F3113BE338A1" );
        private static readonly Guid PersonAliasEntityTypeGuid = new Guid( "90F5E87B-F0D5-4617-8AE9-EB57E673F36F" );

        public const string GroupProcedure = "spPeerNetwork_UpdateGroupConnections";
        public const string FollowingProcedure = "spPeerNetwork_UpdateFollowing";

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly AnchorDates _dates;
        private readonly string _foreignKey;

        public PeerNetworkLoader( RockDatabase db, SampleDataSource source, AnchorDates dates, string foreignKey )
        {
            _db = db;
            _source = source;
            _dates = dates;
            _foreignKey = foreignKey;
        }

        /// <summary>
        /// True to check, after the load, that Rock's own procedures run as of the job's last run change nothing. The
        /// CLI and the verify tool set it with the Cast expectations; Rock's block skips it (decision 217).
        /// </summary>
        public bool IsChecked { get; set; }

        /// <summary>
        /// Whether the package has peer network configuration.
        /// </summary>
        public bool HasConfiguration => _source.PeerNetwork != null;

        /*
            11/19/26 - CLAUDE

            Rock's job runs at 2:05 AM on the 1st of every month, and its score counts the month boundaries since a
            relationship started or ended. So a run any time in the same month gives the same scores. The last run
            is the 1st of the install month, and it sees the groups as they stand on install day, so a run on
            install day changes no score. The run before it, on the 1st of the month before, sees them as they stood
            then, so each trend says what Rock's would: rising while two people stay, falling after one leaves, and
            flat for a follow.

            Reason: Rock's own Calculate Peer Network, run on install day, changes no score (decision D-peers-2).
        */

        /// <summary>
        /// The job's last two monthly runs before install day: 2:05 AM on the 1st of the month before, and of the install month.
        /// </summary>
        public static (DateTime Previous, DateTime Last) RunTimes( DateTime anchor )
        {
            var last = new DateTime( anchor.Year, anchor.Month, 1 ).AddHours( 2 ).AddMinutes( 5 );
            return (last.AddMonths( -1 ), last);
        }

        /// <summary>
        /// Turns Peer Network on for each configured group type, in the configuration step.
        /// </summary>
        public void LoadSetup()
        {
            foreach ( var groupType in _source.PeerNetwork?.GroupTypes ?? new List<PeerNetworkGroupType>() )
            {
                _db.Execute( "UPDATE [GroupType] SET [IsPeerNetworkEnabled] = 1, [RelationshipStrength] = @Strength, [RelationshipGrowthEnabled] = @Growth WHERE [Guid] = @Guid",
                    P( "@Strength", groupType.Points ), P( "@Growth", groupType.IsGrowthEnabled ), P( "@Guid", groupType.Guid ) );
                _db.Log.Write( $"  {groupType.Name}: Peer Network on, {groupType.RelationshipStrength} ({groupType.Points} points){( groupType.IsGrowthEnabled ? ", growing" : string.Empty )}" );
            }
        }

        /// <summary>
        /// Writes the follows, then the peer network.
        /// </summary>
        /// <param name="campusPastors">Each campus's campus pastor, by campus key.</param>
        /// <param name="campusIds">Each campus's Id, by campus key.</param>
        /// <param name="smallGroupTypeId">Rock's Small Group group type.</param>
        public void Load( Dictionary<string, LoadedPerson> campusPastors, IReadOnlyDictionary<string, int> campusIds, int smallGroupTypeId )
        {
            List<PeerFollow> follows;
            using ( _db.Log.TimePart( "Plan and write the follows" ) )
            {
                follows = PlanFollows( campusPastors, campusIds, smallGroupTypeId );
                WriteFollows( follows );
            }

            var (previous, last) = RunTimes( _dates.Anchor );
            var table = new PeerTable();
            PeerChurch church;
            using ( _db.Log.TimePart( "Replay the last two monthly runs" ) )
            {
                church = Read( follows );
                RockPeerNetwork.Run( table, church.AsOf( previous ), previous );
                RockPeerNetwork.Run( table, church, last );
            }

            using ( _db.Log.TimePart( "Write the peer network" ) )
            {
                Write( table );
            }

            var groupRows = table.Rows.Values.Where( r => r.Key.RelationshipTypeValueId == church.GroupConnectionTypeValueId ).ToList();
            _db.Log.Write( $"  Peer network: {table.Rows.Count:N0} rows, {groupRows.Count:N0} from groups ({groupRows.Count( r => r.Trend > 0 ):N0} rising, {groupRows.Count( r => r.Trend < 0 ):N0} falling) and {follows.Count:N0} follows" );

            if ( IsChecked )
            {
                using ( _db.Log.TimePart( "Check with Rock's procedures" ) )
                {
                    CheckWithRock( last );
                }
            }
        }

        /// <summary>
        /// Who follows whom: each campus pastor follows their campus's small group leaders, and each staff member follows the
        /// people their recent care notes are about. One follow per pair, from the earliest reason.
        /// </summary>
        private List<PeerFollow> PlanFollows( Dictionary<string, LoadedPerson> campusPastors, IReadOnlyDictionary<string, int> campusIds, int smallGroupTypeId )
        {
            var rules = _source.PeerNetwork?.Follows;
            var follows = new Dictionary<(int, int), DateTime>();
            void Add( int follower, int followed, DateTime since )
            {
                if ( follower != followed && ( !follows.TryGetValue( (follower, followed), out var existing ) || since < existing ) )
                {
                    follows[(follower, followed)] = since;
                }
            }

            if ( rules == null )
            {
                return new List<PeerFollow>();
            }

            if ( rules.CampusPastorsFollowSmallGroupLeaders )
            {
                var pastorByCampusId = campusPastors.Where( p => campusIds.ContainsKey( p.Key ) ).ToDictionary( p => campusIds[p.Key], p => p.Value.PersonId );
                foreach ( var leader in _db.Query( @"
SELECT [g].[CampusId], [gm].[PersonId], MIN( [gm].[DateTimeAdded] ) AS [Since]
FROM [GroupMember] AS [gm]
INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId]
INNER JOIN [GroupTypeRole] AS [r] ON [r].[Id] = [gm].[GroupRoleId]
WHERE [g].[GroupTypeId] = @SmallGroupTypeId AND [g].[IsActive] = 1 AND [g].[IsArchived] = 0 AND [g].[CampusId] IS NOT NULL
    AND [r].[IsLeader] = 1 AND [gm].[GroupMemberStatus] = 1 AND [gm].[IsArchived] = 0 AND [gm].[DateTimeAdded] IS NOT NULL
GROUP BY [g].[CampusId], [gm].[PersonId]", P( "@SmallGroupTypeId", smallGroupTypeId ) ) )
                {
                    if ( pastorByCampusId.TryGetValue( ( int ) leader["CampusId"], out var pastorId ) )
                    {
                        Add( pastorId, ( int ) leader["PersonId"], ( DateTime ) leader["Since"] );
                    }
                }
            }

            var noteTypeGuids = rules.CareNoteTypes
                .Select( key => _source.Notes?.Types.FirstOrDefault( t => t.Key == key )?.Guid ?? throw new InvalidDataException( $"configuration/peer-network.json names care note type {key}, which configuration/notes.json does not have." ) )
                .ToList();
            if ( noteTypeGuids.Count > 0 )
            {
                foreach ( var author in _db.Query( $@"
SELECT [pa].[PersonId] AS [Author], [n].[EntityId] AS [About], MIN( [n].[CreatedDateTime] ) AS [Since], MAX( [n].[CreatedDateTime] ) AS [Latest]
FROM [Note] AS [n]
INNER JOIN [NoteType] AS [nt] ON [nt].[Id] = [n].[NoteTypeId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [n].[CreatedByPersonAliasId]
INNER JOIN [Person] AS [p] ON [p].[Id] = [n].[EntityId]
WHERE [nt].[Guid] IN ( {string.Join( ", ", noteTypeGuids.Select( g => $"'{g}'" ) )} ) AND [n].[ParentNoteId] IS NULL
    AND [n].[CreatedDateTime] >= @Since AND [n].[CreatedDateTime] < @Anchor AND [pa].[PersonId] <> [n].[EntityId]
GROUP BY [pa].[PersonId], [n].[EntityId]", P( "@Since", _dates.Anchor.AddMonths( -rules.CareMonths ) ), P( "@Anchor", _dates.Anchor ) )
                    .GroupBy( r => ( int ) r["Author"] ) )
                {
                    // A pastor follows a short list: the people with their newest care notes.
                    foreach ( var care in author.OrderByDescending( r => ( DateTime ) r["Latest"] ).ThenBy( r => ( int ) r["About"] ).Take( rules.CareLimit ) )
                    {
                        Add( author.Key, ( int ) care["About"], ( DateTime ) care["Since"] );
                    }
                }
            }

            return follows.Select( f => new PeerFollow( f.Key.Item1, f.Key.Item2, f.Value ) ).OrderBy( f => f.FollowerPersonId ).ThenBy( f => f.FollowedPersonId ).ToList();
        }

        /// <summary>
        /// Writes each follow as Rock's Following row: the follower's primary alias follows the other person's.
        /// </summary>
        private void WriteFollows( List<PeerFollow> follows )
        {
            if ( follows.Count == 0 )
            {
                return;
            }

            var entityTypeId = _db.RequireIdByGuid( "EntityType", PersonAliasEntityTypeGuid );
            var aliasIds = _db.Query( "SELECT [Id], [PrimaryAliasId] FROM [Person] WHERE [PrimaryAliasId] IS NOT NULL" ).ToDictionary( r => ( int ) r["Id"], r => ( int ) r["PrimaryAliasId"] );
            var rows = TableFactory.Create( "EntityTypeId:int", "EntityId:int", "PersonAliasId:int", "CreatedDateTime:date", "ModifiedDateTime:date", "CreatedByPersonAliasId:int", "Guid:guid", "ForeignKey:string" );
            foreach ( var follow in follows )
            {
                var follower = aliasIds[follow.FollowerPersonId];
                rows.Rows.Add( entityTypeId, aliasIds[follow.FollowedPersonId], follower, follow.CreatedDateTime, follow.CreatedDateTime, follower, Guid.NewGuid(), _foreignKey );
            }

            _db.BulkInsert( "Following", rows, keepIdentity: false );
        }

        /// <summary>
        /// Everything the job reads: the group types, the groups of types with Peer Network on, every role and membership, and the follows.
        /// </summary>
        private PeerChurch Read( List<PeerFollow> follows )
        {
            var groupTypes = _db.Query( @"
SELECT [Id], [IsPeerNetworkEnabled], [RelationshipStrength], [RelationshipGrowthEnabled], [LeaderToLeaderRelationshipMultiplier], [LeaderToNonLeaderRelationshipMultiplier],
    [NonLeaderToLeaderRelationshipMultiplier], [NonLeaderToNonLeaderRelationshipMultiplier]
FROM [GroupType] WHERE [IsPeerNetworkEnabled] = 1" ).Select( r => new PeerGroupType( ( int ) r["Id"], ( bool ) r["IsPeerNetworkEnabled"], ( int ) r["RelationshipStrength"], ( bool ) r["RelationshipGrowthEnabled"],
                ( decimal ) r["LeaderToLeaderRelationshipMultiplier"], ( decimal ) r["LeaderToNonLeaderRelationshipMultiplier"], ( decimal ) r["NonLeaderToLeaderRelationshipMultiplier"], ( decimal ) r["NonLeaderToNonLeaderRelationshipMultiplier"] ) )
                .ToDictionary( t => t.Id );

            var groups = _db.Query( @"
SELECT [g].[Id], [g].[GroupTypeId], [g].[Name], [g].[IsActive], [g].[InactiveDateTime], [g].[IsArchived], [g].[ArchivedDateTime], [g].[RelationshipStrengthOverride], [g].[RelationshipGrowthEnabledOverride],
    [g].[LeaderToLeaderRelationshipMultiplierOverride], [g].[LeaderToNonLeaderRelationshipMultiplierOverride], [g].[NonLeaderToLeaderRelationshipMultiplierOverride], [g].[NonLeaderToNonLeaderRelationshipMultiplierOverride]
FROM [Group] AS [g]
INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId]
WHERE [gt].[IsPeerNetworkEnabled] = 1" ).Select( r => new PeerGroup( ( int ) r["Id"], ( int ) r["GroupTypeId"], ( string ) r["Name"], ( bool ) r["IsActive"], r["InactiveDateTime"] as DateTime?, ( bool ) r["IsArchived"], r["ArchivedDateTime"] as DateTime?,
                r["RelationshipStrengthOverride"] as int?, r["RelationshipGrowthEnabledOverride"] as bool?, r["LeaderToLeaderRelationshipMultiplierOverride"] as decimal?, r["LeaderToNonLeaderRelationshipMultiplierOverride"] as decimal?,
                r["NonLeaderToLeaderRelationshipMultiplierOverride"] as decimal?, r["NonLeaderToNonLeaderRelationshipMultiplierOverride"] as decimal? ) )
                .ToDictionary( g => g.Id );

            var roles = _db.Query( "SELECT [Id], [IsLeader], [IsExcludedFromPeerNetwork] FROM [GroupTypeRole]" )
                .Select( r => new PeerRole( ( int ) r["Id"], ( bool ) r["IsLeader"], ( bool ) r["IsExcludedFromPeerNetwork"] ) )
                .ToDictionary( r => r.Id );

            // Every membership counts toward who leads a group; only those in the groups above make pairs.
            var members = _db.Query( @"
SELECT [GroupId], [PersonId], [GroupRoleId], [DateTimeAdded], [IsArchived], [ArchivedDateTime], [GroupMemberStatus], [InactiveDateTime], COALESCE( [DateTimeAdded], [CreatedDateTime] ) AS [Since]
FROM [GroupMember]" ).Select( r => new PeerMember( ( int ) r["GroupId"], ( int ) r["PersonId"], ( int ) r["GroupRoleId"], r["DateTimeAdded"] as DateTime?, ( bool ) r["IsArchived"], r["ArchivedDateTime"] as DateTime?,
                ( int ) r["GroupMemberStatus"], r["InactiveDateTime"] as DateTime?, r["Since"] as DateTime? ) ).ToList();

            return new PeerChurch
            {
                GroupConnectionTypeValueId = _db.RequireIdByGuid( "DefinedValue", GroupConnectionTypeGuid ),
                FollowingTypeValueId = _db.RequireIdByGuid( "DefinedValue", FollowingTypeGuid ),
                GroupTypes = groupTypes,
                Groups = groups,
                Roles = roles,
                Members = members,
                Follows = follows
            };
        }

        /// <summary>
        /// Writes the PeerNetwork rows. The table has no ForeignKey column, so its rows go with their people.
        /// </summary>
        private void Write( PeerTable table )
        {
            var rows = TableFactory.Create( "RelationshipTypeValueId:int", "RelationshipStartDate:date", "RelationshipEndDate:date", "RelatedEntityId:int", "ClassificationEntityId:int",
                "RelationshipScore:decimal", "RelationshipScoreLastUpdateValue:decimal", "RelationshipTrend:int", "LastUpdateDateTime:date", "SourcePersonId:int", "TargetPersonId:int", "Caption:string" );
            foreach ( var row in table.Rows.Values.OrderBy( r => r.Key.SourcePersonId ).ThenBy( r => r.Key.TargetPersonId ).ThenBy( r => r.Key.RelatedEntityId ).ThenBy( r => r.Key.ClassificationEntityId ) )
            {
                rows.Rows.Add( row.Key.RelationshipTypeValueId, row.StartDate, ( object ) row.EndDate ?? DBNull.Value, ( object ) row.Key.RelatedEntityId ?? DBNull.Value, ( object ) row.Key.ClassificationEntityId ?? DBNull.Value,
                    row.Score, row.LastUpdateValue, row.Trend, row.LastUpdateDateTime, row.Key.SourcePersonId, row.Key.TargetPersonId, ( object ) row.Caption ?? DBNull.Value );
            }

            _db.BulkInsert( "PeerNetwork", rows, keepIdentity: false );
        }

        /// <summary>
        /// Runs Rock's own two procedures, as the job does, as of its last run inside a savepoint, and reports any row they
        /// would add, remove, or change in its dates or score. Then rolls them back.
        /// </summary>
        private void CheckWithRock( DateTime now )
        {
            var procedures = new[] { FollowingProcedure, GroupProcedure }.Select( name => (Name: name, Procedure: RockProcedureClock.TryRead( _db, name )) ).ToList();
            foreach ( var (name, _) in procedures )
            {
                _db.Recorder?.Procedures.Add( name );
            }

            if ( procedures.Any( p => p.Procedure == null ) )
            {
                _db.Log.Write( "  WARNING: Rock's peer network procedures read the clock in a way the loader does not know, so the peer network check did not run" );
                return;
            }

            const string Snapshot = @"
SELECT [RelationshipTypeValueId], [SourcePersonId], [TargetPersonId], [RelatedEntityId], [ClassificationEntityId], [RelationshipStartDate], [RelationshipEndDate], [RelationshipScore], [Caption]
FROM [PeerNetwork]";
            static string Row( Dictionary<string, object> r ) => string.Join( "|", r.Values.Select( v => v is DateTime d ? d.ToString( "yyyy-MM-dd", CultureInfo.InvariantCulture ) : v is decimal m ? m.ToString( "0.0", CultureInfo.InvariantCulture ) : Convert.ToString( v, CultureInfo.InvariantCulture ) ) );

            var before = _db.Query( Snapshot ).Select( Row ).ToHashSet();
            _db.Execute( "SAVE TRANSACTION PeerNetworkCheck" );
            HashSet<string> after;
            try
            {
                foreach ( var (_, procedure) in procedures )
                {
                    using var command = procedure.Command( now );
                    command.ExecuteNonQuery();
                }

                after = _db.Query( Snapshot ).Select( Row ).ToHashSet();
            }
            finally
            {
                _db.Execute( "ROLLBACK TRANSACTION PeerNetworkCheck" );
            }

            var added = after.Except( before ).Count();
            var removed = before.Except( after ).Count();
            _db.Log.Write( added == 0 && removed == 0
                ? $"  Peer network check: Rock's own procedures, run as of the job's last run, keep all {before.Count:N0} rows as the port wrote them"
                : $"  WARNING: Rock's own peer network procedures, run as of the job's last run, would change {removed:N0} of {before.Count:N0} rows into {added:N0} others. Check RockPeerNetwork against Rock." );
        }
    }
}
