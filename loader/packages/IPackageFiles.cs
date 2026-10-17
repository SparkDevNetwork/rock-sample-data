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

namespace RockSampleData.Loader.Packages
{
    /// <summary>
    /// The files a load reads, by relative path with forward slashes (for example
    /// "cast/households/decker.json"). A package supplies them when loading; the repository
    /// folder supplies them when the builder makes a package.
    /// </summary>
    internal interface IPackageFiles
    {
        /// <summary>
        /// True when the file exists.
        /// </summary>
        bool Exists( string path );

        /// <summary>
        /// Reads a UTF-8 text file.
        /// </summary>
        string ReadText( string path );

        /// <summary>
        /// Reads a file's bytes.
        /// </summary>
        byte[] ReadBytes( string path );

        /// <summary>
        /// The paths of the files directly inside a folder, in ordinal order, or none when the folder is missing.
        /// </summary>
        IReadOnlyList<string> List( string folder );
    }
}
