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

using RockSampleData.Loader.Model;

namespace RockSampleData.Loader.Generation
{
    /// <summary>
    /// Plans small groups for generated households at load time, campus by campus.
    /// </summary>
    internal static class GeneratedSmallGroups
    {
        /// <summary>
        /// Plans every campus's generated groups and marks their leaders with the leader status.
        /// Active households join at the profile's rate. Every campus but the main one gets its own
        /// test cases; the Cast's groups already cover the main campus.
        /// </summary>
        public static List<SmallGroupPlan> Plan( SmallGroupRules rules, ProfileTargets profile, DateTime anchor, string seed, IReadOnlyList<CastHousehold> generated,
            IEnumerable<string> campusKeys, string mainCampusKey, IReadOnlyList<SmallGroupPlan> castPlans, IReadOnlyList<CastHousehold> cast )
        {
            var planner = new SmallGroupPlanner( rules, PlanningClock.ForLoad( anchor ), seed );
            var joinPercent = profile.Number( rules.Participation.GeneratedProfilePath, 0 );
            var castPeople = cast.SelectMany( h => h.Members ).ToDictionary( p => p.Guid );
            var castLeaders = castPlans.SelectMany( p => p.Members.Where( m => m.Role == SmallGroupRole.Leader ) )
                .Select( m => castPeople[m.Guid] )
                .Select( p => new LeaderName( p.NickName ?? p.FirstName, p.LastName ) )
                .ToList();

            var plans = new List<SmallGroupPlan>();
            foreach ( var campusKey in campusKeys )
            {
                var isMain = campusKey == mainCampusKey;
                plans.AddRange( planner.PlanCampus( new CampusPlanRequest
                {
                    Campus = campusKey,
                    KeyPrefix = $"{campusKey}-",
                    Households = generated.Where( h => h.CampusSlot == campusKey ).ToList(),
                    JoinPercent = ( household, _ ) => IsActiveNow( household ) ? joinPercent : 0,
                    TestCases = isMain ? TestCaseCounts.None : rules.TestCases.EachOtherCampus,
                    TakenLeaderNames = isMain ? castLeaders : new List<LeaderName>()
                } ) );
            }

            var leaders = plans.Where( p => p.Lifecycle is ActiveLifecycle )
                .SelectMany( p => p.Members.Where( m => m.Role == SmallGroupRole.Leader ) )
                .Select( m => m.Guid )
                .ToHashSet();
            foreach ( var person in generated.SelectMany( h => h.Members ).Where( p => leaders.Contains( p.Guid ) ) )
            {
                person.ConnectionStatus = rules.Leaders.BecomeStatus;
            }

            return plans;
        }

        /// <summary>
        /// A household that still attends or still gives on a rhythm. A one-time gift is usually
        /// older than a year, so a household that only gave once is not counted as active.
        /// </summary>
        private static bool IsActiveNow( CastHousehold household )
        {
            var attends = household.Attendance != null && household.Attendance.PercentAttended > 0 && household.Attendance.Ended == null;
            return attends || household.Giving.Any( g => g.Ended == null && g.Persona != "one-time" );
        }
    }
}
