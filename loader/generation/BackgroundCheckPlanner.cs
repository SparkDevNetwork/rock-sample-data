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
    /// Who needs a background check and why: a member of a team that requires one, or a small group leader.
    /// </summary>
    internal enum CheckReason
    {
        Volunteer,
        Leader
    }

    /// <summary>
    /// One person's latest background check: a finished report with its result, or one still in progress.
    /// </summary>
    internal sealed class PlannedCheck
    {
        public LoadedPerson Person { get; set; }

        /// <summary>
        /// When the church asked for the check.
        /// </summary>
        public DateTime Requested { get; set; }

        /// <summary>
        /// The day the report came back, or null while it is in progress.
        /// </summary>
        public DateTime? Completed { get; set; }

        /// <summary>
        /// Pass or Fail once the report is back, otherwise null.
        /// </summary>
        public string Result { get; set; }

        /// <summary>
        /// The request to serve the check was run for, if any.
        /// </summary>
        public int? ConnectionRequestId { get; set; }

        public bool IsPass => Result == "Pass";
    }

    /// <summary>
    /// A group member a Background Check Required requirement applies to, read back from the loaded groups.
    /// </summary>
    internal sealed class RequirementMember
    {
        public int GroupMemberId { get; set; }

        public int GroupRequirementId { get; set; }

        public int PersonId { get; set; }

        public DateTime Joined { get; set; }

        public CheckReason Reason { get; set; }
    }

    /// <summary>
    /// A request to serve that is waiting in the Background Check status.
    /// </summary>
    internal sealed class CheckRequest
    {
        public int ConnectionRequestId { get; set; }

        public int PersonId { get; set; }

        public DateTime Created { get; set; }

        /// <summary>
        /// The day the request reached the Background Check status.
        /// </summary>
        public DateTime StatusSince { get; set; }
    }

    /// <summary>
    /// A requirement result as Rock's Calculate Group Requirements job leaves it: the state and the dates it set.
    /// </summary>
    internal sealed class RequirementResult
    {
        /// <summary>
        /// Rock's MeetsGroupRequirement: 0 Meets, 1 Not Met, 2 Meets With Warning.
        /// </summary>
        public int State { get; set; }

        public DateTime? Met { get; set; }

        public DateTime? Warning { get; set; }

        public DateTime? Failed { get; set; }

        /// <summary>
        /// The first night the job set this state.
        /// </summary>
        public DateTime Since => Failed ?? Warning ?? Met.Value;
    }

    /*
        11/19/26 - CLAUDE

        One planner owns every background check, so a person has one latest check whichever roles they hold, and the
        person attributes, the check row, the report, and every requirement result agree. A Cast story comes first, then
        the strictest role: a volunteer on a team with the requirement passed a check in the last few years (decision
        73), and a small group leader's check is current, expiring, expired, or missing by the designed mix. Under 18s
        meet Rock's rule by age, so they have none. A person waiting in the Background Check status has a check in
        progress, unless their story already has its result.

        Reason: Decision 333. Rock's job, its scheduler, and the reports read the same facts.
    */

    /// <summary>
    /// Plans each person's latest background check, and computes the requirement results Rock's job would save.
    /// </summary>
    internal sealed class BackgroundCheckPlanner
    {
        public const string Pass = "Pass";
        public const string Fail = "Fail";

        private readonly BackgroundChecksConfiguration _config;
        private readonly AnchorDates _dates;
        private readonly string _seed;

        public BackgroundCheckPlanner( BackgroundChecksConfiguration config, AnchorDates dates, string seed )
        {
            _config = config;
            _dates = dates;
            _seed = seed;
        }

        /// <summary>
        /// Plans every check, by person Id.
        /// </summary>
        /// <param name="people">Every loaded person, by person Id.</param>
        /// <param name="castPersonGuids">The Cast's people. Their checks follow their stories, not the leader mix.</param>
        public Dictionary<int, PlannedCheck> Plan( IReadOnlyDictionary<int, LoadedPerson> people, ISet<Guid> castPersonGuids, IReadOnlyList<RequirementMember> members, IReadOnlyList<CheckRequest> requests )
        {
            var withoutCheck = _config.CastWithoutCheck.Select( c => c.Person ).ToHashSet();
            var reasons = members.GroupBy( m => m.PersonId ).ToDictionary( g => g.Key, g => g.Min( m => m.Reason ) );
            var checks = new Dictionary<int, PlannedCheck>();

            foreach ( var person in people.Values.OrderBy( p => p.PersonId ) )
            {
                if ( withoutCheck.Contains( person.Cast.Guid ) )
                {
                    continue;
                }

                var story = person.Cast.Attributes;
                var storyDate = _dates.Resolve( story?.BackgroundCheckDate );
                if ( storyDate.HasValue )
                {
                    checks[person.PersonId] = Completed( person, storyDate.Value, story.BackgroundCheckResult ?? Pass );
                    continue;
                }

                if ( !reasons.TryGetValue( person.PersonId, out var reason ) || IsMinor( person ) )
                {
                    continue;
                }

                var daysAgo = reason == CheckReason.Volunteer
                    ? Draw( $"volunteer:{person.Cast.Guid}", _config.Volunteers.CheckedDaysAgo )
                    : LeaderDaysAgo( person, castPersonGuids.Contains( person.Cast.Guid ) );
                if ( daysAgo.HasValue )
                {
                    checks[person.PersonId] = Completed( person, _dates.Anchor.AddDays( -daysAgo.Value ), Pass );
                }
            }

            foreach ( var request in requests.OrderBy( r => r.ConnectionRequestId ) )
            {
                var person = people[request.PersonId];
                if ( checks.TryGetValue( request.PersonId, out var existing ) )
                {
                    // A story's result came back for this request: link it.
                    if ( existing.Completed >= request.Created && existing.ConnectionRequestId == null )
                    {
                        existing.ConnectionRequestId = request.ConnectionRequestId;
                    }

                    continue;
                }

                if ( withoutCheck.Contains( person.Cast.Guid ) || IsMinor( person ) )
                {
                    continue;
                }

                var random = new SeededRandom( $"{_seed}:background-checks:request:{person.Cast.Guid}" );
                var requested = request.StatusSince.AddDays( random.Between( _config.Requests.RequestedDaysAfterStatus[0], _config.Requests.RequestedDaysAfterStatus[1] ) );
                if ( requested >= _dates.Anchor )
                {
                    requested = _dates.Anchor.AddDays( -1 );
                }

                checks[request.PersonId] = new PlannedCheck
                {
                    Person = person,
                    Requested = requested.Add( OfficeHours( random ) ),
                    ConnectionRequestId = request.ConnectionRequestId
                };
            }

            return checks;
        }

        /// <summary>
        /// What Rock's Calculate Group Requirements job leaves for one member: Meets when Rock's "still valid" view holds
        /// them (under 18, or a Pass in the last 1,095 days), with a warning when its "about to expire" view does too (an
        /// adult's Pass older than 1,035 days), and Not Met otherwise, since the requirement is due at once. Each date is
        /// the first night the state held after the member joined.
        /// </summary>
        public RequirementResult Result( RequirementMember member, LoadedPerson person, PlannedCheck check )
        {
            var anchor = _dates.Anchor;
            var joined = member.Joined.Date;
            var isMinor = IsMinor( person );
            var passed = check?.Completed != null && check.IsPass ? check.Completed.Value : ( DateTime? ) null;
            var isValid = passed.HasValue && passed.Value >= anchor.AddDays( -_config.Rules.ValidDays );
            var isWarned = !isMinor && passed.HasValue && passed.Value < anchor.AddDays( -_config.Rules.WarningAfterDays );

            if ( isMinor || isValid )
            {
                var met = Latest( joined, isValid ? passed.Value : joined );
                return isValid && isWarned
                    ? new RequirementResult { State = 2, Met = met, Warning = Latest( met, passed.Value.AddDays( _config.Rules.WarningAfterDays + 1 ) ) }
                    : new RequirementResult { State = 0, Met = met };
            }

            var failedSince = passed.HasValue ? passed.Value.AddDays( _config.Rules.ValidDays + 1 )
                : check?.Completed != null ? check.Completed.Value
                : joined;
            return new RequirementResult { State = 1, Failed = Latest( joined, failedSince ) };
        }

        private bool IsMinor( LoadedPerson person ) => person.BirthDate.HasValue && _dates.AgeOn( person.BirthDate.Value ) < 18;

        /// <summary>
        /// A Cast leader's check is current. A generated leader's follows the mix: current, expiring, expired, or none.
        /// </summary>
        private int? LeaderDaysAgo( LoadedPerson person, bool isCast )
        {
            var random = new SeededRandom( $"{_seed}:background-checks:leader:{person.Cast.Guid}" );
            var state = isCast ? "current" : random.Pick( _config.Leaders.Mix );
            return _config.Leaders.DaysAgo.TryGetValue( state, out var range ) ? random.Between( range[0], range[1] ) : ( int? ) null;
        }

        private int Draw( string key, int[] range ) => new SeededRandom( $"{_seed}:background-checks:{key}" ).Between( range[0], range[1] );

        private PlannedCheck Completed( LoadedPerson person, DateTime completed, string result )
        {
            var random = new SeededRandom( $"{_seed}:background-checks:requested:{person.Cast.Guid}" );
            var days = _config.Checkr.RequestedDaysBeforeResult;
            return new PlannedCheck
            {
                Person = person,
                Completed = completed.Date,
                Result = result,
                Requested = completed.Date.AddDays( -random.Between( days[0], days[1] ) ).Add( OfficeHours( random ) )
            };
        }

        /// <summary>
        /// A time in the church office's day, when staff ask for checks.
        /// </summary>
        private static TimeSpan OfficeHours( SeededRandom random ) => TimeSpan.FromMinutes( random.Between( 9 * 60, ( 16 * 60 ) + 30 ) );

        private static DateTime Latest( DateTime a, DateTime b ) => a > b ? a : b;
    }
}
