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
    /// Rock install creates, and, when a package is given, it is the Rock the package was built for
    /// (decision D-rock-loader-3).
    /// </summary>
    public sealed class DatabaseReadiness
    {
        /*
            10/21/26 - CLAUDE

            A new Rock database has exactly five people: Admin, Anonymous Giver, Presence (a RestUser
            record), Anonymous Visitor, and System Sender. Any other person means the database holds
            someone's data, or sample data already, and a load must never mix into that. RestUser records
            are API keys, not people, and an admin may add one before loading, so they never count.

            Reason: Sample data must never land in a database with real people in it.
        */
        private static readonly Guid[] FreshInstallPersonGuids =
        {
            new Guid( "7D68D707-2460-4DF6-8DEA-90AE836086AA" ),
            new Guid( "802235DC-3CA5-94B0-4326-AACE71180F48" ),
            new Guid( "86CF11D9-66BC-4CE0-9037-F8AFCBCD608A" ),
            new Guid( "7EBC167B-512D-4683-9D80-98B6BB02E1B9" ),
            new Guid( "817E7C25-6CB1-4ED0-B224-FF3C4DA0B716" )
        };

        private static readonly Guid RestUserRecordTypeGuid = new Guid( "E2261A84-831D-4234-9BE0-4D628BBE751E" );

        private DatabaseReadiness( string newestMigration, int existingPeopleCount, List<string> problems, bool isMigrationMismatch )
        {
            NewestMigration = newestMigration;
            ExistingPeopleCount = existingPeopleCount;
            Problems = problems;
            IsMigrationMismatch = isMigrationMismatch;
        }

        /// <summary>
        /// True when there are no problems.
        /// </summary>
        public bool IsReady => Problems.Count == 0;

        /// <summary>
        /// The database's newest Entity Framework migration, which names its Rock exactly. Null when it has none.
        /// </summary>
        public string NewestMigration { get; }

        /// <summary>
        /// People beyond the five a new Rock install creates. RestUser records do not count.
        /// </summary>
        public int ExistingPeopleCount { get; }

        /// <summary>
        /// Each reason the database cannot take sample data, as a plain sentence for a person to read.
        /// </summary>
        public IReadOnlyList<string> Problems { get; }

        /// <summary>
        /// True when one of the problems is that the database is not the Rock the package was built for.
        /// </summary>
        internal bool IsMigrationMismatch { get; }

        /// <summary>
        /// Checks an open database. With a manifest, also checks that its Rock matches the database.
        /// </summary>
        internal static DatabaseReadiness Read( RockDatabase db, PackageManifest manifest )
        {
            var problems = new List<string>();
            if ( db.Scalar( "SELECT OBJECT_ID( N'[dbo].[__MigrationHistory]' )" ) == null || db.Scalar( "SELECT OBJECT_ID( N'[dbo].[Person]' )" ) == null )
            {
                problems.Add( "This is not a Rock database: it has no Rock migrations or no Person table." );
                return new DatabaseReadiness( null, 0, problems, false );
            }

            var newestMigration = db.Scalar( "SELECT TOP 1 [MigrationId] FROM [__MigrationHistory] ORDER BY [MigrationId] DESC" ) as string;
            var parameters = FreshInstallPersonGuids.Select( ( guid, i ) => P( $"@Person{i}", guid ) ).ToList();
            parameters.Add( P( "@RestUser", RestUserRecordTypeGuid ) );
            var existingPeople = Convert.ToInt32( db.Scalar( $@"
SELECT COUNT( * )
FROM [Person] AS [p]
WHERE [p].[Guid] NOT IN ( {string.Join( ", ", parameters.Take( FreshInstallPersonGuids.Length ).Select( p => p.ParameterName ) )} )
    AND ( [p].[RecordTypeValueId] IS NULL OR [p].[RecordTypeValueId] <> ISNULL( ( SELECT [Id] FROM [DefinedValue] WHERE [Guid] = @RestUser ), 0 ) )", parameters.ToArray() ) );

            if ( existingPeople > 0 )
            {
                problems.Add( $"This database already has {existingPeople:N0} {( existingPeople == 1 ? "person" : "people" )} beyond the ones a new Rock install creates. Sample data loads only into a new Rock database." );
            }

            var expected = manifest?.Rock?.Migration;
            var isMismatch = manifest != null && !string.Equals( expected, newestMigration, StringComparison.Ordinal );
            if ( isMismatch )
            {
                problems.Add( MismatchMessage( manifest, newestMigration ) );
            }

            return new DatabaseReadiness( newestMigration, existingPeople, problems, isMismatch );
        }

        /// <summary>
        /// The sentence that says a package is for another Rock.
        /// </summary>
        internal static string MismatchMessage( PackageManifest manifest, string newestMigration )
        {
            return $"This package is for Rock {manifest.Rock?.Version} (newest migration {manifest.Rock?.Migration ?? "not recorded"}), but the database's newest migration is {newestMigration ?? "missing"}.";
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
