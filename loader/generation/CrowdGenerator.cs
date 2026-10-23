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
    /*
        10/15/26 - CLAUDE

        Phase 0 spike crowd. It makes quick households in the same shape as Cast
        households, so the people, attendance, and giving loaders treat both the same.
        Every choice is sampled from the resolved profile: family size, ages, gender,
        marital status, how many records show activity, attendance frequency, giving
        cadence, and annual giving. Names and streets come from small spike-only lists;
        the real Crowd will use the name lists and address pool from the spec.

        Reason: Measure install speed at scale and give validation enough rows for real statistics.
    */

    /// <summary>
    /// Generates spike crowd households from the profile.
    /// </summary>
    internal sealed class CrowdGenerator
    {
        private static readonly string[] AgeBands = { "00-05", "06-11", "12-17", "18-24", "25-34", "35-44", "45-54", "55-64", "65-74", "75+" };
        private static readonly string[] AdultBands = { "18-24", "25-34", "35-44", "45-54", "55-64", "65-74", "75+" };
        private static readonly string[] Personas = { "one-time", "occasional", "weekly", "bi-weekly", "monthly", "quarterly", "irregular" };
        private static readonly string[] AreaCodes = { "602", "480", "623" };

        /// <summary>
        /// Status weights, and the chance a record with that status shows any activity.
        /// Together these land near the profile's share of adult records with activity.
        /// </summary>
        private static readonly (string Status, double Weight, double ActiveChance)[] Statuses =
        {
            ("Prospect", 8, 0.05),
            ("Participant", 30, 0.15),
            ("Known", 22, 0.35),
            ("Connected", 20, 0.75),
            ("Engaged", 14, 0.95),
            ("Leading", 6, 1.0)
        };

        private readonly ProfileTargets _profile;
        private readonly SampleDataSource _source;
        private readonly string _seed;
        private readonly DateTime _anchor;
        private readonly AddressPool _addresses;

        /// <summary>
        /// The church's own texting numbers, which no generated person may have (decision 93).
        /// </summary>
        private readonly HashSet<string> _churchNumbers;

        public CrowdGenerator( ProfileTargets profile, SampleDataSource source, string seed, DateTime anchor )
        {
            _profile = profile;
            _source = source;
            _seed = seed;
            _anchor = anchor;
            _addresses = AddressPool.Load( source.Files, seed );

            var numbers = source.Communications?.PhoneNumbers;
            _churchNumbers = ( numbers?.Numbers.Select( n => n.Number ) ?? Enumerable.Empty<string>() )
                .Concat( numbers?.CampusPastors.Numbers.Values.Select( n => n.Number ) ?? Enumerable.Empty<string>() )
                .Select( n => n.StartsWith( "+1", StringComparison.Ordinal ) ? n.Substring( 2 ) : n )
                .ToHashSet();
        }

        /// <summary>
        /// Makes the given number of households, spread across the campuses. The main campus gets a bigger share.
        /// </summary>
        public List<CastHousehold> Generate( int count, IReadOnlyList<string> campusKeys, string mainCampusKey, int historyYears )
        {
            var households = new List<CastHousehold>( count );
            var campusWeights = campusKeys.ToDictionary( k => k, k => k == mainCampusKey ? 1.5 : 1.0 );
            var campuses = _source.Campuses.Campuses.ToDictionary( c => c.Key );
            var sizeWeights = SizeWeights();

            for ( var i = 0; i < count; i++ )
            {
                var random = new SeededRandom( $"{_seed}:crowd:{i}" );
                var campusKey = random.Pick( campusWeights );
                households.Add( NewActiveHousehold( i, random, campusKey, campuses[campusKey], sizeWeights, historyYears ) );
            }

            return households;
        }

        /// <summary>
        /// Makes an exact number of households at each listed campus, in the order the campuses are given.
        /// </summary>
        public List<CastHousehold> Generate( IReadOnlyList<(string CampusKey, int Count)> countsByCampus, int historyYears )
        {
            var households = new List<CastHousehold>( countsByCampus.Sum( c => c.Count ) );
            var campuses = _source.Campuses.Campuses.ToDictionary( c => c.Key );
            var sizeWeights = SizeWeights();

            foreach ( var (campusKey, count) in countsByCampus )
            {
                for ( var n = 0; n < count; n++ )
                {
                    var index = households.Count;
                    var random = new SeededRandom( $"{_seed}:crowd:{index}" );
                    households.Add( NewActiveHousehold( index, random, campusKey, campuses[campusKey], sizeWeights, historyYears ) );
                }
            }

            return households;
        }

        /// <summary>
        /// Makes business records for the campuses of the generated households, with contacts from those
        /// households and giving. They take addresses from the same pool, after the households.
        /// </summary>
        public List<CastHousehold> GenerateBusinesses( IReadOnlyList<CastHousehold> generated, IReadOnlyList<string> campusKeys, string mainCampusKey, int castMainCampusBusinesses )
        {
            return new BusinessGenerator( _profile, _seed, _addresses, AnnualTotal, PhoneNumber ).Generate( generated, campusKeys, mainCampusKey, castMainCampusBusinesses );
        }

        /// <summary>
        /// Builds one household with its attendance and giving history.
        /// </summary>
        private CastHousehold NewActiveHousehold( int index, SeededRandom random, string campusKey, CampusDefinition campus, Dictionary<string, double> sizeWeights, int historyYears )
        {
            var household = NewHousehold( index, random, campusKey, campus, sizeWeights );
            AddAttendance( household, random, historyYears );
            AddGiving( household, random, historyYears );
            return household;
        }

        /// <summary>
        /// Builds one household: its people, address, and status.
        /// </summary>
        private CastHousehold NewHousehold( int index, SeededRandom random, string campusKey, CampusDefinition campus, Dictionary<string, double> sizeWeights )
        {
            var size = SizeFromKey( random.Pick( sizeWeights ), random );
            var sizeKey = size >= 6 ? "6+" : size.ToString();
            var averageAdults = _profile.Number( $"people.familySizeMix.{sizeKey}.avgAdults", 1 );
            var lastName = random.Pick( CrowdNames.LastNames );
            var status = PickStatus( random );

            // Some one-person families are a child on their own (often a student who comes without parents).
            var isChildAlone = size == 1 && random.Chance( _profile.Number( "people.familySizeMix.1.avgChildren", 0 ) );
            var adultCount = isChildAlone ? 0
                : size == 1 ? 1
                : Math.Min( Math.Max( ( int ) Math.Floor( averageAdults ) + ( random.Chance( averageAdults % 1 ) ? 1 : 0 ), 1 ), Math.Min( 2, size ) );

            var household = new CastHousehold
            {
                Key = $"crowd-{index:D6}",
                Guid = random.NextGuid(),
                Name = $"{lastName} Family",
                Kind = "family",
                CampusSlot = campusKey,
                Address = NewAddress( random, campus )
            };

            if ( isChildAlone )
            {
                var age = AgeInBand( random.Pick( AgeWeights( new[] { "06-11", "12-17" } ) ), random );
                household.Members.Add( NewPerson( household, 0, random, lastName, Math.Max( age, 10 ), random.Chance( 0.5 ), "child", status, null ) );
                return household;
            }

            // Adults: a couple is usually one man and one woman of similar age. Parents are mostly 25 to 54.
            var hasChildren = size > adultCount;
            var firstAge = AgeInBand( random.Pick( AgeWeights( hasChildren ? new[] { "25-34", "35-44", "45-54" } : AdultBands ) ), random );
            var firstIsFemale = random.Chance( _profile.Number( $"people.pctFemaleByAge.{BandFor( firstAge )}", 55 ) / 100 );
            var isCouple = adultCount == 2;
            var marital = isCouple ? "Married" : PickSingleMarital( random );
            household.Members.Add( NewPerson( household, 0, random, lastName, firstAge, firstIsFemale, "adult", status, marital ) );
            if ( isCouple )
            {
                var spouseAge = Math.Max( 18, firstAge + random.Between( -4, 4 ) );
                household.Members.Add( NewPerson( household, 1, random, lastName, spouseAge, !firstIsFemale, "adult", status, marital ) );
            }

            // Children: younger than the youngest parent by at least 18 years.
            var youngestParent = household.Members.Min( m => m.Birth.Age );
            for ( var c = 0; c < size - adultCount; c++ )
            {
                var maxAge = Math.Min( 17, youngestParent - 18 );
                if ( maxAge < 0 )
                {
                    break;
                }

                var childAge = Math.Min( maxAge, AgeInBand( random.Pick( AgeWeights( new[] { "00-05", "06-11", "12-17" } ) ), random ) );
                household.Members.Add( NewPerson( household, adultCount + c, random, lastName, childAge, random.Chance( 0.5 ), "child", status, null ) );
            }

            return household;
        }

        /// <summary>
        /// Builds one person.
        /// </summary>
        private CastPerson NewPerson( CastHousehold household, int position, SeededRandom random, string lastName, int age, bool isFemale, string role, string status, string marital )
        {
            var firstName = random.Pick( isFemale ? CrowdNames.FemaleFirstNames : CrowdNames.MaleFirstNames );
            var isAdult = role == "adult";
            var hasEmail = isAdult && random.Chance( 0.8 );
            var hasMobile = isAdult ? random.Chance( 0.75 ) : age >= 13 && random.Chance( 0.4 );

            return new CastPerson
            {
                Key = $"{household.Key}-{position}",
                Guid = random.NextGuid(),
                RecordType = "person",
                FirstName = firstName,
                LastName = lastName,
                Gender = isFemale ? "female" : "male",
                FamilyRole = role,
                Birth = new CastBirth { Age = age, Month = random.Between( 1, 12 ), Day = random.Between( 1, 28 ) },
                MaritalStatus = marital,
                RecordStatus = "active",
                ConnectionStatus = status,
                Email = hasEmail ? $"{firstName}.{lastName}{random.Between( 1, 999 )}@example.org".ToLowerInvariant() : null,
                Phones = hasMobile ? new CastPhones { Mobile = PhoneNumber( random ) } : null
            };
        }

        /// <summary>
        /// A reserved phone number that is not one of the church's texting numbers.
        /// </summary>
        private string PhoneNumber( SeededRandom random )
        {
            string number;
            do
            {
                number = $"{random.Pick( AreaCodes )}5550{random.Between( 100, 199 )}";
            }
            while ( _churchNumbers.Contains( number ) );

            return number;
        }

        /// <summary>
        /// The next pool address for the campus (a real street with a house number that does not exist).
        /// When the pool runs out, falls back to a placeholder street at a profile distance.
        /// </summary>
        private CastAddress NewAddress( SeededRandom random, CampusDefinition campus )
        {
            var pooled = _addresses.Next( campus.Key );
            if ( pooled != null )
            {
                return pooled;
            }

            var miles = Percentile( "distance.milesPercentiles", random.Between( 0.0, 1.0 ), 1 );
            var angle = random.Between( 0.0, Math.PI * 2 );
            var latitude = campus.Location.Latitude + ( miles / 69.0 * Math.Sin( angle ) );
            var longitude = campus.Location.Longitude + ( miles / ( 69.0 * Math.Cos( campus.Location.Latitude * Math.PI / 180 ) ) * Math.Cos( angle ) );

            return new CastAddress
            {
                Type = "home",
                Street1 = $"{random.Between( 100, 9899 )} {random.Pick( CrowdNames.Streets )}",
                City = campus.Name,
                State = "AZ",
                PostalCode = "85000",
                Latitude = Math.Round( latitude, 5 ),
                Longitude = Math.Round( longitude, 5 )
            };
        }

        /// <summary>
        /// Gives an active household an attendance rate from the profile's 26-week frequency mix.
        /// Some households start partway through the history (newcomers) and some stop (lapsed).
        /// </summary>
        private void AddAttendance( CastHousehold household, SeededRandom random, int historyYears )
        {
            if ( !IsActive( household, random, out var attends, out _ ) || !attends )
            {
                household.Attendance = new CastAttendance { PercentAttended = 0 };
                return;
            }

            var band = random.Pick( _profile.Map( "attendance.adultFrequencyMixOf26Weeks" ) );
            var percent = band switch
            {
                "01" => 4,
                "02-03" => random.Between( 8, 12 ),
                "04-07" => random.Between( 15, 27 ),
                "08-12" => random.Between( 31, 46 ),
                "13-19" => random.Between( 50, 73 ),
                _ => random.Between( 77, 95 )
            };

            household.Attendance = new CastAttendance
            {
                Persona = "crowd",
                PercentAttended = percent,
                PercentPrimaryService = random.Between( 60, 95 ),
                Started = random.Chance( 0.3 ) ? RandomDate( random, historyYears ) : null,
                Ended = random.Chance( 0.15 ) ? RandomDate( random, historyYears ) : null
            };
        }

        /// <summary>
        /// Gives a giving household one pattern for its first adult: a cadence from the profile's
        /// cadence mix, and an annual total from its giving percentiles, skewed by cadence.
        /// </summary>
        private void AddGiving( CastHousehold household, SeededRandom random, int historyYears )
        {
            // A few married couples are set up to give separately, whether or not they give yet:
            // each spouse is their own giving unit.
            var isCouple = household.Members.Count( m => m.FamilyRole == "adult" && string.Equals( m.MaritalStatus, "married", StringComparison.OrdinalIgnoreCase ) ) == 2;
            household.AdultsGiveSeparately = isCouple && random.Chance( SeparateGivingShare );

            if ( !IsActive( household, random, out _, out var gives ) || !gives || household.Members[0].FamilyRole == "child" )
            {
                return;
            }

            household.Giving.Add( NewGivingPattern( household.Members[0], random, historyYears ) );
            if ( household.AdultsGiveSeparately )
            {
                household.Giving.Add( NewGivingPattern( household.Members[1], random, historyYears ) );
            }
        }

        /// <summary>
        /// The share of married couples whose spouses give separately.
        /// </summary>
        private const double SeparateGivingShare = 0.10;

        /// <summary>
        /// One giving pattern for one giver: a cadence from the profile's cadence mix, and an annual
        /// total from its giving percentiles, skewed by cadence and by the giver's age.
        /// </summary>
        private CastGiving NewGivingPattern( CastPerson giver, SeededRandom random, int historyYears )
        {
            var persona = random.Pick( Personas.ToDictionary( p => p, p => _profile.Number( $"giving.cadenceMix.{GivingLoader.CadenceNames[p]}.pctUnits", 1 ) * PersonaScale( p ) ) );
            // Rhythmic givers sit higher on the annual curve than one-time givers, and older givers higher
            // than younger ones; the skew stands in for the cadence averages.
            var point = Math.Min( HighestPoint( persona ), Math.Pow( random.Between( 0.0, 1.0 ), AnnualSkew( persona ) * AgeSkew( giver.Birth?.Age ) ) );
            var annual = AnnualTotal( point );

            // Someone who gives only a little a year does not split it into tiny weekly gifts: they give
            // monthly, or a few times a year.
            while ( annual / GiftsPerYear( persona ) < MinimumGift && SlowerPersona( persona ) is string slower )
            {
                persona = slower;
            }

            var perGift = ( decimal ) Math.Max( MinimumGift, annual / GiftsPerYear( persona ) );

            /*
                10/23/26 - CLAUDE

                About 4% of givers once split part of each gift to a Building Fund that ran all year. That fund
                is now the building campaign's fund (decision 98), so the whole gift goes to the General Fund
                (decision 99). The draw stays, so every later random choice, and so the rest of the Crowd, is
                the same as before.
            */
            _ = random.Chance( 0.04 );

            var funds = new Dictionary<string, decimal>();
            if ( random.Chance( 0.03 ) )
            {
                funds["Mission Fund"] = Math.Round( perGift * 0.15m );
                perGift -= funds["Mission Fund"];
            }

            funds["General Fund"] = perGift;

            /*
                10/16/26 - CLAUDE

                Givers come and go. Some start inside the history window and some stop, and a stop always
                comes after the start (before, the pattern simply vanished). The two shares are tuned so
                year-over-year retained, lapsed, and new units match the profile.

                Reason: Churn targets (retained, lapsed, new) in the profile.
            */
            var historyDays = historyYears * 365;
            var startedDaysAgo = persona == "one-time" || random.Chance( _profile.Number( "giving.pctNewUnitsOfCurrent", 30 ) / 100 * NewGiverScale ) ? random.Between( 0, historyDays ) : ( int? ) null;
            var endedDaysAgo = persona != "one-time" && random.Chance( EndedShare ) ? random.Between( 0, Math.Max( 0, ( startedDaysAgo ?? historyDays ) - 60 ) ) : ( int? ) null;
            return new CastGiving
            {
                Persona = persona,
                Person = giver.Key,
                Funds = funds,
                PercentOfPeriodsGiven = random.Between( 70, 98 ),
                SpecialGiftPercent = persona is "one-time" or "occasional" ? 0 : 35,
                Started = startedDaysAgo.HasValue ? new RelativeDate { DaysAgo = startedDaysAgo } : null,
                Ended = endedDaysAgo.HasValue ? new RelativeDate { DaysAgo = endedDaysAgo } : null
            };
        }

        // Tuning knobs for churn and the top of the giving curve, found by running the validation loop.
        private const double NewGiverScale = 1.8;
        private const double EndedShare = 0.48;
        private const double TopTailShape = 1.7;
        private const double TopTailCap = 10;

        /// <summary>
        /// The annual total at a point (0 to 1) on the profile's giving curve. Above the 99th percentile the
        /// curve follows a long (Pareto) tail, because a church's largest few givers give far more than the rest.
        /// </summary>
        private double AnnualTotal( double point )
        {
            const string path = "giving.unitAnnualTotalPercentiles";
            if ( point <= 0.99 )
            {
                return Percentile( path, point, 100 );
            }

            var p99 = _profile.Number( $"{path}.p99", 40000 );
            return Math.Min( p99 * TopTailCap, p99 * Math.Pow( 0.01 / Math.Max( 1e-6, 1 - point ), 1 / TopTailShape ) );
        }

        /// <summary>
        /// The skew for the giver's age. Below 1 pulls toward larger totals.
        /// </summary>
        private static double AgeSkew( int? age )
        {
            return age switch
            {
                null => 1,
                < 35 => 1.2,
                < 50 => 1.0,
                < 65 => 0.92,
                _ => 0.88
            };
        }

        /// <summary>
        /// The smallest usual gift, for any cadence.
        /// </summary>
        private const double MinimumGift = 15;

        /// <summary>
        /// The next less frequent cadence, for a giver whose yearly total is too small for their cadence.
        /// </summary>
        private static string SlowerPersona( string persona )
        {
            return persona switch
            {
                "weekly" => "bi-weekly",
                "bi-weekly" => "monthly",
                "irregular" => "occasional",
                _ => null
            };
        }

        /// <summary>
        /// The highest point on the annual curve a cadence reaches. The very largest givers give often;
        /// someone who gives once or a few times a year does not sit at the top of the curve.
        /// </summary>
        private static double HighestPoint( string persona )
        {
            return persona switch
            {
                "one-time" => 0.85,
                "occasional" => 0.97,
                _ => 1
            };
        }

        /// <summary>
        /// Scales the profile's cadence mix when picking a giver's cadence. Small givers later move from weekly
        /// and bi-weekly to monthly, and one-time givers are under-counted once partial years count as other
        /// cadences, so the starting mix leans the other way.
        /// </summary>
        private static double PersonaScale( string persona )
        {
            return persona switch
            {
                "one-time" => 1.4,
                "bi-weekly" => 1.4,
                "monthly" => 0.8,
                _ => 1
            };
        }

        /// <summary>
        /// Decides, once per household, whether it shows activity and which kinds (from the profile's combination mix).
        /// The decision is cached on the household key so attendance and giving agree.
        /// </summary>
        private bool IsActive( CastHousehold household, SeededRandom random, out bool attends, out bool gives )
        {
            if ( !_activity.TryGetValue( household.Key, out var activity ) )
            {
                var status = household.Members[0].ConnectionStatus;
                var activeChance = Math.Min( 1, Statuses.First( s => s.Status == status ).ActiveChance * 1.12 );
                if ( random.Chance( activeChance ) )
                {
                    var combination = random.Pick( _profile.Map( "engagement.amongActiveAdults.combinationMix" ) );

                    // Group and serve are later phases; a household with only those still attends sometimes.
                    var hasAttend = combination.Contains( "attends" ) || !combination.Contains( "gives" );
                    activity = (true, hasAttend, combination.Contains( "gives" ));
                }
                else
                {
                    activity = (false, false, false);
                }

                _activity[household.Key] = activity;
            }

            attends = activity.Attends;
            gives = activity.Gives;
            return activity.Active;
        }

        private readonly Dictionary<string, (bool Active, bool Attends, bool Gives)> _activity = new Dictionary<string, (bool Active, bool Attends, bool Gives)>();

        /// <summary>
        /// Family size weights from the profile.
        /// </summary>
        private Dictionary<string, double> SizeWeights()
        {
            return new[] { "1", "2", "3", "4", "5", "6+" }.ToDictionary( k => k, k => _profile.Number( $"people.familySizeMix.{k}.pctFamilies", 1 ) );
        }

        private static int SizeFromKey( string key, SeededRandom random )
        {
            return key == "6+" ? random.Between( 6, 8 ) : int.Parse( key );
        }

        private Dictionary<string, double> AgeWeights( IEnumerable<string> bands )
        {
            return bands.ToDictionary( b => b, b => _profile.Number( $"people.ageMix.{b}", 1 ) );
        }

        private static int AgeInBand( string band, SeededRandom random )
        {
            if ( band == "75+" )
            {
                return random.Between( 75, 92 );
            }

            var parts = band.Split( '-' );
            return random.Between( int.Parse( parts[0] ), int.Parse( parts[1] ) );
        }

        private static string BandFor( int age )
        {
            return AgeBands.FirstOrDefault( b => b != "75+" && age <= int.Parse( b.Split( '-' )[1] ) ) ?? "75+";
        }

        private static string PickStatus( SeededRandom random )
        {
            return random.Pick( Statuses.ToDictionary( s => s.Status, s => s.Weight ) );
        }

        /// <summary>
        /// A marital status for a single adult. Married people whose spouse is not in the database are common, at a reduced weight.
        /// </summary>
        private string PickSingleMarital( SeededRandom random )
        {
            if ( random.Chance( _profile.Number( "people.pctAdultsNoMaritalStatus", 40 ) / 100 ) )
            {
                return null;
            }

            var weights = _profile.Map( "people.adultMaritalMix" ).ToDictionary( m => m.Key, m => m.Key == "married" ? m.Value * 0.1 : m.Value );
            var pick = random.Pick( weights );
            return char.ToUpperInvariant( pick[0] ) + pick.Substring( 1 );
        }

        private static double GiftsPerYear( string persona )
        {
            return persona switch
            {
                "weekly" => 46,
                "bi-weekly" => 24,
                "monthly" => 11,
                "quarterly" => 4,
                "irregular" => 18,
                "occasional" => 2.4,
                _ => 1
            };
        }

        /// <summary>
        /// A date within the history window, as a relative date.
        /// </summary>
        private RelativeDate RandomDate( SeededRandom random, int historyYears )
        {
            var date = _anchor.AddDays( -random.Between( 0, historyYears * 365 ) );
            return new RelativeDate { YearsAgo = _anchor.Year - date.Year, Month = date.Month, Day = date.Day };
        }

        /// <summary>
        /// Reads a value from a percentile curve (p10, p25, ...) at a point from 0 to 1,
        /// interpolating between points. Below p10 it scales down toward the floor.
        /// </summary>
        private double Percentile( string path, double point, double floor )
        {
            var curve = _profile.Map( path )
                .Where( p => p.Key.StartsWith( "p" ) )
                .Select( p => (Point: double.Parse( p.Key.Substring( 1 ) ) / 100, p.Value) )
                .OrderBy( p => p.Point )
                .ToList();
            var max = _profile.Number( $"{path}.max", curve[curve.Count - 1].Value * 1.5 );
            curve.Insert( 0, (0, Math.Min( floor, curve[0].Value )) );
            curve.Add( (1, max) );

            for ( var i = 1; i < curve.Count; i++ )
            {
                if ( point <= curve[i].Point )
                {
                    var span = curve[i].Point - curve[i - 1].Point;
                    var share = span <= 0 ? 0 : ( point - curve[i - 1].Point ) / span;
                    return curve[i - 1].Value + ( share * ( curve[i].Value - curve[i - 1].Value ) );
                }
            }

            return max;
        }

        /// <summary>
        /// The exponent applied to a cadence's point on the annual giving curve. Above 1 pulls
        /// toward smaller totals (one-time givers); below 1 pulls toward larger ones (weekly givers).
        /// </summary>
        private static double AnnualSkew( string persona )
        {
            return persona switch
            {
                "one-time" => 4.0,
                "occasional" => 2.3,
                "quarterly" => 1.1,
                "irregular" => 0.5,
                "monthly" => 0.42,
                "bi-weekly" => 1.3,
                "weekly" => 0.55,
                _ => 1.0
            };
        }
    }
}
