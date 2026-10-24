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
using System.Reflection;

namespace RockSampleData.Loader
{
    /// <summary>
    /// Which Rock is running. A Rock database stores no version number, so the loader reads it the way
    /// Rock's own VersionInfo does: from the file version of Rock's Rock.Version assembly (decision 52).
    /// </summary>
    public static class RockVersionInfo
    {
        /// <summary>
        /// The name of Rock's version assembly, Rock.Version.dll in RockWeb\bin.
        /// </summary>
        public const string AssemblyName = "Rock.Version";

        /// <summary>
        /// Reads the Rock version from a Rock.Version.dll, for example "20.0.10" from
        /// RockWeb\bin\Rock.Version.dll. The assembly is read, never loaded.
        /// </summary>
        /// <exception cref="FileNotFoundException">There is no file at the path.</exception>
        public static string Read( string rockVersionAssemblyPath )
        {
            if ( !File.Exists( rockVersionAssemblyPath ) )
            {
                throw new FileNotFoundException( $"There is no {AssemblyName}.dll at {rockVersionAssemblyPath}, so the Rock version is unknown.", rockVersionAssemblyPath );
            }

            var info = FileVersionInfo.GetVersionInfo( rockVersionAssemblyPath );
            return $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}";
        }

        /// <summary>
        /// The version of the Rock this process runs, when the loader runs inside Rock. Null outside Rock,
        /// where there is no Rock.Version assembly to find.
        /// </summary>
        public static string ReadRunning()
        {
            try
            {
                var location = Assembly.Load( new AssemblyName( AssemblyName ) ).Location;
                return string.IsNullOrEmpty( location ) ? null : Read( location );
            }
            catch ( IOException )
            {
                // Intentionally ignored: outside Rock there is no Rock.Version assembly, and the version is unknown.
                return null;
            }
        }

        /// <summary>
        /// The major release of a Rock version: 20 for "20.0.10". Null when the text does not start with a number.
        /// </summary>
        public static int? MajorOf( string version )
        {
            var first = version?.Split( '.' )[0];
            return int.TryParse( first, out var major ) && major > 0 ? major : null;
        }
    }
}
