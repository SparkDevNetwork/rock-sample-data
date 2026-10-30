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
    /// Usage: rock-runner --task startup|cleanup|hash-password|sign-in|group-sync|data-views|reports|authorize --rock-bin <RockWeb\bin folder> --config <config file> [--input <file>]
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
    ///   reports  Checks every report's columns the way Rock's report grid reads them, and renders each Lava column, and
    ///            prints "ok" and its Guid, or "error", its Guid, and the problems.
    ///   authorize  Asks Rock's own security check (Authorization.Authorized) each input line's question: an item kind
    ///            (Category, DataView, or Report), its Guid, an action, and a person's Guid, separated by tabs. Prints True or
    ///            False for each, in order.
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
            var tasks = new[] { "startup", "cleanup", "hash-password", "sign-in", "group-sync", "data-views", "reports", "authorize" };
            if ( rockBin == null || config == null || !tasks.Contains( task ) || ( task is "hash-password" or "sign-in" or "authorize" && input == null ) )
            {
                Console.Error.WriteLine( "Usage: rock-runner --task startup|cleanup|hash-password|sign-in|group-sync|data-views|reports|authorize --rock-bin <RockWeb\\bin folder> --config <config file> [--input <file>]" );
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
                    "reports" => worker.Reports(),
                    "authorize" => worker.Authorize( lines ),
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
                            else if ( componentName == "Rock.Reporting.DataSelect.LiquidSelect" )
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
                    }
                }
                catch ( TargetInvocationException ex )
                {
                    problems.Add( ex.GetBaseException().Message.Replace( '\n', ' ' ).Replace( '\r', ' ' ) );
                }

                lines.Add( problems.Count == 0 ? $"ok\t{guid}" : $"error\t{guid}\t{string.Join( "; ", problems )}" );
            }

            return string.Join( "\n", lines );
        }

        /// <summary>
        /// Asks Rock's own security check whether each person may take each action on each category or data view. Rock walks
        /// the item's parent authorities (a data view's category, then its parent categories) as it does on every page.
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
                if ( parts[0] == "Report" )
                {
                    // Rock keeps no cache of reports; its report screens load the report and check it.
                    var reportService = _rock.GetType( "Rock.Model.ReportService", true );
                    item = reportService.GetMethod( "Get", new[] { typeof( Guid ) } )
                        .Invoke( Activator.CreateInstance( reportService, personServiceType.GetProperty( "Context" ).GetValue( personService ) ), new object[] { new Guid( parts[1] ) } );
                }
                else
                {
                    var cache = _rock.GetType( parts[0] == "Category" ? "Rock.Web.Cache.CategoryCache" : "Rock.Web.Cache.DataViewCache", true );
                    item = cache.GetMethod( "Get", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, null, new[] { typeof( Guid ) }, null )
                        .Invoke( null, new object[] { new Guid( parts[1] ) } );
                }

                var person = getPerson.Invoke( personService, new object[] { new Guid( parts[3] ) } );
                results.Add( item == null || person == null ? "missing" : authorized.Invoke( null, new[] { item, parts[2], person } ).ToString() );
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
