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

using System.Data;
using System.Text.RegularExpressions;

using RockSampleData.Loader.Database;

namespace RockSampleData.Loader.Loading
{
    /// <summary>
    /// Collects drawn documents as Rock stores an uploaded file: a BinaryFile row in its file type, with the bytes in
    /// BinaryFileData under the same Id (Rock's database storage), then bulk-copies them.
    /// </summary>
    internal sealed class BinaryFileRows
    {
        private static readonly Regex InvalidFileNameCharacters = new Regex( "[\"<>|\u0000-\u001f:*?\\\\/]+", RegexOptions.CultureInvariant );

        private readonly DataTable _files = TableFactory.Create( "Id:int", "IsTemporary:bool", "IsSystem:bool", "BinaryFileTypeId:int", "FileName:string", "MimeType:string", "StorageEntityTypeId:int", "FileSize:int",
            "ParentEntityTypeId:int", "ParentEntityId:int", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date", "ContentLastModified:date" );

        private readonly DataTable _data = TableFactory.Create( "Id:int", "Content:bytes", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

        private readonly string _foreignKey;
        private int _nextId;

        public BinaryFileRows( RockDatabase db, string foreignKey )
        {
            _foreignKey = foreignKey;
            _nextId = Convert.ToInt32( db.Scalar( "SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [BinaryFile]" ) );
        }

        /// <summary>
        /// How many files are waiting to be written.
        /// </summary>
        public int Count => _files.Rows.Count;

        /// <summary>
        /// Adds a PDF and returns its BinaryFile Id. The parent is what Rock records the file was uploaded for, if anything.
        /// </summary>
        public int AddPdf( BinaryFileType type, string fileName, byte[] content, Guid guid, Guid dataGuid, DateTime created, int? parentEntityTypeId = null, int? parentEntityId = null )
        {
            var id = _nextId++;
            _files.Rows.Add( id, false, false, type.Id, fileName, "application/pdf", type.StorageEntityTypeId, content.Length,
                ( object ) parentEntityTypeId ?? DBNull.Value, ( object ) parentEntityId ?? DBNull.Value, guid, _foreignKey, created, created, created );
            _data.Rows.Add( id, content, dataGuid, _foreignKey, created, created );
            return id;
        }

        /// <summary>
        /// Bulk-copies the files and their bytes.
        /// </summary>
        public void Write( RockDatabase db )
        {
            db.BulkInsert( "BinaryFile", _files, keepIdentity: true );
            db.BulkInsert( "BinaryFileData", _data, keepIdentity: false );
        }

        /// <summary>
        /// The name Rock's PDF generator gives a file: spaces as underscores, characters a file name cannot hold
        /// replaced (MakeValidFileName, with Windows' list), and ".pdf".
        /// </summary>
        public static string PdfFileName( string name ) => InvalidFileNameCharacters.Replace( name.Replace( " ", "_" ), "_" ).Replace( ";", string.Empty ).Replace( ",", string.Empty ) + ".pdf";

        /// <summary>
        /// Reads a binary file type's Id and storage by Guid.
        /// </summary>
        public static BinaryFileType Type( RockDatabase db, Guid guid )
        {
            var row = db.Query( "SELECT [Id], [StorageEntityTypeId] FROM [BinaryFileType] WHERE [Guid] = @Guid", RockDatabase.P( "@Guid", guid ) ).FirstOrDefault()
                ?? throw new InvalidOperationException( $"Rock has no binary file type {guid}." );
            return new BinaryFileType( Convert.ToInt32( row["Id"] ), Convert.ToInt32( row["StorageEntityTypeId"] ) );
        }

        /// <summary>
        /// A binary file type: its Id and where Rock stores its files.
        /// </summary>
        internal readonly struct BinaryFileType
        {
            public BinaryFileType( int id, int storageEntityTypeId )
            {
                Id = id;
                StorageEntityTypeId = storageEntityTypeId;
            }

            public int Id { get; }

            public int StorageEntityTypeId { get; }
        }
    }
}
