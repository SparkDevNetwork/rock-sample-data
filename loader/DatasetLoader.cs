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

using System.Diagnostics;

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Loading;
using RockSampleData.Loader.Model;
using RockSampleData.Loader.Packages;
using RockSampleData.Loader.Validation;

namespace RockSampleData.Loader
{
    /// <summary>
    /// Installs a dataset package into a fresh Rock database: generates the timeline for the
    /// install day and bulk-loads it, all in one transaction.
    /// </summary>
    public static class DatasetLoader
    {
        /// <summary>
        /// Loads the package. Everything rolls back if any step fails.
        /// </summary>
        /// <exception cref="RockVersionMismatchException">The database's Rock is not the one the package was built for. Nothing was loaded.</exception>
        public static void Load( string connectionString, SampleDataPackage package, DatasetLoadOptions options = null )
        {
            options ??= new DatasetLoadOptions();
            var dataset = package.Manifest.Dataset;
            var foreignKey = $"SampleData:{dataset}";
            var source = new SampleDataSource( package );

            // Every relative date counts back from the anchor. Tests pin it.
            var dates = new AnchorDates( options.Anchor ?? DateTime.Today );

            using var db = new RockDatabase( connectionString );
            CheckRockVersion( db, package.Manifest, options.IsAnyRockVersionAllowed );

            var total = Stopwatch.StartNew();
            Console.WriteLine( $"Loading dataset {dataset} (built from {ShortCommit( package.Manifest )} for Rock {package.Manifest.Rock?.Version}, anchor {dates.Anchor:yyyy-MM-dd}){( options.IsDryRun ? " (dry run)" : string.Empty )}" );

            db.BeginTransaction();
            try
            {
                LoadSteps( db, source, dataset, foreignKey, dates, options );
                foreach ( var timing in db.Timings )
                {
                    Console.WriteLine( $"    bulk {timing.Table,-14} {timing.Rows,8:N0} rows {timing.Milliseconds,7:N0} ms" );
                }

                if ( options.IsDryRun )
                {
                    db.Rollback();
                    Console.WriteLine( $"Dry run complete in {total.ElapsedMilliseconds:N0} ms. Everything was rolled back." );
                }
                else
                {
                    db.Commit();
                    Console.WriteLine( $"Done in {total.ElapsedMilliseconds:N0} ms." );
                }
            }
            catch
            {
                db.Rollback();
                throw;
            }
        }

        /// <summary>
        /// Checks the Cast expectations (giving and achievements) in a package against an already loaded database.
        /// Returns the number that fail.
        /// </summary>
        public static int CheckCastExpectations( string connectionString, SampleDataPackage package )
        {
            using var db = new RockDatabase( connectionString );
            return CastExpectations.Check( db, new SampleDataSource( package ) );
        }

        /// <summary>
        /// The release-time parity test: after Rock's own Giving Automation job has run on a loaded
        /// database, compares what Rock wrote with the loader's port. Returns the number of differences.
        /// </summary>
        public static int CheckGivingParity( string connectionString, string dataset )
        {
            using var db = new RockDatabase( connectionString );
            return new GivingAnalyticsLoader( db, new AnchorDates( DateTime.Today ), $"SampleData:{dataset}" ).CheckParity();
        }

        /// <summary>
        /// Refuses a database whose newest migration is not the one the package was built for. A
        /// Rock database stores no version number, and its newest migration identifies its schema exactly.
        /// </summary>
        private static void CheckRockVersion( RockDatabase db, PackageManifest manifest, bool isAnyVersionAllowed )
        {
            var expected = manifest.Rock?.Migration;
            var actual = db.Scalar( "SELECT TOP 1 [MigrationId] FROM [__MigrationHistory] ORDER BY [MigrationId] DESC" ) as string;
            if ( string.Equals( expected, actual, StringComparison.Ordinal ) )
            {
                return;
            }

            var message = $"This package is for Rock {manifest.Rock?.Version} (newest migration {expected ?? "not recorded"}), but the database's newest migration is {actual ?? "missing"}.";
            if ( !isAnyVersionAllowed )
            {
                throw new RockVersionMismatchException( $"{message} Use a package built for this Rock." );
            }

            Console.WriteLine( $"Warning: {message} Loading anyway." );
        }

        /// <summary>
        /// Runs every load step in order inside the open transaction.
        /// </summary>
        private static void LoadSteps( RockDatabase db, SampleDataSource source, string dataset, string foreignKey, AnchorDates dates, DatasetLoadOptions options )
        {
            var step = Stopwatch.StartNew();
            Console.WriteLine( "Configuration" );
            var configuration = new ConfigurationLoader( db, source, dataset, foreignKey );
            configuration.Load();
            var smallGroupSetup = new SmallGroupConfigurationLoader( db, source, foreignKey, configuration.CampusIds ).Load();
            var servingSetup = new ServingConfigurationLoader( db, source, foreignKey, configuration.CampusPlans ).Load();
            new ConnectionConfigurationLoader( db, source, foreignKey, configuration.CampusIds, servingSetup ).Load();
            Console.WriteLine( $"  ({step.ElapsedMilliseconds:N0} ms)" );

            var profile = source.ReadProfile();
            var years = options.HistoryYears;
            var calendar = new SundayCalendar( dates.Anchor, years, profile );
            var seed = options.Seed;

            // The Cast always lives on the main campus. Generated households come from the dataset manifest's
            // count per campus, or from the spike crowd, which spreads across the dataset's campuses.
            step.Restart();
            Console.WriteLine( "People" );
            var mainCampusKey = source.Campuses.Campuses.Single( c => c.RockDefault ).Key;
            var households = source.ReadHouseholds();
            var castHouseholdKeys = households.Select( h => h.Key ).ToHashSet();
            var manifest = source.ReadDataset( dataset );

            // The Cast's small groups come from cast/groups. Generated households get theirs here, before
            // people load, so their new leaders load with the leader status.
            var groupPlans = source.ReadCastSmallGroupPlans();
            var generator = new CrowdGenerator( profile, source, seed, dates.Anchor );
            List<CastHousehold> generated = null;
            if ( options.CrowdHouseholds is { } crowd )
            {
                generated = generator.Generate( crowd, configuration.CampusIds.Keys.ToList(), mainCampusKey, years );
            }
            else if ( manifest?.GeneratedHouseholds?.PerCampus is { } perCampus )
            {
                var unknownCampuses = perCampus.Keys.Except( configuration.CampusIds.Keys ).ToList();
                if ( unknownCampuses.Count > 0 )
                {
                    throw new InvalidOperationException( $"datasets/{dataset}.json lists generated households for campuses not in the dataset: {string.Join( ", ", unknownCampuses )}" );
                }

                var countsByCampus = configuration.CampusIds.Keys.Where( perCampus.ContainsKey ).Select( k => (k, perCampus[k]) ).ToList();
                generated = generator.Generate( countsByCampus, years );
            }

            if ( generated != null )
            {
                var generatedPlans = GeneratedSmallGroups.Plan( source.SmallGroups, profile, dates.Anchor, seed, generated, configuration.CampusIds.Keys, mainCampusKey, groupPlans, households );
                households.AddRange( generated );
                groupPlans.AddRange( generatedPlans );
                var perCampusText = string.Join( ", ", generated.GroupBy( h => h.CampusSlot ).Select( g => $"{g.Key} {g.Count():N0}" ) );
                Console.WriteLine( $"  Generated: {generated.Count:N0} households ({perCampusText}) and {generatedPlans.Count:N0} small groups ({step.ElapsedMilliseconds:N0} ms)" );
            }

            // Serving is planned before people load too, so new team leaders load as Leading.
            var servingPlans = ServingPlanner.Plan( source.Serving, profile, dates, seed, households, castHouseholdKeys, source.ReadCastServingTeams(), configuration.CampusPlans, mainCampusKey );

            var people = new PeopleLoader( db, source, dates, foreignKey, configuration.CampusIds, mainCampusKey );
            people.Load( households );
            Console.WriteLine( $"  ({step.ElapsedMilliseconds:N0} ms)" );

            // Every Cast person's delivered photo, plus pool faces for generated households when the dataset asks.
            step.Restart();
            Console.WriteLine( "Photos" );
            var photoPercent = manifest?.Photos?.GeneratedHouseholdPercent ?? 0;
            new PhotoLoader( db, source, foreignKey, seed ).Load( people.Households, castHouseholdKeys, photoPercent );
            Console.WriteLine( $"  ({step.ElapsedMilliseconds:N0} ms)" );

            // Sunday attendance, small group attendance, and serving requests all copy into Attendance. Its
            // indexes are rebuilt once, after every copy; nothing reads Attendance in between.
            db.DeferIndexRebuild( "Attendance" );

            step.Restart();
            Console.WriteLine( "Attendance" );
            var attendance = new AttendanceLoader( db, calendar, dates, configuration, foreignKey, seed );
            attendance.Load( people.Households );
            Console.WriteLine( $"  ({step.ElapsedMilliseconds:N0} ms)" );

            // Kids check-in streaks and achievements come straight from the check-ins just planned. They read the plan
            // in memory, not Attendance, and no later step reads what they write.
            step.Restart();
            Console.WriteLine( "Achievements" );
            new AchievementsLoader( db, source.Achievements, calendar, dates, foreignKey ).Load( attendance.CheckInSundaysByAlias, attendance.CheckInSundays );
            Console.WriteLine( $"  ({step.ElapsedMilliseconds:N0} ms)" );

            step.Restart();
            Console.WriteLine( "Small groups" );
            new SmallGroupLoader( db, source.SmallGroups, smallGroupSetup, calendar, dates, foreignKey, seed, mainCampusKey, configuration.CampusIds ).Load( groupPlans, people );
            Console.WriteLine( $"  ({step.ElapsedMilliseconds:N0} ms)" );

            // Volunteers serve on the Sundays their family came, so serving follows Sunday attendance.
            step.Restart();
            Console.WriteLine( "Serving" );
            new ServingLoader( db, source.Serving, servingSetup, configuration.CampusPlans, dates, foreignKey, seed ).Load( servingPlans, people, attendance.FamilySundays );
            db.RebuildDeferredIndexes();
            Console.WriteLine( $"  ({step.ElapsedMilliseconds:N0} ms)" );

            step.Restart();
            Console.WriteLine( "Giving" );
            var giving = new GivingLoader( db, source, profile, calendar, dates, configuration, attendance.FamilySundays, foreignKey, seed );

            // Rock's PasswordKey fills the check scanner's matching fields. Without it, the fields stay
            // empty and everything else still loads.
            giving.Keys = options.RockWebConfigPath == null ? null : RockKeys.Load( options.RockWebConfigPath );
            Console.WriteLine( giving.Keys != null
                ? $"  Check scanner fields keyed from {options.RockWebConfigPath}"
                : "  No Rock PasswordKey (give a Rock web.config); check scanner fields stay empty" );
            giving.Load( people.Households );
            Console.WriteLine( $"  ({step.ElapsedMilliseconds:N0} ms)" );

            // Giving attributes, bins, and journey stages, as if Rock's Giving Automation job had run every night.
            step.Restart();
            Console.WriteLine( "Giving analytics" );
            var analytics = new GivingAnalyticsLoader( db, dates, foreignKey );
            analytics.Load( giving.QualifyingGifts );
            Console.WriteLine( $"  ({step.ElapsedMilliseconds:N0} ms)" );

            // The sample church's giving alert types, with the alerts Rock would have raised over the last year.
            step.Restart();
            Console.WriteLine( "Giving alerts" );
            new GivingAlertsLoader( db, source, foreignKey, analytics.RunAt, analytics.Gifts ).Load();
            Console.WriteLine( $"  ({step.ElapsedMilliseconds:N0} ms)" );

            // The Cast givers and kids that documentation and tests lean on must still show what their stories say.
            step.Restart();
            Console.WriteLine( "Cast expectations" );
            CastExpectations.Check( db, source );
            Console.WriteLine( $"  ({step.ElapsedMilliseconds:N0} ms)" );

            // Scanned check images are opt-in, because drawing them is the slowest step.
            step.Restart();
            Console.WriteLine( "Check images" );
            if ( options.IsCheckImagesDrawn )
            {
                var checkImageMonths = options.CheckImageMonths ?? manifest?.CheckImages?.Months ?? 12;
                new CheckImageLoader( db, dates, foreignKey, seed, source.Files ).Load( giving.CheckGifts, checkImageMonths, options.CheckImageCopyFolder );
                Console.WriteLine( $"  ({step.ElapsedMilliseconds:N0} ms)" );
            }
            else
            {
                Console.WriteLine( $"  Skipped ({giving.CheckGifts.Count:N0} check gifts have no images). Turn on check images to draw them." );
            }
        }

        private static string ShortCommit( PackageManifest manifest )
        {
            var commit = manifest.Source?.Commit ?? "unknown";
            return $"{( commit.Length > 7 ? commit.Substring( 0, 7 ) : commit )}{( manifest.Source?.IsModified == true ? " with changes" : string.Empty )}";
        }
    }
}
