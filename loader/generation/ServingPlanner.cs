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

using RockSampleData.Loader.Loading;
using RockSampleData.Loader.Model;

namespace RockSampleData.Loader.Generation
{
    /// <summary>
    /// Plans who serves on every team at every campus, before people load so new team leaders load
    /// as Leading. The Cast's main campus rosters come from cast/groups/serving-*.json as written.
    /// Generated households serve at the profile's share of active adults, chosen from adults whose
    /// status counts as serving. Every choice is seeded by the person, so a volunteer's plan is the
    /// same on every install.
    /// </summary>
    internal static class ServingPlanner
    {
        private const string LeaderRotation = "Every Week";

        public static List<ServingTeamPlan> Plan( ServingConfiguration config, ProfileTargets profile, AnchorDates dates, string seed, IReadOnlyList<CastHousehold> households,
            HashSet<string> castHouseholdKeys, Dictionary<string, CastServingTeam> castTeams, Dictionary<string, CampusPlan> campuses, string mainCampusKey )
        {
            var servePercent = profile.Number( config.Volunteers.GeneratedProfilePath, 0 );
            var leaderPercent = profile.Number( config.Volunteers.LeadersProfilePath, 0 );
            var joinedMonths = profile.Map( config.Volunteers.JoinedMonthIndexProfilePath ).ToDictionary( kv => int.Parse( kv.Key ), kv => kv.Value );
            var castPeople = households.Where( h => castHouseholdKeys.Contains( h.Key ) )
                .SelectMany( h => h.Members.Select( p => (Person: p, Household: h) ) )
                .ToDictionary( x => x.Person.Guid );
            var plans = new List<ServingTeamPlan>();

            foreach ( var campus in campuses.Values )
            {
                var teams = config.Teams.ToDictionary( t => t.Key, t => new ServingTeamPlan( campus.Key, t.Key, new List<ServingMemberPlan>() ) );
                if ( campus.Key == mainCampusKey )
                {
                    foreach ( var castTeam in castTeams.Values )
                    {
                        foreach ( var member in castTeam.Members )
                        {
                            var (person, household) = castPeople[member.Guid];
                            teams[castTeam.Team].Members.Add( PlanMember( config, dates, seed, campus, castTeam.Team, person, household, member.Role == "Leader", joinedMonths ) );
                        }
                    }
                }

                var generated = households.Where( h => h.CampusSlot == campus.Key && !castHouseholdKeys.Contains( h.Key ) ).ToList();
                AssignGenerated( config, dates, seed, campus, generated, teams, servePercent, leaderPercent, joinedMonths );
                plans.AddRange( teams.Values.Where( t => t.Members.Count > 0 ) );
            }

            return plans;
        }

        /// <summary>
        /// Chooses the campus's generated volunteers and spreads them over the teams by need. Each team
        /// first gets its minimum size, then the rest go where the most people are needed. A team
        /// without a leader takes the volunteer whose family attends most, who becomes Leading.
        /// </summary>
        private static void AssignGenerated( ServingConfiguration config, AnchorDates dates, string seed, CampusPlan campus, List<CastHousehold> households,
            Dictionary<string, ServingTeamPlan> teams, double servePercent, double leaderPercent, Dictionary<int, double> joinedMonths )
        {
            var rules = config.Volunteers;
            var random = new SeededRandom( $"{seed}:serving:{campus.Key}" );
            // Active adults are counted the way calibration counts them: in households that attend or give.
            // Volunteers come from households that attend.
            var active = households.Where( IsActiveNow ).ToList();
            var activeAdults = households.Where( h => IsActiveNow( h ) || h.Giving.Any( g => g.Ended == null && g.Persona != "one-time" ) ).SelectMany( h => h.Members.Where( p => IsAdult( p, rules.MinimumAge ) ) ).Count();
            List<(CastPerson Person, CastHousehold Household)> WithStatus( string status ) => active
                .SelectMany( h => h.Members.Where( p => IsAdult( p, rules.MinimumAge ) && p.ConnectionStatus == status ).Select( p => (Person: p, Household: h) ) )
                .OrderBy( x => new SeededRandom( $"{seed}:serving:order:{x.Person.Guid}" ).Between( 0.0, 1.0 ) )
                .ToList();

            // Engaged and Leading adults first, then Connected and Known adults when the campus needs them to reach
            // the measured share or the teams' minimum sizes. Serving makes them Engaged.
            var primary = rules.Statuses.SelectMany( WithStatus ).OrderBy( x => new SeededRandom( $"{seed}:serving:order:{x.Person.Guid}" ).Between( 0.0, 1.0 ) ).ToList();
            var candidates = primary.Concat( rules.FallbackStatuses.SelectMany( WithStatus ) ).ToList();
            var measured = Math.Min( candidates.Count, ( int ) Math.Round( activeAdults * servePercent / 100.0 ) );
            var target = Math.Min( candidates.Count, Math.Max( measured, config.Teams.Count * rules.MinimumTeamSize ) );
            if ( target == 0 )
            {
                return;
            }

            var need = config.Teams.ToDictionary( t => t.Key, t => t.Positions.Sum( p => p.Capacity[1] ) * ( double ) ServingConfigurationLoader.TeamServices( t, campus ).Count );
            var assigned = new HashSet<Guid>();
            var queue = new Queue<(CastPerson Person, CastHousehold Household)>( candidates );

            void Add( ServingTeamDefinition team, CastPerson person, CastHousehold household )
            {
                teams[team.Key].Members.Add( PlanMember( config, dates, seed, campus, team.Key, person, household, false, joinedMonths ) );
                assigned.Add( person.Guid );
                if ( rules.FallbackStatuses.Contains( person.ConnectionStatus ) )
                {
                    person.ConnectionStatus = rules.BecomeMemberStatus;
                }
            }

            void Assign( ServingTeamDefinition team, CastPerson person, CastHousehold household, bool spouseMayJoin )
            {
                Add( team, person, household );
                if ( !spouseMayJoin )
                {
                    return;
                }

                // Couples often serve together.
                var spouse = household.Members.FirstOrDefault( p => p.Guid != person.Guid && candidates.Any( c => c.Person.Guid == p.Guid ) && !assigned.Contains( p.Guid ) && Fits( team, p, rules ) );
                if ( spouse != null && assigned.Count < target && random.Chance( rules.SpouseSameTeamPercent / 100.0 ) )
                {
                    Add( team, spouse, household );
                }
            }

            (CastPerson Person, CastHousehold Household)? NextFor( ServingTeamDefinition team )
            {
                for ( var i = 0; i < queue.Count; i++ )
                {
                    var next = queue.Dequeue();
                    if ( !assigned.Contains( next.Person.Guid ) && Fits( team, next.Person, rules ) )
                    {
                        return next;
                    }

                    if ( !assigned.Contains( next.Person.Guid ) )
                    {
                        queue.Enqueue( next );
                    }
                }

                return null;
            }

            foreach ( var team in config.Teams )
            {
                while ( teams[team.Key].Members.Count < rules.MinimumTeamSize && assigned.Count < target && NextFor( team ) is { } pick )
                {
                    Assign( team, pick.Person, pick.Household, spouseMayJoin: false );
                }
            }

            while ( assigned.Count < target && queue.Count > 0 )
            {
                var weights = config.Teams.ToDictionary( t => t.Key, t => Math.Max( 0.1, need[t.Key] - teams[t.Key].Members.Count ) );
                var teamKey = random.Pick( weights );
                var team = config.Teams.Single( t => t.Key == teamKey );
                if ( NextFor( team ) is not { } pick )
                {
                    need[team.Key] = 0;
                    if ( need.Values.All( v => v <= 0 ) )
                    {
                        break;
                    }

                    continue;
                }

                Assign( team, pick.Person, pick.Household, spouseMayJoin: true );
            }

            // Every team has a leader, and a large team has co-leaders at the measured share of leaders,
            // the way a big team has a coordinator for each service. Leaders come from the generated
            // volunteers whose families attend most; a Cast team keeps its Cast leader.
            var generatedPeople = households.SelectMany( h => h.Members.Select( p => (Person: p, Household: h) ) ).ToDictionary( x => x.Person.Guid );
            foreach ( var plan in teams.Values.Where( t => t.Members.Count > 0 ) )
            {
                var wanted = Math.Max( 1, ( int ) Math.Round( plan.Members.Count * leaderPercent / 100.0 ) );
                var promote = plan.Members
                    .Where( m => !m.IsLeader && generatedPeople.ContainsKey( m.Person ) )
                    .OrderByDescending( m => generatedPeople[m.Person].Household.Attendance?.PercentAttended ?? 0 )
                    .Take( wanted - plan.Members.Count( m => m.IsLeader ) )
                    .ToList();
                foreach ( var leader in promote )
                {
                    plan.Members[plan.Members.IndexOf( leader )] = leader with { IsLeader = true, Rotation = LeaderRotation };
                    generatedPeople[leader.Person].Person.ConnectionStatus = rules.BecomeLeaderStatus;
                }
            }
        }

        /// <summary>
        /// One volunteer's rotation, join date, and preferred service and room, seeded by the person.
        /// Leaders serve every week and have served longer. Nobody joined in the last month; new
        /// volunteers arrive through connection requests.
        /// </summary>
        private static ServingMemberPlan PlanMember( ServingConfiguration config, AnchorDates dates, string seed, CampusPlan campus, string teamKey, CastPerson person, CastHousehold household, bool isLeader, Dictionary<int, double> joinedMonths )
        {
            var rules = config.Volunteers;
            var team = config.Teams.Single( t => t.Key == teamKey );
            var random = new SeededRandom( $"{seed}:serving:{campus.Key}:{teamKey}:{person.Guid}" );
            var rotation = isLeader ? LeaderRotation : random.Pick( rules.Rotations );
            // A recent volunteer joined through a request, so recent join dates follow the requests' month pattern.
            var busiest = joinedMonths.Count > 0 ? joinedMonths.Values.Max() : 1.0;
            DateTime joined;
            do
            {
                var yearsAgo = random.Between( isLeader ? Math.Max( 2, rules.JoinedYearsAgo.Min ) : rules.JoinedYearsAgo.Min, rules.JoinedYearsAgo.Max );
                joined = dates.Anchor.AddDays( -Math.Round( yearsAgo * 365.25 ) );
            }
            while ( joined > dates.Anchor.AddDays( -rules.JoinedMonthIndexDays ) && joinedMonths.Count > 0 && !random.Chance( joinedMonths.GetValueOrDefault( joined.Month, busiest ) / busiest ) );
            var started = dates.Resolve( household.Attendance?.Started );
            if ( started.HasValue && joined < started.Value.AddDays( 30 ) )
            {
                joined = started.Value.AddDays( 30 );
            }

            if ( joined > dates.Anchor.AddDays( -30 ) )
            {
                joined = dates.Anchor.AddDays( -30 );
            }

            var serviceCount = ServingConfigurationLoader.TeamServices( team, campus ).Count;
            int? service = random.Chance( rules.PreferredServicePercent / 100.0 ) ? random.Between( 0, serviceCount - 1 ) : null;
            int? position = team.Positions.Count > 1 && random.Chance( rules.PreferredPositionPercent / 100.0 ) ? random.Between( 0, team.Positions.Count - 1 ) : null;
            return new ServingMemberPlan( person.Guid, isLeader, rotation, joined.Date, service, position );
        }

        private static bool Fits( ServingTeamDefinition team, CastPerson person, VolunteerRules rules ) => IsAdult( person, team.MinimumAge ?? rules.MinimumAge );

        private static bool IsAdult( CastPerson person, int minimumAge ) =>
            person.Birth?.Age >= minimumAge && string.Equals( person.RecordStatus, "active", StringComparison.OrdinalIgnoreCase ) && person.RecordType != "business";

        /// <summary>
        /// A household that still attends, the same rule generated small groups use.
        /// </summary>
        private static bool IsActiveNow( CastHousehold household ) => household.Attendance != null && household.Attendance.PercentAttended > 0 && household.Attendance.Ended == null;
    }

    /// <summary>
    /// One team's volunteers at one campus.
    /// </summary>
    internal sealed record ServingTeamPlan( string Campus, string Team, List<ServingMemberPlan> Members );

    /// <summary>
    /// One volunteer: their rotation (a Rock schedule template name), when they joined, and the index
    /// of their preferred service and room in the team's lists, when they have one.
    /// </summary>
    internal sealed record ServingMemberPlan( Guid Person, bool IsLeader, string Rotation, DateTime Joined, int? PreferredService, int? PreferredPosition );
}
