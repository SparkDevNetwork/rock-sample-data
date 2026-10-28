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

using System.Text;

using RockSampleData.Loader.Loading;
using RockSampleData.Loader.Model;

namespace RockSampleData.Loader.Generation
{
    /// <summary>
    /// Plans who has a Rock Database login, with what username, and when it was made and last used
    /// (decisions 179 to 181). The Cast's logins come from their files. A generated adult gets one
    /// with the chance of their strongest reason in configuration/logins.json. Each person draws from their own
    /// seeded stream, so logins never change any other row.
    /// </summary>
    internal sealed class LoginPlanner
    {
        private readonly LoginsConfiguration _config;
        private readonly AnchorDates _dates;
        private readonly DateTime _historyStart;
        private readonly string _seed;

        public LoginPlanner( LoginsConfiguration config, AnchorDates dates, DateTime historyStart, string seed )
        {
            _config = config;
            _dates = dates;
            _historyStart = historyStart;
            _seed = seed;
        }

        /// <summary>
        /// Plans every login: the Cast's first, then each campus pastor's, then generated adults' in load order.
        /// </summary>
        /// <exception cref="InvalidDataException">A Cast username is taken, or a Cast login belongs to a child.</exception>
        public List<PlannedLogin> Plan( LoginInputs inputs )
        {
            var taken = new HashSet<string>( inputs.TakenUserNames, StringComparer.OrdinalIgnoreCase );
            var reasons = Reasons( inputs );
            var logins = new List<PlannedLogin>();

            // The Cast's usernames are fixed by their files, so docs can name them. They are reserved before any generated one.
            var people = inputs.Households.Where( h => h.Cast.Kind != "business" ).SelectMany( h => h.Members.Select( m => (Household: h, Person: m) ) )
                .GroupBy( x => x.Person.PersonId ).Select( g => g.First() ).ToList();
            foreach ( var (household, person) in people.Where( x => inputs.CastHouseholdKeys.Contains( x.Household.Cast.Key ) ) )
            {
                foreach ( var userName in person.Cast.Logins ?? new List<string>() )
                {
                    if ( !taken.Add( userName ) )
                    {
                        throw new InvalidDataException( $"The Cast login {userName} is already taken in this database." );
                    }

                    if ( !IsOldEnough( person ) )
                    {
                        throw new InvalidDataException( $"cast/households/{household.Cast.Key}.json gives {person.Cast.Key} a login, but only adults have logins." );
                    }

                    var isStaff = person.Cast.SecurityRoles.Any( r => inputs.RockRoleNames.Contains( r ) );
                    logins.Add( Login( person, userName, reasons.GetValueOrDefault( person.PersonId ), isStaff ? Tier.Staff : Tier.Active, canBeUnconfirmed: false ) );
                }
            }

            // A Cast adult with the church app signed in to it, and one on the prayer team holds RSR - Prayer Access
            // through group sync (decision 188), so one with no login in their file gets one by Rock's rule,
            // after every listed Cast username. Someone who already has a login in Rock, such as Alisha Marble with
            // Rock's own admin, keeps only that one.
            foreach ( var (_, person) in people.Where( x => inputs.CastHouseholdKeys.Contains( x.Household.Cast.Key ) ) )
            {
                var personReasons = reasons.GetValueOrDefault( person.PersonId );
                var hasApp = personReasons?.Any( r => r.Key == "app" ) == true;
                var isPrayerTeam = personReasons?.Any( r => r.Key == "prayer-team" ) == true;
                if ( !( hasApp || isPrayerTeam ) || ( person.Cast.Logins?.Count ?? 0 ) > 0 || inputs.PeopleWithLogins.Contains( person.PersonId ) || !IsOldEnough( person ) )
                {
                    continue;
                }

                var userName = UniqueUserName( person.Cast.FirstName, person.Cast.LastName, taken );
                if ( userName != null )
                {
                    logins.Add( Login( person, userName, personReasons, hasApp ? Tier.Frequent : Tier.Active, canBeUnconfirmed: false ) );
                }
            }

            var pastors = inputs.CampusPastors.Values.Select( p => p.PersonId ).ToHashSet();
            var generated = people.Where( x => !inputs.CastHouseholdKeys.Contains( x.Household.Cast.Key ) )
                .OrderBy( x => pastors.Contains( x.Person.PersonId ) ? 0 : 1 )
                .ThenBy( x => x.Person.PersonId )
                .Select( x => x.Person );
            foreach ( var person in generated )
            {
                if ( !IsOldEnough( person ) || person.Cast.IsDeceased || inputs.PeopleWithLogins.Contains( person.PersonId ) )
                {
                    continue;
                }

                var personReasons = reasons.GetValueOrDefault( person.PersonId ) ?? new List<(string Key, DateTime? Since)>();
                if ( pastors.Contains( person.PersonId ) )
                {
                    personReasons.Add( ("campus-pastor", null) );
                }

                var random = new SeededRandom( $"{_seed}:login:{person.Cast.Guid}" );
                if ( !random.Chance( Chance( person, personReasons ) / 100 ) )
                {
                    continue;
                }

                var userName = UniqueUserName( person.Cast.FirstName, person.Cast.LastName, taken );
                if ( userName == null )
                {
                    continue;
                }

                var tier = pastors.Contains( person.PersonId ) ? Tier.Staff
                    : personReasons.Any( r => r.Key == "app" || r.Key == "leader" ) ? Tier.Frequent
                    : personReasons.Any( r => r.Key is "scheduled-online" or "volunteer" or "prayer-team" ) ? Tier.Active
                    : Tier.Occasional;
                logins.Add( Login( person, userName, personReasons, tier, canBeUnconfirmed: tier == Tier.Occasional ) );
            }

            return logins;
        }

        /// <summary>
        /// A person's chance of a login, in percent: their strongest reason's, or the inactive share for an inactive
        /// record. Older adults take a share of it unless a reason is certain.
        /// </summary>
        private double Chance( LoadedPerson person, List<(string Key, DateTime? Since)> reasons )
        {
            var keys = reasons.Select( r => r.Key ).ToHashSet();
            var status = person.Cast.ConnectionStatus ?? string.Empty;
            if ( _config.Connected.Contains( status ) )
            {
                keys.Add( "connected" );
            }
            else if ( _config.Known.Contains( status ) )
            {
                keys.Add( "known" );
            }
            else if ( _config.Prospect.Contains( status ) )
            {
                keys.Add( "prospect" );
            }

            var isCertain = keys.Contains( "app" ) || keys.Contains( "campus-pastor" );
            if ( isCertain )
            {
                return 100;
            }

            // A real account starts from an email address: online giving, a registration, or a group sign-up.
            if ( string.IsNullOrWhiteSpace( person.Cast.Email ) )
            {
                return 0;
            }

            // A reason the file makes certain, such as the prayer team's security role (decision 188), is never scaled down.
            if ( keys.Any( k => _config.Reasons.Any( r => r.Key == k && r.Percent >= 100 ) ) )
            {
                return 100;
            }

            var chance = person.Cast.RecordStatus == "inactive"
                ? _config.InactivePercent
                : keys.Select( _config.Percent ).DefaultIfEmpty( 0 ).Max();

            var isOlder = person.BirthDate.HasValue && _dates.AgeOn( person.BirthDate.Value ) >= _config.OlderAdults.FromAge;
            return isOlder ? chance * _config.OlderAdults.Factor : chance;
        }

        /*
            10/27/26 - CLAUDE

            Each login looks like one a real person made and uses. It was made on the day of its first reason, such
            as a first online gift or the day someone joined a team, or on a day in the history window when it has
            none. Staff and campus pastors signed in within the last few days, app users and leaders within weeks,
            and volunteers, scheduled givers, and the prayer team mostly within two months. Many occasional users
            never signed in again after the day they made the account, and a few never confirmed it. Nothing is
            locked out or marked as needing a new password, so a demo person signs in on the first try.

            Reason: Rock's login screens and reports should show the spread a church really has.
        */

        private PlannedLogin Login( LoadedPerson person, string userName, List<(string Key, DateTime? Since)> reasons, Tier tier, bool canBeUnconfirmed )
        {
            var random = new SeededRandom( $"{_seed}:login:dates:{person.Cast.Guid}" );
            var anchor = _dates.Anchor;
            var first = reasons?.Where( r => r.Since.HasValue ).Select( r => r.Since.Value ).DefaultIfEmpty( DateTime.MaxValue ).Min() ?? DateTime.MaxValue;
            DateTime created;
            if ( first < anchor )
            {
                created = first.Date.AddHours( random.Between( 7, 21 ) ).AddMinutes( random.Between( 0, 59 ) );
            }
            else if ( tier == Tier.Staff )
            {
                created = _historyStart.AddDays( -random.Between( 30, 700 ) ).Date.AddHours( random.Between( 8, 16 ) ).AddMinutes( random.Between( 0, 59 ) );
            }
            else
            {
                var days = Math.Max( 1, ( int ) ( anchor - _historyStart ).TotalDays );
                created = _historyStart.AddDays( random.Between( 0, days - 1 ) ).Date.AddHours( random.Between( 7, 21 ) ).AddMinutes( random.Between( 0, 59 ) );
            }

            if ( created >= anchor )
            {
                created = anchor.AddDays( -1 ).Date.AddHours( 12 );
            }

            var isConfirmed = !( canBeUnconfirmed && random.Chance( _config.UnconfirmedPercent / 100 ) );
            DateTime? lastLogin = null;
            if ( isConfirmed )
            {
                double? daysAgo = tier switch
                {
                    Tier.Staff => random.Between( 0.0, 3.0 ),
                    Tier.Frequent => random.Between( 0.0, 21.0 ),
                    Tier.Active => random.Chance( 0.7 ) ? random.Between( 0.0, 60.0 ) : random.Between( 0.0, ( anchor - created ).TotalDays ),
                    _ => random.Chance( 0.4 ) ? null : random.Between( 0.0, ( anchor - created ).TotalDays )
                };

                lastLogin = daysAgo.HasValue ? anchor.AddDays( -daysAgo.Value ) : created;
                if ( lastLogin < created )
                {
                    lastLogin = created;
                }
            }

            return new PlannedLogin( person, userName, created, lastLogin, isConfirmed, SeededRandom.StableGuid( $"{_seed}:login:{userName}" ) );
        }

        /// <summary>
        /// Every person's reasons for a login, with the day each began when it has one, from the plans every load makes.
        /// </summary>
        private Dictionary<int, List<(string Key, DateTime? Since)>> Reasons( LoginInputs inputs )
        {
            var byGuid = inputs.Households.SelectMany( h => h.Members ).GroupBy( m => m.Cast.Guid ).ToDictionary( g => g.Key, g => g.First().PersonId );
            var byAlias = inputs.Households.SelectMany( h => h.Members ).GroupBy( m => m.AliasId ).ToDictionary( g => g.Key, g => g.First().PersonId );
            var reasons = new Dictionary<int, List<(string, DateTime?)>>();
            void Add( int personId, string key, DateTime? since )
            {
                if ( !reasons.TryGetValue( personId, out var list ) )
                {
                    list = reasons[personId] = new List<(string, DateTime?)>();
                }

                list.Add( (key, since) );
            }

            foreach ( var group in inputs.GroupPlans.Where( g => g.Lifecycle is ActiveLifecycle ) )
            {
                foreach ( var member in group.Members.OfType<ActiveMember>() )
                {
                    if ( byGuid.TryGetValue( member.Guid, out var personId ) )
                    {
                        Add( personId, member.Role == SmallGroupRole.Leader ? "leader" : "group-member", _dates.Resolve( member.Joined ) );
                    }
                }
            }

            foreach ( var member in inputs.ServingPlans.SelectMany( t => t.Members ) )
            {
                if ( byGuid.TryGetValue( member.Person, out var personId ) )
                {
                    Add( personId, member.IsLeader ? "leader" : "volunteer", member.Joined );
                }
            }

            foreach ( var gift in inputs.Gifts.Values.SelectMany( g => g ).Where( g => g.SourceTypeValueId == inputs.WebsiteSourceId ).OrderBy( g => g.When ) )
            {
                if ( byAlias.TryGetValue( gift.PersonAliasId, out var personId ) )
                {
                    Add( personId, gift.IsScheduled ? "scheduled-online" : "online-one-time", gift.When );
                }
            }

            foreach ( var personId in inputs.PrayerTeam )
            {
                Add( personId, "prayer-team", null );
            }

            foreach ( var device in inputs.AppFirstSeen )
            {
                Add( device.Key, "app", device.Value );
            }

            return reasons;
        }

        private bool IsOldEnough( LoadedPerson person )
        {
            return person.IsAdult && person.Cast.RecordType != "business"
                && ( !person.BirthDate.HasValue || _dates.AgeOn( person.BirthDate.Value ) >= _config.MinimumAge );
        }

        /// <summary>
        /// Rock's own username rule (Database.GenerateUsername): the first name's first letter and the last name,
        /// lowercase, then 1, 2, and so on when it is taken. Accents are folded, and anything but a to z and
        /// 0 to 9 is dropped, so every username is easy to type. Null when the name gives no letters.
        /// </summary>
        internal static string UniqueUserName( string firstName, string lastName, HashSet<string> taken )
        {
            var name = Plain( ( string.IsNullOrEmpty( firstName ) ? string.Empty : firstName.Substring( 0, 1 ) ) + lastName );
            if ( name.Length == 0 )
            {
                return null;
            }

            for ( var tryCount = 0; ; tryCount++ )
            {
                var userName = tryCount == 0 ? name : $"{name}{tryCount}";
                if ( taken.Add( userName ) )
                {
                    return userName;
                }
            }
        }

        private static string Plain( string text )
        {
            var plain = new StringBuilder();
            foreach ( var c in ( text ?? string.Empty ).Normalize( NormalizationForm.FormD ).ToLowerInvariant() )
            {
                if ( ( c >= 'a' && c <= 'z' ) || ( c >= '0' && c <= '9' ) )
                {
                    plain.Append( c );
                }
            }

            return plain.ToString();
        }

        /// <summary>
        /// How often a login is used.
        /// </summary>
        private enum Tier
        {
            Staff,
            Frequent,
            Active,
            Occasional
        }
    }

    /// <summary>
    /// What the login planner reads. Everything comes from plans every load makes, except the prayer team and the
    /// app, which exist only when prayer and communications load.
    /// </summary>
    internal sealed class LoginInputs
    {
        public IReadOnlyList<LoadedHousehold> Households { get; set; }

        public HashSet<string> CastHouseholdKeys { get; set; }

        public IReadOnlyDictionary<string, LoadedPerson> CampusPastors { get; set; } = new Dictionary<string, LoadedPerson>();

        public IReadOnlyList<SmallGroupPlan> GroupPlans { get; set; } = new List<SmallGroupPlan>();

        public IReadOnlyList<ServingTeamPlan> ServingPlans { get; set; } = new List<ServingTeamPlan>();

        public IReadOnlyDictionary<string, List<RockGift>> Gifts { get; set; } = new Dictionary<string, List<RockGift>>();

        /// <summary>
        /// Rock's Website transaction source: a gift from it was given online.
        /// </summary>
        public int WebsiteSourceId { get; set; }

        public IReadOnlyCollection<int> PrayerTeam { get; set; } = new List<int>();

        /// <summary>
        /// When each person with the church app first opened it, by person Id.
        /// </summary>
        public IReadOnlyDictionary<int, DateTime> AppFirstSeen { get; set; } = new Dictionary<int, DateTime>();

        /// <summary>
        /// The usernames already in the database, such as Rock's own admin.
        /// </summary>
        public IReadOnlyCollection<string> TakenUserNames { get; set; } = new List<string>();

        /// <summary>
        /// The people who already have a login in the database, such as Rock's admin person.
        /// </summary>
        public HashSet<int> PeopleWithLogins { get; set; } = new HashSet<int>();

        /// <summary>
        /// The Cast role names that are Rock security roles, from configuration/security-roles.json.
        /// </summary>
        public HashSet<string> RockRoleNames { get; set; } = new HashSet<string>();
    }

    /// <summary>
    /// One planned login.
    /// </summary>
    internal sealed record PlannedLogin( LoadedPerson Person, string UserName, DateTime Created, DateTime? LastLogin, bool IsConfirmed, Guid Guid );
}
