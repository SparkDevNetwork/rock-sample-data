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

using RockSampleData.Loader;
using RockSampleData.Loader.Packages;
using RockSampleData.LoaderCli;

/*
    The loader CLI: loads a dataset package into a fresh Rock database, for developers and test
    harnesses. Rock's own installer uses the same loader library.

    Usage:
      rock-sample-data-load --package core-1.zip --connection "<connection string>" [--parts Photos,Giving,...] [--anchor yyyy-MM-dd] [--seed text] [--years 3] [--crowd N] [--check-images] [--check-image-months N] [--check-image-copies <folder>] [--rock <Rock checkout> | --web-config <file>] [--any-rock] [--dry-run]
      rock-sample-data-load --connection "<connection string>" --calibrate <output file>
      rock-sample-data-load --connection "<connection string>" --package core-1.zip --check-cast [--parts Photos,Giving,...]
      rock-sample-data-load --connection "<connection string>" --giving-parity [--dataset core-1]
*/

var options = ParseArguments( args );
if ( !options.TryGetValue( "connection", out var connectionString ) )
{
    Console.Error.WriteLine( "Usage: rock-sample-data-load --package <file.zip> --connection \"<connection string>\" [--anchor yyyy-MM-dd] [--any-rock] [--dry-run]" );
    return 1;
}

try
{
    // --calibrate <file> runs the calibration scripts against the database instead of loading anything.
    if ( options.TryGetValue( "calibrate", out var calibrateOutput ) )
    {
        Console.WriteLine( $"Running calibration scripts into {calibrateOutput}" );
        CalibrationRunner.Run( connectionString, calibrateOutput );
        return 0;
    }

    // --giving-parity is the release-time parity test (validation/README.md): after Rock's own Giving Automation
    // job has run on a loaded database, it compares the values Rock wrote with the loader's port.
    if ( options.ContainsKey( "giving-parity" ) )
    {
        var parityDataset = options.GetValueOrDefault( "dataset" ) ?? ( options.TryGetValue( "package", out var parityPackage ) ? SampleDataPackage.Open( parityPackage ).Manifest.Dataset : "core-1" );
        Console.WriteLine( $"Checking giving attributes against Rock's job (SampleData:{parityDataset})" );
        return DatasetLoader.CheckGivingParity( connectionString, parityDataset, new ConsoleProgress() ) == 0 ? 0 : 1;
    }

    if ( !options.TryGetValue( "package", out var packagePath ) )
    {
        Console.Error.WriteLine( "Give the package to load with --package <file.zip>. The builder (builder/) writes packages." );
        return 1;
    }

    var package = SampleDataPackage.Open( packagePath );

    // --parts names the parts to load, by SampleDataPart name, comma separated. Without it every part loads,
    // except check images, the slowest step, which --check-images and the two options below turn on.
    var isCheckImagesDrawn = options.ContainsKey( "check-images" ) || options.ContainsKey( "check-image-months" ) || options.ContainsKey( "check-image-copies" );
    var parts = options.TryGetValue( "parts", out var partNames )
        ? ParseParts( partNames )
        : SampleDataParts.All.Select( p => p.Part ).Where( p => p != SampleDataPart.CheckImages ).ToList();
    if ( isCheckImagesDrawn && !parts.Contains( SampleDataPart.CheckImages ) )
    {
        parts.Add( SampleDataPart.CheckImages );
    }

    // --check-cast checks the package's Cast expectations (giving and achievements) against an already loaded database.
    if ( options.ContainsKey( "check-cast" ) )
    {
        Console.WriteLine( "Checking Cast expectations" );
        return DatasetLoader.CheckCastExpectations( connectionString, package, parts, new ConsoleProgress() ) == 0 ? 0 : 1;
    }

    // Rock's PasswordKey fills the check scanner's matching fields. It comes from a Rock checkout's
    // RockWeb/web.config: --rock <checkout folder>, or the ROCK_PATH environment variable. --web-config
    // names the file directly.
    var rockPath = options.GetValueOrDefault( "rock" ) ?? Environment.GetEnvironmentVariable( "ROCK_PATH" );
    var webConfig = options.GetValueOrDefault( "web-config" ) ?? ( string.IsNullOrEmpty( rockPath ) ? null : Path.Combine( rockPath, "RockWeb", "web.config" ) );

    DatasetLoader.Load( connectionString, package, new DatasetLoadOptions
    {
        Anchor = options.TryGetValue( "anchor", out var anchor ) ? DateTime.Parse( anchor ) : null,
        Seed = options.GetValueOrDefault( "seed", "rock-sample-data" ),
        HistoryYears = int.Parse( options.GetValueOrDefault( "years", "3" ) ),
        CrowdHouseholds = options.TryGetValue( "crowd", out var crowd ) ? int.Parse( crowd ) : null,
        Parts = parts,
        Progress = new ConsoleProgress(),
        CheckImageMonths = options.TryGetValue( "check-image-months", out var months ) ? int.Parse( months ) : null,
        CheckImageCopyFolder = options.GetValueOrDefault( "check-image-copies" ),
        RockWebConfigPath = webConfig,
        IsDryRun = options.ContainsKey( "dry-run" ),
        IsAnyRockVersionAllowed = options.ContainsKey( "any-rock" )
    } );
    return 0;
}
catch ( InvalidDataException ex )
{
    Console.Error.WriteLine( $"The package cannot be loaded. {ex.Message}" );
    return 1;
}
catch ( RockVersionMismatchException ex )
{
    Console.Error.WriteLine( $"Nothing was loaded. {ex.Message} For a development database, pass --any-rock to load anyway." );
    return 1;
}
catch ( DatabaseNotReadyException ex )
{
    Console.Error.WriteLine( $"Nothing was loaded. {ex.Message}" );
    return 1;
}
catch ( ArgumentException ex )
{
    Console.Error.WriteLine( ex.Message );
    return 1;
}
catch ( Exception ex )
{
    Console.Error.WriteLine( $"Load failed and was rolled back: {ex.Message}" );
    return 1;
}

/// <summary>
/// Reads a comma-separated list of part names, such as "Photos,SmallGroups", in any case.
/// </summary>
static List<SampleDataPart> ParseParts( string names )
{
    var parts = new List<SampleDataPart>();
    foreach ( var name in names.Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) )
    {
        if ( !Enum.TryParse<SampleDataPart>( name, true, out var part ) || !Enum.IsDefined( part ) )
        {
            throw new ArgumentException( $"--parts has \"{name}\", which is not a part. The parts are {string.Join( ", ", SampleDataParts.All.Select( p => p.Part ) )}." );
        }

        parts.Add( part );
    }

    return parts;
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
