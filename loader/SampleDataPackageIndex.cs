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

namespace RockSampleData.Loader
{
    /// <summary>
    /// The hosted list of dataset packages, index.json (decision 123). The format is in the
    /// loader README, under "The package index".
    /// </summary>
    public sealed class SampleDataPackageIndex
    {
        /// <summary>
        /// The index format this loader reads.
        /// </summary>
        public const int CurrentFormatVersion = 1;

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private SampleDataPackageIndex( int formatVersion, IReadOnlyList<SampleDataPackageIndexEntry> packages )
        {
            FormatVersion = formatVersion;
            Packages = packages;
        }

        /// <summary>
        /// The index format.
        /// </summary>
        public int FormatVersion { get; }

        /// <summary>
        /// Every package in the index, in the order the index lists them.
        /// </summary>
        public IReadOnlyList<SampleDataPackageIndexEntry> Packages { get; }

        /// <summary>
        /// Reads index.json, after checking that Spark signed it. The index is read only from the bytes
        /// the signature covers (decision 172).
        /// </summary>
        /// <exception cref="InvalidDataException">The index is not signed by one of Spark's keys, its signature does not match, the text is not JSON, it uses another format version, or an entry is missing a required field.</exception>
        public static SampleDataPackageIndex Parse( string json )
        {
            return Parse( json, IndexSignature.SparkKeys );
        }

        /// <summary>
        /// Reads an index signed by one of the given keys. Only tests trust keys other than Spark's.
        /// </summary>
        internal static SampleDataPackageIndex Parse( string json, IReadOnlyList<IndexSigningKey> keys )
        {
            var signedJson = IndexSignature.Verify( json, keys );

            IndexFile file;
            try
            {
                file = JsonSerializer.Deserialize<IndexFile>( signedJson, Json );
            }
            catch ( JsonException ex )
            {
                throw new InvalidDataException( $"The package index is not valid JSON: {ex.Message}", ex );
            }

            if ( file == null )
            {
                throw new InvalidDataException( "The package index is empty." );
            }

            if ( file.FormatVersion != CurrentFormatVersion )
            {
                throw new InvalidDataException( $"The package index uses format {file.FormatVersion}; this loader reads format {CurrentFormatVersion}." );
            }

            var packages = ( file.Packages ?? new List<IndexFileEntry>() ).Select( ( entry, i ) => ToEntry( entry, i ) ).ToList();
            return new SampleDataPackageIndex( file.FormatVersion, packages );
        }

        private static SampleDataPackageIndexEntry ToEntry( IndexFileEntry entry, int position )
        {
            string Required( string value, string name )
            {
                return string.IsNullOrWhiteSpace( value )
                    ? throw new InvalidDataException( $"Package {position + 1} in the package index has no {name}." )
                    : value;
            }

            var sha256 = Required( entry?.Sha256, "sha256" );
            if ( sha256.Length != 64 || !sha256.All( Uri.IsHexDigit ) )
            {
                throw new InvalidDataException( $"Package {position + 1} in the package index has a sha256 that is not 64 hex digits." );
            }

            return new SampleDataPackageIndexEntry(
                Required( entry.Dataset, "dataset" ),
                Required( entry.Name, "name" ),
                entry.Description ?? string.Empty,
                entry.BestFor ?? string.Empty,
                ( entry.Highlights ?? new List<string>() ).Where( h => !string.IsNullOrWhiteSpace( h ) ).ToList(),
                entry.People,
                entry.Households,
                entry.Campuses,
                entry.TypicalInstallSeconds,
                Required( entry.Version, "version" ),
                Required( entry.Url, "url" ),
                sha256.ToLowerInvariant(),
                entry.Size,
                RockMajor( entry, position ),
                MinimumLoaderVersion( entry.MinimumLoaderVersion, position ),
                Required( entry.RockVersion, "rockVersion" ),
                Required( entry.RockMigration, "rockMigration" ) );
        }

        /// <summary>
        /// The entry's Rock major release. An index written before rockMajor gives only the exact
        /// Rock version, whose first number is the major.
        /// </summary>
        private static int RockMajor( IndexFileEntry entry, int position )
        {
            return entry.RockMajor > 0 ? entry.RockMajor
                : RockVersionInfo.MajorOf( entry.RockVersion )
                    ?? throw new InvalidDataException( $"Package {position + 1} in the package index has no rockMajor, and its rockVersion does not give one." );
        }

        /// <summary>
        /// The entry's minimum loader version, checked to be a version. Null in an index written before it.
        /// </summary>
        private static string MinimumLoaderVersion( string version, int position )
        {
            if ( string.IsNullOrWhiteSpace( version ) )
            {
                return null;
            }

            try
            {
                LoaderVersion.Compare( version, version );
                return version;
            }
            catch ( FormatException ex )
            {
                throw new InvalidDataException( $"Package {position + 1} in the package index has a minimumLoaderVersion that is not a version. {ex.Message}", ex );
            }
        }

        private sealed class IndexFile
        {
            public int FormatVersion { get; set; }

            public List<IndexFileEntry> Packages { get; set; }
        }

        private sealed class IndexFileEntry
        {
            public string Dataset { get; set; }

            public string Name { get; set; }

            public string Description { get; set; }

            public string BestFor { get; set; }

            public List<string> Highlights { get; set; }

            public int People { get; set; }

            public int Households { get; set; }

            public int Campuses { get; set; }

            public int TypicalInstallSeconds { get; set; }

            public string Version { get; set; }

            public string Url { get; set; }

            public string Sha256 { get; set; }

            public long Size { get; set; }

            public int RockMajor { get; set; }

            public string MinimumLoaderVersion { get; set; }

            public string RockVersion { get; set; }

            public string RockMigration { get; set; }
        }
    }

    /// <summary>
    /// One package in the package index.
    /// </summary>
    public sealed class SampleDataPackageIndexEntry
    {
        internal SampleDataPackageIndexEntry( string dataset, string name, string description, string bestFor, IReadOnlyList<string> highlights, int people, int households, int campuses, int typicalInstallSeconds, string version, string url, string sha256, long size, int rockMajor, string minimumLoaderVersion, string rockVersion, string rockMigration )
        {
            RockMajor = rockMajor;
            MinimumLoaderVersion = minimumLoaderVersion;
            Dataset = dataset;
            Name = name;
            Description = description;
            BestFor = bestFor;
            Highlights = highlights;
            People = people;
            Households = households;
            Campuses = campuses;
            TypicalInstallSeconds = typicalInstallSeconds;
            Version = version;
            Url = url;
            Sha256 = sha256;
            Size = size;
            RockVersion = rockVersion;
            RockMigration = rockMigration;
        }

        /// <summary>
        /// The dataset key, for example "core-1".
        /// </summary>
        public string Dataset { get; }

        /// <summary>
        /// The dataset's name, for example "Core 1".
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// A sentence or two about who is in the dataset: the card's summary. Empty when the index has none.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// One sentence about what the dataset is best for. Empty when the index has none, as in an index
        /// written before the full card joined it.
        /// </summary>
        public string BestFor { get; }

        /// <summary>
        /// Two to four short phrases worth knowing about the dataset. Empty when the index has none.
        /// </summary>
        public IReadOnlyList<string> Highlights { get; }

        /// <summary>
        /// About how many people the dataset loads. 0 when the index has none.
        /// </summary>
        public int People { get; }

        /// <summary>
        /// About how many family households the dataset loads, businesses included. 0 when the index has none.
        /// </summary>
        public int Households { get; }

        /// <summary>
        /// How many campuses the dataset has. 0 when the index has none.
        /// </summary>
        public int Campuses { get; }

        /// <summary>
        /// About how long a load of every part takes, in seconds, measured on a developer workstation.
        /// 0 when the index has none.
        /// </summary>
        public int TypicalInstallSeconds { get; }

        /// <summary>
        /// The package release, for example "1.0.0".
        /// </summary>
        public string Version { get; }

        /// <summary>
        /// Where to download the package: an absolute URL, or one relative to the index's own URL.
        /// </summary>
        public string Url { get; }

        /// <summary>
        /// The SHA-256 of the package .zip, in lowercase hex. Check a download against it before opening it.
        /// </summary>
        public string Sha256 { get; }

        /// <summary>
        /// The size of the package .zip in bytes.
        /// </summary>
        public long Size { get; }

        /// <summary>
        /// The Rock major release the package is for, for example 21 (decision 52).
        /// </summary>
        public int RockMajor { get; }

        /// <summary>
        /// The oldest loader that can load the package, for example "0.1.0-alpha.13" (decision 158).
        /// Null in an index written before it.
        /// </summary>
        public string MinimumLoaderVersion { get; }

        /// <summary>
        /// The exact Rock the package was built and verified on, for example "21.0.2".
        /// </summary>
        public string RockVersion { get; }

        /// <summary>
        /// The newest migration of that Rock. Loaders before 0.1.0-alpha.13 match packages by it, so the
        /// index keeps it for them. Newer loaders use <see cref="IsInstallableOn"/>.
        /// </summary>
        public string RockMigration { get; }

        /// <summary>
        /// True when this loader can install the package on a Rock, for example "20.0.10": the Rock is the
        /// package's major release, and this loader is at least the package's minimum loader version.
        /// </summary>
        public bool IsInstallableOn( string rockVersion )
        {
            return RockVersionInfo.MajorOf( rockVersion ) == RockMajor && LoaderVersion.IsAtLeast( MinimumLoaderVersion );
        }
    }
}
