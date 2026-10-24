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

using System.Text.Encodings.Web;
using System.Text.Json;

namespace RockSampleData.Loader.Packages
{
    /// <summary>
    /// package.json: what a dataset package holds and which Rock it was built for.
    /// </summary>
    public sealed class PackageManifest
    {
        internal static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            NewLine = "\n"
        };

        /// <summary>
        /// The package format. A loader reads only the format versions it knows.
        /// </summary>
        public int FormatVersion { get; set; }

        /// <summary>
        /// The dataset key, for example "core-1".
        /// </summary>
        public string Dataset { get; set; }

        /// <summary>
        /// The dataset's display name, for example "Core 1".
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The calibration profile the builder resolved into profile.json.
        /// </summary>
        public string Profile { get; set; }

        /// <summary>
        /// The rock-sample-data commit the package was built from.
        /// </summary>
        public PackageSource Source { get; set; }

        /// <summary>
        /// The Rock major release this package is for, and the exact Rock it was built and verified on.
        /// The loader refuses a database on any other major release.
        /// </summary>
        public PackageRock Rock { get; set; }

        /// <summary>
        /// The oldest loader that can load this package, for example "0.1.0-alpha.13". An older loader
        /// refuses it. Null in packages built before loaders checked it.
        /// </summary>
        public string MinimumLoaderVersion { get; set; }

        /// <summary>
        /// Every file in the package except package.json, with its SHA-256 (lowercase hex), in ordinal order.
        /// </summary>
        public SortedDictionary<string, string> Files { get; set; } = new SortedDictionary<string, string>( StringComparer.Ordinal );
    }

    /// <summary>
    /// Where a package came from.
    /// </summary>
    public sealed class PackageSource
    {
        /// <summary>
        /// The full commit hash, or "unknown" when the builder could not read it.
        /// </summary>
        public string Commit { get; set; }

        /// <summary>
        /// True when the working tree had uncommitted changes, so the commit alone cannot rebuild the package.
        /// </summary>
        public bool IsModified { get; set; }
    }

    /// <summary>
    /// The Rock a package is for.
    /// </summary>
    public sealed class PackageRock
    {
        /// <summary>
        /// The Rock major release the package is for, for example 21. Rock's rule is no model changes
        /// in a dot release, so one package serves every Rock 21 (decision D-rock-major-1). 0 in packages
        /// built before this field, whose major comes from <see cref="Version"/>.
        /// </summary>
        public int Major { get; set; }

        /// <summary>
        /// The exact Rock the package was built and verified on, for example "21.0.2".
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// The newest Entity Framework migration in that Rock, for example
        /// "202610141827486_CodeGenerated_20261014". Loaders before 0.1.0-alpha.13 refuse a database
        /// whose newest migration differs, so it stays for them. Newer loaders compare the major release.
        /// </summary>
        public string Migration { get; set; }

        /// <summary>
        /// The major release, read from <see cref="Version"/> in a package built before <see cref="Major"/>.
        /// </summary>
        internal int? MajorRelease => Major > 0 ? Major : RockVersionInfo.MajorOf( Version );
    }
}
