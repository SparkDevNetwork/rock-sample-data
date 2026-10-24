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

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Packages;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader
{
    /// <summary>
    /// Whether a database can take sample data: it is a Rock database with no people beyond the ones a new
    /// Rock install creates (decision 122), and, when a package is given, its Rock is the package's
    /// major release (decision 52).
    /// </summary>
    public sealed class DatabaseReadiness
    {
        /*
            10/21/26 - CLAUDE

            A new Rock database has exactly five people: Admin, Anonymous Giver, Presence (a RestUser
            record), Anonymous Visitor, and System Sender. Any other person means the database holds
            someone's data, or sample data already, and a load must never mix into that. RestUser records
            are API keys, not people, and an admin may add one before loading, so they never count.

            Admin has no fixed Guid. Rock's first migration inserts it as Person Id 1, and its Guid
            differs between installs (the trusted base and RockSample-base have different ones), so
            Admin is known by its Id. The other three have Rock SystemGuids.

            Reason: Sample data must never land in a database with real people in it.
        */
        private const int AdminPersonId = 1;

        private static readonly Guid[] FreshInstallPersonGuids =
        {
            new Guid( "802235DC-3CA5-94B0-4326-AACE71180F48" ),
            new Guid( "7EBC167B-512D-4683-9D80-98B6BB02E1B9" ),
            new Guid( "817E7C25-6CB1-4ED0-B224-FF3C4DA0B716" )
        };

        private static readonly Guid RestUserRecordTypeGuid = new Guid( "E2261A84-831D-4234-9BE0-4D628BBE751E" );

        private DatabaseReadiness( string newestMigration, string rockVersion, int existingPeopleCount, List<string> problems, bool isRockMismatch )
        {
            NewestMigration = newestMigration;
            RockVersion = rockVersion;
            ExistingPeopleCount = existingPeopleCount;
            Problems = problems;
            IsRockMismatch = isRockMismatch;
        }

        /// <summary>
        /// True when there are no problems.
        /// </summary>
        public bool IsReady => Problems.Count == 0;

        /// <summary>
        /// The database's newest Entity Framework migration. Null when it has none. Loaders before
        /// 0.1.0-alpha.13 matched packages by it; Rock versions now come from <see cref="RockVersion"/>.
        /// </summary>
        public string NewestMigration { get; }

        /// <summary>
        /// The Rock the database belongs to, for example "20.0.10", as the check was given it or read it
        /// from Rock's Rock.Version assembly. Null when it is unknown.
        /// </summary>
        public string RockVersion { get; }

        /// <summary>
        /// People beyond the five a new Rock install creates. RestUser records do not count.
        /// </summary>
        public int ExistingPeopleCount { get; }

        /// <summary>
        /// Each reason the database cannot take sample data, as a plain sentence for a person to read.
        /// </summary>
        public IReadOnlyList<string> Problems { get; }

        /// <summary>
        /// True when one of the problems is that the database is not on the package's Rock major release,
        /// or its Rock is unknown.
        /// </summary>
        internal bool IsRockMismatch { get; }

        /// <summary>
        /// Checks an open database. With a manifest, also checks that the database's Rock is the package's
        /// major release.
        /// </summary>
        internal static DatabaseReadiness Read( RockDatabase db, PackageManifest manifest, string rockVersion )
        {
            var problems = new List<string>();
            if ( db.Scalar( "SELECT OBJECT_ID( N'[dbo].[__MigrationHistory]' )" ) == null || db.Scalar( "SELECT OBJECT_ID( N'[dbo].[Person]' )" ) == null )
            {
                problems.Add( "This is not a Rock database: it has no Rock migrations or no Person table." );
                return new DatabaseReadiness( null, rockVersion, 0, problems, false );
            }

            var newestMigration = db.Scalar( "SELECT TOP 1 [MigrationId] FROM [__MigrationHistory] ORDER BY [MigrationId] DESC" ) as string;
            var parameters = FreshInstallPersonGuids.Select( ( guid, i ) => P( $"@Person{i}", guid ) ).ToList();
            parameters.Add( P( "@RestUser", RestUserRecordTypeGuid ) );
            var existingPeople = Convert.ToInt32( db.Scalar( $@"
SELECT COUNT( * )
FROM [Person] AS [p]
WHERE [p].[Id] <> {AdminPersonId}
    AND [p].[Guid] NOT IN ( {string.Join( ", ", parameters.Take( FreshInstallPersonGuids.Length ).Select( p => p.ParameterName ) )} )
    AND ( [p].[RecordTypeValueId] IS NULL OR [p].[RecordTypeValueId] <> ISNULL( ( SELECT [Id] FROM [DefinedValue] WHERE [Guid] = @RestUser ), 0 ) )", parameters.ToArray() ) );

            if ( existingPeople > 0 )
            {
                problems.Add( $"This database already has {existingPeople:N0} {( existingPeople == 1 ? "person" : "people" )} beyond the ones a new Rock install creates. Sample data loads only into a new Rock database." );
            }

            var major = RockVersionInfo.MajorOf( rockVersion );
            var isMismatch = manifest != null && ( major == null || major != manifest.Rock?.MajorRelease );
            if ( isMismatch )
            {
                problems.Add( MismatchMessage( manifest, rockVersion ) );
            }

            return new DatabaseReadiness( newestMigration, rockVersion, existingPeople, problems, isMismatch );
        }

        /// <summary>
        /// The sentence that says a package is for another Rock major release, or that the database's Rock is unknown.
        /// </summary>
        internal static string MismatchMessage( PackageManifest manifest, string rockVersion )
        {
            var package = $"This package is for Rock {manifest.Rock?.MajorRelease?.ToString() ?? "(not recorded)"}";
            return RockVersionInfo.MajorOf( rockVersion ) == null
                ? $"{package}, but this database's Rock version is unknown. Rock passes its version; outside Rock, give the one in RockWeb\\bin\\Rock.Version.dll."
                : $"{package}, but this database is Rock {rockVersion}.";
        }
    }

    /// <summary>
    /// The database cannot take sample data. Nothing was loaded. The message lists every problem.
    /// </summary>
    public sealed class DatabaseNotReadyException : InvalidOperationException
    {
        internal DatabaseNotReadyException( IReadOnlyList<string> problems )
            : base( string.Join( " ", problems ) )
        {
            Problems = problems;
        }

        /// <summary>
        /// Each reason, as a plain sentence for a person to read.
        /// </summary>
        public IReadOnlyList<string> Problems { get; }
    }
}
