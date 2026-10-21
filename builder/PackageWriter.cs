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
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using RockSampleData.Loader;
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Packages;

namespace RockSampleData.Builder
{
    /// <summary>
    /// Writes one dataset's package: the data files a load reads, under the same paths as in this
    /// repository, plus a resolved profile.json and package.json. Never SQL, and no timeline: the
    /// loader generates every date on install day.
    /// </summary>
    internal static class PackageWriter
    {
        private const string PoolSet = "core-3-pool";

        private static readonly string[] TextExtensions = { ".json", ".md", ".txt" };

        /// <summary>
        /// Every zip entry gets this time, so the same inputs always make the same bytes.
        /// </summary>
        private static readonly DateTimeOffset EntryTime = new DateTimeOffset( 2000, 1, 1, 0, 0, 0, TimeSpan.Zero );

        /// <summary>
        /// Builds, checks, and saves one dataset's package. Returns its path and manifest.
        /// </summary>
        public static (string Path, PackageManifest Manifest) Write( string root, string dataset, string profileName, PackageRock rock, string outputFolder )
        {
            var repository = new FolderFiles( root );
            var source = new SampleDataSource( repository );
            var datasetManifest = source.ReadDataset( dataset )
                ?? throw new InvalidOperationException( $"There is no datasets/{dataset}.json." );
            var files = new SortedDictionary<string, byte[]>( StringComparer.Ordinal );

            void AddFile( string path ) => files[path] = Normalize( path, repository.ReadBytes( path ) );
            void AddFolder( string folder, string extension = null )
            {
                foreach ( var path in repository.List( folder ).Where( p => extension == null || p.EndsWith( extension, StringComparison.Ordinal ) ) )
                {
                    AddFile( path );
                }
            }

            // The church setup, the Cast, and the dataset's own settings, as they are in the repository.
            AddFolder( "configuration", ".json" );
            AddFolder( "cast/households", ".json" );
            AddFolder( "cast/groups", ".json" );
            if ( repository.Exists( SampleDataSource.CastPrayerRequestsPath ) )
            {
                AddFile( SampleDataSource.CastPrayerRequestsPath );
            }

            AddFile( $"datasets/{dataset}.json" );

            // The profile is resolved here, so a load never blends calibration sources itself.
            files[SampleDataPackage.ProfilePath] = Encoding.UTF8.GetBytes( ProfileTargets.Resolve( repository, profileName ).ToJson() );

            // Only the dataset's campuses' addresses, and only the photos the dataset shows.
            var campusKeys = source.Campuses.Campuses.Where( c => c.Datasets.Contains( dataset ) ).Select( c => c.Key ).ToHashSet();
            files["addresses/pool.json"] = Utf8( FilterAddressPool( repository.ReadText( "addresses/pool.json" ), campusKeys ) );
            var isPoolIncluded = ( datasetManifest.Photos?.GeneratedHouseholdPercent ?? 0 ) > 0;
            var (photoManifest, photoFiles) = FilterPhotos( repository.ReadText( "photos/manifest.json" ), isPoolIncluded );
            files["photos/manifest.json"] = Utf8( photoManifest );
            foreach ( var photo in photoFiles )
            {
                AddFile( $"photos/{photo}" );
            }

            // Every dataset has the Cast, so every package carries the Cast's small group photos.
            if ( repository.Exists( SampleDataSource.GroupPhotoManifestPath ) )
            {
                AddFile( SampleDataSource.GroupPhotoManifestPath );
                foreach ( var photo in source.ReadGroupPhotoManifest().Images )
                {
                    AddFile( $"photos/{photo.File}" );
                }
            }

            // Fonts for check images, with their licenses, the prayer text pools, and the license for the data itself.
            AddFolder( "content/fonts" );
            AddFolder( "content/prayer", ".json" );
            AddFolder( "content/prayer/categories", ".json" );
            AddFile( "LICENSE.md" );

            // A README that travels with the package: what is in it, what the loader makes from it, and what
            // makes a load repeatable. Filled in for this dataset and this Rock.
            files["README.md"] = Normalize( "README.md", Utf8( repository.ReadText( "builder/package-readme.md" )
                .Replace( "{{name}}", datasetManifest.Name )
                .Replace( "{{dataset}}", dataset )
                .Replace( "{{rockVersion}}", rock?.Version ?? "(unknown)" )
                .Replace( "{{rockMigration}}", rock?.Migration ?? "(unknown)" ) ) );

            var manifest = new PackageManifest
            {
                FormatVersion = SampleDataPackage.CurrentFormatVersion,
                Dataset = dataset,
                Name = datasetManifest.Name,
                Profile = profileName,
                Source = ReadSource( root ),
                Rock = rock
            };
            foreach ( var (path, bytes) in files )
            {
                manifest.Files[path] = SampleDataPackage.Sha256( bytes );
            }

            var zip = Zip( manifest, files );
            Check( zip, campusKeys );

            Directory.CreateDirectory( outputFolder );
            var packagePath = Path.Combine( outputFolder, $"{dataset}.zip" );
            File.WriteAllBytes( packagePath, zip );
            return (packagePath, manifest);
        }

        /// <summary>
        /// Opens the finished package the way a load does and reads every file a load reads, so a
        /// package that would fail to load is never written.
        /// </summary>
        private static void Check( byte[] zip, HashSet<string> campusKeys )
        {
            using var stream = new MemoryStream( zip );
            var package = SampleDataPackage.Open( stream );
            var source = new SampleDataSource( package );
            IPackageFiles files = package;

            source.ReadHouseholds();
            var castGroupKeys = source.ReadCastSmallGroupPlans().Select( p => p.Key ).ToHashSet();
            foreach ( var photo in source.ReadGroupPhotoManifest().Images )
            {
                if ( !castGroupKeys.Contains( photo.Group ) )
                {
                    throw new InvalidOperationException( $"photos/groups/manifest.json has a photo for {photo.Group}, which is not a Cast small group." );
                }

                if ( !files.Exists( $"photos/{photo.File}" ) )
                {
                    throw new InvalidOperationException( $"photos/groups/manifest.json lists {photo.File}, but the file is missing." );
                }
            }

            source.ReadProfile();
            AddressPool.Load( files, "check" );
            foreach ( var photo in source.ReadPhotoManifest().Images.Where( p => !files.Exists( $"photos/{p.File}" ) ) )
            {
                throw new InvalidOperationException( $"photos/manifest.json lists {photo.File}, but the file is missing." );
            }

            var poolCampuses = JsonNode.Parse( files.ReadText( "addresses/pool.json" ) )["campuses"].AsObject();
            var missingCampus = campusKeys.OrderBy( k => k, StringComparer.Ordinal ).FirstOrDefault( k => !poolCampuses.ContainsKey( k ) );
            if ( missingCampus != null )
            {
                throw new InvalidOperationException( $"addresses/pool.json has no addresses for campus {missingCampus}." );
            }
        }

        /// <summary>
        /// Keeps only the given campuses in the address pool. Each campus's list is unchanged, so
        /// generated households get the same addresses as from the full pool.
        /// </summary>
        private static string FilterAddressPool( string json, HashSet<string> campusKeys )
        {
            var pool = JsonNode.Parse( json ).AsObject();
            var campuses = pool["campuses"].AsObject();
            foreach ( var key in campuses.Select( c => c.Key ).Where( k => !campusKeys.Contains( k ) ).ToList() )
            {
                campuses.Remove( key );
            }

            return pool.ToJsonString() + "\n";
        }

        /// <summary>
        /// Keeps the Cast photos, plus the pool when the dataset gives generated households faces.
        /// Returns the filtered manifest and the photo files to include.
        /// </summary>
        private static (string Manifest, List<string> Files) FilterPhotos( string json, bool isPoolIncluded )
        {
            var manifest = JsonNode.Parse( json ).AsObject();
            var images = manifest["images"].AsArray();
            foreach ( var image in images.Where( i => !( i["set"].GetValue<string>() == "cast" || ( isPoolIncluded && i["set"].GetValue<string>() == PoolSet ) ) ).ToList() )
            {
                images.Remove( image );
            }

            var options = new JsonSerializerOptions { WriteIndented = true, NewLine = "\n", Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            return (manifest.ToJsonString( options ) + "\n", images.Select( i => i["file"].GetValue<string>() ).ToList());
        }

        /// <summary>
        /// Writes the zip: package.json first, then every file in ordinal order, all with one fixed
        /// time, so the same inputs always make a byte-identical package.
        /// </summary>
        private static byte[] Zip( PackageManifest manifest, SortedDictionary<string, byte[]> files )
        {
            using var buffer = new MemoryStream();
            using ( var zip = new ZipArchive( buffer, ZipArchiveMode.Create, leaveOpen: true ) )
            {
                void Add( string path, byte[] bytes )
                {
                    var entry = zip.CreateEntry( path, CompressionLevel.Optimal );
                    entry.LastWriteTime = EntryTime;
                    using var stream = entry.Open();
                    stream.Write( bytes );
                }

                Add( SampleDataPackage.ManifestPath, Utf8( JsonSerializer.Serialize( manifest, PackageManifest.Json ) + "\n" ) );
                foreach ( var (path, bytes) in files )
                {
                    Add( path, bytes );
                }
            }

            return buffer.ToArray();
        }

        /// <summary>
        /// Text files get LF line endings, so a package does not depend on the machine that built it.
        /// </summary>
        private static byte[] Normalize( string path, byte[] bytes )
        {
            if ( !TextExtensions.Any( e => path.EndsWith( e, StringComparison.OrdinalIgnoreCase ) ) )
            {
                return bytes;
            }

            return Utf8( Encoding.UTF8.GetString( bytes ).TrimStart( '﻿' ).Replace( "\r\n", "\n" ) );
        }

        private static byte[] Utf8( string text ) => Encoding.UTF8.GetBytes( text );

        /// <summary>
        /// The repository's current commit, and whether the working tree has changes. Git is
        /// optional: without it the commit is "unknown".
        /// </summary>
        private static PackageSource ReadSource( string root )
        {
            var commit = RunGit( root, "rev-parse HEAD" );
            var status = RunGit( root, "status --porcelain" );
            return new PackageSource
            {
                Commit = string.IsNullOrWhiteSpace( commit ) ? "unknown" : commit.Trim(),
                IsModified = !string.IsNullOrWhiteSpace( status )
            };
        }

        private static string RunGit( string root, string arguments )
        {
            try
            {
                using var process = Process.Start( new ProcessStartInfo( "git", arguments )
                {
                    WorkingDirectory = root,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                } );
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return process.ExitCode == 0 ? output : null;
            }
            catch ( System.ComponentModel.Win32Exception )
            {
                // Intentionally ignored: git is not installed, so the commit is recorded as unknown.
                return null;
            }
        }
    }
}
