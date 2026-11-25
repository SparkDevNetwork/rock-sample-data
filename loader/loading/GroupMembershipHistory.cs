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

namespace RockSampleData.Loader.Loading
{
    /*
        11/24/26 - CLAUDE

        Rock keeps a history of each group member, and of each group, when its group type turns on group history, as the
        church's small group and serving team types do. Each row holds the member's role, status, and archived flag from
        one moment until the next change. So who was in a group at a past moment reads straight back from it. A group
        with no history rows, such as a serving team, has never changed, so its own row answers.

        Reason: The Ministry by Design metrics on small groups and serving teams count each past week's members.
    */

    /// <summary>
    /// Who was an active member of a group type's groups at a past moment, from Rock's group member and group history.
    /// </summary>
    internal sealed class GroupMembershipHistory
    {
        private readonly RockDatabase _db;
        private readonly Dictionary<string, List<(int Person, int Group, DateTime From, DateTime To)>> _spans = new Dictionary<string, List<(int, int, DateTime, DateTime)>>();
        private readonly Dictionary<int, Dictionary<int, List<(DateTime From, DateTime To, bool IsActive, bool IsArchived)>>> _groupHistory = new Dictionary<int, Dictionary<int, List<(DateTime, DateTime, bool, bool)>>>();
        private readonly Dictionary<int, Dictionary<int, (bool IsActive, bool IsArchived)>> _groups = new Dictionary<int, Dictionary<int, (bool, bool)>>();

        public GroupMembershipHistory( RockDatabase db )
        {
            _db = db;
        }

        /// <summary>
        /// The people who were members of the group type's groups at the moment, as Rock's in-group-type data filter reads
        /// them today: not archived, in one of the roles and the status when given, in a group that was not archived, and
        /// active or not when that is given.
        /// </summary>
        /// <param name="roles">Group type role Ids, or none for any role.</param>
        /// <param name="status">The member status, or null for any.</param>
        /// <param name="groupIsActive">Whether the group must be active or inactive, or null for either.</param>
        public HashSet<int> MembersAt( int groupTypeId, IReadOnlyCollection<int> roles, int? status, bool? groupIsActive, DateTime at )
        {
            var key = $"{groupTypeId}|{string.Join( ",", roles.OrderBy( r => r ) )}|{status}";
            if ( !_spans.TryGetValue( key, out var spans ) )
            {
                spans = _db.Query( $@"
SELECT [gm].[PersonId], [h].[GroupId], [h].[EffectiveDateTime], [h].[ExpireDateTime]
FROM [GroupMemberHistorical] AS [h]
INNER JOIN [GroupMember] AS [gm] ON [gm].[Id] = [h].[GroupMemberId]
INNER JOIN [Group] AS [g] ON [g].[Id] = [h].[GroupId]
WHERE [g].[GroupTypeId] = {groupTypeId} AND [h].[IsArchived] = 0
    {( roles.Count == 0 ? string.Empty : $"AND [h].[GroupRoleId] IN ( {string.Join( ", ", roles )} )" )}
    {( status == null ? string.Empty : $"AND [h].[GroupMemberStatus] = {status}" )}" )
                    .Select( r => (( int ) r["PersonId"], ( int ) r["GroupId"], ( DateTime ) r["EffectiveDateTime"], ( DateTime ) r["ExpireDateTime"]) )
                    .ToList();
                _spans[key] = spans;
            }

            if ( !_groups.ContainsKey( groupTypeId ) )
            {
                _groups[groupTypeId] = _db.Query( $"SELECT [Id], [IsActive], [IsArchived] FROM [Group] WHERE [GroupTypeId] = {groupTypeId}" )
                    .ToDictionary( r => ( int ) r["Id"], r => (( bool ) r["IsActive"], ( bool ) r["IsArchived"]) );
                _groupHistory[groupTypeId] = _db.Query( $@"
SELECT [gh].[GroupId], [gh].[EffectiveDateTime], [gh].[ExpireDateTime], [gh].[IsActive], [gh].[IsArchived]
FROM [GroupHistorical] AS [gh]
INNER JOIN [Group] AS [g] ON [g].[Id] = [gh].[GroupId]
WHERE [g].[GroupTypeId] = {groupTypeId}" )
                    .GroupBy( r => ( int ) r["GroupId"] )
                    .ToDictionary( g => g.Key, g => g.Select( r => (( DateTime ) r["EffectiveDateTime"], ( DateTime ) r["ExpireDateTime"], ( bool ) r["IsActive"], ( bool ) r["IsArchived"]) ).ToList() );
            }

            var groups = _groups[groupTypeId];
            var groupHistory = _groupHistory[groupTypeId];
            bool GroupCounts( int group )
            {
                var (isActive, isArchived) = groupHistory.TryGetValue( group, out var rows )
                    ? rows.Where( r => r.From <= at && at < r.To ).Select( r => (r.IsActive, r.IsArchived) ).DefaultIfEmpty( (false, true) ).First()
                    : groups[group];
                return !isArchived && ( groupIsActive == null || isActive == groupIsActive );
            }

            return spans.Where( s => s.From <= at && at < s.To && GroupCounts( s.Group ) ).Select( s => s.Person ).ToHashSet();
        }
    }
}
