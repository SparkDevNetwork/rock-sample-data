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

namespace RockSampleData.Loader.Generation
{
    /// <summary>
    /// A random source seeded from text (for example a household Guid), so the same seed,
    /// anchor, and inputs always produce the same data. Each household gets its own stream,
    /// so adding a household never changes anyone else's data.
    /// </summary>
    internal sealed class SeededRandom
    {
        private readonly Random _random;

        public SeededRandom( string seed )
        {
            _random = new Random( StableHash( seed ) );
        }

        /// <summary>
        /// True with the given probability (0 to 1).
        /// </summary>
        public bool Chance( double probability )
        {
            return _random.NextDouble() < probability;
        }

        /// <summary>
        /// A whole number from min to max, inclusive.
        /// </summary>
        public int Between( int min, int max )
        {
            return _random.Next( min, max + 1 );
        }

        /// <summary>
        /// A number from min up to max.
        /// </summary>
        public double Between( double min, double max )
        {
            return min + _random.NextDouble() * ( max - min );
        }

        /// <summary>
        /// A long-tailed multiplier around 1 (log-normal, with an average of 1). A bigger spread gives
        /// more small values and a few large ones.
        /// </summary>
        public double LogNormal( double spread )
        {
            // Box-Muller turns two even draws into one bell-curve draw.
            var normal = Math.Sqrt( -2 * Math.Log( 1 - _random.NextDouble() ) ) * Math.Cos( 2 * Math.PI * _random.NextDouble() );
            return Math.Exp( ( spread * normal ) - ( spread * spread / 2 ) );
        }

        /// <summary>
        /// Picks a key by weight. Weights do not need to add to anything in particular.
        /// </summary>
        public string Pick( IReadOnlyDictionary<string, double> weights )
        {
            var total = weights.Values.Sum();
            var roll = _random.NextDouble() * total;
            foreach ( var kv in weights )
            {
                roll -= kv.Value;
                if ( roll <= 0 )
                {
                    return kv.Key;
                }
            }

            return weights.Keys.Last();
        }

        /// <summary>
        /// Picks one item from a list.
        /// </summary>
        public T Pick<T>( IReadOnlyList<T> items )
        {
            return items[_random.Next( items.Count )];
        }

        [ThreadStatic]
        private static System.Security.Cryptography.SHA256 _sha;

        /// <summary>
        /// A Guid from a 128-bit hash of the text, for tables with millions of rows: streams seeded from a 32-bit
        /// hash repeat now and then at that size, and two rows would get the same Guid.
        /// </summary>
        public static Guid StableGuid( string text )
        {
            var sha = _sha ??= System.Security.Cryptography.SHA256.Create();
            var bytes = sha.ComputeHash( System.Text.Encoding.UTF8.GetBytes( text ) ).Take( 16 ).ToArray();
            bytes[7] = ( byte ) ( ( bytes[7] & 0x0F ) | 0x40 );
            bytes[8] = ( byte ) ( ( bytes[8] & 0x3F ) | 0x80 );
            return new Guid( bytes );
        }

        /// <summary>
        /// A Guid from this stream, so generated records keep the same Guid on every run with the same seed.
        /// </summary>
        public Guid NextGuid()
        {
            var bytes = new byte[16];
            _random.NextBytes( bytes );
            bytes[7] = ( byte ) ( ( bytes[7] & 0x0F ) | 0x40 );
            bytes[8] = ( byte ) ( ( bytes[8] & 0x3F ) | 0x80 );
            return new Guid( bytes );
        }

        /// <summary>
        /// A stable 32-bit hash (FNV-1a). string.GetHashCode changes per process, so it cannot be used.
        /// </summary>
        private static int StableHash( string text )
        {
            unchecked
            {
                var hash = 2166136261;
                foreach ( var c in text )
                {
                    hash = ( hash ^ c ) * 16777619;
                }

                return ( int ) hash;
            }
        }
    }
}
