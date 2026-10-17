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

using RockSampleData.Loader.Model;
using RockSampleData.Loader.Packages;

namespace RockSampleData.Loader.Generation
{
    /// <summary>
    /// The committed address pool (addresses/pool.json): real streets with house numbers that
    /// do not exist, already placed around each campus. Each address is handed out once.
    /// </summary>
    internal sealed class AddressPool
    {
        private readonly Dictionary<string, Queue<CastAddress>> _byCampus = new Dictionary<string, Queue<CastAddress>>();

        private AddressPool()
        {
        }

        /// <summary>
        /// Loads the pool, shuffling each campus's list with the seed so a run is repeatable.
        /// Each campus shuffles on its own, so a package that carries only its dataset's campuses
        /// hands out the same addresses. Returns an empty pool when the file is missing.
        /// </summary>
        public static AddressPool Load( IPackageFiles files, string seed )
        {
            var pool = new AddressPool();
            if ( !files.Exists( "addresses/pool.json" ) )
            {
                return pool;
            }

            using var document = JsonDocument.Parse( files.ReadText( "addresses/pool.json" ) );
            foreach ( var campus in document.RootElement.GetProperty( "campuses" ).EnumerateObject() )
            {
                var random = new SeededRandom( $"{seed}:addresses:{campus.Name}" );
                var rows = campus.Value.EnumerateArray().Select( row => new CastAddress
                {
                    Type = "home",
                    Street1 = row[0].GetString(),
                    City = row[1].GetString(),
                    State = "AZ",
                    PostalCode = row[2].GetString(),
                    Latitude = row[3].GetDouble(),
                    Longitude = row[4].GetDouble()
                } ).ToList();

                // Fisher-Yates with the seeded stream.
                for ( var i = rows.Count - 1; i > 0; i-- )
                {
                    var j = random.Between( 0, i );
                    ( rows[i], rows[j] ) = ( rows[j], rows[i] );
                }

                pool._byCampus[campus.Name] = new Queue<CastAddress>( rows );
            }

            return pool;
        }

        /// <summary>
        /// The next unused address for a campus, or null when the campus has none left.
        /// </summary>
        public CastAddress Next( string campusKey )
        {
            return _byCampus.TryGetValue( campusKey, out var queue ) && queue.Count > 0 ? queue.Dequeue() : null;
        }
    }
}
