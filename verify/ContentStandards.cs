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
    /// The content standards (docs/content-standards.md) as checks: the Cast validator and the repository-wide
    /// standards check. Both are Node scripts, so a contributor can run them without a database.
    /// </summary>
    internal static class ContentStandards
    {
        public static CheckResult Run( string root )
        {
            var problems = new List<string>();
            var notes = new List<string>();
            foreach ( var script in new[] { "cast/validate-cast.js", "validation/check-standards.js" } )
            {
                var start = new ProcessStartInfo( "node" )
                {
                    WorkingDirectory = root,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                start.ArgumentList.Add( script );

                using var process = Process.Start( start );
                var errors = process.StandardError.ReadToEndAsync();
                var lines = process.StandardOutput.ReadToEnd().Split( '\n' ).Select( l => l.Trim() ).Where( l => l.Length > 0 ).ToList();
                process.WaitForExit();

                problems.AddRange( lines.Where( l => l.StartsWith( "ERROR" ) ).Select( l => $"{script}: {l.Substring( 5 ).Trim()}" ) );
                notes.Add( $"{script}: {lines.LastOrDefault()}" );
                if ( process.ExitCode != 0 && !lines.Any( l => l.StartsWith( "ERROR" ) ) )
                {
                    problems.Add( $"{script} failed: {errors.Result.Trim()}" );
                }
            }

            return CheckResult.From( "content-standards", "The Cast validator and the content standards check pass", problems, notes );
        }
    }
}
