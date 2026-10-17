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
        /// The one Rock version this package is for. The loader refuses any other.
        /// </summary>
        public PackageRock Rock { get; set; }

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
        /// The Rock version, for example "21.0.1". For people and the download list.
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// The newest Entity Framework migration in that Rock, for example
        /// "202609222138362_AddWorkflowBuilder". A Rock database stores no version number,
        /// so the loader compares this with the database's newest migration.
        /// </summary>
        public string Migration { get; set; }
    }
}
