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

using RockSampleData.Loader.Packages;

namespace RockSampleData.Verify
{
    /// <summary>
    /// Which commit of this repository ran the checks, and whether it had uncommitted changes.
    /// </summary>
    internal static class Git
    {
        public static PackageSource Source( string root )
        {
            return new PackageSource
            {
                Commit = Run( root, "rev-parse", "HEAD" ).Trim(),
                IsModified = Run( root, "status", "--porcelain" ).Trim().Length > 0
            };
        }

        private static string Run( string root, params string[] arguments )
        {
            var start = new ProcessStartInfo( "git" ) { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true };
            foreach ( var argument in arguments )
            {
                start.ArgumentList.Add( argument );
            }

            using var process = Process.Start( start );
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return output;
        }
    }
}
