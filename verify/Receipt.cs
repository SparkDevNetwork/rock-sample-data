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

using System.Security.Cryptography;
using System.Text.Json;

using RockSampleData.Loader.Packages;

namespace RockSampleData.Verify
{
    /// <summary>
    /// A receipt: the record that one exact package (by its SHA-256) passed every check on one exact Rock.
    /// The verify tool writes it next to the package as {dataset}.verify.json, and publishing refuses a package
    /// without a passing receipt (see <see cref="CheckFolder"/>).
    /// </summary>
    internal sealed class Receipt
    {
        public const int CurrentFormat = 1;

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public int Format { get; set; } = CurrentFormat;

        public bool Passed { get; set; }

        public ReceiptPackage Package { get; set; }

        public PackageRock Rock { get; set; }

        public DateTime VerifiedAt { get; set; }

        /// <summary>
        /// The install day the verification load used. Checks hold at any anchor; this one is recorded so a run can be repeated.
        /// </summary>
        public string Anchor { get; set; }

        public string Seed { get; set; }

        /// <summary>
        /// This repository's commit that ran the checks.
        /// </summary>
        public PackageSource Verifier { get; set; }

        public TrustedBase.Certificate Base { get; set; }

        public List<CheckResult> Checks { get; set; } = new List<CheckResult>();

        internal sealed class ReceiptPackage
        {
            public string File { get; set; }

            public string Sha256 { get; set; }

            public string Dataset { get; set; }

            public PackageSource Source { get; set; }

            /// <summary>
            /// The oldest loader that can load the package, from its package.json. The release copies it into the package index.
            /// </summary>
            public string MinimumLoaderVersion { get; set; }
        }

        public static string PathFor( string packagePath )
        {
            return Path.Combine( Path.GetDirectoryName( packagePath ), $"{Path.GetFileNameWithoutExtension( packagePath )}.verify.json" );
        }

        public static string Sha256( string path )
        {
            using var stream = File.OpenRead( path );
            return Convert.ToHexStringLower( SHA256.HashData( stream ) );
        }

        public void Save( string path )
        {
            File.WriteAllText( path, JsonSerializer.Serialize( this, Json ).Replace( "\r\n", "\n" ) + "\n" );
        }

        /// <summary>
        /// The publishing gate. Every package in the folder must have a receipt for exactly its bytes, that passed,
        /// for the Rock the package names, made from committed source by a committed verifier. Returns the problems.
        /// </summary>
        public static List<string> CheckFolder( string folder )
        {
            var problems = new List<string>();
            var packages = Directory.GetFiles( folder, "*.zip" ).OrderBy( p => p, StringComparer.Ordinal ).ToList();
            if ( packages.Count == 0 )
            {
                problems.Add( $"{folder} has no packages." );
            }

            foreach ( var package in packages )
            {
                var name = Path.GetFileName( package );
                var receiptPath = PathFor( package );
                if ( !File.Exists( receiptPath ) )
                {
                    problems.Add( $"{name} has no receipt. Run the verify tool on it." );
                    continue;
                }

                var receipt = JsonSerializer.Deserialize<Receipt>( File.ReadAllText( receiptPath ), Json );
                var manifest = SampleDataPackage.Open( package ).Manifest;
                if ( receipt.Format != CurrentFormat )
                {
                    problems.Add( $"{name}: the receipt is format {receipt.Format}; this gate reads format {CurrentFormat}." );
                }

                if ( receipt.Package?.Sha256 != Sha256( package ) )
                {
                    problems.Add( $"{name}: the receipt is for different bytes. The package changed after it was verified." );
                }

                if ( !receipt.Passed )
                {
                    problems.Add( $"{name}: verification failed ({string.Join( ", ", receipt.Checks.Where( c => !c.Passed ).Select( c => c.Name ) )})." );
                }

                if ( receipt.Rock?.Major != manifest.Rock?.Major || receipt.Rock?.Migration != manifest.Rock?.Migration || receipt.Rock?.Version != manifest.Rock?.Version )
                {
                    problems.Add( $"{name}: verified on Rock {receipt.Rock?.Version} ({receipt.Rock?.Migration}), but the package is for Rock {manifest.Rock?.Major}, built on {manifest.Rock?.Version} ({manifest.Rock?.Migration})." );
                }

                if ( receipt.Package?.MinimumLoaderVersion != manifest.MinimumLoaderVersion || string.IsNullOrEmpty( manifest.MinimumLoaderVersion ) )
                {
                    problems.Add( $"{name}: the receipt says the package needs loader {receipt.Package?.MinimumLoaderVersion ?? "(none)"}, but its package.json says {manifest.MinimumLoaderVersion ?? "(none)"}. Every published package names its oldest loader." );
                }

                if ( manifest.Source?.IsModified != false )
                {
                    problems.Add( $"{name}: built from uncommitted changes. Publish only packages built from a commit." );
                }

                if ( receipt.Verifier?.IsModified != false )
                {
                    problems.Add( $"{name}: verified by uncommitted changes to the checks. Verify again from a commit." );
                }
            }

            return problems;
        }
    }
}
