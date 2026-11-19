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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace RockSampleData.RockRunner
{
    /// <summary>
    /// Runs a piece of Rock against the database named in a config file, outside IIS.
    ///
    /// Usage: rock-runner --task startup|cleanup|hash-password|sign-in|group-sync|data-views|data-view-people|reports|authorize|assessment-scores|segments|adaptive-messages|email-sections|signals|personal-links|notifications|process-reminders|process-workflows|launch-step-workflows|group-requirements --rock-bin <RockWeb\bin folder> --config <config file> [--input <file>]
    ///
    ///   startup  Rock's own start-up steps that write to the database: the RockGetDate function, plugin and
    ///            hotfix migrations, and the attribute value views, then the Lava engine, Rock's one-time update jobs
    ///            (which a new install runs on its first evening), and its one-time post-install job (its first night).
    ///            The trusted base runs it once after EF's migrations, so it matches a Rock that has started and run
    ///            its first night, not just a migrated database.
    ///   cleanup  Rock's Rock Cleanup job once, with its default settings. It prints the job's result.
    ///   hash-password  Rock's Database login hash of the password on the input file's first line, the way the
    ///            Sample Data Installer block makes the hash it passes the loader (decision 180).
    ///   sign-in  Signs in through Rock's Database login with each input line's username and password, separated by
    ///            a tab, and prints "username signed in" or "username refused" for each.
    ///   group-sync  Runs Rock's own group sync (GroupSyncService.SyncGroups, what the Group Sync job calls) on every sync,
    ///            whatever its interval, and prints how many syncs ran, the members added, removed, and not added, and
    ///            each warning.
    ///   data-views  Runs every data view through Rock's own engine (DataViewCache.GetEntityIds) and prints, for each,
    ///            "ok", its Guid, how many it found, and its milliseconds, or "error", its Guid, and Rock's message.
    ///   data-view-people  Runs each data view whose Guid is a line of the input file through Rock's own engine
    ///            (DataViewCache.GetEntityIds) and prints its Guid, a tab, and the Ids it found, separated by commas.
    ///   reports  Checks every report's columns the way Rock's report grid reads them, and renders each Lava column, and
    ///            prints "ok" and its Guid, or "error", its Guid, and the problems.
    ///   authorize  Asks Rock's own security check (Authorization.Authorized) each input line's question: an item kind
    ///            (Category, DataView, Report, PersonalizationSegment, Page, SignalType, Snippet, Tag, or BenevolenceType), its Guid, an action,
    ///            and a person's Guid, separated by tabs. Prints True or False for each, in order.
    ///   assessment-scores  Scores every assessment's saved answers with Rock's own scoring services and writes its answer
    ///            data again with Rock's own serializer. Prints each assessment's Id, "same" or "differs" for the answer
    ///            data, and the person attribute values Rock's service would save, as key=value, separated by tabs.
    ///   segments  Runs Rock's own segment update (PersonalizationSegmentService.UpdatePersonAliasPersonalizationDataForSegment,
    ///            what the Update Personalization Data job calls) on every personalization segment, and prints each
    ///            segment's Guid, how many people it added, and how many it removed, separated by tabs.
    ///   process-reminders  Runs Rock's Process Reminders job once, as its stored job with the settings Rock's scheduler
    ///            gives it, as Rock runs it every weekday morning. Prints "processed" and how many reminders it notified about, then "error" and each
    ///            error the job recorded, one per line.
    ///   process-workflows  Runs Rock's Process Workflows job once, as its stored job with the settings Rock's scheduler
    ///            gives it, as Rock runs it every ten minutes. Prints the job's result.
    ///   launch-step-workflows  For each input line's step Id, step workflow trigger Id, and workflow attribute key, separated
    ///            by tabs, launches the trigger's workflow on the step the way Rock does when a step becomes complete, without
    ///            saving it. Prints the step Id, the workflow's name, the assigned person's Guid, the
    ///            attribute's value, whether it waits at a form, and Rock's errors, separated by tabs.
    ///   adaptive-messages  Renders Rock's adaptivemessage Lava command for each input line's person Guid and message key,
    ///            separated by a tab, with views not tracked, and prints the adaptation Id it shows and its call to action,
    ///            separated by a tab, or an empty line when it shows none. For "block:" and an HTML Content block's Guid in
    ///            place of the key, it renders that block's content for the person instead, on one line.
    ///   email-sections  Checks every email section the way Rock's email editor saves one: the model's validation and the
    ///            markup's string validation. Prints "ok" and its Guid, or "error", its Guid, and Rock's message.
    ///   signals  Runs Rock's own Person.CalculateSignals for everyone with a signal, saving nothing, and prints each
    ///            person whose top signal it would change, then "people" and how many it checked.
    ///   personal-links  Asks Rock for each input line's person's bookmark menu (PersonalLinkService.GetPersonalLinksData)
    ///            and whether Rock would add a section order for them, saving nothing. It prints the version Rock keeps for
    ///            the shared links and midnight today, then a line for each person: the answer, the version of their own
    ///            links, and each section with its links' names, separated by tabs.
    ///   notifications  Answers what Rock's top-bar bell shows: for "count" and a person's Guid, how many unread messages
    ///            Rock counts for them, and for "link" and a message's Guid, the link Rock's Note Mention component opens.
    ///   group-requirements  Runs Rock's Calculate Group Requirements job once, with its default settings, and prints its result.
    ///
    /// The config file holds the RockContext connection string, Rock's appSettings, and its binding redirects
    /// (the verify tool writes it from the checkout's web.config). Rock runs in its own AppDomain whose base is
    /// the RockWeb folder, with assemblies from bin, as under IIS. Exits with 0 when the task ran,
    /// even if some cleanup tasks reported errors; the caller reads the output.
    /// </summary>
    internal static class Program
    {
        private static int Main( string[] args )
        {
            var task = Argument( args, "--task" ) ?? "cleanup";
            var rockBin = Argument( args, "--rock-bin" );
            var config = Argument( args, "--config" );
            var input = Argument( args, "--input" );
            var tasks = new[] { "startup", "cleanup", "hash-password", "sign-in", "group-sync", "data-views", "data-view-people", "reports", "authorize", "assessment-scores", "segments", "adaptive-messages", "email-sections", "signals", "personal-links", "notifications", "process-reminders", "process-workflows", "launch-step-workflows", "group-requirements" };
            if ( rockBin == null || config == null || !tasks.Contains( task ) || ( task is "hash-password" or "sign-in" or "authorize" or "data-view-people" or "adaptive-messages" or "personal-links" or "notifications" or "launch-step-workflows" && input == null ) )
            {
                Console.Error.WriteLine( "Usage: rock-runner --task startup|cleanup|hash-password|sign-in|group-sync|data-views|data-view-people|reports|authorize|assessment-scores|segments|adaptive-messages|email-sections|signals|personal-links|notifications|process-reminders|process-workflows|launch-step-workflows|group-requirements --rock-bin <RockWeb\\bin folder> --config <config file> [--input <file>]" );
                return 2;
            }

            /*
                10/21/26 - CLAUDE

                Rock reads some files relative to its AppDomain base, such as the census data that the post-install
                job loads into AnalyticsSourcePostalCode (App_Data\Formatted_Census_Data.xlsx). Under IIS the base
                is the site folder and assemblies come from bin. With bin as the base, Rock found no census file and
                quietly left the postal code table empty.

                Reason: Match IIS so Rock finds its App_Data files.
            */
            var setup = new AppDomainSetup
            {
                ApplicationBase = Path.GetDirectoryName( Path.GetFullPath( rockBin ).TrimEnd( '\\', '/' ) ),
                PrivateBinPath = "bin",
                ConfigurationFile = config
            };

            var domain = AppDomain.CreateDomain( "Rock", null, setup );
            try
            {
                var worker = ( RockWorker ) domain.CreateInstanceFromAndUnwrap( typeof( RockWorker ).Assembly.Location, typeof( RockWorker ).FullName );
                var lines = input == null ? null : File.ReadAllLines( input );
                Console.WriteLine( task switch
                {
                    "startup" => worker.Startup(),
                    "hash-password" => worker.HashPassword( lines.FirstOrDefault() ?? string.Empty ),
                    "sign-in" => worker.SignIn( lines ),
                    "group-sync" => worker.GroupSync(),
                    "data-views" => worker.DataViews(),
                    "data-view-people" => worker.DataViewPeople( lines ),
                    "reports" => worker.Reports(),
                    "authorize" => worker.Authorize( lines ),
                    "assessment-scores" => worker.AssessmentScores(),
                    "segments" => worker.Segments(),
                    "adaptive-messages" => worker.AdaptiveMessages( lines ),
                    "email-sections" => worker.EmailSections(),
                    "signals" => worker.Signals(),
                    "personal-links" => worker.PersonalLinks( lines ),
                    "notifications" => worker.Notifications( lines ),
                    "process-reminders" => worker.ProcessReminders(),
                    "process-workflows" => worker.ProcessWorkflows(),
                    "launch-step-workflows" => worker.LaunchStepWorkflows( lines ),
                    "group-requirements" => worker.GroupRequirements(),
                    _ => worker.Cleanup()
                } );
                return 0;
            }
            catch ( Exception ex )
            {
                Console.Error.WriteLine( $"Rock {task} did not run: {ex.GetBaseException().Message}" );
                Console.Error.WriteLine( ex );
                return 1;
            }
            finally
            {
                AppDomain.Unload( domain );
            }
        }

        private static string Argument( string[] args, string name )
        {
            var index = Array.IndexOf( args, name );
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
    }

    /// <summary>
    /// Lives inside the Rock AppDomain. Everything Rock is reached by reflection, because this assembly is built without Rock.
    /// </summary>
    public sealed class RockWorker : MarshalByRefObject
    {
        private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance;

        private Assembly _rock;
        private Type _startupHelper;

        /// <summary>
        /// Runs the start-up steps from RockApplicationStartupHelper that change the database, in Rock's order.
        /// Steps that need a web server (block types from .ascx files) are left out.
        /// </summary>
        public string Startup()
        {
            Start();
            var steps = new List<string>();

            CallStartup( "MigratePlugins" );
            steps.Add( "plugin and hotfix migrations" );

            CallStartup( "InitializeQueryableAttributeValues" );
            steps.Add( "attribute value views" );

            StartMessageBus();
            steps.Add( "the message bus" );

            /*
                10/25/26 - CLAUDE

                Rock's migrations queue one-time "Rock Update Helper" jobs (Rock.Jobs.Post...) for 9pm, and a new
                install runs them on its first evening; each deletes itself when it finishes. Among them are the jobs
                that swap WebForms blocks for Obsidian ones (Pledge Entry, Account Entry, Registration Entry, and more),
                so a Rock that has run a night shows Obsidian blocks where a base made only by migrations still shows
                WebForms ones. The loader's campaign step once broke on exactly that difference.

                Reason: The trusted base must match a Rock that has run its first evening's update jobs.
            */
            // Rock starts its Lava engine before its job scheduler, and some update jobs need it: the nameless
            // locations job formats each address through Lava, and without an engine it fails on an empty result.
            StartLava();
            steps.Add( "the Lava engine" );

            /*
                10/26/26 - CLAUDE

                Rock's web start-up compiles every block type and registers its settings before any job runs
                (BlockTypeService.VerifyBlockTypeInstanceProperties), and marks each one verified. Compiling a WebForms
                block type means compiling its .ascx, which only works inside IIS, so the trusted base has never done
                it. The block swap jobs verify their replacement block types first, and each WebForms one among them
                logged a BuildManager NullReferenceException (6 on Rock 21.0.2), which the base check rightly refuses.

                Reason: Mark WebForms block types verified, as a started Rock has them, so the jobs never compile an .ascx.
            */
            MarkWebFormsBlockTypesVerified();

            steps.Add( RunUpdateJobs() );

            /*
                10/19/26 - CLAUDE

                A new install schedules Rock's PostInstallDataMigrations job for 2am on its first night. It fills
                AnalyticsSourceDate (150 years back to 100 ahead) and the identity verification codes. Rock Cleanup's
                age step reads each person's age from AnalyticsSourceDate, so without it Rock Cleanup sets every
                age to null, which no church database that has run a night does.

                Reason: The trusted base must match a Rock that has run its first night's one-time job.
            */
            RunJob( "Rock.Jobs.PostInstallDataMigrations" );
            steps.Add( "and the post-install job (analytics dates and identity verification codes)" );

            return $"Ran Rock's start-up steps: the RockGetDate function, {string.Join( ", ", steps )}.";
        }

        /// <summary>
        /// Runs Rock Cleanup with its default settings and returns its result text, with one line per failed task.
        /// </summary>
        public string Cleanup()
        {
            Start();
            StartMessageBus();
            var jobType = _rock.GetType( "Rock.Jobs.RockCleanup", true );
            var job = Activator.CreateInstance( jobType );
            try
            {
                Execute( job );
            }
            catch ( TargetInvocationException ex ) when ( ex.InnerException?.GetType().Name == "RockJobWarningException" && ex.InnerException.InnerException is AggregateException errors )
            {
                // After every task has run, the job throws a warning that wraps each failed task's error. Only that is a
                // finished run; any other exception means the job never ran, and the caller must not read it as a pass.
                // Each error's own message is "task title:message", so the title survives for the caller to match.
                var lines = string.Join( "\n", errors.InnerExceptions.Select( e => $"Task error: {e.Message.Split( '\n' )[0]}" ) );
                return $"{jobType.GetProperty( "Result" ).GetValue( job )}\n{lines}";
            }

            return ( string ) jobType.GetProperty( "Result" ).GetValue( job );
        }

        /// <summary>
        /// Runs Rock's Calculate Group Requirements job with its default settings and returns its result text: how many
        /// requirement results it inserted, updated, and deleted. Verify compares the results before and after.
        /// </summary>
        public string GroupRequirements()
        {
            Start();
            StartMessageBus();
            var jobType = _rock.GetType( "Rock.Jobs.CalculateGroupRequirements", true );
            var job = Activator.CreateInstance( jobType );
            Execute( job );
            return ( string ) jobType.GetProperty( "Result" ).GetValue( job );
        }

        /// <summary>
        /// Runs Rock's Process Reminders job as Rock's scheduler does and returns how many reminders it notified about, then
        /// every error it recorded. The job's own result keeps only five errors, so the runner reads its full list.
        /// </summary>
        public string ProcessReminders()
        {
            Start();
            StartMessageBus();
            StartLava();
            var jobType = _rock.GetType( "Rock.Jobs.ProcessReminders", true );
            var job = Activator.CreateInstance( jobType );
            RunStoredJob( job, new Guid( "3F697C80-4C33-4552-9038-D3470445EA40" ) );
            var errors = ( IEnumerable<string> ) ( jobType.GetField( "_jobErrors", Hidden )
                ?? throw new InvalidOperationException( "This Rock's Process Reminders job has no _jobErrors. The runner needs updating for this Rock." ) ).GetValue( job );
            var processed = jobType.GetField( "_totalProcessedReminders", Hidden )?.GetValue( job );
            return string.Join( "\n", new[] { $"processed {processed}" }.Concat( ( errors ?? Enumerable.Empty<string>() ).Select( e => $"error {e.Replace( '\r', ' ' ).Replace( '\n', ' ' )}" ) ) );
        }

        /// <summary>
        /// Runs Rock's Process Workflows job as Rock's scheduler does and returns its result text.
        /// </summary>
        public string ProcessWorkflows()
        {
            Start();
            StartMessageBus();
            StartLava();
            var jobType = _rock.GetType( "Rock.Jobs.ProcessWorkflows", true );
            var job = Activator.CreateInstance( jobType );
            RunStoredJob( job, new Guid( "35EABBDB-1EFA-46F1-86D4-4199FFA2D9A7" ) );
            return ( string ) jobType.GetProperty( "Result" ).GetValue( job );
        }

        /*
            11/18/26 - CLAUDE

            The launch first ran in a transaction it rolled back. Rock saves a persisted workflow as it processes it, and some
            of Rock's own reads during processing use other connections, which waited on that transaction's locks until they
            timed out. On Full 7 each launch took minutes and logged timeouts. Rock's WorkflowService.Process saves only when the
            workflow or its type is persisted, so the runner marks the type's cached copy not persisted, and Rock processes every
            action exactly as it would and writes nothing.

            Reason: The launch check must prove Rock's own processing without writing to the loaded database.
        */

        /// <summary>
        /// Launches step workflows the way Rock's LaunchStepChangeWorkflows task does when a step becomes complete: activates the
        /// trigger's workflow type with the trigger's name, and processes it with the step as its entity. The type's cached copy
        /// is marked not persisted, so Rock writes nothing, and a transaction rolled back after each launch catches any write.
        /// </summary>
        public string LaunchStepWorkflows( string[] lines )
        {
            Start();
            StartMessageBus();
            StartLava();
            var rockAppType = _rock.GetType( "Rock.Configuration.RockApp", true );
            var app = rockAppType.GetProperty( "Current" ).GetValue( null );
            var createContext = _rock.GetType( "Rock.Configuration.RockAppExtensions", true ).GetMethod( "CreateRockContext", BindingFlags.Public | BindingFlags.Static, null, new[] { rockAppType }, null );
            var contextType = _rock.GetType( "Rock.Data.RockContext", true );
            var workflowType = _rock.GetType( "Rock.Model.Workflow", true );
            var typeCache = _rock.GetType( "Rock.Web.Cache.WorkflowTypeCache", true );
            var activate = workflowType.GetMethod( "Activate", new[] { typeCache, typeof( string ), contextType } )
                ?? throw new InvalidOperationException( "This Rock has no Workflow.Activate( WorkflowTypeCache, string, RockContext ). The runner needs updating for this Rock." );
            var workflowService = _rock.GetType( "Rock.Model.WorkflowService", true );
            var process = workflowService.GetMethod( "Process", new[] { workflowType, typeof( object ), typeof( List<string> ).MakeByRefType() } )
                ?? throw new InvalidOperationException( "This Rock has no WorkflowService.Process( Workflow, object, out List<string> ). The runner needs updating for this Rock." );
            var getType = typeCache.GetMethod( "Get", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, null, new[] { typeof( int ) }, null );
            object Get( object item, string name ) => item?.GetType().GetProperty( name )?.GetValue( item );
            object Find( string service, int id, object context ) => _rock.GetType( service, true ).GetMethod( "Get", new[] { typeof( int ) } ).Invoke( Activator.CreateInstance( _rock.GetType( service, true ), context ), new object[] { id } );

            var results = new List<string>();
            foreach ( var line in lines.Where( l => l.Contains( '\t' ) ) )
            {
                var parts = line.Split( '\t' );
                var context = createContext.Invoke( null, new[] { app } );
                var database = Get( context, "Database" );
                var transaction = database.GetType().GetMethod( "BeginTransaction", Type.EmptyTypes ).Invoke( database, null );
                try
                {
                    var step = Find( "Rock.Model.StepService", int.Parse( parts[0] ), context );
                    var trigger = Find( "Rock.Model.StepWorkflowTriggerService", int.Parse( parts[1] ), context );
                    var cache = getType.Invoke( null, new[] { Get( trigger, "WorkflowTypeId" ) } );
                    typeCache.GetProperty( "IsPersisted" ).GetSetMethod( true ).Invoke( cache, new object[] { false } );
                    var workflow = activate.Invoke( null, new[] { cache, Get( trigger, "WorkflowName" ), context } );
                    var arguments = new object[] { workflow, step, null };
                    process.Invoke( Activator.CreateInstance( workflowService, context ), arguments );
                    var activity = ( ( System.Collections.IEnumerable ) Get( workflow, "Activities" ) ).Cast<object>().FirstOrDefault();
                    var assignedAliasId = Get( activity, "AssignedPersonAliasId" ) as int?;
                    var assignee = assignedAliasId.HasValue ? Get( Get( Find( "Rock.Model.PersonAliasService", assignedAliasId.Value, context ), "Person" ), "Guid" ) : null;
                    var value = workflowType.GetMethod( "GetAttributeValue", new[] { typeof( string ) } ).Invoke( workflow, new object[] { parts[2] } );
                    var isWaiting = activity != null && ( ( System.Collections.IEnumerable ) Get( activity, "Actions" ) ).Cast<object>()
                        .Any( a => Get( Get( a, "ActionTypeCache" ), "WorkflowFormId" ) is int && Get( a, "CompletedDateTime" ) == null );
                    var errors = string.Join( " ", ( ( IEnumerable<string> ) arguments[2] ?? Enumerable.Empty<string>() ).Select( e => e.Replace( '\t', ' ' ).Replace( '\n', ' ' ) ) );
                    results.Add( $"{parts[0]}\t{Get( workflow, "Name" )}\t{assignee}\t{value}\t{isWaiting}\t{errors}" );
                }
                catch ( TargetInvocationException ex )
                {
                    results.Add( $"{parts[0]}\terror\t\t\tFalse\t{ex.GetBaseException().Message.Replace( '\t', ' ' ).Replace( '\n', ' ' )}" );
                }
                finally
                {
                    transaction.GetType().GetMethod( "Rollback", Type.EmptyTypes ).Invoke( transaction, null );
                }
            }

            return string.Join( "\n", results );
        }

        /// <summary>
        /// Hashes a password with Rock's Database login, as Rock's own account pages and the Sample Data Installer block do.
        /// </summary>
        public string HashPassword( string password )
        {
            Start();
            var database = DatabaseLogin();
            return ( string ) database.GetType().GetMethod( "EncryptPassword", new[] { typeof( string ) } ).Invoke( database, new object[] { password } );
        }

        /// <summary>
        /// Signs in through Rock's Database login with each "username, tab, password" line, the check Rock's login block
        /// makes: the login exists, is confirmed, and Rock's Database login accepts the password.
        /// </summary>
        public string SignIn( string[] lines )
        {
            Start();
            var database = DatabaseLogin();
            var authenticate = database.GetType().GetMethods().First( m => m.Name == "Authenticate" && m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType == typeof( string ) );
            var rockAppType = _rock.GetType( "Rock.Configuration.RockApp", true );
            var app = rockAppType.GetProperty( "Current" ).GetValue( null );
            var createContext = _rock.GetType( "Rock.Configuration.RockAppExtensions", true ).GetMethod( "CreateRockContext", BindingFlags.Public | BindingFlags.Static, null, new[] { rockAppType }, null );
            var serviceType = _rock.GetType( "Rock.Model.UserLoginService", true );
            var service = Activator.CreateInstance( serviceType, createContext.Invoke( null, new[] { app } ) );
            var results = new List<string>();
            foreach ( var line in lines.Where( l => l.Contains( '\t' ) ) )
            {
                var userName = line.Substring( 0, line.IndexOf( '\t' ) );
                var password = line.Substring( line.IndexOf( '\t' ) + 1 );
                var user = serviceType.GetMethod( "GetByUserName", new[] { typeof( string ) } ).Invoke( service, new object[] { userName } );
                var isConfirmed = user != null && ( ( bool? ) user.GetType().GetProperty( "IsConfirmed" ).GetValue( user ) ?? false );
                var isAccepted = isConfirmed && ( bool ) authenticate.Invoke( database, new[] { user, password } );
                results.Add( $"{userName} {( isAccepted ? "signed in" : "refused" )}" );
            }

            return string.Join( "\n", results );
        }

        /// <summary>
        /// Runs Rock's group sync on every sync in the database, the call the Group Sync job makes, and reports what it changed.
        /// The job itself would skip a sync whose interval has not passed, so the runner hands Rock every sync.
        /// </summary>
        public string GroupSync()
        {
            Start();
            StartMessageBus();
            var rockAppType = _rock.GetType( "Rock.Configuration.RockApp", true );
            var app = rockAppType.GetProperty( "Current" ).GetValue( null );
            var createContext = _rock.GetType( "Rock.Configuration.RockAppExtensions", true ).GetMethod( "CreateRockContext", BindingFlags.Public | BindingFlags.Static, null, new[] { rockAppType }, null );
            var serviceType = _rock.GetType( "Rock.Model.GroupSyncService", true );
            var syncType = _rock.GetType( "Rock.Model.GroupSync", true );
            var infoType = _rock.GetType( "Rock.Model.GroupSyncInfo", true );
            var service = Activator.CreateInstance( serviceType, createContext.Invoke( null, new[] { app } ) );
            var syncs = ( ( IEnumerable<object> ) serviceType.GetMethod( "Queryable", Type.EmptyTypes ).Invoke( service, null ) ).ToList();

            var infos = ( System.Collections.IList ) Activator.CreateInstance( typeof( List<> ).MakeGenericType( infoType ) );
            foreach ( var sync in syncs )
            {
                var id = syncType.GetProperty( "Id" ).GetValue( sync );
                var info = Activator.CreateInstance( infoType );
                infoType.GetProperty( "SyncId" ).SetValue( info, id );
                infoType.GetProperty( "GroupName" ).SetValue( info, $"sync {id}" );
                infos.Add( info );
            }

            var syncGroups = serviceType.GetMethod( "SyncGroups", BindingFlags.Public | BindingFlags.Static )
                ?? throw new InvalidOperationException( "This Rock has no GroupSyncService.SyncGroups. The runner needs updating for this Rock." );
            var result = syncGroups.Invoke( null, new object[] { infos, 180, false, null } );
            var resultType = result.GetType();
            var lines = new List<string>
            {
                $"syncs {syncs.Count}",
                $"added {resultType.GetProperty( "AddedMemberCount" ).GetValue( result )}",
                $"removed {resultType.GetProperty( "DeletedMemberCount" ).GetValue( result )}",
                $"not added {resultType.GetProperty( "NotAddedMemberCount" ).GetValue( result )}"
            };
            lines.AddRange( ( ( IEnumerable<string> ) resultType.GetProperty( "WarningMessages" ).GetValue( result ) ).Select( w => $"warning {w}" ) );
            lines.AddRange( ( ( IEnumerable<Exception> ) resultType.GetProperty( "WarningExceptions" ).GetValue( result ) ).Select( e => $"warning {e.GetBaseException().Message}" ) );
            return string.Join( "\n", lines );
        }

        /// <summary>
        /// Runs every data view through Rock's own engine, the call Rock's data view screens and jobs make. A filter Rock
        /// cannot read fails here, the way it would fail for a person opening the view.
        /// </summary>
        public string DataViews()
        {
            Start();
            StartMessageBus();
            var cache = _rock.GetType( "Rock.Web.Cache.DataViewCache", true );
            var all = ( System.Collections.IEnumerable ) cache.GetMethod( "All", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, null, Type.EmptyTypes, null ).Invoke( null, null );
            var getEntityIds = cache.GetMethod( "GetEntityIds", Type.EmptyTypes )
                ?? throw new InvalidOperationException( "This Rock has no DataViewCache.GetEntityIds(). The runner needs updating for this Rock." );
            var lines = new List<string>();
            foreach ( var view in all )
            {
                var guid = cache.GetProperty( "Guid" ).GetValue( view );
                var timer = Stopwatch.StartNew();
                try
                {
                    var ids = ( System.Collections.ICollection ) getEntityIds.Invoke( view, null );
                    lines.Add( $"ok\t{guid}\t{ids.Count}\t{timer.ElapsedMilliseconds}" );
                }
                catch ( TargetInvocationException ex )
                {
                    lines.Add( $"error\t{guid}\t{ex.GetBaseException().Message.Replace( '\n', ' ' ).Replace( '\r', ' ' )}" );
                }
            }

            return string.Join( "\n", lines );
        }

        /// <summary>
        /// Runs each data view the input names through Rock's own engine and returns the Ids it finds, so verify can compare
        /// them with the loader's own copy of the view's rule.
        /// </summary>
        public string DataViewPeople( string[] guids )
        {
            Start();
            StartMessageBus();
            var cache = _rock.GetType( "Rock.Web.Cache.DataViewCache", true );
            var get = cache.GetMethod( "Get", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, null, new[] { typeof( Guid ) }, null )
                ?? throw new InvalidOperationException( "This Rock has no DataViewCache.Get( Guid ). The runner needs updating for this Rock." );
            var getEntityIds = cache.GetMethod( "GetEntityIds", Type.EmptyTypes )
                ?? throw new InvalidOperationException( "This Rock has no DataViewCache.GetEntityIds(). The runner needs updating for this Rock." );
            var lines = new List<string>();
            foreach ( var line in guids.Where( l => Guid.TryParse( l.Trim(), out _ ) ) )
            {
                var view = get.Invoke( null, new object[] { Guid.Parse( line.Trim() ) } );
                var ids = view == null ? Enumerable.Empty<int>() : ( ( System.Collections.IEnumerable ) getEntityIds.Invoke( view, null ) ).Cast<int>();
                lines.Add( $"{line.Trim()}\t{string.Join( ",", ids.OrderBy( i => i ) )}" );
            }

            return string.Join( "\n", lines );
        }

        /// <summary>
        /// Checks every email section the way Rock saves one from its email editor: the model's own validation, then the
        /// string validation the editor's save endpoint runs on the markup (Rock.Rest/v2/ControlsController.cs, Lava and
        /// basic HTML). Prints "ok" and each section's Guid, or "error", its Guid, and Rock's message, separated by tabs.
        /// </summary>
        public string EmailSections()
        {
            Start();
            var rockAppType = _rock.GetType( "Rock.Configuration.RockApp", true );
            var app = rockAppType.GetProperty( "Current" ).GetValue( null );
            var createContext = _rock.GetType( "Rock.Configuration.RockAppExtensions", true ).GetMethod( "CreateRockContext", BindingFlags.Public | BindingFlags.Static, null, new[] { rockAppType }, null );
            var serviceType = _rock.GetType( "Rock.Model.EmailSectionService", true );
            var sections = ( ( IEnumerable<object> ) serviceType.GetMethod( "Queryable", Type.EmptyTypes ).Invoke( Activator.CreateInstance( serviceType, createContext.Invoke( null, new[] { app } ) ), null ) ).ToList();
            var enums = Assembly.Load( "Rock.Enums" );
            var profileType = enums.GetType( "Rock.Enums.Security.StringValidationProfile", true );
            var ruleType = enums.GetType( "Rock.Enums.Security.StringValidationRule", true );
            var validator = _rock.GetType( "Rock.Security.StringValueValidator", true );
            var rules = validator.GetMethod( "GetEffectiveRules", new[] { profileType, ruleType, ruleType } )
                .Invoke( null, new[] { Enum.Parse( profileType, "LavaAndBasicHtml" ), Enum.ToObject( ruleType, 0 ), Enum.ToObject( ruleType, 0 ) } );
            var validate = validator.GetMethod( "Validate", new[] { typeof( string ), ruleType, typeof( Type ), typeof( string ) } );

            var lines = new List<string>();
            foreach ( var section in sections )
            {
                var type = section.GetType();
                var guid = type.GetProperty( "Guid" ).GetValue( section );
                var problems = new List<string>();
                if ( !( bool ) type.GetProperty( "IsValid" ).GetValue( section ) )
                {
                    problems.AddRange( ( ( System.Collections.IEnumerable ) type.GetProperty( "ValidationResults" ).GetValue( section ) ).Cast<object>().Select( r => r.ToString() ) );
                }

                try
                {
                    validate.Invoke( null, new[] { type.GetProperty( "SourceMarkup" ).GetValue( section ), rules, _rock.GetType( "Rock.Model.EmailSection", true ), "SourceMarkup" } );
                }
                catch ( TargetInvocationException ex )
                {
                    problems.Add( ex.InnerException?.Message ?? ex.Message );
                }

                lines.Add( problems.Count == 0 ? $"ok\t{guid}" : $"error\t{guid}\t{string.Join( " ", problems )}" );
            }

            return string.Join( "\n", lines );
        }

        /// <summary>
        /// Checks every report's columns against Rock the way its report grid reads them: a property must be one of Rock's
        /// reportable fields for the entity, an attribute must exist, and a data select component must build its expression
        /// from the stored selection. A Lava column is rendered by Rock's Lava engine for the first row its data view finds.
        /// </summary>
        public string Reports()
        {
            Start();
            StartMessageBus();
            StartLava();
            var rockAppType = _rock.GetType( "Rock.Configuration.RockApp", true );
            var app = rockAppType.GetProperty( "Current" ).GetValue( null );
            var createContext = _rock.GetType( "Rock.Configuration.RockAppExtensions", true ).GetMethod( "CreateRockContext", BindingFlags.Public | BindingFlags.Static, null, new[] { rockAppType }, null );
            var context = createContext.Invoke( null, new[] { app } );
            var serviceType = _rock.GetType( "Rock.Model.ReportService", true );
            var reports = ( ( IEnumerable<object> ) serviceType.GetMethod( "Queryable", Type.EmptyTypes ).Invoke( Activator.CreateInstance( serviceType, context ), null ) ).ToList();
            var entityTypeCache = _rock.GetType( "Rock.Web.Cache.EntityTypeCache", true );
            var getEntityType = entityTypeCache.GetMethod( "Get", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, null, new[] { typeof( int ) }, null );
            var getEntityFields = _rock.GetType( "Rock.Reporting.EntityHelper", true ).GetMethod( "GetEntityFields", new[] { typeof( Type ), typeof( bool ), typeof( bool ) } );
            var attributeCache = _rock.GetType( "Rock.Web.Cache.AttributeCache", true ).GetMethod( "Get", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, null, new[] { typeof( Guid ) }, null );
            var getComponent = _rock.GetType( "Rock.Reporting.DataSelectContainer", true ).GetMethod( "GetComponent", new[] { typeof( string ) } );
            var rockContextType = _rock.GetType( "Rock.Data.RockContext", true );
            var resolve = _rock.GetType( "Rock.ExtensionMethods", true ).GetMethod( "ResolveMergeFields", new[] { typeof( string ), typeof( IDictionary<string, object> ), typeof( bool ), typeof( bool ) } );
            var dataViewCache = _rock.GetType( "Rock.Web.Cache.DataViewCache", true );

            var lines = new List<string>();
            var webOnly = 0;
            foreach ( var report in reports )
            {
                var guid = report.GetType().GetProperty( "Guid" ).GetValue( report );
                var problems = new List<string>();
                try
                {
                    var entity = getEntityType.Invoke( null, new object[] { report.GetType().GetProperty( "EntityTypeId" ).GetValue( report ) } );
                    var type = ( Type ) entity.GetType().GetMethod( "GetEntityType", Type.EmptyTypes ).Invoke( entity, null );
                    var fieldNames = ( ( System.Collections.IEnumerable ) getEntityFields.Invoke( null, new object[] { type, true, false } ) ).Cast<object>()
                        .Select( f => ( string ) f.GetType().GetProperty( "Name" ).GetValue( f ) ).ToHashSet();
                    var idProperty = System.Linq.Expressions.Expression.Property( System.Linq.Expressions.Expression.Parameter( type, "p" ), "Id" );
                    var view = dataViewCache.GetMethod( "Get", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, null, new[] { typeof( int ) }, null )
                        .Invoke( null, new object[] { report.GetType().GetProperty( "DataViewId" ).GetValue( report ) } );
                    var firstId = ( ( System.Collections.IEnumerable ) dataViewCache.GetMethod( "GetEntityIds", Type.EmptyTypes ).Invoke( view, null ) ).Cast<int>().FirstOrDefault();

                    foreach ( var field in ( ( System.Collections.IEnumerable ) report.GetType().GetProperty( "ReportFields" ).GetValue( report ) ).Cast<object>() )
                    {
                        var fieldType = Convert.ToInt32( field.GetType().GetProperty( "ReportFieldType" ).GetValue( field ) );
                        var selection = ( string ) field.GetType().GetProperty( "Selection" ).GetValue( field );
                        var header = ( string ) field.GetType().GetProperty( "ColumnHeaderText" ).GetValue( field );
                        if ( fieldType == 0 && !fieldNames.Contains( selection ) )
                        {
                            problems.Add( $"{header}: {selection} is not a reportable field of {type.Name}" );
                        }
                        else if ( fieldType == 1 && attributeCache.Invoke( null, new object[] { Guid.Parse( selection ) } ) == null )
                        {
                            problems.Add( $"{header}: attribute {selection} does not exist" );
                        }
                        else if ( fieldType == 2 )
                        {
                            var componentType = getEntityType.Invoke( null, new object[] { field.GetType().GetProperty( "DataSelectComponentEntityTypeId" ).GetValue( field ) } );
                            var componentName = ( string ) componentType.GetType().GetProperty( "Name" ).GetValue( componentType );
                            var component = getComponent.Invoke( null, new object[] { componentName } );
                            if ( component == null )
                            {
                                problems.Add( $"{header}: Rock has no data select component {componentName}" );
                            }
                            else
                            {
                                try
                                {
                                    if ( componentName == "Rock.Reporting.DataSelect.LiquidSelect" )
                                    {
                                        var mergeFields = new Dictionary<string, object> { ["Id"] = firstId, ["BaptismDate"] = "1/1/2026", ["Allergy"] = "Peanuts" };
                                        resolve.Invoke( null, new object[] { selection, mergeFields, false, true } );
                                    }
                                    else
                                    {
                                        var getExpression = component.GetType().GetMethod( "GetExpression", new[] { rockContextType, typeof( System.Linq.Expressions.MemberExpression ), typeof( string ) } );
                                        if ( getExpression.Invoke( component, new[] { context, idProperty, selection ?? string.Empty } ) == null )
                                        {
                                            problems.Add( $"{header}: {componentName} built no expression from \"{selection}\"" );
                                        }
                                    }
                                }
                                catch ( TargetInvocationException ex ) when ( ex.GetBaseException().Message.Contains( "cannot be made absolute" ) )
                                {
                                    // A link column builds its URL from the web site's root, which only exists under IIS. Rock's own
                                    // reports do the same, so the column is left to the web site, as Rock Cleanup's web-only tasks are.
                                    webOnly++;
                                }
                                catch ( TargetInvocationException ex )
                                {
                                    problems.Add( $"{header}: {ex.GetBaseException().Message.Replace( '\n', ' ' ).Replace( '\r', ' ' )}" );
                                }
                            }
                        }
                    }
                }
                catch ( TargetInvocationException ex )
                {
                    problems.Add( ex.GetBaseException().Message.Replace( '\n', ' ' ).Replace( '\r', ' ' ) );
                }

                lines.Add( problems.Count == 0 ? $"ok\t{guid}" : $"error\t{guid}\t{string.Join( "; ", problems )}" );
            }

            lines.Add( $"web-only\t{webOnly}" );
            return string.Join( "\n", lines );
        }

        /// <summary>
        /// Asks Rock's own security check whether each person may take each action on each category or data view. Rock walks
        /// the item's parent authorities (a data view's category, then its parent categories) as it does on every page.
        /// A snippet answers for itself, as SMS Conversations asks it.
        /// </summary>
        public string Authorize( string[] lines )
        {
            Start();
            var rockAppType = _rock.GetType( "Rock.Configuration.RockApp", true );
            var app = rockAppType.GetProperty( "Current" ).GetValue( null );
            var createContext = _rock.GetType( "Rock.Configuration.RockAppExtensions", true ).GetMethod( "CreateRockContext", BindingFlags.Public | BindingFlags.Static, null, new[] { rockAppType }, null );
            var personServiceType = _rock.GetType( "Rock.Model.PersonService", true );
            var personService = Activator.CreateInstance( personServiceType, createContext.Invoke( null, new[] { app } ) );
            var getPerson = personServiceType.GetMethod( "Get", new[] { typeof( Guid ) } );
            var securedType = _rock.GetType( "Rock.Security.ISecured", true );
            var authorized = _rock.GetType( "Rock.Security.Authorization", true ).GetMethod( "Authorized", BindingFlags.Public | BindingFlags.Static, null,
                new[] { securedType, typeof( string ), _rock.GetType( "Rock.Model.Person", true ) }, null )
                ?? throw new InvalidOperationException( "This Rock has no Authorization.Authorized( ISecured, string, Person ). The runner needs updating for this Rock." );
            var results = new List<string>();
            foreach ( var line in lines.Where( l => l.Contains( '\t' ) ) )
            {
                var parts = line.Split( '\t' );
                object item;
                var person = getPerson.Invoke( personService, new object[] { new Guid( parts[3] ) } );
                if ( parts[0] == "Tag" )
                {
                    // Rock keeps no cache of tags. Its tag bar loads the tag and asks the tag itself, which lets a personal
                    // tag's owner do anything with it and keeps everyone else out, before Rock's usual rules.
                    var tagService = _rock.GetType( "Rock.Model.TagService", true );
                    var tag = tagService.GetMethod( "Get", new[] { typeof( Guid ) } )
                        .Invoke( Activator.CreateInstance( tagService, personServiceType.GetProperty( "Context" ).GetValue( personService ) ), new object[] { new Guid( parts[1] ) } );
                    results.Add( tag == null || person == null ? "missing"
                        : securedType.GetMethod( "IsAuthorized" ).Invoke( tag, new[] { parts[2], person } ).ToString() );
                    continue;
                }

                if ( parts[0] == "Snippet" )
                {
                    // SMS Conversations asks each snippet itself, whose own check lets only its owner see a personal one
                    // (Rock.Blocks/Communication/SmsConversations.cs and Snippet.Logic.cs).
                    var snippetService = _rock.GetType( "Rock.Model.SnippetService", true );
                    var snippet = snippetService.GetMethod( "Get", new[] { typeof( Guid ) } )
                        .Invoke( Activator.CreateInstance( snippetService, personServiceType.GetProperty( "Context" ).GetValue( personService ) ), new object[] { new Guid( parts[1] ) } );
                    var snippetPerson = getPerson.Invoke( personService, new object[] { new Guid( parts[3] ) } );
                    results.Add( snippet == null || snippetPerson == null ? "missing"
                        : snippet.GetType().GetMethod( "IsAuthorized", new[] { typeof( string ), _rock.GetType( "Rock.Model.Person", true ) } ).Invoke( snippet, new[] { parts[2], snippetPerson } ).ToString() );
                    continue;
                }

                if ( parts[0] == "Report" || parts[0] == "BenevolenceType" )
                {
                    // Rock keeps no cache of reports or benevolence types; its screens load the item and check it.
                    var service = _rock.GetType( $"Rock.Model.{parts[0]}Service", true );
                    item = service.GetMethod( "Get", new[] { typeof( Guid ) } )
                        .Invoke( Activator.CreateInstance( service, personServiceType.GetProperty( "Context" ).GetValue( personService ) ), new object[] { new Guid( parts[1] ) } );
                }
                else
                {
                    var cacheName = parts[0] switch
                    {
                        "Category" => "Rock.Web.Cache.CategoryCache",
                        "PersonalizationSegment" => "Rock.Web.Cache.PersonalizationSegmentCache",
                        "Page" => "Rock.Web.Cache.PageCache",
                        "SignalType" => "Rock.Web.Cache.SignalTypeCache",
                        _ => "Rock.Web.Cache.DataViewCache"
                    };
                    var cache = _rock.GetType( cacheName, true );
                    item = cache.GetMethod( "Get", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, null, new[] { typeof( Guid ) }, null )
                        .Invoke( null, new object[] { new Guid( parts[1] ) } );
                }

                results.Add( item == null || person == null ? "missing" : authorized.Invoke( null, new[] { item, parts[2], person } ).ToString() );
            }

            return string.Join( "\n", results );
        }

        /// <summary>
        /// Scores every assessment's saved answers with Rock's own scoring services, and writes the answer data again with
        /// Rock's own serializer, the way each assessment block saves it. Prints one line per assessment with answers: its
        /// Id, "same" or "differs" for the answer data, then each person attribute value Rock's service would save, as
        /// key=value, separated by tabs.
        /// </summary>
        public string AssessmentScores()
        {
            Start();
            StartMessageBus();
            var common = Assembly.Load( "Rock.Common" );
            var json = common.GetType( "Rock.JsonExtensions", true );
            var toJson = json.GetMethod( "ToJson", new[] { typeof( object ) } );
            T FromJson<T>( string text ) => ( T ) json.GetMethod( "FromJsonOrThrow" ).MakeGenericMethod( typeof( T ) ).Invoke( null, new object[] { text } );
            object Get( object item, string name ) => item.GetType().GetProperty( name )?.GetValue( item ) ?? item.GetType().GetField( name )?.GetValue( item );
            string Join( object guids ) => string.Join( ",", ( ( IEnumerable<Guid> ) guids ).Select( g => g.ToString() ) );

            var rockAppType = _rock.GetType( "Rock.Configuration.RockApp", true );
            var app = rockAppType.GetProperty( "Current" ).GetValue( null );
            var createContext = _rock.GetType( "Rock.Configuration.RockAppExtensions", true ).GetMethod( "CreateRockContext", BindingFlags.Public | BindingFlags.Static, null, new[] { rockAppType }, null );
            var serviceType = _rock.GetType( "Rock.Model.AssessmentService", true );
            var assessments = ( ( IEnumerable<object> ) serviceType.GetMethod( "Queryable", Type.EmptyTypes ).Invoke( Activator.CreateInstance( serviceType, createContext.Invoke( null, new[] { app } ) ), null ) )
                .Where( a => Get( a, "AssessmentResultData" ) != null )
                .ToList();
            var typeCache = _rock.GetType( "Rock.Web.Cache.AssessmentTypeCache", true ).GetMethod( "Get", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, null, new[] { typeof( int ) }, null );
            var disc = _rock.GetType( "Rock.Model.DiscService", true );
            var gifts = _rock.GetType( "Rock.Model.SpiritualGiftsService", true );
            var conflict = _rock.GetType( "Rock.Model.ConflictProfileService", true );
            var eq = _rock.GetType( "Rock.Model.EQInventoryService", true );
            var motivators = _rock.GetType( "Rock.Model.MotivatorService", true );
            var discTexts = ( ( System.Collections.IEnumerable ) disc.GetMethod( "GetResponses" ).Invoke( null, null ) ).Cast<object>()
                .ToDictionary( r => $"{Get( r, "QuestionNumber" )}:{Get( r, "MostScore" )}", r => ( string ) Get( r, "ResponseText" ) );

            var lines = new List<string>();
            foreach ( var assessment in assessments )
            {
                var data = ( string ) Get( assessment, "AssessmentResultData" );
                var typeGuid = ( ( Guid ) Get( typeCache.Invoke( null, new[] { Get( assessment, "AssessmentTypeId" ) } ), "Guid" ) ).ToString().ToUpperInvariant();
                var values = new List<string>();
                string rewritten;
                try
                {
                    var timeToTake = FromJson<TimedData>( data ).TimeToTake;
                    switch ( typeGuid )
                    {
                        case "A5CB2E3D-118A-41F2-972B-325A328B0B54":
                            {
                                var result = FromJson<DiscData>( data ).Result;
                                var letters = new[] { "D", "I", "S", "C" };
                                var counts = letters.Select( l => result.Values.Count( q => q["Most"][0] == l ) ).Concat( letters.Select( l => result.Values.Count( q => q["Least"][0] == l ) ) ).Cast<object>().ToArray();
                                var scores = disc.GetMethod( "Score" ).Invoke( null, counts );
                                foreach ( var l in letters )
                                {
                                    values.Add( $"Adaptive{l}={Get( scores, "AdaptiveBehavior" + l )}" );
                                }

                                foreach ( var l in letters )
                                {
                                    values.Add( $"Natural{l}={Get( scores, "NaturalBehavior" + l )}" );
                                }

                                values.Add( $"PersonalityType={Get( scores, "PersonalityType" )}" );
                                values.Add( $"core_DISCDISCProfile={Get( Get( scores, "DISCProfile" ), "Guid" )}" );
                                var answers = result.ToDictionary( q => q.Key, q => new Dictionary<string, string[]>
                                {
                                    ["Most"] = new[] { q.Value["Most"][0], discTexts[$"{q.Key}:{q.Value["Most"][0]}"] },
                                    ["Least"] = new[] { q.Value["Least"][0], discTexts[$"{q.Key}:{q.Value["Least"][0]}"] }
                                } );
                                rewritten = ( string ) toJson.Invoke( null, new object[] { new Dictionary<string, object> { ["Result"] = answers, ["TimeToTake"] = timeToTake } } );
                                break;
                            }

                        case "B8FBD371-6B32-4BE5-872F-51400D16EC5D":
                            {
                                var responses = FromJson<GiftsData>( data ).Result;
                                var result = gifts.GetMethod( "GetResult" ).Invoke( null, new object[] { responses } );
                                values.Add( $"core_DominantGifts={Join( Get( result, "DominantGifts" ) )}" );
                                values.Add( $"core_SupportiveGifts={Join( Get( result, "SupportiveGifts" ) )}" );
                                values.Add( $"core_OtherGifts={Join( Get( result, "OtherGifts" ) )}" );
                                var resultData = Activator.CreateInstance( gifts.GetNestedType( "AssessmentResultData" ) );
                                resultData.GetType().GetProperty( "Result" ).SetValue( resultData, responses );
                                resultData.GetType().GetProperty( "ResultScores" ).SetValue( resultData, Get( result, "SpiritualGiftScores" ) );
                                resultData.GetType().GetProperty( "TimeToTake" ).SetValue( resultData, timeToTake );
                                rewritten = ( string ) toJson.Invoke( null, new[] { resultData } );
                                break;
                            }

                        case "CE7E1986-FDC3-4A3F-9464-26A65D684E29":
                            {
                                var result = conflict.GetMethod( "GetResult" ).Invoke( null, new object[] { Flatten( FromJson<GroupedData>( data ) ) } );
                                foreach ( var (key, property) in new[] { ("core_ConflictModeWinning", "ModeWinningScore"), ("core_ConflictModeResolving", "ModeResolvingScore"), ("core_ConflictModeCompromising", "ModeCompromisingScore"),
                                    ("core_ConflictModeAvoiding", "ModeAvoidingScore"), ("core_ConflictModeYielding", "ModeYieldingScore"), ("core_ConflictThemeAccommodating", "EngagementAccommodatingScore"),
                                    ("core_ConflictThemeWinning", "EngagementWinningScore"), ("core_ConflictThemeSolving", "EngagementSolvingScore") } )
                                {
                                    values.Add( $"{key}={Get( result, property )}" );
                                }

                                rewritten = ( string ) toJson.Invoke( null, new object[] { new Dictionary<string, object> { ["Result"] = Get( result, "AssessmentData" ), ["TimeToTake"] = timeToTake } } );
                                break;
                            }

                        case "DE2D65A3-FC36-4472-B5DF-FAD30EEC27A0":
                            {
                                var result = eq.GetMethod( "GetResult" ).Invoke( null, new object[] { Flatten( FromJson<GroupedData>( data ) ) } );
                                foreach ( var (key, property) in new[] { ("core_EQSelfAwareness", "SelfAwareConstruct"), ("core_EQSelfRegulating", "SelfRegulatingConstruct"), ("core_EQOthersAwareness", "OtherAwarenessContruct"),
                                    ("core_EQOthersRegulating", "OthersRegulatingConstruct"), ("core_EQProblemSolving", "EQ_ProblemSolvingScale"), ("core_EQUnderStress", "EQ_UnderStressScale") } )
                                {
                                    values.Add( $"{key}={Get( result, property )}" );
                                }

                                rewritten = ( string ) toJson.Invoke( null, new object[] { new Dictionary<string, object> { ["Result"] = Get( result, "AssessmentData" ), ["TimeToTake"] = timeToTake } } );
                                break;
                            }

                        case "EABE1ABA-4A23-44FF-8C7F-6B71383BED09":
                            {
                                var result = motivators.GetMethod( "GetResult" ).Invoke( null, new object[] { Flatten( FromJson<GroupedData>( data ) ) } );
                                string ScoreKey( object definedValue ) => ( string ) definedValue.GetType().GetMethod( "GetAttributeValue", new[] { typeof( string ) } ).Invoke( definedValue, new object[] { "AttributeScoreKey" } );
                                var scores = ( ( System.Collections.IEnumerable ) Get( result, "MotivatorScores" ) ).Cast<object>().ToList();
                                values.Add( $"core_MotivatorsTop5Motivators={string.Join( ",", scores.Take( 5 ).Select( s => Get( Get( s, "DefinedValue" ), "Guid" ).ToString() ) )}" );
                                foreach ( var score in scores.Concat( ( ( System.Collections.IEnumerable ) Get( result, "MotivatorThemeScores" ) ).Cast<object>() ) )
                                {
                                    values.Add( $"{ScoreKey( Get( score, "DefinedValue" ) )}={Get( score, "Value" )}" );
                                }

                                rewritten = ( string ) toJson.Invoke( null, new object[] { new Dictionary<string, object> { ["Result"] = Get( result, "AssessmentData" ), ["TimeToTake"] = timeToTake } } );
                                break;
                            }

                        default:
                            continue;
                    }
                }
                catch ( TargetInvocationException ex )
                {
                    lines.Add( $"{Get( assessment, "Id" )}\terror\t{ex.GetBaseException().Message.Replace( '\n', ' ' ).Replace( '\r', ' ' )}" );
                    continue;
                }

                lines.Add( $"{Get( assessment, "Id" )}\t{( rewritten == data ? "same" : "differs" )}\t{string.Join( "\t", values )}" );
            }

            return string.Join( "\n", lines );
        }

        /// <summary>
        /// Every answer of a Conflict Profile, EQ, or Motivators answer set, which Rock saves grouped by what each scores.
        /// </summary>
        private static Dictionary<string, int> Flatten( GroupedData data )
        {
            return data.Result.Values.SelectMany( group => group ).ToDictionary( answer => answer.Key, answer => int.Parse( answer.Value ) );
        }

        /// <summary>
        /// Rock's DISC answer data: each question number's Most and Least, each a letter and its response's text.
        /// </summary>
        public sealed class DiscData
        {
            public Dictionary<string, Dictionary<string, string[]>> Result { get; set; }
        }

        /// <summary>
        /// Rock's Spiritual Gifts answer data: each question's answer.
        /// </summary>
        public sealed class GiftsData
        {
            public Dictionary<string, int> Result { get; set; }
        }

        /// <summary>
        /// Rock's Conflict Profile, EQ, and Motivators answer data: each answer, grouped by what it scores.
        /// </summary>
        public sealed class GroupedData
        {
            public Dictionary<string, Dictionary<string, string>> Result { get; set; }
        }

        public sealed class TimedData
        {
            public double TimeToTake { get; set; }
        }

        /// <summary>
        /// Runs Rock's own segment update on every personalization segment, the call Rock's Update Personalization Data job
        /// makes for a segment that is due, and reports what each one changed. The job itself would skip a segment refreshed
        /// today, so the runner hands Rock every segment.
        /// </summary>
        public string Segments()
        {
            Start();
            StartMessageBus();
            var rockAppType = _rock.GetType( "Rock.Configuration.RockApp", true );
            var app = rockAppType.GetProperty( "Current" ).GetValue( null );
            var createContext = _rock.GetType( "Rock.Configuration.RockAppExtensions", true ).GetMethod( "CreateRockContext", BindingFlags.Public | BindingFlags.Static, null, new[] { rockAppType }, null );
            var serviceType = _rock.GetType( "Rock.Model.PersonalizationSegmentService", true );
            var segmentCache = _rock.GetType( "Rock.Web.Cache.PersonalizationSegmentCache", true );
            var update = serviceType.GetMethod( "UpdatePersonAliasPersonalizationDataForSegment", Hidden, null, new[] { segmentCache }, null )
                ?? throw new InvalidOperationException( "This Rock has no PersonalizationSegmentService.UpdatePersonAliasPersonalizationDataForSegment. The runner needs updating for this Rock." );
            var all = ( System.Collections.IEnumerable ) segmentCache.GetMethod( "All", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, null, Type.EmptyTypes, null ).Invoke( null, null );
            var lines = new List<string>();
            foreach ( var segment in all.Cast<object>().ToList() )
            {
                // Each segment gets its own context, as the job gives each one.
                var service = Activator.CreateInstance( serviceType, createContext.Invoke( null, new[] { app } ) );
                var result = update.Invoke( service, new[] { segment } );
                var resultType = result.GetType();
                lines.Add( $"{segmentCache.GetProperty( "Guid" ).GetValue( segment )}\t{resultType.GetField( "CountAddedSegment" ).GetValue( result )}\t{resultType.GetField( "CountRemovedFromSegment" ).GetValue( result )}" );
            }

            return string.Join( "\n", lines );
        }

        /// <summary>
        /// Runs Rock's own Person.CalculateSignals, the method Rock's signal list and its Calculate Person Signals job run,
        /// for everyone with a signal, and saves nothing. Prints one line for each person whose top signal it would change:
        /// their Guid, then the stored and the calculated top signal Id, color, and icon. The last line is "people" and how
        /// many it checked.
        /// </summary>
        public string Signals()
        {
            Start();
            var rockAppType = _rock.GetType( "Rock.Configuration.RockApp", true );
            var app = rockAppType.GetProperty( "Current" ).GetValue( null );
            var createContext = _rock.GetType( "Rock.Configuration.RockAppExtensions", true ).GetMethod( "CreateRockContext", BindingFlags.Public | BindingFlags.Static, null, new[] { rockAppType }, null );
            var context = createContext.Invoke( null, new[] { app } );
            var signalServiceType = _rock.GetType( "Rock.Model.PersonSignalService", true );
            var signalType = _rock.GetType( "Rock.Model.PersonSignal", true );
            var personServiceType = _rock.GetType( "Rock.Model.PersonService", true );
            var personType = _rock.GetType( "Rock.Model.Person", true );
            var calculate = personType.GetMethod( "CalculateSignals", Type.EmptyTypes )
                ?? throw new InvalidOperationException( "This Rock has no Person.CalculateSignals(). The runner needs updating for this Rock." );
            var signals = ( ( IEnumerable<object> ) signalServiceType.GetMethod( "Queryable", Type.EmptyTypes ).Invoke( Activator.CreateInstance( signalServiceType, context ), null ) ).ToList();
            var personService = Activator.CreateInstance( personServiceType, context );
            var getPerson = personServiceType.GetMethod( "Get", new[] { typeof( int ) } );
            string Top( object person ) => string.Join( "|", new[] { "TopSignalId", "TopSignalColor", "TopSignalIconCssClass" }.Select( p => personType.GetProperty( p ).GetValue( person )?.ToString() ?? string.Empty ) );

            var lines = new List<string>();
            var byPerson = signals.GroupBy( s => ( int ) signalType.GetProperty( "PersonId" ).GetValue( s ) ).ToList();
            foreach ( var group in byPerson )
            {
                var person = getPerson.Invoke( personService, new object[] { group.Key } );
                var own = ( System.Collections.IList ) Activator.CreateInstance( typeof( List<> ).MakeGenericType( signalType ) );
                foreach ( var signal in group )
                {
                    own.Add( signal );
                }

                personType.GetProperty( "Signals" ).SetValue( person, own );
                var stored = Top( person );
                calculate.Invoke( person, null );
                var calculated = Top( person );
                if ( stored != calculated )
                {
                    lines.Add( $"{personType.GetProperty( "Guid" ).GetValue( person )}\t{stored}\t{calculated}" );
                }
            }

            lines.Add( $"people {byPerson.Count}" );
            return string.Join( "\n", lines );
        }

        /// <summary>
        /// Asks Rock for each input line's person's bookmark menu, with PersonalLinkService.GetPersonalLinksData, the call
        /// Rock's bookmark block makes, and runs AddMissingPersonalLinkSectionOrders, which Rock runs when a person opens
        /// their links page, without saving. The first line is "shared" and the version Rock keeps for the shared links,
        /// then "today" and midnight today. Each person's line is whether Rock would add a section order, the version of
        /// their own links, and each section as its name and its links' names, separated by "&gt;", all separated by tabs.
        /// </summary>
        public string PersonalLinks( string[] lines )
        {
            Start();
            var rockAppType = _rock.GetType( "Rock.Configuration.RockApp", true );
            var app = rockAppType.GetProperty( "Current" ).GetValue( null );
            var createContext = _rock.GetType( "Rock.Configuration.RockAppExtensions", true ).GetMethod( "CreateRockContext", BindingFlags.Public | BindingFlags.Static, null, new[] { rockAppType }, null );
            var personType = _rock.GetType( "Rock.Model.Person", true );
            var personServiceType = _rock.GetType( "Rock.Model.PersonService", true );
            var getPerson = personServiceType.GetMethod( "Get", new[] { typeof( Guid ) } );
            var linkServiceType = _rock.GetType( "Rock.Model.PersonalLinkService", true );
            var getData = linkServiceType.GetMethod( "GetPersonalLinksData", BindingFlags.Public | BindingFlags.Static, null, new[] { personType }, null )
                ?? throw new InvalidOperationException( "This Rock has no PersonalLinkService.GetPersonalLinksData( Person ). The runner needs updating for this Rock." );
            var addMissing = linkServiceType.GetMethod( "AddMissingPersonalLinkSectionOrders", new[] { personType } )
                ?? throw new InvalidOperationException( "This Rock has no PersonalLinkService.AddMissingPersonalLinkSectionOrders( Person ). The runner needs updating for this Rock." );
            var sharedVersion = _rock.GetType( "Rock.Web.Cache.SharedPersonalLinkSectionCache", true ).GetProperty( "LastModifiedDateTime", BindingFlags.Public | BindingFlags.Static ).GetValue( null );
            var today = _rock.GetType( "Rock.RockDateTime", true ).GetProperty( "Today", BindingFlags.Public | BindingFlags.Static ).GetValue( null );
            var results = new List<string> { $"shared\t{( DateTime ) sharedVersion:o}", $"today\t{( DateTime ) today:o}" };
            foreach ( var line in lines.Where( l => Guid.TryParse( l.Trim(), out _ ) ) )
            {
                var context = createContext.Invoke( null, new[] { app } );
                var person = getPerson.Invoke( Activator.CreateInstance( personServiceType, context ), new object[] { new Guid( line.Trim() ) } );
                if ( person == null )
                {
                    results.Add( "missing" );
                    continue;
                }

                var data = getData.Invoke( null, new[] { person } );
                var sections = ( ( System.Collections.IEnumerable ) data.GetType().GetProperty( "PersonLinksSectionList" ).GetValue( data ) ).Cast<object>()
                    .Select( s => string.Join( ">", new[] { ( string ) s.GetType().GetProperty( "Name" ).GetValue( s ) }
                        .Concat( ( ( System.Collections.IEnumerable ) s.GetType().GetProperty( "PersonalLinks" ).GetValue( s ) ).Cast<object>().Select( l => ( string ) l.GetType().GetProperty( "Name" ).GetValue( l ) ) ) ) );
                var added = addMissing.Invoke( Activator.CreateInstance( linkServiceType, context ), new[] { person } );
                var ownVersion = ( DateTime ) data.GetType().GetProperty( "LastNonSharedLinksModifiedDateTime" ).GetValue( data );
                results.Add( string.Join( "\t", new[] { added.ToString(), ownVersion.ToString( "o" ) }.Concat( sections ) ) );
            }

            return string.Join( "\n", results );
        }

        /// <summary>
        /// Answers what Rock's top-bar bell shows, on Rock's internal site. "count" and a person's Guid gives how many unread
        /// notification messages Rock counts for them (NotificationMessageService.GetUnreadMessagesForPerson, which the bell
        /// reads). "link" and a message's Guid gives the link Rock's Note Mention component opens for it.
        /// </summary>
        public string Notifications( string[] lines )
        {
            Start();
            var rockAppType = _rock.GetType( "Rock.Configuration.RockApp", true );
            var app = rockAppType.GetProperty( "Current" ).GetValue( null );
            var context = _rock.GetType( "Rock.Configuration.RockAppExtensions", true ).GetMethod( "CreateRockContext", BindingFlags.Public | BindingFlags.Static, null, new[] { rockAppType }, null )
                .Invoke( null, new[] { app } );
            var siteCache = _rock.GetType( "Rock.Web.Cache.SiteCache", true );
            var internalSite = siteCache.GetMethod( "Get", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, null, new[] { typeof( Guid ) }, null )
                .Invoke( null, new object[] { new Guid( "C2D29296-6A87-47A9-A753-EE4E9159C4C4" ) } );
            var serviceType = _rock.GetType( "Rock.Model.NotificationMessageService", true );
            var service = Activator.CreateInstance( serviceType, context );
            var messageType = _rock.GetType( "Rock.Model.NotificationMessage", true );
            var unread = serviceType.GetMethod( "GetUnreadMessagesForPerson", new[] { typeof( Guid ), siteCache } )
                ?? throw new InvalidOperationException( "This Rock has no NotificationMessageService.GetUnreadMessagesForPerson( Guid, SiteCache ). The runner needs updating for this Rock." );
            var count = typeof( Queryable ).GetMethods().First( m => m.Name == "Count" && m.GetParameters().Length == 1 ).MakeGenericMethod( messageType );
            // Rock builds a page's link from the web route table, which its start-up fills from the PageRoute rows.
            _rock.GetType( "Rock.Web.RockRouteHandler", true ).GetMethod( "RegisterRoutes", BindingFlags.Public | BindingFlags.Static ).Invoke( null, null );
            var mention = Activator.CreateInstance( _rock.GetType( "Rock.Core.NotificationMessageTypes.NoteMention", true ), nonPublic: true );
            var action = mention.GetType().GetMethod( "GetActionForNotificationMessage" );
            var results = new List<string>();
            foreach ( var line in lines.Where( l => l.Contains( '\t' ) ) )
            {
                var parts = line.Split( '\t' );
                if ( parts[0] == "count" )
                {
                    results.Add( count.Invoke( null, new[] { unread.Invoke( service, new[] { new Guid( parts[1] ), internalSite } ) } ).ToString() );
                    continue;
                }

                var message = serviceType.GetMethod( "Get", new[] { typeof( Guid ) } ).Invoke( service, new object[] { new Guid( parts[1] ) } );
                var bag = message == null ? null : action.Invoke( mention, new[] { message, internalSite, null } );
                results.Add( bag?.GetType().GetProperty( "Url" ).GetValue( bag ) as string ?? "none" );
            }

            return string.Join( "\n", results );
        }

        /// <summary>
        /// Renders Rock's adaptivemessage Lava command for each person and message, as a page with the command enabled
        /// renders it for that person, and returns the adaptation it shows. Views are not tracked, so nothing is written.
        /// </summary>
        public string AdaptiveMessages( string[] lines )
        {
            Start();
            StartMessageBus();
            StartLava();
            var rockAppType = _rock.GetType( "Rock.Configuration.RockApp", true );
            var app = rockAppType.GetProperty( "Current" ).GetValue( null );
            var createContext = _rock.GetType( "Rock.Configuration.RockAppExtensions", true ).GetMethod( "CreateRockContext", BindingFlags.Public | BindingFlags.Static, null, new[] { rockAppType }, null );
            var personServiceType = _rock.GetType( "Rock.Model.PersonService", true );
            var getPerson = personServiceType.GetMethod( "Get", new[] { typeof( Guid ) } );
            var resolve = _rock.GetType( "Rock.ExtensionMethods", true ).GetMethod( "ResolveMergeFields", new[] { typeof( string ), typeof( IDictionary<string, object> ), typeof( string ), typeof( bool ), typeof( bool ) } )
                ?? throw new InvalidOperationException( "This Rock has no ResolveMergeFields( string, IDictionary, string, bool, bool ). The runner needs updating for this Rock." );
            var htmlContentService = _rock.GetType( "Rock.Model.HtmlContentService", true );
            var getActiveContent = htmlContentService.GetMethod( "GetActiveContentHtml", new[] { typeof( int ), typeof( string ) } );
            var blockCache = _rock.GetType( "Rock.Web.Cache.BlockCache", true );
            var results = new List<string>();
            foreach ( var line in lines.Where( l => l.Contains( '\t' ) ) )
            {
                var parts = line.Split( '\t' );
                var context = createContext.Invoke( null, new[] { app } );
                var person = getPerson.Invoke( Activator.CreateInstance( personServiceType, context ), new object[] { new Guid( parts[0] ) } );
                string lava;
                if ( parts[1].StartsWith( "block:", StringComparison.Ordinal ) )
                {
                    // An HTML Content block's active content, rendered as the block renders it for the signed-in person.
                    var block = blockCache.GetMethod( "Get", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, null, new[] { typeof( Guid ) }, null )
                        .Invoke( null, new object[] { new Guid( parts[1].Substring( "block:".Length ) ) } );
                    lava = block == null ? null : ( string ) getActiveContent.Invoke( Activator.CreateInstance( htmlContentService, context ), new object[] { ( int ) blockCache.GetProperty( "Id" ).GetValue( block ), string.Empty } );
                    if ( lava == null )
                    {
                        results.Add( "error\tthe block or its content is missing" );
                        continue;
                    }
                }
                else
                {
                    lava = $"{{% adaptivemessage messagekey:'{parts[1]}' trackviews:'false' %}}{{{{ messageAdaptation.Id }}}}\t{{{{ messageAdaptation | Attribute:'CallToAction' }}}}{{% endadaptivemessage %}}";
                }

                try
                {
                    var mergeFields = new Dictionary<string, object> { ["Person"] = person, ["CurrentPerson"] = person };
                    var output = ( string ) resolve.Invoke( null, new object[] { lava, mergeFields, "AdaptiveMessage", false, true } );
                    results.Add( parts[1].StartsWith( "block:", StringComparison.Ordinal ) ? string.Join( " ", output.Split( new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries ).Select( s => s.Trim() ) ) : output.Trim() );
                }
                catch ( TargetInvocationException ex )
                {
                    results.Add( $"error\t{ex.GetBaseException().Message.Replace( '\n', ' ' ).Replace( '\r', ' ' )}" );
                }
            }

            return string.Join( "\n", results );
        }

        /// <summary>
        /// Rock's Database authentication component, with its settings read from the database.
        /// </summary>
        private object DatabaseLogin()
        {
            return Activator.CreateInstance( _rock.GetType( "Rock.Security.Authentication.Database", true ) );
        }

        /// <summary>
        /// Runs every stored "Rock Update Helper" job, oldest first, the way Rock's scheduler runs them: as its own
        /// ServiceJob, so it reads its stored settings (such as which block types to swap) and deletes itself when done.
        /// Any failure stops the caller.
        /// </summary>
        private string RunUpdateJobs()
        {
            var rockAppType = _rock.GetType( "Rock.Configuration.RockApp", true );
            var app = rockAppType.GetProperty( "Current" ).GetValue( null );
            var createContext = _rock.GetType( "Rock.Configuration.RockAppExtensions", true ).GetMethod( "CreateRockContext", BindingFlags.Public | BindingFlags.Static, null, new[] { rockAppType }, null )
                ?? throw new InvalidOperationException( "This Rock has no RockAppExtensions.CreateRockContext( RockApp ). The runner needs updating for this Rock." );
            object NewContext() => createContext.Invoke( null, new[] { app } );
            var serviceType = _rock.GetType( "Rock.Model.ServiceJobService", true );
            var jobType = _rock.GetType( "Rock.Model.ServiceJob", true );
            var queryable = ( IEnumerable<object> ) serviceType.GetMethod( "Queryable", Type.EmptyTypes ).Invoke( Activator.CreateInstance( serviceType, NewContext() ), null );
            var jobs = queryable
                .Select( j => (Id: ( int ) jobType.GetProperty( "Id" ).GetValue( j ), Class: ( string ) jobType.GetProperty( "Class" ).GetValue( j ), Name: ( string ) jobType.GetProperty( "Name" ).GetValue( j )) )
                .Where( j => j.Class.StartsWith( "Rock.Jobs.Post", StringComparison.Ordinal ) && j.Class != "Rock.Jobs.PostInstallDataMigrations" )
                .OrderBy( j => j.Id )
                .ToList();

            var rockJob = _rock.GetType( "Rock.Jobs.RockJob", true );
            var loadAttributes = _rock.GetType( "Rock.Attribute.Helper", true ).GetMethods( BindingFlags.Public | BindingFlags.Static )
                .First( m => m.Name == "LoadAttributes" && m.GetParameters().Length == 1 );
            foreach ( var (id, className, name) in jobs )
            {
                var type = AppDomain.CurrentDomain.GetAssemblies().Select( a => a.GetType( className ) ).FirstOrDefault( t => t != null )
                    ?? throw new InvalidOperationException( $"Rock has no job class {className} (job {id}, {name})." );
                var job = Activator.CreateInstance( type );
                var stored = serviceType.GetMethod( "Get", new[] { typeof( int ) } ).Invoke( Activator.CreateInstance( serviceType, NewContext() ), new object[] { id } );
                loadAttributes.Invoke( null, new[] { stored } );
                rockJob.GetProperty( "ServiceJobId" ).GetSetMethod( true ).Invoke( job, new object[] { id } );
                rockJob.GetProperty( "ServiceJob", Hidden ).SetValue( job, stored );
                try
                {
                    Execute( job );
                }
                catch ( TargetInvocationException ex )
                {
                    throw new InvalidOperationException( $"Rock's update job {id} ({name}) failed: {ex.GetBaseException().Message}", ex.InnerException );
                }
            }

            return $"Rock's {jobs.Count} one-time update jobs (WebForms to Obsidian block swaps among them)";
        }

        /*
            11/18/26 - CLAUDE

            Rock's scheduler registers a job type's settings, with their defaults, when it first builds the job
            (ServiceJobService.UpdateAttributesIfNeeded), and runs the stored job with its settings. A new job definition, as
            RockJob.ExecuteInternal makes, has no settings, so Process Reminders found no notification and never tried to
            send one.

            Reason: The Process Reminders and Process Workflows checks must run each job as Rock does.
        */

        /// <summary>
        /// Runs a job as Rock's scheduler does: registers its type's settings, then runs its stored ServiceJob with them.
        /// </summary>
        private void RunStoredJob( object job, Guid serviceJobGuid )
        {
            var rockAppType = _rock.GetType( "Rock.Configuration.RockApp", true );
            var app = rockAppType.GetProperty( "Current" ).GetValue( null );
            var createContext = _rock.GetType( "Rock.Configuration.RockAppExtensions", true ).GetMethod( "CreateRockContext", BindingFlags.Public | BindingFlags.Static, null, new[] { rockAppType }, null );
            var serviceType = _rock.GetType( "Rock.Model.ServiceJobService", true );
            serviceType.GetMethod( "UpdateAttributesIfNeeded", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof( Type ) }, null )
                .Invoke( null, new object[] { job.GetType() } );
            var stored = serviceType.GetMethod( "Get", new[] { typeof( Guid ) } ).Invoke( Activator.CreateInstance( serviceType, createContext.Invoke( null, new[] { app } ) ), new object[] { serviceJobGuid } )
                ?? throw new InvalidOperationException( $"Rock has no stored job {serviceJobGuid}." );
            _rock.GetType( "Rock.Attribute.Helper", true ).GetMethods( BindingFlags.Public | BindingFlags.Static )
                .First( m => m.Name == "LoadAttributes" && m.GetParameters().Length == 1 ).Invoke( null, new[] { stored } );
            var rockJob = _rock.GetType( "Rock.Jobs.RockJob", true );
            rockJob.GetProperty( "ServiceJobId" ).GetSetMethod( true ).Invoke( job, new object[] { stored.GetType().GetProperty( "Id" ).GetValue( stored ) } );
            rockJob.GetProperty( "ServiceJob", Hidden ).SetValue( job, stored );
            Execute( job );
        }

        /// <summary>
        /// Runs one Rock job with its default settings. Any failure stops the caller.
        /// </summary>
        private void RunJob( string typeName )
        {
            try
            {
                Execute( Activator.CreateInstance( _rock.GetType( typeName, true ) ) );
            }
            catch ( TargetInvocationException ex )
            {
                throw new InvalidOperationException( $"{typeName} failed: {ex.GetBaseException().Message}", ex.InnerException );
            }
        }

        /// <summary>
        /// RockJob.ExecuteInternal( Dictionary ) is how Rock's own tests run a job: a new job definition
        /// with its default attribute values, then Execute.
        /// </summary>
        private void Execute( object job )
        {
            var execute = _rock.GetType( "Rock.Jobs.RockJob", true ).GetMethod( "ExecuteInternal", Hidden, null, new[] { typeof( Dictionary<string, string> ) }, null )
                ?? throw new InvalidOperationException( "This Rock.dll has no RockJob.ExecuteInternal( Dictionary ). The runner needs updating for this Rock." );
            execute.Invoke( job, new object[] { new Dictionary<string, string>() } );
        }

        /// <summary>
        /// Starts Rock the way EF's design-time tools do (from the config file's connection string and appSettings),
        /// then runs the first start-up steps every task needs: the organization time zone, save hooks, and RockGetDate.
        /// </summary>
        private void Start()
        {
            _rock = Assembly.Load( "Rock" );
            _startupHelper = Assembly.Load( "Rock.WebStartup" ).GetType( "Rock.WebStartup.RockApplicationStartupHelper", true );

            var rockAppType = _rock.GetType( "Rock.Configuration.RockApp", true );
            var initialize = rockAppType.GetMethod( "InitializeForEntityFrameworkDesignTime", Hidden )
                ?? throw new InvalidOperationException( "This Rock.dll has no InitializeForEntityFrameworkDesignTime. Build Rock for .NET Framework (WebForms)." );
            initialize.Invoke( null, null );

            CallStartup( "InitializeRockOrgTimeZone" );
            CallStartup( "ConfigureEntitySaveHooks" );

            var orgTimeZone = ( TimeZoneInfo ) Assembly.Load( "Rock.Common" ).GetType( "Rock.RockDateTime", true ).GetProperty( "OrgTimeZoneInfo" ).GetValue( null );
            _rock.GetType( "Rock.RockDateTimeHelper", true ).GetMethod( "SynchronizeTimeZoneConfiguration", Hidden ).Invoke( null, new object[] { orgTimeZone.Id } );

            // Rock marks the database available once the time zone is set; jobs and plugin migrations check it.
            var app = ( IServiceProvider ) rockAppType.GetProperty( "Current" ).GetValue( null );
            var databaseConfiguration = app.GetService( _rock.GetType( "Rock.Configuration.IDatabaseConfiguration", true ) );
            databaseConfiguration?.GetType().GetProperty( "IsDatabaseAvailable" )?.SetValue( databaseConfiguration, true );
        }

        /*
            10/21/26 - CLAUDE

            When a job updates something cached, Rock publishes a cache message on its message bus from a
            background task. With the bus never started, that task logs a BusException ("could not be
            published before the message bus was able to be ready") to the Exception Log, but only if it runs
            before the runner unloads Rock. That race made Rock Cleanup's referee check fail now and then, with
            anywhere from 0 to 65 new exceptions from the same data. Rock's own startup starts the bus after the
            plugin migrations (RockApplicationStartupHelper.RunApplicationStartupStage1), and Global.asax then marks Rock
            started. With no other transport set up, Rock uses its in-memory bus, as a single server does.

            Reason: Every publish succeeds, as it does in a running Rock, so the Exception Log only shows real errors.
        */
        private void StartMessageBus()
        {
            var bus = _rock.GetType( "Rock.Bus.RockMessageBus", true );
            var start = bus.GetMethod( "StartAsync", Hidden, null, Type.EmptyTypes, null )
                ?? throw new InvalidOperationException( "This Rock has no RockMessageBus.StartAsync(). The runner needs updating for this Rock." );
            try
            {
                ( ( Task ) start.Invoke( null, null ) ).Wait();
            }
            catch ( AggregateException ex )
            {
                throw new InvalidOperationException( $"The message bus did not start: {ex.GetBaseException().Message}", ex.GetBaseException() );
            }

            bus.GetProperty( "IsRockStarted", Hidden )?.SetValue( null, true );
        }

        /// <summary>
        /// Marks every WebForms (.ascx) block type's instance properties verified in Rock's cache, the state Rock's web
        /// start-up leaves them in, so nothing tries to compile an .ascx outside IIS. Obsidian block types are left alone.
        /// </summary>
        private void MarkWebFormsBlockTypesVerified()
        {
            var cache = _rock.GetType( "Rock.Web.Cache.BlockTypeCache", true );
            var all = ( IEnumerable<object> ) cache.GetMethod( "All", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, null, Type.EmptyTypes, null ).Invoke( null, null );
            var mark = cache.GetMethod( "MarkInstancePropertiesVerified", new[] { typeof( bool ) } )
                ?? throw new InvalidOperationException( "This Rock has no BlockTypeCache.MarkInstancePropertiesVerified( bool ). The runner needs updating for this Rock." );
            foreach ( var blockType in all.Where( b => !string.IsNullOrWhiteSpace( ( string ) cache.GetProperty( "Path" ).GetValue( b ) ) ).ToList() )
            {
                mark.Invoke( blockType, new object[] { true } );
            }
        }

        /// <summary>
        /// Starts Rock's Lava engine with Rock's default configuration. Rock's own InitializeLava gets the engine from an
        /// ILavaEngineFactory service that only the web start-up registers, so the runner makes Rock's factory itself and
        /// hands its engine to LavaService: the same engine and settings a running Rock has.
        /// </summary>
        private void StartLava()
        {
            var factory = Activator.CreateInstance( _rock.GetType( "Rock.Lava.LavaEngineFactory", true ), nonPublic: true );
            var create = factory.GetType().GetMethod( "CreateEngine", Hidden )
                ?? throw new InvalidOperationException( "This Rock has no LavaEngineFactory.CreateEngine. The runner needs updating for this Rock." );
            var lavaService = _rock.GetType( "Rock.Lava.LavaService", true );
            var engineInterface = AppDomain.CurrentDomain.GetAssemblies().Select( a => a.GetType( "Rock.Lava.ILavaEngine" ) ).First( t => t != null );
            var setCurrent = lavaService.GetMethod( "SetCurrentEngine", BindingFlags.Public | BindingFlags.Static, null, new[] { engineInterface }, null )
                ?? throw new InvalidOperationException( "This Rock has no LavaService.SetCurrentEngine( ILavaEngine ). The runner needs updating for this Rock." );
            try
            {
                setCurrent.Invoke( null, new[] { create.Invoke( factory, new object[] { null } ) } );
            }
            catch ( TargetInvocationException ex )
            {
                throw new InvalidOperationException( $"The Lava engine did not start: {ex.GetBaseException().Message}", ex.InnerException );
            }
        }

        private void CallStartup( string name )
        {
            var method = _startupHelper.GetMethod( name, Hidden )
                ?? throw new InvalidOperationException( $"This Rock has no RockApplicationStartupHelper.{name}. The runner needs updating for this Rock." );
            try
            {
                method.Invoke( null, null );
            }
            catch ( TargetInvocationException ex )
            {
                throw new InvalidOperationException( $"{name} failed: {ex.GetBaseException().Message}", ex.InnerException );
            }
        }
    }
}
