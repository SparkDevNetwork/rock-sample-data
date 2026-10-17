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

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RockSampleData.Loader.Packages
{
    /// <summary>
    /// A dataset package: a zip of data files (never SQL) plus package.json. Opening one reads it
    /// into memory and checks it, so a load never starts from a damaged or altered package.
    /// </summary>
    public sealed class SampleDataPackage : IPackageFiles
    {
        /// <summary>
        /// The package format this loader reads.
        /// </summary>
        public const int CurrentFormatVersion = 1;

        internal const string ManifestPath = "package.json";

        internal const string ProfilePath = "profile.json";

        private readonly Dictionary<string, byte[]> _files;

        /// <summary>
        /// The package's package.json.
        /// </summary>
        public PackageManifest Manifest { get; }

        private SampleDataPackage( PackageManifest manifest, Dictionary<string, byte[]> files )
        {
            Manifest = manifest;
            _files = files;
        }

        /// <summary>
        /// Opens and checks a package file.
        /// </summary>
        /// <exception cref="InvalidDataException">The package is not a format this loader reads, or a file is missing, changed, or not listed.</exception>
        public static SampleDataPackage Open( string path )
        {
            using var stream = File.OpenRead( path );
            return Open( stream );
        }

        /// <summary>
        /// Opens and checks a package from a stream, for example a download.
        /// </summary>
        /// <exception cref="InvalidDataException">The package is not a format this loader reads, or a file is missing, changed, or not listed.</exception>
        public static SampleDataPackage Open( Stream stream )
        {
            var files = new Dictionary<string, byte[]>( StringComparer.Ordinal );
            using ( var zip = new ZipArchive( stream, ZipArchiveMode.Read, leaveOpen: true ) )
            {
                foreach ( var entry in zip.Entries.Where( e => !e.FullName.EndsWith( "/" ) ) )
                {
                    using var entryStream = entry.Open();
                    using var buffer = new MemoryStream();
                    entryStream.CopyTo( buffer );
                    files[entry.FullName] = buffer.ToArray();
                }
            }

            if ( !files.TryGetValue( ManifestPath, out var manifestBytes ) )
            {
                throw new InvalidDataException( "This is not a sample data package: it has no package.json." );
            }

            var manifest = JsonSerializer.Deserialize<PackageManifest>( manifestBytes, PackageManifest.Json );
            if ( manifest.FormatVersion != CurrentFormatVersion )
            {
                throw new InvalidDataException( $"The package uses format {manifest.FormatVersion}; this loader reads format {CurrentFormatVersion}." );
            }

            files.Remove( ManifestPath );
            var unlisted = files.Keys.Where( k => !manifest.Files.ContainsKey( k ) ).OrderBy( k => k, StringComparer.Ordinal ).ToList();
            if ( unlisted.Count > 0 )
            {
                throw new InvalidDataException( $"The package has files package.json does not list: {string.Join( ", ", unlisted.Take( 5 ) )}." );
            }

            foreach ( var (path, hash) in manifest.Files )
            {
                if ( !files.TryGetValue( path, out var bytes ) )
                {
                    throw new InvalidDataException( $"The package is missing {path}." );
                }

                if ( Sha256( bytes ) != hash )
                {
                    throw new InvalidDataException( $"{path} does not match its checksum in package.json." );
                }
            }

            return new SampleDataPackage( manifest, files );
        }

        /// <summary>
        /// The lowercase hex SHA-256 of some bytes, as package.json records it.
        /// </summary>
        internal static string Sha256( byte[] bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );

        bool IPackageFiles.Exists( string path ) => _files.ContainsKey( path );

        string IPackageFiles.ReadText( string path ) => Encoding.UTF8.GetString( Get( path ) ).TrimStart( '﻿' );

        byte[] IPackageFiles.ReadBytes( string path ) => Get( path );

        IReadOnlyList<string> IPackageFiles.List( string folder )
        {
            var prefix = $"{folder.TrimEnd( '/' )}/";
            return _files.Keys
                .Where( k => k.StartsWith( prefix, StringComparison.Ordinal ) && k.IndexOf( '/', prefix.Length ) < 0 )
                .OrderBy( k => k, StringComparer.Ordinal )
                .ToList();
        }

        private byte[] Get( string path )
        {
            return _files.TryGetValue( path, out var bytes ) ? bytes : throw new FileNotFoundException( $"The package has no {path}.", path );
        }
    }
}
