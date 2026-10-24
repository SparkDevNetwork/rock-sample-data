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

using System.Reflection;

namespace RockSampleData.Loader.Packages
{
    /// <summary>
    /// This loader's own version, and the order of loader versions. A loader version is the NuGet
    /// package's semantic version, for example "1.2.0" or "0.1.0-alpha.13" (decision D-rock-major-2).
    /// </summary>
    public static class LoaderVersion
    {
        /// <summary>
        /// This loader's version, without any build metadata after a "+".
        /// </summary>
        public static string Current { get; } = typeof( LoaderVersion ).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split( '+' )[0]
            ?? typeof( LoaderVersion ).Assembly.GetName().Version.ToString( 3 );

        /// <summary>
        /// True when this loader is the given version or newer. A null minimum is met by every loader.
        /// </summary>
        /// <exception cref="FormatException">The minimum is not a semantic version.</exception>
        public static bool IsAtLeast( string minimum )
        {
            return minimum == null || Compare( Current, minimum ) >= 0;
        }

        /// <summary>
        /// Orders two semantic versions as SemVer 2.0 does: by major, minor, and patch, then a prerelease
        /// sorts below its release, and prerelease parts compare one by one, numbers below words.
        /// Build metadata after a "+" is ignored.
        /// </summary>
        /// <exception cref="FormatException">Either text is not a semantic version.</exception>
        public static int Compare( string a, string b )
        {
            var (aNumbers, aLabels) = Parse( a );
            var (bNumbers, bLabels) = Parse( b );
            for ( var i = 0; i < 3; i++ )
            {
                if ( aNumbers[i] != bNumbers[i] )
                {
                    return aNumbers[i].CompareTo( bNumbers[i] );
                }
            }

            if ( aLabels.Length == 0 || bLabels.Length == 0 )
            {
                return bLabels.Length.CompareTo( aLabels.Length );
            }

            for ( var i = 0; i < Math.Min( aLabels.Length, bLabels.Length ); i++ )
            {
                var isANumber = long.TryParse( aLabels[i], out var aNumber );
                var isBNumber = long.TryParse( bLabels[i], out var bNumber );
                var order = isANumber && isBNumber ? aNumber.CompareTo( bNumber )
                    : isANumber != isBNumber ? ( isANumber ? -1 : 1 )
                    : string.CompareOrdinal( aLabels[i], bLabels[i] );
                if ( order != 0 )
                {
                    return Math.Sign( order );
                }
            }

            return aLabels.Length.CompareTo( bLabels.Length );
        }

        /// <summary>
        /// Splits a version into its three numbers and its prerelease parts.
        /// </summary>
        private static (long[] Numbers, string[] Labels) Parse( string version )
        {
            var text = version?.Split( '+' )[0] ?? string.Empty;
            var dash = text.IndexOf( '-' );
            var numbers = ( dash < 0 ? text : text.Substring( 0, dash ) ).Split( '.' );
            var labels = dash < 0 ? new string[0] : text.Substring( dash + 1 ).Split( '.' );
            if ( numbers.Length != 3 || labels.Any( string.IsNullOrEmpty ) )
            {
                throw new FormatException( $"\"{version}\" is not a version like 1.0.0 or 1.1.0-beta.1." );
            }

            var parsed = new long[3];
            for ( var i = 0; i < 3; i++ )
            {
                if ( !long.TryParse( numbers[i], out parsed[i] ) || parsed[i] < 0 )
                {
                    throw new FormatException( $"\"{version}\" is not a version like 1.0.0 or 1.1.0-beta.1." );
                }
            }

            return (parsed, labels);
        }
    }
}
