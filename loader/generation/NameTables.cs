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

using System.Text.Json;

using RockSampleData.Loader.Packages;

namespace RockSampleData.Loader.Generation
{
    /*
        11/23/26 - CLAUDE

        Crowd names came from 56 female first names, 56 male first names, and 88 last names at every age, so Full 7's
        25,000 people had only about 9,900 possible full names. The tables in names/ hold the top 200 first names for
        each gender and birth decade, from Social Security baby names, and the top 2,000 surnames, from the Census
        Bureau, each with its count. A first name is drawn by the person's gender and birth decade, and a surname by its
        count. Each draw takes exactly one number from the caller's stream, as picking from the old lists did, so every
        later draw in a household stays where it was and only names change.

        Reason: Crowd names follow real name data by birth decade (decision D-crowd-names-1).
    */

    /// <summary>
    /// The Crowd's name tables (names/*.json): first names by gender and birth decade, surnames, and nicknames.
    /// </summary>
    internal sealed class NameTables
    {
        public const string FirstNamesPath = "names/first-names.json";

        public const string SurnamesPath = "names/surnames.json";

        public const string NicknamesPath = "names/nicknames.json";

        /// <summary>
        /// The birth decades an adult of unknown age is drawn from, such as a guest who sends a prayer request: people
        /// born in the 1950s to the 1990s.
        /// </summary>
        private const int AdultFirstDecade = 1950;

        private const int AdultLastDecade = 1990;

        private readonly SortedDictionary<int, (WeightedNames Female, WeightedNames Male)> _firstNames;
        private readonly (WeightedNames Female, WeightedNames Male) _adultFirstNames;
        private readonly WeightedNames _surnames;
        private readonly Dictionary<string, string[]> _nicknames;

        private NameTables( SortedDictionary<int, (WeightedNames Female, WeightedNames Male)> firstNames, WeightedNames surnames, Dictionary<string, string[]> nicknames )
        {
            _firstNames = firstNames;
            _surnames = surnames;
            _nicknames = nicknames;

            var adultDecades = firstNames.Where( d => d.Key >= AdultFirstDecade && d.Key <= AdultLastDecade ).Select( d => d.Value ).DefaultIfEmpty( firstNames.Values.Last() ).ToList();
            _adultFirstNames = (WeightedNames.Combine( adultDecades.Select( d => d.Female ) ), WeightedNames.Combine( adultDecades.Select( d => d.Male ) ));
        }

        /// <summary>
        /// Reads the package's name tables. A package built before them has none, and gets the short lists it was built with.
        /// </summary>
        public static NameTables Load( IPackageFiles files )
        {
            if ( !files.Exists( FirstNamesPath ) )
            {
                var spike = (WeightedNames.Even( CrowdNames.FemaleFirstNames ), WeightedNames.Even( CrowdNames.MaleFirstNames ));
                return new NameTables( new SortedDictionary<int, (WeightedNames, WeightedNames)> { [0] = spike }, WeightedNames.Even( CrowdNames.LastNames ), new Dictionary<string, string[]>() );
            }

            var firstNames = new SortedDictionary<int, (WeightedNames Female, WeightedNames Male)>();
            using ( var document = JsonDocument.Parse( files.ReadText( FirstNamesPath ) ) )
            {
                foreach ( var decade in document.RootElement.GetProperty( "decades" ).EnumerateObject() )
                {
                    firstNames[int.Parse( decade.Name )] = (WeightedNames.Read( decade.Value.GetProperty( "female" ) ), WeightedNames.Read( decade.Value.GetProperty( "male" ) ));
                }
            }

            WeightedNames surnames;
            using ( var document = JsonDocument.Parse( files.ReadText( SurnamesPath ) ) )
            {
                surnames = WeightedNames.Read( document.RootElement.GetProperty( "surnames" ) );
            }

            var nicknames = new Dictionary<string, string[]>( StringComparer.Ordinal );
            using ( var document = JsonDocument.Parse( files.ReadText( NicknamesPath ) ) )
            {
                foreach ( var name in document.RootElement.GetProperty( "nicknames" ).EnumerateObject() )
                {
                    nicknames[name.Name] = name.Value.EnumerateArray().Select( n => n.GetString() ).ToArray();
                }
            }

            return new NameTables( firstNames, surnames, nicknames );
        }

        /// <summary>
        /// A first name for someone of this gender born in this year, from their birth decade's table. A year before the
        /// first decade uses the first, and a year after the last uses the last. One draw from the stream.
        /// </summary>
        public string FirstName( bool isFemale, int birthYear, SeededRandom random )
        {
            var decade = birthYear - ( ( ( birthYear % 10 ) + 10 ) % 10 );
            if ( !_firstNames.TryGetValue( decade, out var table ) )
            {
                table = decade < _firstNames.Keys.First() ? _firstNames.Values.First() : _firstNames.Values.Last();
            }

            return ( isFemale ? table.Female : table.Male ).Pick( random );
        }

        /// <summary>
        /// A first name for an adult whose age is not known, from the tables for people born in the 1950s to the 1990s
        /// together. One draw from the stream.
        /// </summary>
        public string AdultFirstName( bool isFemale, SeededRandom random )
        {
            return ( isFemale ? _adultFirstNames.Female : _adultFirstNames.Male ).Pick( random );
        }

        /// <summary>
        /// A surname, by how many people have it. One draw from the stream.
        /// </summary>
        public string Surname( SeededRandom random )
        {
            return _surnames.Pick( random );
        }

        /// <summary>
        /// The usual nicknames for a first name, the most common first, or none.
        /// </summary>
        public IReadOnlyList<string> Nicknames( string firstName )
        {
            return _nicknames.TryGetValue( firstName, out var nicknames ) ? nicknames : Array.Empty<string>();
        }

        /// <summary>
        /// Names with weights, picked with one draw.
        /// </summary>
        private sealed class WeightedNames
        {
            private readonly string[] _names;

            /// <summary>
            /// Each name's running total of weights, or null when every name weighs the same.
            /// </summary>
            private readonly double[] _cumulative;

            private WeightedNames( string[] names, double[] cumulative )
            {
                _names = names;
                _cumulative = cumulative;
            }

            /// <summary>
            /// Names that weigh the same, picked exactly as a list pick does, so an older package draws the names it always did.
            /// </summary>
            public static WeightedNames Even( IEnumerable<string> names ) => new WeightedNames( names.ToArray(), null );

            /// <summary>
            /// Reads a table's [ name, count ] pairs.
            /// </summary>
            public static WeightedNames Read( JsonElement pairs )
            {
                return FromCounts( pairs.EnumerateArray().Select( p => (p[0].GetString(), p[1].GetDouble()) ) );
            }

            /// <summary>
            /// Several tables as one, with each name's counts added together.
            /// </summary>
            public static WeightedNames Combine( IEnumerable<WeightedNames> tables )
            {
                var counts = new Dictionary<string, double>( StringComparer.Ordinal );
                var order = new List<string>();
                foreach ( var table in tables )
                {
                    for ( var i = 0; i < table._names.Length; i++ )
                    {
                        var weight = table._cumulative == null ? 1 : table._cumulative[i] - ( i == 0 ? 0 : table._cumulative[i - 1] );
                        if ( !counts.ContainsKey( table._names[i] ) )
                        {
                            order.Add( table._names[i] );
                            counts[table._names[i]] = 0;
                        }

                        counts[table._names[i]] += weight;
                    }
                }

                return FromCounts( order.Select( n => (n, counts[n]) ) );
            }

            private static WeightedNames FromCounts( IEnumerable<(string Name, double Count)> counts )
            {
                var list = counts.ToList();
                var cumulative = new double[list.Count];
                var total = 0d;
                for ( var i = 0; i < list.Count; i++ )
                {
                    total += list[i].Count;
                    cumulative[i] = total;
                }

                return new WeightedNames( list.Select( n => n.Name ).ToArray(), cumulative );
            }

            public string Pick( SeededRandom random )
            {
                if ( _cumulative == null )
                {
                    return random.Pick( _names );
                }

                var roll = random.Between( 0.0, _cumulative[_cumulative.Length - 1] );
                var index = Array.BinarySearch( _cumulative, roll );
                return _names[Math.Min( index < 0 ? ~index : index, _names.Length - 1 )];
            }
        }
    }
}
