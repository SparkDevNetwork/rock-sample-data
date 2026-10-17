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

using RockSampleData.Builder;
using RockSampleData.Loader;
using RockSampleData.Loader.Model;

/*
    The builder: reads this repository and writes dataset packages. It never touches a database;
    the loader CLI (loader-cli/) loads packages.

    Usage:
      dotnet run -- [--dataset core-1|all] [--out <folder>] [--rock <Rock checkout>] [--rock-version 21.0.1] [--rock-migration <id>] [--profile sample-church] [--root <repository folder>]
      dotnet run -- --plan-cast-groups [--seed text] [--root <repository folder>]
      dotnet run -- --deliver-photos <batch.json> [--root <repository folder>]
*/

var options = ParseArguments( args );
var root = options.TryGetValue( "root", out var rootOption ) ? rootOption : SampleDataSource.FindRoot( AppContext.BaseDirectory );

// --plan-cast-groups writes the Cast's small groups to cast/groups.
if ( options.ContainsKey( "plan-cast-groups" ) )
{
    var castPlans = CastGroupWriter.Run( root, options.GetValueOrDefault( "seed", "rock-sample-data" ) );
    foreach ( var plan in castPlans )
    {
        var state = plan.Lifecycle.GetType().Name.Replace( "Lifecycle", string.Empty );
        Console.WriteLine( $"  {plan.Key,-22} {plan.Name,-20} {state,-9} {plan.TestCase,-8} {plan.Members.Count( m => m is ActiveMember ),2} active of {plan.Members.Count,2}" );
    }

    Console.WriteLine( $"Planned {castPlans.Count} Cast small groups into cast/groups." );
    return 0;
}

// --deliver-photos <batch.json> turns new images from the image model into delivered photos (photos/brief.md).
if ( options.TryGetValue( "deliver-photos", out var photoBatch ) )
{
    try
    {
        var delivered = PhotoDelivery.Run( root, photoBatch );
        Console.WriteLine( $"Delivered {delivered} photos. Run node cast/validate-cast.js, then node cast/build-pages.js." );
        return 0;
    }
    catch ( Exception ex ) when ( ex is InvalidOperationException || ex is FileNotFoundException )
    {
        Console.Error.WriteLine( $"Nothing was delivered. {ex.Message}" );
        return 1;
    }
}

// Otherwise, write packages. Every package is for one exact Rock: the version and newest migration
// come from a Rock checkout (--rock, or ROCK_PATH), or are given directly.
try
{
    var rock = RockCheckout.Read(
        options.GetValueOrDefault( "rock" ) ?? Environment.GetEnvironmentVariable( "ROCK_PATH" ),
        options.GetValueOrDefault( "rock-version" ),
        options.GetValueOrDefault( "rock-migration" ) );
    var outputFolder = options.GetValueOrDefault( "out" ) ?? Path.Combine( root, "packages" );
    var profile = options.GetValueOrDefault( "profile", "sample-church" );
    var datasetOption = options.GetValueOrDefault( "dataset", "all" );
    var datasets = datasetOption == "all"
        ? Directory.GetFiles( Path.Combine( root, "datasets" ), "*.json" ).Select( Path.GetFileNameWithoutExtension ).OrderBy( k => k, StringComparer.Ordinal ).ToList()
        : new List<string> { datasetOption };

    Console.WriteLine( $"Packing for Rock {rock.Version} (newest migration {rock.Migration})" );
    foreach ( var dataset in datasets )
    {
        var (path, manifest) = PackageWriter.Write( root, dataset, profile, rock, outputFolder );
        Console.WriteLine( $"  {dataset}: {manifest.Files.Count:N0} files, {new FileInfo( path ).Length / 1024.0 / 1024.0:N1} MB -> {path}" );
    }

    return 0;
}
catch ( Exception ex ) when ( ex is InvalidOperationException || ex is IOException || ex is InvalidDataException )
{
    Console.Error.WriteLine( $"No package was written. {ex.Message}" );
    return 1;
}

/// <summary>
/// Reads "--name value" pairs and bare "--flag" switches.
/// </summary>
static Dictionary<string, string> ParseArguments( string[] args )
{
    var result = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
    for ( var i = 0; i < args.Length; i++ )
    {
        if ( !args[i].StartsWith( "--" ) )
        {
            continue;
        }

        var name = args[i].Substring( 2 );
        var hasValue = i + 1 < args.Length && !args[i + 1].StartsWith( "--" );
        result[name] = hasValue ? args[++i] : "true";
    }

    return result;
}
