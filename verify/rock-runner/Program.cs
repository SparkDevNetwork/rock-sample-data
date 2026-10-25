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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace RockSampleData.RockRunner
{
    /// <summary>
    /// Runs a piece of Rock against the database named in a config file, outside IIS.
    ///
    /// Usage: rock-runner --task startup|cleanup --rock-bin <RockWeb\bin folder> --config <config file>
    ///
    ///   startup  Rock's own start-up steps that write to the database: the RockGetDate function, plugin and
    ///            hotfix migrations, and the attribute value views, then Rock's one-time post-install job, which a new
    ///            install runs on its first night. The trusted base runs it once after EF's migrations, so it matches
    ///            a Rock that has started, not just a migrated database.
    ///   cleanup  Rock's Rock Cleanup job once, with its default settings. It prints the job's result.
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
            if ( rockBin == null || config == null || ( task != "startup" && task != "cleanup" ) )
            {
                Console.Error.WriteLine( "Usage: rock-runner --task startup|cleanup --rock-bin <RockWeb\\bin folder> --config <config file>" );
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
                Console.WriteLine( task == "startup" ? worker.Startup() : worker.Cleanup() );
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
        /// Steps that need a web server (block types from .ascx files, Lava) are left out.
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
