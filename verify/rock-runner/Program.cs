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
using System.Linq;
using System.Reflection;

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
    /// RockWeb\bin, so Rock's assemblies load exactly as they do under IIS. Exits with 0 when the task ran,
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

            var setup = new AppDomainSetup
            {
                ApplicationBase = rockBin,
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
        /// Steps that need a web server (block types from .ascx files, the message bus, Lava) are left out.
        /// </summary>
        public string Startup()
        {
            Start();
            var steps = new List<string>();

            CallStartup( "MigratePlugins" );
            steps.Add( "plugin and hotfix migrations" );

            CallStartup( "InitializeQueryableAttributeValues" );
            steps.Add( "attribute value views" );

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
