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

using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Reflection;

using Rock;
using Rock.Data;
using Rock.Model;
using Rock.Web.Cache;

namespace RockSampleData.AchievementParity
{
    /*
        10/19/26 - CLAUDE

        The achievements parity test. It runs Rock's own code (Rock.dll, not the loader's port) against a loaded
        copy of a sample database, with Rock's clock frozen at the moment Rock would have run it, and compares
        what Rock computes with what the loader wrote:

        1. GetStreakData as of the anchor, against each streak row's counts (RockCleanup refreshes them daily).
        2. Achievement processing for each child's streak as of their last check-in Sunday, as a live check-in
           would run it, against the loader's attempts.
        3. The Streak Type Detail "Rebuild" as of the anchor, against the loader's occurrence map, engagement
           maps, and enrollment dates.

        Steps 2 and 3 rewrite rows, so run it on a copy. Rock's integration tests freeze the clock the same way
        (RockDateTime.TestCurrentDateTimeUtcOverride) and set up Rock the same way (TestHelper.ConfigureRockApp).

        Reason: The strongest proof the port is right is Rock itself.
    */
    internal static class Program
    {
        private const string StreakTypeGuid = "11D6638D-37B7-43A4-B8E3-295060C71E44";
        private const string CheckInTemplateGuid = "FEDD389A-616F-4A53-906C-63D8255631C5";

        private static int Main( string[] args )
        {
            var connection = Argument( args, "--connection" );
            var anchorText = Argument( args, "--anchor" );
            if ( connection == null || anchorText == null || !args.Contains( "--changes-database" ) )
            {
                Console.WriteLine( "Usage: --connection \"<a copy of a loaded database>\" --anchor yyyy-MM-dd --changes-database" );
                Console.WriteLine( "It rewrites achievement attempts and streaks, so never point it at a database you want to keep." );
                return 2;
            }

            var anchor = DateTime.Parse( anchorText );
            if ( !connection.Contains( "MultipleActiveResultSets" ) )
            {
                connection += ";MultipleActiveResultSets=true";
            }

            typeof( Rock.Tests.Shared.TestFramework.TestHelper )
                .GetMethod( "ConfigureRockApp", BindingFlags.NonPublic | BindingFlags.Static )
                .Invoke( null, new object[] { connection } );

            var streakTypeId = ( int ) Query( connection, $"SELECT [Id] FROM [StreakType] WHERE [Guid] = '{StreakTypeGuid}'" )[0][0];
            var differences = CheckStreakData( connection, streakTypeId, anchor )
                + CheckAttempts( connection, streakTypeId )
                + CheckRebuild( connection, streakTypeId, anchor );

            Console.WriteLine( differences == 0 ? "Rock and the loader agree." : $"{differences} differences." );
            return differences == 0 ? 0 : 1;
        }

        /// <summary>
        /// Rock's GetStreakData as of the anchor, against the counts on each streak row.
        /// </summary>
        private static int CheckStreakData( string connection, int streakTypeId, DateTime anchor )
        {
            Freeze( anchor.AddHours( 3 ) );
            var differences = 0;
            var rows = Query( connection, $@"
SELECT [Id], [EngagementCount], [CurrentStreakCount], [CurrentStreakStartDate], [LongestStreakCount], [LongestStreakStartDate], [LongestStreakEndDate]
FROM [Streak] WHERE [StreakTypeId] = {streakTypeId}" );

            foreach ( var row in rows )
            {
                using ( var rockContext = new RockContext() )
                {
                    var data = new StreakTypeService( rockContext ).GetStreakData( ( int ) row[0], out var error );
                    var rock = $"{data?.EngagementCount} {data?.CurrentStreakCount} {data?.CurrentStreakStartDate:yyyy-MM-dd} {data?.LongestStreakCount} {data?.LongestStreakStartDate:yyyy-MM-dd} {data?.LongestStreakEndDate:yyyy-MM-dd}";
                    var loader = $"{row[1]} {row[2]} {Date( row[3] )} {row[4]} {Date( row[5] )} {Date( row[6] )}";
                    if ( rock != loader || !string.IsNullOrEmpty( error ) )
                    {
                        differences++;
                        Console.WriteLine( $"    streak {row[0]}: Rock {rock} {error}, loader {loader}" );
                    }
                }
            }

            Console.WriteLine( $"  Streak counts as of {anchor:yyyy-MM-dd}: {rows.Count:N0} streaks, {differences} differ" );
            return differences;
        }

        /// <summary>
        /// Deletes the loader's attempts, lets Rock process each child's streak as of their last check-in Sunday, and
        /// compares the two sets of attempts.
        /// </summary>
        private static int CheckAttempts( string connection, int streakTypeId )
        {
            var attemptsSql = $@"
SELECT CONCAT( [a].[AchieverEntityId], ' ', [t].[Name], ' ', CONVERT( varchar(10), [a].[AchievementAttemptStartDateTime], 23 ), ' ', CONVERT( varchar(10), [a].[AchievementAttemptEndDateTime], 23 ),
    ' ', [a].[Progress], ' closed ', [a].[IsClosed], ' successful ', [a].[IsSuccessful] )
FROM [AchievementAttempt] AS [a] INNER JOIN [AchievementType] AS [t] ON [t].[Id] = [a].[AchievementTypeId]
WHERE [t].[ComponentConfigJson] LIKE '%{StreakTypeGuid}%'";
            var loader = Query( connection, attemptsSql ).Select( r => ( string ) r[0] ).ToList();

            Execute( connection, $"DELETE FROM [AchievementAttempt] WHERE [AchievementTypeId] IN ( SELECT [Id] FROM [AchievementType] WHERE [ComponentConfigJson] LIKE '%{StreakTypeGuid}%' )" );
            var lastCheckIns = Query( connection, $@"
SELECT [s].[Id], MAX( [ao].[OccurrenceDate] )
FROM [Streak] AS [s]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [s].[PersonAliasId]
INNER JOIN [PersonAlias] AS [all] ON [all].[PersonId] = [pa].[PersonId]
INNER JOIN [Attendance] AS [a] ON [a].[PersonAliasId] = [all].[Id] AND [a].[DidAttend] = 1
INNER JOIN [AttendanceOccurrence] AS [ao] ON [ao].[Id] = [a].[OccurrenceId]
INNER JOIN [Group] AS [g] ON [g].[Id] = [ao].[GroupId]
INNER JOIN [GroupTypeAssociation] AS [gta] ON [gta].[ChildGroupTypeId] = [g].[GroupTypeId]
INNER JOIN [GroupType] AS [template] ON [template].[Id] = [gta].[GroupTypeId] AND [template].[Guid] = '{CheckInTemplateGuid}'
WHERE [s].[StreakTypeId] = {streakTypeId}
GROUP BY [s].[Id]" );

            // A live check-in saves the child's streak, and Rock then processes its achievements.
            foreach ( var row in lastCheckIns )
            {
                Freeze( ( ( DateTime ) row[1] ).Date.AddHours( 9 ) );
                using ( var rockContext = new RockContext() )
                {
                    AchievementTypeCache.ProcessAchievements( new StreakService( rockContext ).Get( ( int ) row[0] ) );
                }
            }

            var rock = Query( connection, attemptsSql ).Select( r => ( string ) r[0] ).ToList();
            return Compare( "Attempts as of each child's last check-in", loader, rock );
        }

        /// <summary>
        /// Runs Rock's streak type rebuild from attendance as of the anchor, and compares the maps and enrollment dates.
        /// </summary>
        private static int CheckRebuild( string connection, int streakTypeId, DateTime anchor )
        {
            var mapsSql = $@"
SELECT CONCAT( 'occurrence map ', CONVERT( varchar(max), [OccurrenceMap], 2 ) ) FROM [StreakType] WHERE [Id] = {streakTypeId}
UNION ALL
SELECT CONCAT( 'alias ', [PersonAliasId], ' enrolled ', CONVERT( varchar(10), [EnrollmentDate], 23 ), ' map ', CONVERT( varchar(max), [EngagementMap], 2 ) ) FROM [Streak] WHERE [StreakTypeId] = {streakTypeId}";
            var loader = Query( connection, mapsSql ).Select( r => ( string ) r[0] ).ToList();

            Freeze( anchor.AddHours( 3 ) );
            StreakTypeService.RebuildStreakType( null, streakTypeId, out var error );
            if ( !string.IsNullOrEmpty( error ) )
            {
                Console.WriteLine( $"  Rebuild failed: {error}" );
                return 1;
            }

            var rock = Query( connection, mapsSql ).Select( r => ( string ) r[0] ).ToList();
            return Compare( $"Rebuild from attendance as of {anchor:yyyy-MM-dd} (occurrence map and streaks)", loader, rock );
        }

        private static int Compare( string what, List<string> loader, List<string> rock )
        {
            var onlyLoader = loader.Except( rock ).ToList();
            var onlyRock = rock.Except( loader ).ToList();
            Console.WriteLine( $"  {what}: loader {loader.Count:N0}, Rock {rock.Count:N0}, {onlyLoader.Count + onlyRock.Count} differ" );
            foreach ( var line in onlyLoader.Take( 10 ) )
            {
                Console.WriteLine( $"    only the loader: {line}" );
            }

            foreach ( var line in onlyRock.Take( 10 ) )
            {
                Console.WriteLine( $"    only Rock: {line}" );
            }

            return onlyLoader.Count + onlyRock.Count;
        }

        /// <summary>
        /// Freezes RockDateTime.Now at an organization-local time, as Rock's integration tests do.
        /// </summary>
        private static void Freeze( DateTime organizationTime )
        {
            var utc = TimeZoneInfo.ConvertTimeToUtc( DateTime.SpecifyKind( organizationTime, DateTimeKind.Unspecified ), RockDateTime.OrgTimeZoneInfo );
            typeof( RockDateTime )
                .GetProperty( "TestCurrentDateTimeUtcOverride", BindingFlags.NonPublic | BindingFlags.Static )
                .SetValue( null, ( DateTime? ) utc );
        }

        private static string Argument( string[] args, string name )
        {
            var index = Array.IndexOf( args, name );
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        private static string Date( object value ) => value is DateTime d ? d.ToString( "yyyy-MM-dd" ) : string.Empty;

        private static void Execute( string connection, string sql )
        {
            using ( var c = new SqlConnection( connection ) )
            {
                c.Open();
                new SqlCommand( sql, c ) { CommandTimeout = 600 }.ExecuteNonQuery();
            }
        }

        private static List<object[]> Query( string connection, string sql )
        {
            var rows = new List<object[]>();
            using ( var c = new SqlConnection( connection ) )
            {
                c.Open();
                using ( var reader = new SqlCommand( sql, c ) { CommandTimeout = 600 }.ExecuteReader() )
                {
                    while ( reader.Read() )
                    {
                        var values = new object[reader.FieldCount];
                        reader.GetValues( values );
                        rows.Add( values );
                    }
                }
            }

            return rows;
        }
    }
}
