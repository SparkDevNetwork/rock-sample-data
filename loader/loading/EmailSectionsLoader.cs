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

using System.Net;

using SixLabors.ImageSharp;

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /*
        11/17/26 - CLAUDE

        Rock 20.0.10 and 21.0.2 store an email section for the newer email editor the same way: an EmailSection row
        with the section component's markup, a usage summary, a category of the EmailSection entity type, and a
        thumbnail file. Rock's Save as Section dialog saves a staff member's section as not IsSystem, names its
        thumbnail after the section with its first space made a hyphen, and stores the thumbnail and every image
        the editor uploads as Rock's Communication Image file type. That type keeps its files on the web server's
        disk, which a load cannot write, so each file here is stored in the database. Rock reads a file through the
        storage provider on its own row, and moves it to its type's provider the next time it is saved.

        Reason: Staff find the church's sections in the editor's Sections panel, with their thumbnails (decision 301).
    */

    /// <summary>
    /// Writes the church's sections for Rock's newer email editor from content/communications/email-sections.json.
    /// </summary>
    internal sealed class EmailSectionsLoader
    {
        private static readonly Guid CommunicationImageFileTypeGuid = new Guid( "60B896C3-F00C-411C-A31C-2D5D4CCBB65F" );
        private const string DatabaseStorageProvider = "Rock.Storage.Provider.Database";

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly string _dataset;
        private readonly string _foreignKey;

        public EmailSectionsLoader( RockDatabase db, SampleDataSource source, string dataset, string foreignKey )
        {
            _db = db;
            _source = source;
            _dataset = dataset;
            _foreignKey = foreignKey;
        }

        /// <summary>
        /// Writes every section with its thumbnail and image. Returns how many it wrote.
        /// </summary>
        /// <param name="campuses">The dataset's campuses, with their service times.</param>
        /// <param name="creatorAliasId">The church's communications sender, who saved the sections.</param>
        public int Load( List<EmailCampus> campuses, int? creatorAliasId )
        {
            var library = _source.ReadEmailSectionLibrary();
            if ( library == null )
            {
                return 0;
            }

            var categoryGuid = _source.Categories.Categories.First( c => c.Key == library.Category ).Guid;
            var categoryId = _db.RequireIdByGuid( "Category", categoryGuid );
            var fileTypeId = _db.RequireIdByGuid( "BinaryFileType", CommunicationImageFileTypeGuid );
            var storageId = Convert.ToInt32( _db.Scalar( "SELECT [Id] FROM [EntityType] WHERE [Name] = @Name", P( "@Name", DatabaseStorageProvider ) )
                ?? throw new InvalidDataException( $"This Rock has no {DatabaseStorageProvider} entity type." ) );
            var photos = _source.ReadEventPhotoManifest().Images.ToDictionary( p => p.Event, p => p.File, StringComparer.Ordinal );
            var now = DateTime.Now;

            foreach ( var section in library.Sections )
            {
                if ( section.Image != null )
                {
                    var photo = photos.TryGetValue( section.Image.Event, out var file ) ? file
                        : throw new InvalidDataException( $"{SampleDataSource.EmailSectionsPath}: {section.Key} shows the photo of {section.Image.Event}, which {SampleDataSource.EventPhotoManifestPath} does not have." );
                    WriteFile( $"photos/{photo}", Path.GetFileName( photo ), "image/jpeg", section.Image.Guid, fileTypeId, storageId, now );
                }

                // Rock's Save as Section dialog names the thumbnail after the section, with only its first space made a hyphen.
                var thumbnail = section.ServiceTimes ? $"{EmailSectionLibrary.Folder}/{section.Key}-{_dataset}.png" : $"{EmailSectionLibrary.Folder}/{section.Key}.png";
                var thumbnailId = WriteFile( thumbnail, $"{ReplaceFirst( section.Name, " ", "-" )}.png", "image/png", section.ThumbnailGuid, fileTypeId, storageId, now );

                var markup = _source.Files.ReadText( $"{EmailSectionLibrary.Folder}/{section.Key}.html" ).Trim();
                if ( section.ServiceTimes )
                {
                    markup = markup.Replace( EmailSectionLibrary.CampusServiceTimesToken, CampusServiceTimes( campuses ) );
                }

                _db.Execute( @"
INSERT INTO [EmailSection] ( [Name], [UsageSummary], [IsSystem], [SourceMarkup], [CategoryId], [ThumbnailBinaryFileId],
    [Guid], [CreatedDateTime], [ModifiedDateTime], [CreatedByPersonAliasId], [ModifiedByPersonAliasId], [ForeignKey] )
VALUES ( @Name, @UsageSummary, 0, @Markup, @CategoryId, @ThumbnailId, @Guid, @Now, @Now, @Author, @Author, @ForeignKey )",
                    P( "@Name", section.Name ), P( "@UsageSummary", section.UsageSummary ), P( "@Markup", markup ), P( "@CategoryId", categoryId ),
                    P( "@ThumbnailId", thumbnailId ), P( "@Guid", section.Guid ), P( "@Now", now ), P( "@Author", creatorAliasId ), P( "@ForeignKey", _foreignKey ) );
            }

            _db.Log.Write( $"  Email sections: {string.Join( ", ", library.Sections.Select( s => s.Name ) )}, for Rock's email editor" );
            return library.Sections.Count;
        }

        /// <summary>
        /// One line for each of the dataset's campuses with its Sunday service times, as make-sections.js draws the thumbnails.
        /// </summary>
        public static string CampusServiceTimes( IEnumerable<EmailCampus> campuses )
        {
            return string.Concat( campuses.Select( c => $"<p style=\"margin: 0;\"><strong>{WebUtility.HtmlEncode( c.Name )}</strong>&nbsp;&nbsp;{c.ServiceTimes}</p>" ) );
        }

        /// <summary>
        /// Stores a package file as a Communication Image in the database, not temporary, and returns its Id.
        /// </summary>
        private int WriteFile( string path, string fileName, string mimeType, Guid guid, int fileTypeId, int storageId, DateTime now )
        {
            var bytes = _source.Files.ReadBytes( path );
            var info = Image.Identify( bytes );
            var id = ( int ) _db.Scalar( @"
INSERT INTO [BinaryFile] ( [IsTemporary], [IsSystem], [BinaryFileTypeId], [FileName], [MimeType], [StorageEntityTypeId], [FileSize], [Width], [Height],
    [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime], [ContentLastModified] )
OUTPUT INSERTED.[Id]
VALUES ( 0, 0, @FileTypeId, @FileName, @MimeType, @StorageId, @FileSize, @Width, @Height, @Guid, @ForeignKey, @Now, @Now, @Now )",
                P( "@FileTypeId", fileTypeId ), P( "@FileName", fileName ), P( "@MimeType", mimeType ), P( "@StorageId", storageId ), P( "@FileSize", bytes.Length ),
                P( "@Width", info.Width ), P( "@Height", info.Height ), P( "@Guid", guid ), P( "@ForeignKey", _foreignKey ), P( "@Now", now ) );
            _db.Execute( @"
INSERT INTO [BinaryFileData] ( [Id], [Content], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
VALUES ( @Id, @Content, NEWID(), @ForeignKey, @Now, @Now )",
                P( "@Id", id ), P( "@Content", bytes ), P( "@ForeignKey", _foreignKey ), P( "@Now", now ) );
            return id;
        }

        private static string ReplaceFirst( string text, string find, string replacement )
        {
            var index = text.IndexOf( find, StringComparison.Ordinal );
            return index < 0 ? text : text.Substring( 0, index ) + replacement + text.Substring( index + find.Length );
        }
    }
}
