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

namespace RockSampleData.Verify
{
    /// <summary>
    /// Runs a piece of Rock itself against one database, through rock-runner (see rock-runner/Program.cs):
    /// Rock's start-up steps, its Rock Cleanup job, or its Database login's hashing and sign-in.
    /// </summary>
    internal static class RockRunner
    {
        /// <summary>
        /// Runs a task. The input, when given, goes to rock-runner in a file, one item per line.
        /// </summary>
        public static string Run( RockRelease rock, SqlServer sql, string database, string task, IEnumerable<string> input = null )
        {
            var work = Directory.CreateTempSubdirectory( "rock-sample-data-runner-" );
            try
            {
                var config = Path.Combine( work.FullName, "rock-runner.config" );
                rock.WriteRunnerConfig( sql.RockConnectionString( database ), config );

                var start = new ProcessStartInfo( FindRunner() )
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                foreach ( var argument in new[] { "--task", task, "--rock-bin", rock.BinPath, "--config", config } )
                {
                    start.ArgumentList.Add( argument );
                }

                if ( input != null )
                {
                    var inputFile = Path.Combine( work.FullName, "input.txt" );
                    File.WriteAllLines( inputFile, input );
                    start.ArgumentList.Add( "--input" );
                    start.ArgumentList.Add( inputFile );
                }

                using var process = Process.Start( start );
                var errors = process.StandardError.ReadToEndAsync();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                if ( process.ExitCode != 0 )
                {
                    // The first line names the failure; the whole output, with Rock's stack trace, is kept for whoever fixes it.
                    var log = Path.Combine( Path.GetTempPath(), $"rock-sample-data-runner-{database}-{task}.log" );
                    File.WriteAllText( log, errors.Result );
                    throw new InvalidOperationException( $"Rock {task} did not run on {database}: {errors.Result.Trim().Split( '\n' )[0]} (full output: {log})" );
                }

                return output;
            }
            finally
            {
                work.Delete( true );
            }
        }

        private static string FindRunner()
        {
            var runner = Path.Combine( AppContext.BaseDirectory, "rock-runner", "rock-runner.exe" );
            return File.Exists( runner ) ? runner : throw new FileNotFoundException( "rock-runner was not built next to the verify tool.", runner );
        }
    }
}
