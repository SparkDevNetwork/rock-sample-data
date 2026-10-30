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

namespace RockSampleData.Loader
{
    /// <summary>
    /// One dataset as a person choosing it sees it: what it holds, what it is for, and how big it is.
    /// </summary>
    public sealed class SampleDataDatasetInfo
    {
        internal SampleDataDatasetInfo( string key, string name, string summary, string bestFor, IReadOnlyList<string> highlights, int people, int households, int campuses, int typicalInstallSeconds, int databaseSpaceMegabytes )
        {
            Key = key;
            Name = name;
            Summary = summary;
            BestFor = bestFor;
            Highlights = highlights;
            People = people;
            Households = households;
            Campuses = campuses;
            TypicalInstallSeconds = typicalInstallSeconds;
            DatabaseSpaceMegabytes = databaseSpaceMegabytes;
        }

        /// <summary>
        /// The dataset key, for example "core-1". A package's manifest names its dataset by this key.
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// The name to show, for example "Core 1".
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// One or two plain sentences that say who is in the dataset.
        /// </summary>
        public string Summary { get; }

        /// <summary>
        /// One plain sentence that says what the dataset is best for.
        /// </summary>
        public string BestFor { get; }

        /// <summary>
        /// Two to four short phrases worth knowing about the dataset.
        /// </summary>
        public IReadOnlyList<string> Highlights { get; }

        /// <summary>
        /// How many people a load adds, measured from a real load at the default seed and 3 years of history.
        /// Round it for display.
        /// </summary>
        public int People { get; }

        /// <summary>
        /// How many family households a load adds, measured the same way. Businesses count.
        /// </summary>
        public int Households { get; }

        /// <summary>
        /// How many campuses the dataset has.
        /// </summary>
        public int Campuses { get; }

        /// <summary>
        /// About how long a load of every part, check images included, takes, in seconds. Measured on a
        /// developer workstation, so a slower server takes longer.
        /// </summary>
        public int TypicalInstallSeconds { get; }

        /// <summary>
        /// About how much disk space the database needs, in megabytes: its data and log files at their
        /// largest after a load of every part, check images included, counting the new Rock install the load
        /// starts from. Measured by the verify tool and rounded up to the next 100 MB. Show it in gigabytes.
        /// </summary>
        public int DatabaseSpaceMegabytes { get; }
    }

    /// <summary>
    /// The datasets this loader can install, read from the dataset manifests built into it (decision 124).
    /// </summary>
    public static class SampleDataDatasets
    {
        /*
            10/21/26 - CLAUDE

            The card text and numbers live in datasets/{key}.json beside the settings the builder reads,
            and the loader project embeds those files. So the catalog ships in the same NuGet package as
            the loader that installs those datasets, and the two can never disagree. The builder refuses
            to write a package whose manifest is missing a card field, and the verify tool fails a load
            whose people or households drift more than 2% from the manifest's numbers.

            Reason: Rock's installer shows the datasets without downloading a package first.
        */

        private const string ResourcePrefix = "RockSampleData.Loader.datasets.";

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        private static readonly Lazy<IReadOnlyList<SampleDataDatasetInfo>> Embedded = new Lazy<IReadOnlyList<SampleDataDatasetInfo>>( ReadEmbedded );

        /// <summary>
        /// Every dataset, in key order: core-1, core-3, full-7.
        /// </summary>
        public static IReadOnlyList<SampleDataDatasetInfo> All => Embedded.Value;

        /// <summary>
        /// The dataset with this key, for example "core-1". Null when there is none.
        /// </summary>
        public static SampleDataDatasetInfo Get( string key )
        {
            return All.FirstOrDefault( d => string.Equals( d.Key, key, StringComparison.Ordinal ) );
        }

        /// <summary>
        /// The card for one dataset manifest.
        /// </summary>
        /// <exception cref="InvalidDataException">The manifest is missing a card field.</exception>
        internal static SampleDataDatasetInfo FromManifest( DatasetManifest manifest, string file )
        {
            var missing = new List<string>();
            void Require( bool isPresent, string field )
            {
                if ( !isPresent )
                {
                    missing.Add( field );
                }
            }

            var highlights = ( manifest.Highlights ?? new List<string>() ).Where( h => !string.IsNullOrWhiteSpace( h ) ).ToList();
            Require( !string.IsNullOrWhiteSpace( manifest.Key ), "key" );
            Require( !string.IsNullOrWhiteSpace( manifest.Name ), "name" );
            Require( !string.IsNullOrWhiteSpace( manifest.Summary ), "summary" );
            Require( !string.IsNullOrWhiteSpace( manifest.BestFor ), "bestFor" );
            Require( highlights.Count >= 2 && highlights.Count <= 4, "highlights (2 to 4)" );
            Require( manifest.People > 0, "people" );
            Require( manifest.Households > 0, "households" );
            Require( manifest.Campuses > 0, "campuses" );
            Require( manifest.TypicalInstallSeconds > 0, "typicalInstallSeconds" );
            Require( manifest.DatabaseSpaceMegabytes > 0, "databaseSpaceMegabytes" );
            if ( missing.Count > 0 )
            {
                throw new InvalidDataException( $"{file} is missing the dataset card's {string.Join( ", ", missing )}." );
            }

            return new SampleDataDatasetInfo( manifest.Key, manifest.Name, manifest.Summary, manifest.BestFor, highlights, manifest.People, manifest.Households, manifest.Campuses, manifest.TypicalInstallSeconds, manifest.DatabaseSpaceMegabytes );
        }

        private static IReadOnlyList<SampleDataDatasetInfo> ReadEmbedded()
        {
            var assembly = typeof( SampleDataDatasets ).Assembly;
            var datasets = new List<SampleDataDatasetInfo>();
            foreach ( var name in assembly.GetManifestResourceNames().Where( n => n.StartsWith( ResourcePrefix, StringComparison.Ordinal ) ) )
            {
                using ( var stream = assembly.GetManifestResourceStream( name ) )
                using ( var reader = new StreamReader( stream ) )
                {
                    var file = $"datasets/{name.Substring( ResourcePrefix.Length )}";
                    var manifest = JsonSerializer.Deserialize<DatasetManifest>( reader.ReadToEnd(), Json );
                    datasets.Add( FromManifest( manifest, file ) );
                }
            }

            return datasets.OrderBy( d => d.Key, StringComparer.Ordinal ).ToList();
        }
    }
}
