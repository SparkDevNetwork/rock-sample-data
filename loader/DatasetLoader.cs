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
        /// Every load step in order, with the part it belongs to. Null is a step that always runs.
        /// </summary>
        private static readonly (string Name, SampleDataPart? Part)[] Steps =
        {
            ("Configuration", null),
            ("People", null),
            ("Photos", SampleDataPart.Photos),
            ("Attendance", SampleDataPart.Attendance),
            ("Achievements", SampleDataPart.Attendance),
            ("Small groups", SampleDataPart.SmallGroups),
            ("Serving", SampleDataPart.Serving),
            ("Connection requests", SampleDataPart.Serving),
            ("Giving", SampleDataPart.Giving),
            ("Giving analytics", SampleDataPart.Giving),
            ("Giving alerts", SampleDataPart.Giving),
            ("Prayer", SampleDataPart.Prayer),
            ("Notes", SampleDataPart.Notes),
            ("Steps", SampleDataPart.Steps),
            ("Cast expectations", null),
            ("Check images", SampleDataPart.CheckImages)
        };

        /// <summary>
        /// Loads the package. Everything rolls back if any step fails.
        /// </summary>
        /// <exception cref="DatabaseNotReadyException">The database has people beyond a new Rock install, or another load is running. Nothing was loaded.</exception>
        /// <exception cref="RockVersionMismatchException">The database's Rock is not the one the package was built for. Nothing was loaded.</exception>
        /// <exception cref="ArgumentException">The PasswordKey option is not hex.</exception>
        public static void Load( string connectionString, SampleDataPackage package, DatasetLoadOptions options = null )
        {
            options ??= new DatasetLoadOptions();
            var dataset = package.Manifest.Dataset;
            var foreignKey = $"SampleData:{dataset}";
            var source = new SampleDataSource( package );
            var parts = SampleDataParts.WithDependencies( options.Parts ?? SampleDataParts.All.Select( p => p.Part ).ToList() );
            var keys = ReadKeys( options );
            var log = new LoadLog( options.Progress ) { TotalSteps = Steps.Count( s => IsLoaded( s.Part, parts ) ) };

            // Every relative date counts back from the anchor. Tests pin it.
            var dates = new AnchorDates( options.Anchor ?? DateTime.Today );

            using var db = new RockDatabase( connectionString ) { Recorder = options.Recorder, Log = log };
            db.BeginTransaction();
            try
            {
                // The lock and the guard run inside the load's transaction, so two loads can never both pass.
                if ( !db.TryLockLoad() )
                {
                    throw new DatabaseNotReadyException( new[] { "Another sample data load is running on this database. Try again when it finishes." } );
                }

                CheckReadiness( db, package.Manifest, options.IsAnyRockVersionAllowed );

                var total = Stopwatch.StartNew();
                log.Write( $"Loading dataset {dataset} (built from {ShortCommit( package.Manifest )} for Rock {package.Manifest.Rock?.Version}, anchor {dates.Anchor:yyyy-MM-dd}){( options.IsDryRun ? " (dry run)" : string.Empty )}" );

                LoadSteps( db, source, dataset, foreignKey, dates, options, parts, keys );
                foreach ( var timing in db.Timings )
                {
                    log.Write( $"    bulk {timing.Table,-14} {timing.Rows,8:N0} rows {timing.Milliseconds,7:N0} ms" );
                }

                if ( options.IsDryRun )
                {
                    db.Rollback();
                    log.Write( $"Dry run complete in {total.ElapsedMilliseconds:N0} ms. Everything was rolled back." );
                }
                else
                {
                    db.Commit();
                    log.Write( $"Done in {total.ElapsedMilliseconds:N0} ms." );
                }
            }
            catch
            {
                db.Rollback();
                throw;
            }
        }

        /// <summary>
        /// Checks whether a database can take sample data: a Rock database with no people beyond a new
        /// install. With a package, also checks that the database is the Rock the package was built for.
        /// It never honors <see cref="DatasetLoadOptions.IsAnyRockVersionAllowed"/>; only a load does.
        /// <see cref="Load"/> runs the same check inside its transaction.
        /// </summary>
        public static DatabaseReadiness CheckDatabase( string connectionString, SampleDataPackage package = null )
        {
            using var db = new RockDatabase( connectionString );
            return DatabaseReadiness.Read( db, package?.Manifest );
        }

        /// <summary>
        /// Checks the Cast expectations in a package against an already loaded database. Returns the
        /// number that fail.
        /// </summary>
        /// <param name="connectionString">The loaded database.</param>
        /// <param name="package">The package that was loaded.</param>
        /// <param name="parts">The parts that were loaded. Null checks every part.</param>
        /// <param name="progress">Receives each result line. Null reports nothing.</param>
        public static int CheckCastExpectations( string connectionString, SampleDataPackage package, IEnumerable<SampleDataPart> parts = null, IProgress<LoadProgressReport> progress = null )
        {
            using var db = new RockDatabase( connectionString ) { Log = new LoadLog( progress ) };
            return CastExpectations.Check( db, new SampleDataSource( package ), SampleDataParts.WithDependencies( parts ?? SampleDataParts.All.Select( p => p.Part ) ) );
        }

        /// <summary>
        /// The release-time parity test: after Rock's own Giving Automation job has run on a loaded
        /// database, compares what Rock wrote with the loader's port. Returns the number of differences.
        /// </summary>
        /// <param name="connectionString">The loaded database.</param>
        /// <param name="dataset">The dataset that was loaded, for example "core-1".</param>
        /// <param name="progress">Receives each result line. Null reports nothing.</param>
        public static int CheckGivingParity( string connectionString, string dataset, IProgress<LoadProgressReport> progress = null )
        {
            using var db = new RockDatabase( connectionString ) { Log = new LoadLog( progress ) };
            return new GivingAnalyticsLoader( db, new AnchorDates( DateTime.Today ), $"SampleData:{dataset}" ).CheckParity();
        }

        /// <summary>
        /// Refuses a database that is not ready, before anything is written. A database on another Rock
        /// gets a warning instead when any Rock version is allowed.
        /// </summary>
        private static void CheckReadiness( RockDatabase db, PackageManifest manifest, bool isAnyVersionAllowed )
        {
            var readiness = DatabaseReadiness.Read( db, manifest );
            var mismatch = readiness.IsMigrationMismatch ? DatabaseReadiness.MismatchMessage( manifest, readiness.NewestMigration ) : null;
            var problems = readiness.Problems.Where( p => !( isAnyVersionAllowed && p == mismatch ) ).ToList();
            if ( problems.Count == 1 && problems[0] == mismatch )
            {
                throw new RockVersionMismatchException( $"{mismatch} Use a package built for this Rock." );
            }

            if ( problems.Count > 0 )
            {
                throw new DatabaseNotReadyException( problems );
            }

            if ( mismatch != null )
            {
                db.Log.Write( $"Warning: {mismatch} Loading anyway." );
            }
        }

        /// <summary>
        /// Rock's keys: each one from the options when set, otherwise from the web.config. Null when there is no PasswordKey.
        /// </summary>
        private static RockKeys ReadKeys( DatasetLoadOptions options )
        {
            var webConfig = options.RockWebConfigPath == null ? (null, null) : RockKeys.ReadWebConfig( options.RockWebConfigPath );
            return RockKeys.FromKeys( options.PasswordKey ?? webConfig.PasswordKey, options.DataEncryptionKey ?? webConfig.DataEncryptionKey );
        }

        /*
            10/21/26 - CLAUDE

            Every step plans in memory whether or not its part loads, and only the writes are skipped.
            Planning reads only what earlier always-on steps wrote, and each piece of data draws from
            its own seeded stream, so skipping a part never changes the rows another part writes.
            Attendance and giving are planned even when they do not load, because serving, giving,
            prayer, and steps read those plans. A part another part's rows need is added by SampleDataParts.
            Steps are the one exception to planning everything: they plan from rows that small groups,
            serving, and giving wrote, so they plan only when they load, and they need those parts.
            Their student check-ins come from the attendance plan, so they do not need attendance.
            Nothing after them reads what they plan or write.

            Reason: A person can choose parts, and each chosen part looks the same as in a full load.
        */

        /// <summary>
        /// Runs every load step in order inside the open transaction, skipping the writes of parts not chosen.
        /// </summary>
        private static void LoadSteps( RockDatabase db, SampleDataSource source, string dataset, string foreignKey, AnchorDates dates, DatasetLoadOptions options, IReadOnlyCollection<SampleDataPart> parts, RockKeys keys )
        {
            var log = db.Log;
            var steps = new StepRunner( log, parts );

            steps.Begin( "Configuration" );
            var configuration = new ConfigurationLoader( db, source, dataset, foreignKey );
            configuration.Load();
            var smallGroupSetup = new SmallGroupConfigurationLoader( db, source, foreignKey, configuration.CampusIds ).Load();
            var servingSetup = new ServingConfigurationLoader( db, source, foreignKey, configuration.CampusPlans ).Load();
            var connectionSetup = new ConnectionConfigurationLoader( db, source, foreignKey, configuration.CampusIds, servingSetup ).Load();
            new CampaignLoader( db, source, dates, foreignKey ).Load();

            var profile = source.ReadProfile();
            var years = options.HistoryYears;
            var calendar = new SundayCalendar( dates.Anchor, years, profile );
            var seed = options.Seed;

            // The Cast always lives on the main campus. Generated households come from the dataset manifest's
            // count per campus, or from the spike crowd, which spreads across the dataset's campuses.
            steps.Begin( "People" );
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
                log.Write( $"  Generated: {generated.Count:N0} households ({perCampusText}) and {generatedPlans.Count:N0} small groups ({steps.Elapsed:N0} ms)" );

                // Businesses come after small groups are planned, so they never change a planned group.
                var castMainCampusBusinesses = households.Count( h => h.Kind == "business" && castHouseholdKeys.Contains( h.Key ) );
                var businesses = generator.GenerateBusinesses( generated, configuration.CampusIds.Keys.ToList(), mainCampusKey, castMainCampusBusinesses );
                households.AddRange( businesses );
                var businessText = string.Join( ", ", businesses.GroupBy( h => h.CampusSlot ).Select( g => $"{g.Key} {g.Count():N0}" ) );
                log.Write( $"  Generated: {businesses.Count:N0} businesses ({businessText})" );
            }

            // Serving is planned before people load too, so new team leaders load as Leading.
            var servingPlans = ServingPlanner.Plan( source.Serving, profile, dates, seed, households, castHouseholdKeys, source.ReadCastServingTeams(), configuration.CampusPlans, mainCampusKey );

            var people = new PeopleLoader( db, source, dates, foreignKey, configuration.CampusIds, mainCampusKey );
            people.Load( households );
            var contactCount = new RelationshipsLoader( db, foreignKey ).Load( people.People );
            log.Write( $"  Business contacts: {contactCount:N0}" );

            // Cast staff join the security roles their stories give them, all roles Rock ships.
            var securityRoles = new SecurityRolesLoader( db, foreignKey );
            log.Write( $"  Security role memberships: {securityRoles.LoadCast( source, people.People.Values ):N0}" );

            // Every Cast person's delivered photo, plus pool faces for generated households when the dataset asks.
            if ( steps.Begin( "Photos" ) )
            {
                var photoPercent = manifest?.Photos?.GeneratedHouseholdPercent ?? 0;
                var photoLoader = new PhotoLoader( db, source, foreignKey, seed );
                photoLoader.Load( people.Households, castHouseholdKeys, photoPercent );
                log.Write( $"  Opportunity photos: {photoLoader.LoadOpportunityPhotos( connectionSetup.OpportunityIds )}" );
            }

            // Sunday attendance, small group attendance, and serving requests all copy into Attendance. Its
            // indexes are rebuilt once, after every copy; nothing reads Attendance in between.
            db.DeferIndexRebuild( "Attendance" );

            var isAttendanceLoaded = steps.Begin( "Attendance" );
            var attendance = new AttendanceLoader( db, calendar, dates, configuration, foreignKey, seed );
            attendance.Plan( people.Households );
            if ( isAttendanceLoaded )
            {
                attendance.Write();
            }

            // Kids check-in streaks and achievements come straight from the check-ins just planned. They read the plan
            // in memory, not Attendance, and no later step reads what they write.
            if ( steps.Begin( "Achievements" ) )
            {
                new AchievementsLoader( db, source.Achievements, calendar, dates, foreignKey ).Load( attendance.CheckInSundaysByAlias, attendance.CheckInSundays );
            }

            if ( steps.Begin( "Small groups" ) )
            {
                new SmallGroupLoader( db, source.SmallGroups, smallGroupSetup, calendar, dates, foreignKey, seed, mainCampusKey, configuration.CampusIds ).Load( groupPlans, people );
                var groupPhotoCount = new PhotoLoader( db, source, foreignKey, seed ).LoadGroupPhotos( groupPlans );
                log.Write( $"  Group photos: {groupPhotoCount}" );
            }

            // Volunteers serve on the Sundays their family came, so serving follows Sunday attendance.
            if ( steps.Begin( "Serving" ) )
            {
                new ServingLoader( db, source.Serving, servingSetup, configuration.CampusPlans, dates, foreignKey, seed, configuration.KioskDeviceId, calendar.HistoryStart ).Load( servingPlans, people, attendance.FamilySundays );
            }

            db.RebuildDeferredIndexes();

            // Requests to serve end in the team memberships serving just loaded, so they follow it.
            if ( steps.Begin( "Connection requests" ) )
            {
                new ConnectionRequestLoader( db, source, connectionSetup, servingSetup, profile, dates, foreignKey, seed ).Load( servingPlans, people );
            }

            // Rock's PasswordKey fills the check scanner's matching fields. Without it, the fields stay
            // empty and everything else still loads.
            var isGivingLoaded = steps.Begin( "Giving" );
            if ( isGivingLoaded )
            {
                log.Write( keys == null
                    ? "  No Rock PasswordKey (give a Rock web.config); check scanner fields stay empty"
                    : options.PasswordKey == null ? $"  Check scanner fields keyed from {options.RockWebConfigPath}" : "  Check scanner fields keyed from the given PasswordKey" );
            }

            // The building campaign's pledges are planned first, so the gifts that follow them are part of giving.
            // The pledges and campaign gifts are the giving part's rows, but the fund is church setup, so its goal
            // is written in every load (decision D-rock-loader-7).
            var pledges = source.Campaign == null ? null : new PledgeLoader( db, source.Campaign, source.Accounts.Accounts.First( a => a.Name == source.Campaign.Fund ).Guid, dates, foreignKey, seed );
            pledges?.Plan( people.Households, castHouseholdKeys, profile.Number( "giving.pctUnitsWithPledge", 12 ) / 100 );
            pledges?.WriteGoal();
            var giving = new GivingLoader( db, source, profile, calendar, dates, configuration, attendance.FamilySundays, foreignKey, seed ) { Keys = keys, Campaign = pledges?.Giving };
            giving.Plan( people.Households );
            if ( isGivingLoaded )
            {
                giving.Write();
                pledges?.Write();

                // Rock Cleanup's nightly campus tithe metric, from the postal codes of the families that gave.
                log.Write( $"  Campus tithe metrics: {giving.UpdateCampusTitheMetric()}" );
            }

            // Giving attributes, bins, and journey stages, as if Rock's Giving Automation job had run every night.
            GivingAnalyticsLoader analytics = null;
            if ( steps.Begin( "Giving analytics" ) )
            {
                analytics = new GivingAnalyticsLoader( db, dates, foreignKey );
                analytics.Load( giving.QualifyingGifts );
            }

            // The sample church's giving alert types, with the alerts Rock would have raised over the last year.
            if ( steps.Begin( "Giving alerts" ) )
            {
                new GivingAlertsLoader( db, source, foreignKey, analytics.RunAt, analytics.Gifts ).Load();
            }

            // Prayer requests and the prayer team's activity. Who asks depends on who attended, gave, or is in a group.
            if ( steps.Begin( "Prayer" ) )
            {
                var prayer = new PrayerLoader( db, source, dates, foreignKey, seed );
                prayer.Load( people.Households, attendance.FamilySundays, giving.QualifyingGifts, smallGroupSetup.GroupTypeId, calendar.HistoryStart, profile );

                // Rock Cleanup's daily interaction counts, for every channel that keeps them. Run after the last step that writes interactions.
                prayer.UpdateDailyCounts();
            }

            // Note types with their security, the Cast's hand-written notes, and generated notes for generated households.
            // Rock's DataEncryptionKey, read with the PasswordKey, turns staff mentions into real mentions.
            if ( steps.Begin( "Notes" ) )
            {
                new NotesLoader( db, source, profile, dates, foreignKey, seed, years, keys ).Load( people, castHouseholdKeys, mainCampusKey, securityRoles );
            }

            // The adult and student step programs. Every step comes from a record the earlier steps wrote: group and
            // team memberships, the giving journey, the Baptism Date (decision 86), and the student check-ins in the
            // attendance plan. Steps need giving (SampleDataParts.All), so the giving analytics have run whenever
            // steps load. The check-ins come from the plan, so steps do not need the Attendance part.
            if ( steps.Begin( "Steps" ) )
            {
                new StepsLoader( db, source, dates, foreignKey, seed, calendar.HistoryStart, smallGroupSetup.GroupTypeId, servingSetup.GroupTypeId )
                    .Load( people, castHouseholdKeys, attendance, analytics.Gifts, analytics.FinalJourneyRun );
            }

            // The Cast givers and kids that documentation and tests lean on must still show what their stories say.
            steps.Begin( "Cast expectations" );
            CastExpectations.Check( db, source, parts );

            // Scanned check images are the slowest step, so the CLI leaves them out unless asked.
            if ( steps.Begin( "Check images", $"  Skipped ({giving.CheckGifts.Count:N0} check gifts have no images). Turn on check images to draw them." ) )
            {
                var checkImageMonths = options.CheckImageMonths ?? manifest?.CheckImages?.Months ?? 12;
                new CheckImageLoader( db, dates, foreignKey, seed, source.Files ).Load( giving.CheckGifts, checkImageMonths, options.CheckImageCopyFolder );
            }

            steps.Finish();
        }

        /// <summary>
        /// Whether a step of this part runs. A step with no part always runs.
        /// </summary>
        private static bool IsLoaded( SampleDataPart? part, IReadOnlyCollection<SampleDataPart> parts ) => part == null || parts.Contains( part.Value );

        private static string ShortCommit( PackageManifest manifest )
        {
            var commit = manifest.Source?.Commit ?? "unknown";
            return $"{( commit.Length > 7 ? commit.Substring( 0, 7 ) : commit )}{( manifest.Source?.IsModified == true ? " with changes" : string.Empty )}";
        }

        /// <summary>
        /// Walks <see cref="Steps"/> in order: reports each step, times it, and says whether its part loads.
        /// </summary>
        private sealed class StepRunner
        {
            private readonly LoadLog _log;
            private readonly IReadOnlyCollection<SampleDataPart> _parts;
            private readonly Stopwatch _timer = new Stopwatch();
            private int _next;
            private bool _isRunning;

            public StepRunner( LoadLog log, IReadOnlyCollection<SampleDataPart> parts )
            {
                _log = log;
                _parts = parts;
            }

            /// <summary>
            /// Milliseconds since the current step started.
            /// </summary>
            public long Elapsed => _timer.ElapsedMilliseconds;

            /// <summary>
            /// Finishes the step before and starts the next one, which must be <paramref name="name"/>.
            /// Returns false, after reporting it skipped, when the step's part does not load.
            /// </summary>
            public bool Begin( string name, string skippedLine = "  Skipped (not chosen)" )
            {
                FinishRunningStep();
                var step = _next < Steps.Length ? Steps[_next] : default;
                if ( step.Name != name )
                {
                    throw new InvalidOperationException( $"The load ran step \"{name}\" where DatasetLoader.Steps has \"{step.Name}\"." );
                }

                _next++;
                if ( !IsLoaded( step.Part, _parts ) )
                {
                    _log.Write( name );
                    _log.Write( skippedLine );
                    return false;
                }

                _log.StartStep( name );
                _isRunning = true;
                _timer.Restart();
                return true;
            }

            /// <summary>
            /// Finishes the last step. Every step in <see cref="Steps"/> must have been begun.
            /// </summary>
            public void Finish()
            {
                FinishRunningStep();
                if ( _next != Steps.Length )
                {
                    throw new InvalidOperationException( $"The load stopped before step \"{Steps[_next].Name}\"." );
                }
            }

            private void FinishRunningStep()
            {
                if ( !_isRunning )
                {
                    return;
                }

                _log.Write( $"  ({_timer.ElapsedMilliseconds:N0} ms)" );
                _log.FinishStep();
                _isRunning = false;
            }
        }
    }
}
