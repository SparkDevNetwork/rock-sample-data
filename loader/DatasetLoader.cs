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
        /// The step that checks the Cast expectations. It runs only when <see cref="DatasetLoadOptions.IsCastExpectationsChecked"/> is true.
        /// </summary>
        private const string CastExpectationsStep = "Cast expectations";

        /// <summary>
        /// Every load step in order, with the part it belongs to. Null is a step that always runs, except the
        /// Cast expectations, which run only when asked.
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
            ("Communications", SampleDataPart.Communications),
            ("Calendar", SampleDataPart.Calendar),
            ("Registrations", SampleDataPart.Registrations),
            ("Data views", null),
            ("Logins", null),
            (CastExpectationsStep, null),
            ("Check images", SampleDataPart.CheckImages)
        };

        /// <summary>
        /// Loads the package. Everything rolls back if any step fails.
        /// </summary>
        /// <exception cref="DatabaseNotReadyException">The database has people beyond a new Rock install, or another load is running. Nothing was loaded.</exception>
        /// <exception cref="RockVersionMismatchException">The database's Rock is not the package's major release, or is unknown. Nothing was loaded.</exception>
        /// <exception cref="ArgumentException">The PasswordKey option is not hex.</exception>
        public static void Load( string connectionString, SampleDataPackage package, DatasetLoadOptions options = null )
        {
            options ??= new DatasetLoadOptions();
            var dataset = package.Manifest.Dataset;
            var foreignKey = $"SampleData:{dataset}";
            var source = new SampleDataSource( package );
            var parts = SampleDataParts.WithDependencies( options.Parts ?? SampleDataParts.All.Select( p => p.Part ).ToList() );
            var keys = ReadKeys( options );
            if ( options.LoginPasswordHash != null && !LoginsLoader.IsBcryptHash( options.LoginPasswordHash ) )
            {
                throw new ArgumentException( "LoginPasswordHash must be a BCrypt hash, made the way Rock's Database login stores a password. Nothing was loaded." );
            }

            var log = new LoadLog( options.Progress ) { TotalSteps = Steps.Count( s => IsLoaded( s.Part, parts ) && ( s.Name != CastExpectationsStep || options.IsCastExpectationsChecked ) ) };

            using var db = new RockDatabase( connectionString ) { Recorder = options.Recorder, Log = log };

            // Every relative date counts back from the anchor. Tests pin it. Without one, it is the database
            // server's date, the clock Rock Cleanup uses for ages and daily counts (decision 152).
            var dates = new AnchorDates( options.Anchor ?? db.ServerToday() );

            db.BeginTransaction();
            try
            {
                // The lock and the guard run inside the load's transaction, so two loads can never both pass.
                if ( !db.TryLockLoad() )
                {
                    throw new DatabaseNotReadyException( new[] { "Another sample data load is running on this database. Try again when it finishes." } );
                }

                CheckReadiness( db, package.Manifest, options.RockVersion ?? RockVersionInfo.ReadRunning(), options.IsAnyRockVersionAllowed );

                var total = Stopwatch.StartNew();
                log.Write( $"Loading dataset {dataset} (built from {ShortCommit( package.Manifest )} for Rock {package.Manifest.Rock?.MajorRelease}, verified on {package.Manifest.Rock?.Version}, anchor {dates.Anchor:yyyy-MM-dd}){( options.IsDryRun ? " (dry run)" : string.Empty )}" );

                LoadSteps( db, source, dataset, foreignKey, dates, options, parts, keys );
                db.RequireIndexesRebuilt();
                foreach ( var timing in db.Timings )
                {
                    log.WriteTiming( timing.Kind == LoadTimingKind.BulkCopy
                        ? $"    bulk {timing.Name,-14} {timing.Rows,8:N0} rows {timing.Milliseconds,7:N0} ms"
                        : $"    index {timing.Name,-13} {string.Empty,13} {timing.Milliseconds,7:N0} ms", timing );
                }

                var finish = Stopwatch.StartNew();
                if ( options.IsDryRun )
                {
                    db.Rollback();
                    log.WriteTiming( $"  (Rollback: {finish.ElapsedMilliseconds:N0} ms)", new LoadTiming( LoadTimingKind.Load, "Rollback", null, finish.ElapsedMilliseconds ) );
                    log.WriteTiming( $"Dry run complete in {total.ElapsedMilliseconds:N0} ms. Everything was rolled back.", new LoadTiming( LoadTimingKind.Load, "Load", null, total.ElapsedMilliseconds ) );
                }
                else
                {
                    db.Commit();
                    log.WriteTiming( $"  (Commit: {finish.ElapsedMilliseconds:N0} ms)", new LoadTiming( LoadTimingKind.Load, "Commit", null, finish.ElapsedMilliseconds ) );
                    log.WriteTiming( $"Done in {total.ElapsedMilliseconds:N0} ms.", new LoadTiming( LoadTimingKind.Load, "Load", null, total.ElapsedMilliseconds ) );
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
        /// install. With a package, also checks that the database's Rock is the package's major release,
        /// reading the Rock version from the Rock.Version assembly this process runs (inside Rock).
        /// It never honors <see cref="DatasetLoadOptions.IsAnyRockVersionAllowed"/>; only a load does.
        /// <see cref="Load"/> runs the same check inside its transaction.
        /// </summary>
        public static DatabaseReadiness CheckDatabase( string connectionString, SampleDataPackage package = null )
        {
            return CheckDatabase( connectionString, package, RockVersionInfo.ReadRunning() );
        }

        /// <summary>
        /// Checks whether a database can take sample data, as <see cref="CheckDatabase(string, SampleDataPackage)"/>
        /// does, for a database whose Rock version is given, for example "20.0.10".
        /// </summary>
        public static DatabaseReadiness CheckDatabase( string connectionString, SampleDataPackage package, string rockVersion )
        {
            using var db = new RockDatabase( connectionString );
            return DatabaseReadiness.Read( db, package?.Manifest, rockVersion );
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
        /// major release, or an unknown Rock, gets a warning instead when any Rock version is allowed.
        /// </summary>
        private static void CheckReadiness( RockDatabase db, PackageManifest manifest, string rockVersion, bool isAnyVersionAllowed )
        {
            var readiness = DatabaseReadiness.Read( db, manifest, rockVersion );
            var mismatch = readiness.IsRockMismatch ? DatabaseReadiness.MismatchMessage( manifest, rockVersion ) : null;
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
            var categoryCount = new CategoriesLoader( db, source, foreignKey ).Load();
            if ( categoryCount > 0 )
            {
                log.Write( $"  Church categories: {categoryCount}" );
            }

            // The Staff person attributes, in their category, with the block that shows them (decision 201).
            var staffLoader = new StaffLoader( db, source, dates, foreignKey );
            if ( staffLoader.HasAttributes )
            {
                log.Write( $"  Staff attributes: {staffLoader.LoadSetup()}" );
            }

            // The org chart's units in Rock's Organization Unit groups (decision 202).
            if ( staffLoader.HasOrgChart )
            {
                log.Write( $"  Org chart: {staffLoader.LoadOrgChartSetup()}" );
            }

            var profile = source.ReadProfile();
            var years = options.HistoryYears;
            var calendar = new SundayCalendar( dates.Anchor, years, profile );
            var seed = options.Seed;

            // Most of the Cast lives on the main campus. A Cast household on another campus, such as its campus
            // pastor's, comes only in a package with that campus (decision 199). Generated households come
            // from the dataset manifest's count per campus, or from the spike crowd, which spreads across the
            // dataset's campuses.
            steps.Begin( "People" );
            var mainCampusKey = source.Campuses.Campuses.Single( c => c.RockDefault ).Key;
            var households = source.ReadHouseholds();
            var offCampus = households.Where( h => h.CampusSlot != "main" && !string.IsNullOrEmpty( h.CampusSlot ) && !configuration.CampusIds.ContainsKey( h.CampusSlot ) ).Select( h => h.Key ).ToList();
            if ( offCampus.Count > 0 )
            {
                throw new InvalidDataException( $"The package has Cast households on campuses not in {dataset}: {string.Join( ", ", offCampus )}. The builder leaves them out." );
            }

            var castHouseholdKeys = households.Select( h => h.Key ).ToHashSet();
            var manifest = source.ReadDataset( dataset );

            // The Cast's small groups come from cast/groups. Generated households get theirs here, before
            // people load, so their new leaders load with the leader status.
            var groupPlans = source.ReadCastSmallGroupPlans();
            var generator = new CrowdGenerator( profile, source, seed, dates.Anchor );
            List<CastHousehold> generated = null;
            using ( log.TimePart( "Generate the Crowd" ) )
            {
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
                    if ( manifest.GeneratedHouseholds.StatusWeights is { } statusWeights )
                    {
                        generator.UseStatusWeights( statusWeights );
                    }

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
            }

            // Serving is planned before people load too, so new team leaders load as Leading.
            List<ServingTeamPlan> servingPlans;
            using ( log.TimePart( "Plan serving" ) )
            {
                servingPlans = ServingPlanner.Plan( source.Serving, profile, dates, seed, households, castHouseholdKeys, source.ReadCastServingTeams(), configuration.CampusPlans, mainCampusKey );
            }

            var people = new PeopleLoader( db, source, dates, foreignKey, configuration.CampusIds, mainCampusKey );
            SecurityRolesLoader securityRoles;
            using ( log.TimePart( "Write people" ) )
            {
                people.Load( households );
                var contactCount = new RelationshipsLoader( db, foreignKey ).Load( people.People );
                log.Write( $"  Business contacts: {contactCount:N0}" );

                // Cast staff join the security roles their stories give them, all roles Rock ships.
                securityRoles = new SecurityRolesLoader( db, foreignKey );
                log.Write( $"  Security role memberships: {securityRoles.LoadCast( source, people.People.Values ):N0}" );

                // Each loaded staff member's title, department, campus, bio, quote, supervisor, and start date.
                if ( staffLoader.HasAttributes )
                {
                    var campusGuids = source.Campuses.Campuses.ToDictionary( c => c.Key, c => c.Guid );
                    log.Write( $"  Staff attribute values: {staffLoader.LoadValues( people, campusGuids ):N0}" );
                }

                // Each org chart unit's leader and staff.
                if ( staffLoader.HasOrgChart )
                {
                    log.Write( $"  Org chart members: {staffLoader.LoadOrgChartMembers( people ):N0}" );
                }
            }

            // Every Cast person's delivered photo, plus pool faces for generated households when the dataset asks.
            if ( steps.Begin( "Photos" ) )
            {
                var photoPercent = manifest?.Photos?.GeneratedHouseholdPercent ?? 0;
                var photoLoader = new PhotoLoader( db, source, foreignKey, seed );
                photoLoader.Load( people.Households, castHouseholdKeys, photoPercent );
                log.Write( $"  Opportunity photos: {photoLoader.LoadOpportunityPhotos( connectionSetup.OpportunityIds )}" );

                // Each staff member's staff photo, as their Staff Photo attribute (decision 201).
                var staffPhotoAttribute = source.Staff?.Attributes.FirstOrDefault( a => a.Key == "StaffPhoto" );
                if ( staffLoader.HasAttributes && staffPhotoAttribute != null )
                {
                    log.Write( $"  Staff photos: {photoLoader.LoadStaffPhotos( people.Households, staffPhotoAttribute.Guid )}" );
                }
            }

            // Sunday attendance, small group attendance, and serving requests all copy into Attendance. Its
            // indexes are rebuilt once, after every copy; nothing reads Attendance in between.
            db.DeferIndexRebuild( "Attendance" );

            var isAttendanceLoaded = steps.Begin( "Attendance" );
            var attendance = new AttendanceLoader( db, calendar, dates, configuration, foreignKey, seed );
            using ( log.TimePart( "Plan attendance" ) )
            {
                attendance.Plan( people.Households );
            }

            if ( isAttendanceLoaded )
            {
                using ( log.TimePart( "Write attendance" ) )
                {
                    attendance.Write();
                }
            }

            // The Student Path's camps, trips, decisions, and Student Leader steps come from the student check-ins just
            // planned. They are planned in every load, so the steps part copies them and the registrations part reads
            // them, and last summer's campers are the people with its camp step whichever parts load (decision 206).
            StudentPath studentPath = null;
            if ( source.Steps != null )
            {
                using ( log.TimePart( "Plan the Student Path" ) )
                {
                    var stepPeople = StepPerson.Of( people.Households ).Select( p => StepPerson.From( p.Household, p.Person, castHouseholdKeys.Contains( p.Household.Cast.Key ) ) ).ToList();
                    var studentCheckIns = attendance.CheckInDays( source.Steps.Programs.Where( p => p.Students != null ).SelectMany( p => p.Students.CheckInGroups ) );
                    studentPath = StudentPathPlanner.Plan( source.Steps, stepPeople, studentCheckIns, calendar.HistoryStart, dates.Anchor, seed );
                }
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

            db.RebuildDeferredIndexes( "Attendance" );

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
            // is written in every load (decision 126).
            var pledges = source.Campaign == null ? null : new PledgeLoader( db, source.Campaign, source.Accounts.Accounts.First( a => a.Name == source.Campaign.Fund ).Guid, dates, foreignKey, seed );
            GivingLoader giving;
            using ( log.TimePart( "Plan gifts" ) )
            {
                pledges?.Plan( people.Households, castHouseholdKeys, profile.Number( "giving.pctUnitsWithPledge", 12 ) / 100 );
                pledges?.WriteGoal();
                giving = new GivingLoader( db, source, profile, calendar, dates, configuration, attendance.FamilySundays, foreignKey, seed ) { Keys = keys, Campaign = pledges?.Giving, CheckWeightScale = manifest?.Giving?.CheckWeightScale ?? 1 };
                giving.Plan( people.Households );
            }

            if ( isGivingLoaded )
            {
                using ( log.TimePart( "Write gifts" ) )
                {
                    giving.Write();
                    pledges?.Write();

                    // Rock Cleanup's nightly campus tithe metric, from the postal codes of the families that gave.
                    log.Write( $"  Campus tithe metrics: {giving.UpdateCampusTitheMetric()}" );
                }
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
            var prayer = new PrayerLoader( db, source, dates, foreignKey, seed );
            if ( steps.Begin( "Prayer" ) )
            {
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
                    .Load( people, castHouseholdKeys, attendance, studentPath, analytics.Gifts, analytics.FinalJourneyRun );
            }

            // The church's texting numbers, its branded template, and its communication lists. The lists copy the
            // serving teams, the small group leaders, and the prayer team, so communications need those parts, and
            // who is involved enough for a newsletter comes from the attendance and giving plans.
            // History takes over a million rows from communications, then thousands more from logins. Its indexes stay
            // off until logins are written, and nothing in between reads it.
            db.DeferIndexRebuild( "History" );
            if ( steps.Begin( "Communications" ) )
            {
                new CommunicationsLoader( db, source, profile, dates, dataset, foreignKey, seed )
                    .Load( people, castHouseholdKeys, mainCampusKey, configuration.CampusIds, attendance.FamilySundays, giving.QualifyingGifts, prayer.TeamPersonIds, smallGroupSetup.GroupTypeId, servingSetup.GroupTypeId );
            }

            // The church's calendar. Its contacts are Cast staff and campus pastors, and its dates come from the same rules
            // steps and messages use, so it reads only configuration and people.
            List<PlannedEvent> calendarPlan = null;
            if ( steps.Begin( "Calendar" ) )
            {
                calendarPlan = new CalendarLoader( db, source, dates, foreignKey, seed, calendar.HistoryStart ).Load( people, castHouseholdKeys, mainCampusKey, configuration.CampusPlans );
            }

            // Registrations link to the occurrences the calendar just wrote, so they take its plan instead of planning the
            // calendar again. Registrations need the calendar (SampleDataParts.All), so it has run whenever they load. Students
            // register from the Student Path plan, and adults and volunteers from the small group and serving plans, which
            // every load makes, so they need neither the steps, small groups, nor serving part.
            if ( steps.Begin( "Registrations" ) )
            {
                new RegistrationsLoader( db, source, dates, dataset, foreignKey, seed ).Load( calendarPlan, people, configuration.CampusPlans, studentPath, groupPlans, servingPlans, mainCampusKey, castHouseholdKeys );
            }

            // The church's layered data views (docs/reporting.md). Some point at the group sync and registration views, so
            // they come after both. A view that needs a part this load left out is skipped.
            steps.Begin( "Data views" );
            new ReportingLoader( db, source, dates, foreignKey, seed ).Load();

            // Logins come last, because who has one reads the group, serving, and giving plans, and the prayer team and
            // app devices when those parts load. They load only with a password hash. Account protection follows
            // logins, security roles, and scheduled gifts, so every load sets it here.
            steps.Begin( "Logins" );
            var logins = new LoginsLoader( db, foreignKey );
            if ( options.LoginPasswordHash == null || source.Logins == null )
            {
                log.Write( options.LoginPasswordHash == null ? "  No password was given, so no logins were made." : "  Skipped (this package has no configuration/logins.json)" );
            }
            else
            {
                var plannedLogins = new LoginPlanner( source.Logins, dates, calendar.HistoryStart, seed ).Plan( new LoginInputs
                {
                    Households = people.Households,
                    CastHouseholdKeys = castHouseholdKeys,
                    CampusPastors = CampusPastors.Pick( people, people.Households.Where( h => !castHouseholdKeys.Contains( h.Cast.Key ) && h.Cast.Kind != "business" ), mainCampusKey, source.Staff ),
                    GroupPlans = groupPlans,
                    ServingPlans = servingPlans,
                    Gifts = giving.QualifyingGifts,
                    WebsiteSourceId = giving.WebsiteSourceId,
                    PrayerTeam = prayer.TeamPersonIds,
                    AppFirstSeen = logins.AppFirstSeen(),
                    TakenUserNames = logins.TakenUserNames(),
                    PeopleWithLogins = logins.PeopleWithLogins(),
                    RockRoleNames = source.SecurityRoles?.Roles.Select( r => r.Name ).ToHashSet() ?? new HashSet<string>()
                } );
                logins.Write( plannedLogins, options.LoginPasswordHash );
                var castPeople = people.Households.Where( h => castHouseholdKeys.Contains( h.Cast.Key ) ).SelectMany( h => h.Members ).Select( m => m.PersonId ).ToHashSet();
                var castLogins = plannedLogins.Count( l => castPeople.Contains( l.Person.PersonId ) );
                log.Write( $"  Logins: {plannedLogins.Count:N0} ({castLogins:N0} Cast, {plannedLogins.Count( l => !l.IsConfirmed ):N0} unconfirmed)" );
            }

            log.Write( $"  Account protection profiles set: {logins.UpdateAccountProtection():N0}" );
            db.RebuildDeferredIndexes( "History" );

            /*
                10/30/26 - CLAUDE

                The Cast expectations check that the people documentation and tests lean on still show what their
                stories say. They are a check for those who build the sample data, and the verify tool holds every
                release to them. A person installing through Rock's block saw "WARNING: 1 of 437 Cast expectations do
                not hold" on a complete install, which read as a failure. So only a caller that asks runs them.

                Reason: People installing see only what matters to them (decision 217).
            */
            if ( options.IsCastExpectationsChecked )
            {
                steps.Begin( CastExpectationsStep );
                CastExpectations.Check( db, source, parts );
            }
            else
            {
                steps.Skip( CastExpectationsStep );
            }

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
            /// Finishes the step before and moves past the next one, which must be <paramref name="name"/>,
            /// without reporting it. For a step the caller did not ask for, such as the Cast expectations.
            /// </summary>
            public void Skip( string name )
            {
                FinishRunningStep();
                var step = _next < Steps.Length ? Steps[_next] : default;
                if ( step.Name != name )
                {
                    throw new InvalidOperationException( $"The load skipped step \"{name}\" where DatasetLoader.Steps has \"{step.Name}\"." );
                }

                _next++;
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

                var milliseconds = _timer.ElapsedMilliseconds;
                _log.WriteTiming( $"  ({milliseconds:N0} ms)", new LoadTiming( LoadTimingKind.Step, _log.CurrentStep, _log.CurrentStep, milliseconds ) );
                _log.FinishStep();
                _isRunning = false;
            }
        }
    }
}
