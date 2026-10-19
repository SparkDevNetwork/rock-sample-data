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
        10/19/26 - CLAUDE

        A church keeps a business record only because the business gives. So every generated
        business gives without a stop, on a cadence sure to land gifts in the last 12 months:
        monthly, bi-weekly (with payroll), or irregular (when cash flow allows). Quarterly and
        year-end are left out on purpose: quarterly givers are a fraction of a percent of giving
        units in the profile, and a large once-a-year business gift skews the one-time givers. The
        Cast's businesses carry those cases. Each business has one contact, or a married
        couple as two, taken from the households on its campus, and a pool address on a
        residential street: these are small, often home-based businesses.

        How many: at least MainCampusMinimum on the main campus (the Cast's businesses count) and
        CampusMinimum on every other campus, or the profile's share of giving units when that is more.

        Reason: Business giving, contacts, and Business Detail have data at every campus.
    */

    /// <summary>
    /// Generates business records for the campuses that have generated households.
    /// </summary>
    internal sealed class BusinessGenerator
    {
        /// <summary>
        /// The fewest businesses the main campus has, counting the Cast's.
        /// </summary>
        public const int MainCampusMinimum = 6;

        /// <summary>
        /// The fewest businesses any other campus has.
        /// </summary>
        public const int CampusMinimum = 2;

        // Fictional place names, none used by a Cast business. Each pairs with a trade to name a business.
        private static readonly string[] Places =
        {
            "Agave Flats", "Arroyo Seco", "Blue Palo", "Brittlebush", "Canyon Wren", "Cactus Wash", "Coyote Bend", "Creosote Hill",
            "Desert Marigold", "Dry Fork", "Greasewood", "Hedgehog Hill", "Ironwood Mesa", "Jojoba Ridge", "Kingbird", "Palo Fierro",
            "Lone Butte", "Mariposa Wash", "Saltbush", "Night Heron", "Ocotillo Bend", "Prickly Pear", "Quail Run", "Red Rock Wash",
            "Roadrunner Ridge", "Sage Flat", "Sidewinder", "Silver Creek", "Sonoran Sky", "Sun Valley", "Thrasher", "Verde Wash", "Whitetail", "Yucca Point"
        };

        // Trades a small, often home-based business can run.
        private static readonly string[] Trades =
        {
            "Landscaping", "Plumbing", "Pool Service", "Bookkeeping", "Electric", "Painting", "Home Cleaning", "Photography",
            "Heating and Cooling", "Tile and Stone", "Auto Repair", "Insurance Agency", "Realty", "Catering", "Pest Control", "Roofing",
            "Tax Service", "Graphic Design", "Handyman Service", "Dog Grooming", "Tutoring", "Construction"
        };

        private static readonly string[] AreaCodes = { "602", "480", "623" };

        // Cadences sure to land gifts in the last 12 months, weighted toward where the profile has room.
        private static readonly Dictionary<string, double> PersonaWeights = new Dictionary<string, double>
        {
            ["monthly"] = 50,
            ["bi-weekly"] = 25,
            ["irregular"] = 25
        };

        // How a business pays. A check arrives by mail; bank and card gifts are scheduled.
        private static readonly Dictionary<string, double> CurrencyWeights = new Dictionary<string, double>
        {
            ["check"] = 20,
            ["ach"] = 25,
            ["card"] = 55
        };

        private readonly ProfileTargets _profile;
        private readonly string _seed;
        private readonly AddressPool _addresses;
        private readonly Func<double, double> _annualTotal;
        private readonly HashSet<string> _usedNames = new HashSet<string>();

        public BusinessGenerator( ProfileTargets profile, string seed, AddressPool addresses, Func<double, double> annualTotal )
        {
            _profile = profile;
            _seed = seed;
            _addresses = addresses;
            _annualTotal = annualTotal;
        }

        /// <summary>
        /// Makes the businesses for each campus that has generated households, in campus order. A campus
        /// gets enough to reach its minimum (the Cast's main campus businesses count toward the main
        /// campus) or the profile's share of its giving units, whichever is more.
        /// </summary>
        public List<CastHousehold> Generate( IReadOnlyList<CastHousehold> generated, IReadOnlyList<string> campusKeys, string mainCampusKey, int castMainCampusBusinesses )
        {
            var businesses = new List<CastHousehold>();
            var share = _profile.Number( "giving.pctUnitsBusiness", 3 ) / 100;
            foreach ( var campusKey in campusKeys )
            {
                var families = generated.Where( h => h.CampusSlot == campusKey && h.Kind == "family" ).ToList();
                if ( families.Count == 0 )
                {
                    continue;
                }

                var isMain = campusKey == mainCampusKey;
                var existing = isMain ? castMainCampusBusinesses : 0;
                var givingUnits = families.Sum( h => h.Giving.Count( GaveInLastYear ) ) + existing;
                var measured = ( int ) Math.Round( givingUnits * share / ( 1 - share ) );
                var count = Math.Max( isMain ? MainCampusMinimum : CampusMinimum, measured ) - existing;

                var contacts = new ContactPicker( families, new SeededRandom( $"{_seed}:business-contacts:{campusKey}" ) );
                for ( var n = 0; n < count; n++ )
                {
                    var random = new SeededRandom( $"{_seed}:business:{campusKey}:{n}" );
                    var household = NewBusiness( $"business-{campusKey}-{n:D3}", campusKey, random );
                    if ( household != null )
                    {
                        AddContactsAndGiving( household, contacts.Next( random ), random );
                        businesses.Add( household );
                    }
                }
            }

            return businesses;
        }

        /// <summary>
        /// True when a generated giving pattern has gifts in the last 12 months, the window the profile's
        /// share of business units is measured over: it has not stopped, or stopped within the year, and
        /// a one-time gift fell within the year. Generated patterns give their dates in days ago.
        /// </summary>
        private static bool GaveInLastYear( CastGiving pattern )
        {
            if ( pattern.Ended?.DaysAgo is int endedDaysAgo && endedDaysAgo > 365 )
            {
                return false;
            }

            return pattern.Persona != "one-time" || pattern.Started?.DaysAgo is int startedDaysAgo && startedDaysAgo <= 365;
        }

        /// <summary>
        /// Builds the business record and its household, or null when the campus has no address left.
        /// </summary>
        private CastHousehold NewBusiness( string key, string campusKey, SeededRandom random )
        {
            var address = _addresses.Next( campusKey );
            if ( address == null )
            {
                return null;
            }

            address.Type = "work";
            var name = NewName( random );
            var slug = new string( name.ToLowerInvariant().Where( char.IsLetterOrDigit ).ToArray() );
            var household = new CastHousehold
            {
                Key = key,
                Guid = random.NextGuid(),
                Name = name,
                Kind = "business",
                CampusSlot = campusKey,
                Address = address
            };

            household.Members.Add( new CastPerson
            {
                Key = $"{key}-0",
                Guid = random.NextGuid(),
                RecordType = "business",
                LastName = name,
                Gender = "unknown",
                FamilyRole = "adult",
                RecordStatus = "active",
                ConnectionStatus = "Prospect",
                Email = $"{slug}@example.org",
                Phones = new CastPhones { Work = $"{random.Pick( AreaCodes )}5550{random.Between( 100, 199 )}" }
            } );

            return household;
        }

        /// <summary>
        /// A business name not used yet in this load, such as "Quail Run Pool Service".
        /// </summary>
        private string NewName( SeededRandom random )
        {
            for ( var tries = 0; ; tries++ )
            {
                var name = $"{random.Pick( Places )} {random.Pick( Trades )}";
                if ( _usedNames.Add( name ) )
                {
                    return name;
                }

                if ( tries > 1000 )
                {
                    throw new InvalidOperationException( "Ran out of business names. Add places or trades to BusinessGenerator." );
                }
            }
        }

        /// <summary>
        /// Makes the contacts known relationships of the business, and gives the business its giving pattern.
        /// A check is signed by the first contact.
        /// </summary>
        private void AddContactsAndGiving( CastHousehold household, List<CastPerson> contacts, SeededRandom random )
        {
            var business = household.Members[0];
            foreach ( var contact in contacts )
            {
                contact.Relationships.Add( new CastRelationship { Type = "business", With = business.Guid } );
            }

            var persona = random.Pick( PersonaWeights );
            var currency = random.Pick( CurrencyWeights );
            var signer = contacts.FirstOrDefault();

            // Businesses sit high on the annual curve: the profile has them as a small share of giving
            // units but a much larger share of dollars. They stop short of the very top, which belongs
            // to the church's largest individual givers.
            var annual = _annualTotal( Math.Min( HighestPoint, Math.Pow( random.Between( 0.0, 1.0 ), AnnualSkew ) ) );
            var perGift = RoundAmount( ( decimal ) ( annual / GiftsPerYear( persona ) ) );

            var funds = new Dictionary<string, decimal>();
            if ( random.Chance( 0.2 ) )
            {
                funds["Building Fund"] = RoundAmount( perGift * 0.4m );
                perGift -= funds["Building Fund"];
            }
            else if ( random.Chance( 0.1 ) )
            {
                funds["Mission Fund"] = RoundAmount( perGift * 0.25m );
                perGift -= funds["Mission Fund"];
            }

            funds["General Fund"] = perGift;

            household.Giving.Add( new CastGiving
            {
                Persona = persona,
                Person = business.Key,
                Funds = funds,
                Currency = currency,
                Signer = currency == "check" && signer != null ? $"{signer.FirstName} {signer.LastName}" : null,
                PercentOfPeriodsGiven = random.Between( 88, 100 ),
                SpecialGiftPercent = 0,

                // Some businesses began giving inside the history window, but more than a year ago, so
                // every one has a full year of gifts.
                Started = random.Chance( 0.3 ) ? new RelativeDate { DaysAgo = random.Between( 400, 1500 ) } : null
            } );
        }

        /// <summary>
        /// The exponent on a business's point on the annual giving curve. Below 1 pulls toward larger totals.
        /// Tuned so businesses' share of dollars lands near the profile.
        /// </summary>
        private const double AnnualSkew = 0.35;

        /// <summary>
        /// The highest point on the annual giving curve a business reaches.
        /// </summary>
        private const double HighestPoint = 0.97;

        /// <summary>
        /// About how many gifts a year each cadence gives, as the crowd's giving uses.
        /// </summary>
        private static double GiftsPerYear( string persona )
        {
            return persona switch
            {
                "bi-weekly" => 24,
                "irregular" => 18,
                _ => 12
            };
        }

        /// <summary>
        /// Rounds a business gift to a typed, round amount: the nearest $10 under $100, $50 under $1,000, then $100.
        /// </summary>
        private static decimal RoundAmount( decimal amount )
        {
            var step = amount < 100 ? 10m : amount < 1000 ? 50m : 100m;
            return Math.Max( 20m, Math.Round( amount / step ) * step );
        }

        /*
            10/19/26 - CLAUDE

            A contact is an adult, 25 or older, from a family on the business's campus. Engaged and
            Leading adults are picked first, then Connected, then anyone, because the people a church
            knows well are the ones whose businesses give. No one is a contact of two businesses. A
            married couple sometimes owns the business together, and both are contacts.
        */

        /// <summary>
        /// Hands out business contacts for one campus.
        /// </summary>
        private sealed class ContactPicker
        {
            private const double CoupleShare = 0.3;

            private readonly List<CastHousehold> _ordered;
            private int _next;

            public ContactPicker( List<CastHousehold> families, SeededRandom random )
            {
                _ordered = families
                    .Where( h => h.Members.Any( IsCandidate ) )
                    .Select( h => (Household: h, Rank: Rank( h.Members.First( IsCandidate ).ConnectionStatus ), Tie: random.Between( 0.0, 1.0 )) )
                    .OrderBy( c => c.Rank )
                    .ThenBy( c => c.Tie )
                    .Select( c => c.Household )
                    .ToList();
            }

            /// <summary>
            /// The next family's contacts: its first adult, and sometimes that adult's spouse. Empty when
            /// the campus has no adults left.
            /// </summary>
            public List<CastPerson> Next( SeededRandom random )
            {
                if ( _next >= _ordered.Count )
                {
                    return new List<CastPerson>();
                }

                var adults = _ordered[_next++].Members.Where( IsCandidate ).ToList();
                var isCouple = adults.Count == 2 && adults.All( a => string.Equals( a.MaritalStatus, "married", StringComparison.OrdinalIgnoreCase ) );
                return isCouple && random.Chance( CoupleShare ) ? adults : adults.Take( 1 ).ToList();
            }

            private static bool IsCandidate( CastPerson person )
            {
                return person.FamilyRole == "adult" && person.RecordType != "business" && ( person.Birth?.Age ?? 0 ) >= 25;
            }

            private static int Rank( string status )
            {
                return status switch
                {
                    "Leading" or "Engaged" => 0,
                    "Connected" => 1,
                    _ => 2
                };
            }
        }
    }
}
