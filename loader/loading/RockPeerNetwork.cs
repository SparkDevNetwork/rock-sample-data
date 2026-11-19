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

namespace RockSampleData.Loader.Loading
{
    /*
        11/19/26 - CLAUDE

        A line-for-line port of Rock's Calculate Peer Network job, so the loader can write the peer network Rock's
        monthly runs would have left. Keep it a faithful copy: same pairs, same dates, same score formula, same
        rounding, same order of steps. Do not "improve" it.

        Rock sources this mirrors (paths in the SparkDevNetwork/Rock repository; the same in Rock 20.0.10 and 21.0.2):
        - Rock/Jobs/CalculatePeerNetwork.cs (Execute): following first, then groups, both on by default.
        - The installed spPeerNetwork_UpdateFollowing: a flat 5 points per follow, new ones rising.
        - The installed spPeerNetwork_UpdateGroupConnections: a row per source, target, group, and source role for
          every pair in a group at the same time; then the score, which grows 0.1 a month from the strength while both
          stay and falls 0.2 a month after either leaves; then the clean-up of scores at 0 or below.

        Quirks kept on purpose:
        - A target counts as a leader when they lead any group at all, of any type, archived or not.
        - The trend compares the stored score with the new score before it is rounded to one decimal place.
        - A row whose group is gone scores 0, and the clean-up removes it.

        Rock reads GETDATE() inside these procedures. The port takes "now" as a parameter, so the loader can run each
        monthly run as of the time Rock would have run it.

        The version guard (validation/rock-lock.json) fails when any of these change in Rock.

        Reason: Peer networks on a fresh install must match what Rock itself computes.
    */

    /// <summary>
    /// The port of Rock's Calculate Peer Network job. Nothing here touches a database.
    /// </summary>
    internal static class RockPeerNetwork
    {
        /// <summary>
        /// spPeerNetwork_UpdateFollowing's static score.
        /// </summary>
        public const int FollowingScore = 5;

        /// <summary>
        /// The procedure's stand-in end date for a relationship that has not ended.
        /// </summary>
        private static readonly DateTime StillActive = new DateTime( 9999, 12, 31 );

        /// <summary>
        /// One run of the job at <paramref name="now"/> over <paramref name="church"/> as it stood then.
        /// </summary>
        public static void Run( PeerTable table, PeerChurch church, DateTime now )
        {
            UpdateFollowing( table, church, now );
            UpdateGroupConnections( table, church, now );
        }

        /// <summary>
        /// spPeerNetwork_UpdateFollowing.
        /// </summary>
        private static void UpdateFollowing( PeerTable table, PeerChurch church, DateTime now )
        {
            var type = church.FollowingTypeValueId;
            var dataset = church.Follows
                .GroupBy( f => (f.FollowerPersonId, f.FollowedPersonId) )
                .ToDictionary( g => new PeerKey( type, g.Key.FollowerPersonId, g.Key.FollowedPersonId, null, null ), g => g.Min( f => f.CreatedDateTime ) );

            foreach ( var (key, start) in dataset )
            {
                if ( table.Rows.TryGetValue( key, out var row ) )
                {
                    row.StartDate = start.Date;
                    row.LastUpdateDateTime = now;
                    row.Score = FollowingScore;
                    row.LastUpdateValue = FollowingScore;
                    row.Trend = 0;
                }
                else
                {
                    table.Rows[key] = new PeerRow( key ) { StartDate = start.Date, Score = FollowingScore, LastUpdateValue = 0, Trend = 1, LastUpdateDateTime = now };
                }
            }

            foreach ( var key in table.Rows.Keys.Where( k => k.RelationshipTypeValueId == type && !dataset.ContainsKey( k ) ).ToList() )
            {
                table.Rows.Remove( key );
            }
        }

        /// <summary>
        /// spPeerNetwork_UpdateGroupConnections: the pairs, the scores, and the clean-up.
        /// </summary>
        private static void UpdateGroupConnections( PeerTable table, PeerChurch church, DateTime now )
        {
            var type = church.GroupConnectionTypeValueId;

            // STEP 1: the pairs. GroupMemberDataSet is every member of a group whose type has Peer Network on, whose
            // strength is not None, in a role that is not left out.
            var members = church.Members
                .Where( m => church.Groups.ContainsKey( m.GroupId ) )
                .Select( m => (Member: m, Group: church.Groups[m.GroupId]) )
                .Where( x => church.GroupTypes.TryGetValue( x.Group.GroupTypeId, out var gt ) && gt.IsPeerNetworkEnabled
                    && ( ( x.Group.RelationshipStrengthOverride == null && gt.RelationshipStrength != 0 ) || ( x.Group.RelationshipStrengthOverride.HasValue && x.Group.RelationshipStrengthOverride != 0 ) )
                    && church.Roles.TryGetValue( x.Member.RoleId, out var role ) && !role.IsExcludedFromPeerNetwork )
                .Select( x => (x.Member, x.Group, InactiveDate: x.Member.IsArchived ? x.Member.ArchivedDateTime : x.Member.Status == 0 ? x.Member.InactiveDateTime : StillActive) )
                .ToList();

            var dataset = new Dictionary<PeerKey, (DateTime Start, DateTime? End, string Caption)>();
            foreach ( var group in members.GroupBy( m => m.Group.Id ) )
            {
                var list = group.ToList();
                foreach ( var s in list )
                {
                    foreach ( var t in list )
                    {
                        // A null date fails every comparison, as in SQL.
                        if ( s.Member.PersonId == t.Member.PersonId || s.Member.DateTimeAdded == null || t.Member.DateTimeAdded == null || s.InactiveDate == null || t.InactiveDate == null
                            || s.Member.DateTimeAdded > t.InactiveDate || t.Member.DateTimeAdded > s.InactiveDate )
                        {
                            continue;
                        }

                        var start = ( s.Member.DateTimeAdded < t.Member.DateTimeAdded ? t.Member.DateTimeAdded.Value : s.Member.DateTimeAdded.Value ).Date;
                        DateTime? end;
                        if ( !s.Group.IsActive )
                        {
                            end = s.Group.InactiveDateTime;
                        }
                        else if ( s.Group.IsArchived )
                        {
                            end = s.Group.ArchivedDateTime;
                        }
                        else if ( t.InactiveDate == StillActive && s.InactiveDate == StillActive )
                        {
                            end = StillActive;
                        }
                        else if ( t.InactiveDate == StillActive )
                        {
                            end = s.InactiveDate;
                        }
                        else if ( s.InactiveDate == StillActive )
                        {
                            end = t.InactiveDate;
                        }
                        else
                        {
                            end = t.InactiveDate < s.InactiveDate ? t.InactiveDate : s.InactiveDate;
                        }

                        // GROUP BY source, target, group, and source role: the earliest start and the latest end.
                        var key = new PeerKey( type, s.Member.PersonId, t.Member.PersonId, s.Group.Id, s.Member.RoleId );
                        if ( dataset.TryGetValue( key, out var existing ) )
                        {
                            var laterEnd = existing.End == null ? end : end == null ? existing.End : end > existing.End ? end : existing.End;
                            dataset[key] = (start < existing.Start ? start : existing.Start, laterEnd, existing.Caption);
                        }
                        else
                        {
                            dataset[key] = (start, end, s.Group.Name);
                        }
                    }
                }
            }

            // The MERGE: update each pair's dates, add new pairs at 0, and end the rows whose pair is gone.
            foreach ( var (key, pair) in dataset )
            {
                var end = pair.End == StillActive || pair.End == null ? ( DateTime? ) null : pair.End.Value.Date;
                if ( table.Rows.TryGetValue( key, out var row ) )
                {
                    row.EndDate = end;
                    row.StartDate = pair.Start;
                }
                else
                {
                    table.Rows[key] = new PeerRow( key ) { StartDate = pair.Start, EndDate = end, Score = 0, LastUpdateValue = 0, Trend = 0, LastUpdateDateTime = now, Caption = pair.Caption };
                }
            }

            foreach ( var row in table.Rows.Values.Where( r => r.Key.RelationshipTypeValueId == type && r.EndDate == null && !dataset.ContainsKey( r.Key ) ) )
            {
                row.EndDate = now.Date;
            }

            // STEP 2: score every group connection.
            var targetIsLeader = church.TargetIsLeader();
            foreach ( var row in table.Rows.Values.Where( r => r.Key.RelationshipTypeValueId == type ) )
            {
                var score = Score( church, row, targetIsLeader, now );
                var trend = score == null ? 0 : row.Score > score ? -1 : row.Score < score ? 1 : 0;
                row.LastUpdateValue = row.Score;
                row.Score = score.HasValue ? Math.Round( score.Value, 1, MidpointRounding.AwayFromZero ) : 0;
                row.LastUpdateDateTime = now;
                row.Trend = trend;
            }

            // STEP 3: remove every relationship at 0 or below. Each key is one row, so there are no duplicates to remove.
            foreach ( var key in table.Rows.Where( r => r.Value.Score <= 0 ).Select( r => r.Key ).ToList() )
            {
                table.Rows.Remove( key );
            }
        }

        /// <summary>
        /// The procedure's score for one group connection, or null when SQL's would be null.
        /// </summary>
        private static decimal? Score( PeerChurch church, PeerRow row, Dictionary<int, bool> targetIsLeader, DateTime now )
        {
            if ( !church.Groups.TryGetValue( row.Key.RelatedEntityId ?? 0, out var group ) || !church.GroupTypes.TryGetValue( group.GroupTypeId, out var groupType ) )
            {
                return null;
            }

            var growthDate = row.EndDate ?? row.StartDate;
            var adjustment = row.EndDate == null ? 0.1m : -0.2m;
            var isGrowthEnabled = group.RelationshipGrowthEnabledOverride ?? groupType.RelationshipGrowthEnabled;
            var strength = group.RelationshipStrengthOverride ?? groupType.RelationshipStrength;
            bool? sourceIsLeader = church.Roles.TryGetValue( row.Key.ClassificationEntityId ?? 0, out var role ) ? role.IsLeader : null;
            bool? isTargetLeader = targetIsLeader.TryGetValue( row.Key.TargetPersonId, out var leads ) ? leads : null;
            if ( sourceIsLeader == null || isTargetLeader == null )
            {
                return null;
            }

            var multiplier = (sourceIsLeader.Value, isTargetLeader.Value) switch
            {
                (true, true) => group.LeaderToLeaderOverride ?? groupType.LeaderToLeader,
                (true, false) => group.LeaderToNonLeaderOverride ?? groupType.LeaderToNonLeader,
                (false, true) => group.NonLeaderToLeaderOverride ?? groupType.NonLeaderToLeader,
                _ => group.NonLeaderToNonLeaderOverride ?? groupType.NonLeaderToNonLeader
            };

            if ( isGrowthEnabled || adjustment < 0 )
            {
                var months = ( now.Year - growthDate.Year ) * 12 + now.Month - growthDate.Month;
                return ( strength + months * adjustment ) * multiplier;
            }

            return strength * multiplier;
        }
    }

    /// <summary>
    /// A PeerNetwork row's identity: its type, source and target people, and for a group connection, the group and the source's role.
    /// </summary>
    internal sealed record PeerKey( int RelationshipTypeValueId, int SourcePersonId, int TargetPersonId, int? RelatedEntityId, int? ClassificationEntityId );

    /// <summary>
    /// One PeerNetwork row.
    /// </summary>
    internal sealed class PeerRow
    {
        public PeerRow( PeerKey key )
        {
            Key = key;
        }

        public PeerKey Key { get; }

        public DateTime StartDate { get; set; }

        public DateTime? EndDate { get; set; }

        public decimal Score { get; set; }

        public decimal LastUpdateValue { get; set; }

        public int Trend { get; set; }

        public DateTime LastUpdateDateTime { get; set; }

        public string Caption { get; set; }
    }

    /// <summary>
    /// The PeerNetwork table as the job leaves it.
    /// </summary>
    internal sealed class PeerTable
    {
        public Dictionary<PeerKey, PeerRow> Rows { get; } = new Dictionary<PeerKey, PeerRow>();
    }

    internal sealed record PeerGroupType( int Id, bool IsPeerNetworkEnabled, int RelationshipStrength, bool RelationshipGrowthEnabled,
        decimal LeaderToLeader, decimal LeaderToNonLeader, decimal NonLeaderToLeader, decimal NonLeaderToNonLeader );

    internal sealed record PeerGroup( int Id, int GroupTypeId, string Name, bool IsActive, DateTime? InactiveDateTime, bool IsArchived, DateTime? ArchivedDateTime,
        int? RelationshipStrengthOverride, bool? RelationshipGrowthEnabledOverride, decimal? LeaderToLeaderOverride, decimal? LeaderToNonLeaderOverride,
        decimal? NonLeaderToLeaderOverride, decimal? NonLeaderToNonLeaderOverride );

    internal sealed record PeerRole( int Id, bool IsLeader, bool IsExcludedFromPeerNetwork );

    /// <summary>
    /// A group membership. <paramref name="Since"/> is when the row was made, for a run before install day.
    /// </summary>
    internal sealed record PeerMember( int GroupId, int PersonId, int RoleId, DateTime? DateTimeAdded, bool IsArchived, DateTime? ArchivedDateTime, int Status, DateTime? InactiveDateTime, DateTime? Since );

    internal sealed record PeerFollow( int FollowerPersonId, int FollowedPersonId, DateTime CreatedDateTime );

    /// <summary>
    /// What the job reads: group types, groups, roles, every membership, and the follows.
    /// </summary>
    internal sealed class PeerChurch
    {
        public int GroupConnectionTypeValueId { get; init; }

        public int FollowingTypeValueId { get; init; }

        public Dictionary<int, PeerGroupType> GroupTypes { get; init; } = new Dictionary<int, PeerGroupType>();

        public Dictionary<int, PeerGroup> Groups { get; init; } = new Dictionary<int, PeerGroup>();

        public Dictionary<int, PeerRole> Roles { get; init; } = new Dictionary<int, PeerRole>();

        /// <summary>
        /// Every membership of every group, for the pairs and for who leads a group.
        /// </summary>
        public List<PeerMember> Members { get; init; } = new List<PeerMember>();

        public List<PeerFollow> Follows { get; init; } = new List<PeerFollow>();

        /// <summary>
        /// The procedure's TargetIsLeader for each person in a group: true when they hold a leader role in any group.
        /// </summary>
        public Dictionary<int, bool> TargetIsLeader()
        {
            var leaders = new Dictionary<int, bool>();
            foreach ( var member in Members )
            {
                var isLeader = Roles.TryGetValue( member.RoleId, out var role ) && role.IsLeader;
                leaders[member.PersonId] = leaders.TryGetValue( member.PersonId, out var already ) ? already || isLeader : isLeader;
            }

            return leaders;
        }

        /*
            11/19/26 - CLAUDE

            The load writes groups as they stand on install day. A run a month earlier saw them as they stood then:
            no member who joined later, everyone who left later still active, and every group that closed later
            still open. Each date the load writes says when the change came, so the church as it stood then
            follows from them. A role a member changed in place is not dated, so it stays as it is now.

            Reason: The run before the last one scores what Rock saw that month, so each trend is Rock's.
        */

        /// <summary>
        /// The church as it stood at <paramref name="when"/>.
        /// </summary>
        public PeerChurch AsOf( DateTime when )
        {
            return new PeerChurch
            {
                GroupConnectionTypeValueId = GroupConnectionTypeValueId,
                FollowingTypeValueId = FollowingTypeValueId,
                GroupTypes = GroupTypes,
                Roles = Roles,
                Groups = Groups.Values.ToDictionary( g => g.Id, g => g with
                {
                    IsActive = g.IsActive || !( g.InactiveDateTime <= when ),
                    IsArchived = g.IsArchived && g.ArchivedDateTime <= when
                } ),
                Members = Members
                    .Where( m => !( m.Since > when ) )
                    .Select( m => m with
                    {
                        IsArchived = m.IsArchived && m.ArchivedDateTime <= when,
                        Status = m.Status == 0 && !( m.InactiveDateTime <= when ) ? 1 : m.Status
                    } )
                    .ToList(),
                Follows = Follows.Where( f => f.CreatedDateTime <= when ).ToList()
            };
        }
    }
}
