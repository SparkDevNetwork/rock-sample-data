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

using System.Text.RegularExpressions;
using System.Xml.Linq;

using RockSampleData.Loader.Packages;

namespace RockSampleData.Builder
{
    /// <summary>
    /// Reads which Rock a package is for from a Rock checkout: the version in Directory.Build.props
    /// and the newest Entity Framework migration in Rock.Migrations.
    /// </summary>
    internal static class RockCheckout
    {
        private static readonly Regex MigrationFile = new Regex( @"^\d{15}_[A-Za-z0-9_]+\.cs$" );

        /// <summary>
        /// Reads the Rock version and newest migration from a checkout. Either can be given instead,
        /// and a given value wins. Throws when a value is missing and no checkout supplies it.
        /// </summary>
        public static PackageRock Read( string checkoutPath, string version, string migration )
        {
            if ( !string.IsNullOrEmpty( checkoutPath ) )
            {
                version ??= ReadVersion( checkoutPath );
                migration ??= ReadNewestMigration( checkoutPath );
            }

            if ( string.IsNullOrEmpty( version ) || string.IsNullOrEmpty( migration ) )
            {
                throw new InvalidOperationException( "A package must say which Rock it is for. Pass --rock <Rock checkout> (or set ROCK_PATH), or both --rock-version and --rock-migration." );
            }

            return new PackageRock { Version = version, Migration = migration };
        }

        /// <summary>
        /// The Version property in the checkout's Directory.Build.props, for example "21.0.1".
        /// </summary>
        private static string ReadVersion( string checkoutPath )
        {
            var props = Path.Combine( checkoutPath, "Directory.Build.props" );
            if ( !File.Exists( props ) )
            {
                throw new FileNotFoundException( "The Rock checkout has no Directory.Build.props to read the version from. Pass --rock-version.", props );
            }

            // The props file may use the MSBuild namespace, so match the element by its local name.
            return XDocument.Load( props ).Descendants().FirstOrDefault( e => e.Name.LocalName == "Version" )?.Value.Trim()
                ?? throw new InvalidOperationException( $"{props} has no Version. Pass --rock-version." );
        }

        /// <summary>
        /// The newest migration's Id, for example "202609222138362_AddWorkflowBuilder". Migration
        /// Ids start with a timestamp, so the largest name is the newest.
        /// </summary>
        private static string ReadNewestMigration( string checkoutPath )
        {
            var folder = Path.Combine( checkoutPath, "Rock.Migrations", "Migrations" );
            if ( !Directory.Exists( folder ) )
            {
                throw new DirectoryNotFoundException( $"The Rock checkout has no {folder}. Pass --rock-migration." );
            }

            return Directory.GetFiles( folder, "*.cs" )
                .Select( Path.GetFileName )
                .Where( name => MigrationFile.IsMatch( name ) )
                .Select( Path.GetFileNameWithoutExtension )
                .OrderByDescending( name => name, StringComparer.Ordinal )
                .FirstOrDefault()
                ?? throw new InvalidOperationException( $"{folder} has no migrations. Pass --rock-migration." );
        }
    }
}
