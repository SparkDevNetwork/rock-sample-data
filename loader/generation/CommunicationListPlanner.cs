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
    /*
        10/23/26 - CLAUDE

        Who is on each communication list follows how involved they are (decision 133). Involved
        adults are on their campus newsletter, and only a third of the rest are, so the Saturation
        report shows adults who get nothing. Men and Women are opt-in, and Leading and Engaged adults
        opt in far more than Known adults. Volunteers, Prayer Team, and Small Group Leaders copy the
        teams, the prayer team, and the groups, so the busiest people are on the most lists. Nobody who
        chose "No mass emails" or "Do not email" is on a list. Each person draws from their own seeded
        stream, so adding a list or a person never moves anyone else.

        Reason: The Saturation report shows a real spread of messages per person.
    */

    /// <summary>
    /// Plans the members of every communication list.
    /// </summary>
    internal static class CommunicationListPlanner
    {
        public const string Newsletter = "newsletter";
        public const string Men = "men";
        public const string Women = "women";
        public const string ParentsOfKids = "parents-of-kids";
        public const string Students = "students";
        public const string Volunteers = "volunteers";
        public const string PrayerTeam = "prayer-team";
        public const string SmallGroupLeaders = "small-group-leaders";

        private static readonly HashSet<string> Rules = new HashSet<string> { Newsletter, Men, Women, ParentsOfKids, Students, Volunteers, PrayerTeam, SmallGroupLeaders };

        /// <summary>
        /// Plans every list's members, in list order and then person order.
        /// </summary>
        /// <param name="lists">The lists in configuration/communications.json.</param>
        /// <param name="rules">The membership rules.</param>
        /// <param name="people">Everyone who could be on a list.</param>
        /// <param name="campusKeys">The dataset's campuses. A list per campus is planned for each.</param>
        /// <param name="anchor">The install day.</param>
        /// <param name="seed">The load's seed.</param>
        public static List<PlannedListMember> Plan( IReadOnlyList<CommunicationListDefinition> lists, ListMembershipRules rules, IReadOnlyList<ListCandidate> people, IReadOnlyCollection<string> campusKeys, DateTime anchor, string seed )
        {
            var unknown = lists.Select( l => l.Members ).FirstOrDefault( m => !Rules.Contains( m ) );
            if ( unknown != null )
            {
                throw new InvalidDataException( $"configuration/communications.json has a list with members \"{unknown}\", which is not a membership rule." );
            }

            var members = new List<PlannedListMember>();
            foreach ( var list in lists )
            {
                foreach ( var person in people.OrderBy( p => p.PersonId ) )
                {
                    if ( !person.MayGetMassMessages )
                    {
                        continue;
                    }

                    var random = new SeededRandom( $"{seed}:communication-lists:{list.Key}:{person.Guid}" );
                    if ( !IsMember( list.Members, person, rules, random ) )
                    {
                        continue;
                    }

                    var campusKey = list.Campus ? person.CampusKey : null;
                    if ( list.Campus && !campusKeys.Contains( campusKey ) )
                    {
                        continue;
                    }

                    members.Add( new PlannedListMember
                    {
                        ListKey = list.Key,
                        CampusKey = campusKey,
                        PersonId = person.PersonId,
                        Joined = Joined( list.Members, person, rules, random, anchor ),
                        IsSms = list.Preference == "sms" && person.CanText
                    } );
                }
            }

            return members;
        }

        /// <summary>
        /// Whether the person belongs on a list with this membership rule.
        /// </summary>
        private static bool IsMember( string rule, ListCandidate person, ListMembershipRules rules, SeededRandom random )
        {
            switch ( rule )
            {
                case Newsletter:
                    return person.IsAdult && person.HasActiveEmail && ( person.IsInvolved || random.Chance( rules.OtherAdultsPercent / 100 ) );

                case Men:
                case Women:
                    var gender = rule == Men ? "male" : "female";
                    var percent = rules.OptInPercentByStatus.GetValueOrDefault( rule )?.GetValueOrDefault( person.ConnectionStatus ?? string.Empty ) ?? 0;
                    return person.IsAdult && person.Gender == gender && person.HasActiveEmail && random.Chance( percent / 100 );

                case ParentsOfKids:
                    return person.IsAdult && person.HasKid && person.HasActiveEmail && ( person.FamilyCameLastYear || random.Chance( rules.OtherParentsPercent / 100 ) );

                case Students:
                    return !person.IsAdult && person.Grade >= rules.StudentGrades.Min && person.Grade <= rules.StudentGrades.Max
                        && person.FamilyCameLastYear && ( person.HasActiveEmail || person.CanText );

                case Volunteers:
                    return person.ServingSince.HasValue;

                case PrayerTeam:
                    return person.IsPrayerTeam;

                default:
                    return person.LeadingSince.HasValue;
            }
        }

        /// <summary>
        /// When the person joined the list: when they joined the team or group a staff list copies, or a
        /// few years back, or soon after their household's first Sunday when that came later.
        /// </summary>
        private static DateTime Joined( string rule, ListCandidate person, ListMembershipRules rules, SeededRandom random, DateTime anchor )
        {
            var copied = rule == Volunteers ? person.ServingSince : rule == SmallGroupLeaders ? person.LeadingSince : null;
            if ( copied.HasValue )
            {
                return copied.Value;
            }

            var earliestRecent = anchor.AddYears( -rules.JoinedYearsAgo.Min );
            DateTime joined;
            if ( person.FirstSunday.HasValue && person.FirstSunday.Value > earliestRecent )
            {
                joined = person.FirstSunday.Value.AddDays( random.Between( rules.JoinedDaysAfterFirstVisit.Min, rules.JoinedDaysAfterFirstVisit.Max ) );
            }
            else
            {
                var earliest = anchor.AddYears( -rules.JoinedYearsAgo.Max );
                joined = earliest.AddDays( random.Between( 0, ( int ) ( earliestRecent - earliest ).TotalDays ) );
            }

            // Morning office hours, and never on or after install day.
            joined = joined.Date.AddHours( random.Between( 8, 16 ) ).AddMinutes( random.Between( 0, 59 ) );
            return joined < anchor.Date ? joined : anchor.Date.AddDays( -1 ).AddHours( 9 );
        }
    }

    /// <summary>
    /// What the list planner needs to know about one person.
    /// </summary>
    internal sealed class ListCandidate
    {
        public int PersonId { get; set; }

        public Guid Guid { get; set; }

        public string CampusKey { get; set; }

        public bool IsAdult { get; set; }

        /// <summary>
        /// The school grade on install day, or null.
        /// </summary>
        public int? Grade { get; set; }

        public string Gender { get; set; }

        public string ConnectionStatus { get; set; }

        /// <summary>
        /// The person has an email that is active.
        /// </summary>
        public bool HasActiveEmail { get; set; }

        /// <summary>
        /// False when the person chose "No mass emails" or "Do not email". Such a person is on no list.
        /// </summary>
        public bool MayGetMassMessages { get; set; }

        /// <summary>
        /// The person has a mobile phone that can text.
        /// </summary>
        public bool CanText { get; set; }

        /// <summary>
        /// The person attended, gave, or was in a small group in the last year.
        /// </summary>
        public bool IsInvolved { get; set; }

        public bool FamilyCameLastYear { get; set; }

        /// <summary>
        /// The person is a parent in a family with a child from birth to the kids ministry's last grade.
        /// </summary>
        public bool HasKid { get; set; }

        /// <summary>
        /// The household's first Sunday in the attendance history, or null.
        /// </summary>
        public DateTime? FirstSunday { get; set; }

        /// <summary>
        /// When the person first joined a serving team they are still active on, or null.
        /// </summary>
        public DateTime? ServingSince { get; set; }

        /// <summary>
        /// When the person first became a leader of an active small group they still lead, or null.
        /// </summary>
        public DateTime? LeadingSince { get; set; }

        public bool IsPrayerTeam { get; set; }
    }

    /// <summary>
    /// One member of one communication list.
    /// </summary>
    internal sealed class PlannedListMember
    {
        public string ListKey { get; set; }

        /// <summary>
        /// The campus of a list per campus, or null.
        /// </summary>
        public string CampusKey { get; set; }

        public int PersonId { get; set; }

        public DateTime Joined { get; set; }

        /// <summary>
        /// True when the member gets the list's messages by text.
        /// </summary>
        public bool IsSms { get; set; }
    }
}
