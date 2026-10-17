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
    /// Files read straight from a folder, such as the rock-sample-data repository.
    /// </summary>
    internal sealed class FolderFiles : IPackageFiles
    {
        /// <summary>
        /// The folder every path is relative to.
        /// </summary>
        public string Root { get; }

        public FolderFiles( string root )
        {
            Root = root;
        }

        /// <inheritdoc/>
        public bool Exists( string path ) => File.Exists( FullPath( path ) );

        /// <inheritdoc/>
        public string ReadText( string path ) => File.ReadAllText( FullPath( path ) );

        /// <inheritdoc/>
        public byte[] ReadBytes( string path ) => File.ReadAllBytes( FullPath( path ) );

        /// <inheritdoc/>
        public IReadOnlyList<string> List( string folder )
        {
            var full = FullPath( folder );
            if ( !Directory.Exists( full ) )
            {
                return Array.Empty<string>();
            }

            return Directory.GetFiles( full )
                .Select( f => $"{folder.TrimEnd( '/' )}/{Path.GetFileName( f )}" )
                .OrderBy( f => f, StringComparer.Ordinal )
                .ToList();
        }

        private string FullPath( string path ) => Path.Combine( Root, path.Replace( '/', Path.DirectorySeparatorChar ) );
    }
}
