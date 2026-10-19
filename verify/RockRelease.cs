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

namespace RockSampleData.Verify
{
    /// <summary>
    /// The Rock a verification runs against, read from a built Rock checkout (a release tag, or develop).
    /// </summary>
    internal sealed class RockRelease
    {
        private static readonly Regex MigrationId = new Regex( @"IMigrationMetadata\.Id\s*\{\s*get\s*\{\s*return\s*""([^""]+)""" );
        private static readonly Regex HotfixNumber = new Regex( @"\[MigrationNumber\(\s*(\d+)" );

        public string CheckoutPath { get; private set; }

        public string Version { get; private set; }

        /// <summary>
        /// Every migration Id in Rock.Migrations, sorted. A migration's file name is not always its Id,
        /// so the Ids come from each migration's designer file.
        /// </summary>
        public List<string> MigrationIds { get; private set; }

        public string NewestMigration => MigrationIds.Last();

        /// <summary>
        /// Every hotfix migration number in Rock/Plugin/HotFixes. Rock applies these as plugin migrations when it starts,
        /// so two patch releases with the same EF migrations still differ here.
        /// </summary>
        public List<int> HotfixNumbers { get; private set; }

        public string BinPath => Path.Combine( CheckoutPath, "RockWeb", "bin" );

        public string WebConfigPath => Path.Combine( CheckoutPath, "RockWeb", "web.config" );

        public static RockRelease Read( string checkoutPath )
        {
            if ( string.IsNullOrEmpty( checkoutPath ) || !Directory.Exists( checkoutPath ) )
            {
                throw new InvalidOperationException( "Name a built Rock checkout with --rock <folder> (or set ROCK_PATH)." );
            }

            var props = Path.Combine( checkoutPath, "Directory.Build.props" );
            var version = XDocument.Load( props ).Descendants().FirstOrDefault( e => e.Name.LocalName == "Version" )?.Value.Trim()
                ?? throw new InvalidOperationException( $"{props} has no Version." );

            var migrations = Directory.GetFiles( Path.Combine( checkoutPath, "Rock.Migrations", "Migrations" ), "*.cs", SearchOption.AllDirectories )
                .Where( f => f.EndsWith( ".designer.cs", StringComparison.OrdinalIgnoreCase ) )
                .Select( f => MigrationId.Match( File.ReadAllText( f ) ) )
                .Where( m => m.Success )
                .Select( m => m.Groups[1].Value )
                .Distinct()
                .OrderBy( id => id, StringComparer.Ordinal )
                .ToList();
            if ( migrations.Count == 0 )
            {
                throw new InvalidOperationException( $"{checkoutPath} has no Rock migrations." );
            }

            var hotfixFolder = Path.Combine( checkoutPath, "Rock", "Plugin", "HotFixes" );
            var hotfixes = Directory.Exists( hotfixFolder )
                ? Directory.GetFiles( hotfixFolder, "*.cs" )
                    .Select( f => HotfixNumber.Match( File.ReadAllText( f ) ) )
                    .Where( m => m.Success )
                    .Select( m => int.Parse( m.Groups[1].Value ) )
                    .Distinct()
                    .OrderBy( n => n )
                    .ToList()
                : new List<int>();

            var release = new RockRelease { CheckoutPath = checkoutPath, Version = version, MigrationIds = migrations, HotfixNumbers = hotfixes };
            if ( !File.Exists( Path.Combine( release.BinPath, "Rock.dll" ) ) || !File.Exists( Path.Combine( release.BinPath, "Rock.Migrations.dll" ) ) )
            {
                throw new InvalidOperationException( $"{release.BinPath} has no Rock.dll and Rock.Migrations.dll. Build the Rock checkout first." );
            }

            return release;
        }

        /// <summary>
        /// The EF6 command-line tool from the checkout's packages, which applies Rock's migrations without a web server.
        /// </summary>
        public string FindEf6()
        {
            var roots = new[]
            {
                Path.Combine( CheckoutPath, "packages" ),
                Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.UserProfile ), ".nuget", "packages", "entityframework" )
            };

            return roots.Where( Directory.Exists )
                .SelectMany( root => Directory.GetFiles( root, "ef6.exe", SearchOption.AllDirectories ) )
                .Where( f => f.Replace( '\\', '/' ).Contains( "/tools/net45/any/", StringComparison.OrdinalIgnoreCase ) )
                .OrderByDescending( f => f, StringComparer.OrdinalIgnoreCase )
                .FirstOrDefault()
                ?? throw new InvalidOperationException( "No EF6 tools (ef6.exe) were found in the Rock checkout's packages or the NuGet cache. Restore Rock's packages first." );
        }

        /// <summary>
        /// Writes the checkout's web.config with its connection string pointed at one database, for EF's tools.
        /// The checkout's own connection strings are never used, so nothing touches a developer's databases.
        /// </summary>
        public void WriteWebConfig( string connectionString, string path )
        {
            var document = XDocument.Load( WebConfigPath );
            document.Root.Element( "connectionStrings" )?.Remove();
            document.Root.Element( "configSections" ).AddAfterSelf( ConnectionStrings( connectionString ) );
            document.Save( path );
        }

        /// <summary>
        /// Writes a console-app config with the parts of web.config Rock needs to start outside IIS:
        /// its config sections, Entity Framework setup, appSettings, and binding redirects.
        /// </summary>
        public void WriteRunnerConfig( string connectionString, string path )
        {
            var web = XDocument.Load( WebConfigPath ).Root;
            var sections = new XElement( web.Element( "configSections" ) );
            sections.Elements( "section" ).Where( s => ( string ) s.Attribute( "name" ) == "resizer" ).Remove();

            var config = new XElement( "configuration",
                sections,
                web.Element( "entityFramework" ),
                ConnectionStrings( connectionString ),
                web.Element( "appSettings" ),
                web.Element( "runtime" ) );
            new XDocument( config ).Save( path );
        }

        private static XElement ConnectionStrings( string connectionString )
        {
            return new XElement( "connectionStrings",
                new XElement( "add",
                    new XAttribute( "name", "RockContext" ),
                    new XAttribute( "connectionString", connectionString ),
                    new XAttribute( "providerName", "System.Data.SqlClient" ) ) );
        }
    }
}
