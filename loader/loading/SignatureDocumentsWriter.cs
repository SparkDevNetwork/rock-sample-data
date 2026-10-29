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
using RockSampleData.Loader.Generation;

namespace RockSampleData.Loader.Loading
{
    /*
        10/29/26 - CLAUDE

        Each document is the one Rock's registration entry saves when a release is embedded (RegistrationEntry
        CreateSignatureDocument, the same in Rock 20 and 21). The registrar is logged in and fills the form, so they are
        both the person the document is assigned to and the signer; the registrant is who it applies to. A parent signs
        for a child, and a student who registers themselves signs their own. They sign a minute or two before they
        submit, and the document is saved with the registration. The typed name, email, IP address, and user agent are
        HTML-encoded, as Rock encodes what the browser sends, and the hash covers them. A typed signature has no drawn
        data, so SignatureDataEncrypted is empty. Rock sets no EntityTypeId or EntityId here. The PDF goes in the
        release's binary file type, with the release as its parent, and Rock's receipt emails it right after the save.
        A registrar with no email in Rock types one into the signature form; Rock keeps it on the document only.

        Reason: Decision D-registrations-14. The Signed Documents list, the registrant's signed release, and its PDF
        read the way Rock wrote them.
    */

    /// <summary>
    /// Writes the signed release, its PDF, and its binary file for every registrant on a template that requires one.
    /// </summary>
    internal sealed class SignatureDocumentsWriter
    {
        /// <summary>
        /// Rock's SignatureDocumentStatus.Signed.
        /// </summary>
        private const int StatusSigned = 2;

        private static readonly Guid SignatureDocumentTemplateEntityTypeGuid = new Guid( "3F9828CC-8224-4AB0-98A5-6D60001EBE32" );

        /// <summary>
        /// The browsers registrars sign from. Each registrar keeps one.
        /// </summary>
        private static readonly string[] UserAgents =
        {
            "Mozilla/5.0 (iPhone; CPU iPhone OS 18_6 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.6 Mobile/15E148 Safari/604.1",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Linux; Android 14; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Mobile Safari/537.36",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.6 Safari/605.1.15",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36 Edg/141.0.0.0",
            "Mozilla/5.0 (iPad; CPU OS 18_6 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.6 Mobile/15E148 Safari/604.1"
        };

        private static readonly Regex InvalidFileNameCharacters = new Regex( "[\"<>|\u0000-\u001f:*?\\\\/]+", RegexOptions.CultureInvariant );

        private readonly RockDatabase _db;
        private readonly string _foreignKey;
        private readonly string _seed;

        public SignatureDocumentsWriter( RockDatabase db, string foreignKey, string seed )
        {
            _db = db;
            _foreignKey = foreignKey;
            _seed = seed;
        }

        /// <summary>
        /// Writes every signed release. Returns each registrant's document Id.
        /// </summary>
        public Dictionary<PlannedRegistrant, int> Write( List<PlannedRegistration> registrations, RegistrationIds ids )
        {
            var documentIds = new Dictionary<PlannedRegistrant, int>();
            var signed = registrations.Where( r => ids.Templates[r.Instance.Template.Key].Signature != null ).ToList();
            if ( signed.Count == 0 )
            {
                return documentIds;
            }

            var organization = OrganizationName();
            var templateEntityTypeId = _db.RequireIdByGuid( "EntityType", SignatureDocumentTemplateEntityTypeGuid );
            var releases = _db.Query( @"
SELECT [sdt].[Id], [sdt].[BinaryFileTypeId], [bft].[StorageEntityTypeId], [sdt].[CompletionSystemCommunicationId]
FROM [SignatureDocumentTemplate] AS [sdt]
INNER JOIN [BinaryFileType] AS [bft] ON [bft].[Id] = [sdt].[BinaryFileTypeId]" )
                .ToDictionary( r => Convert.ToInt32( r["Id"] ), r => (FileTypeId: Convert.ToInt32( r["BinaryFileTypeId"] ), StorageTypeId: Convert.ToInt32( r["StorageEntityTypeId"] ), SendsReceipt: r["CompletionSystemCommunicationId"] is int) );

            var files = TableFactory.Create( "Id:int", "IsTemporary:bool", "IsSystem:bool", "BinaryFileTypeId:int", "FileName:string", "MimeType:string", "StorageEntityTypeId:int", "FileSize:int",
                "ParentEntityTypeId:int", "ParentEntityId:int", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date", "ContentLastModified:date" );
            var data = TableFactory.Create( "Id:int", "Content:bytes", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var documents = TableFactory.Create( "Id:int", "SignatureDocumentTemplateId:int", "Name:string", "AppliesToPersonAliasId:int", "AssignedToPersonAliasId:int", "Status:int", "LastStatusDate:date",
                "BinaryFileId:int", "SignedByPersonAliasId:int", "InviteCount:int", "SignedDocumentText:string", "SignedName:string", "SignedClientIp:string", "SignedClientUserAgent:string",
                "SignedDateTime:date", "SignedByEmail:string", "CompletionEmailSentDateTime:date", "SignatureVerificationHash:string", "SignatureDataEncrypted:string",
                "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            var nextFileId = NextId( "BinaryFile" );
            var nextDocumentId = NextId( "SignatureDocument" );
            foreach ( var registration in signed )
            {
                var template = ids.Templates[registration.Instance.Template.Key];
                var release = releases[template.SignatureTemplateId.Value];
                var registrar = registration.Registrar;
                var device = new SeededRandom( $"{_seed}:registrations:signature-device:{registrar.Cast.Guid}" );
                var userAgent = SignatureVerification.HtmlEncode( UserAgents[device.Between( 0, UserAgents.Length - 1 )] );
                var ipAddress = SignatureVerification.HtmlEncode( IpAddress( registrar ) );
                var signedName = SignatureVerification.HtmlEncode( FullName( registrar ) );
                var signedEmail = SignatureVerification.HtmlEncode( registrar.Cast.Email ?? TypedEmail( registrar ) );
                var random = new SeededRandom( $"{_seed}:registrations:signatures:{registration.Guid}" );

                // Each registrant's release is signed in turn, a minute or two before the registration is submitted.
                var minutes = registration.Registrants.Count + random.Between( 0, 3 );
                foreach ( var registrant in registration.Registrants )
                {
                    var signedAt = registration.When.AddMinutes( -minutes ).AddSeconds( random.Between( 0, 59 ) );
                    if ( signedAt < registration.When.Date )
                    {
                        signedAt = registration.When.Date;
                    }

                    minutes--;
                    var person = registrant.Person.Cast;
                    var text = SignatureLava.Render( template.Signature.LavaTemplate, person.FirstName, person.LastName, registration.Instance.Name, organization );
                    var hash = SignatureVerification.Hash( text, ipAddress, userAgent, signedAt, registrar.AliasId, string.Empty, signedName );
                    var name = $"{FullName( registrant.Person )} ({registration.Instance.Name})";
                    var pdf = SignedReleasePdf.Draw( name, text, FullName( registrar ), signedAt, IpAddress( registrar ), hash );
                    var receiptSent = release.SendsReceipt ? registration.When.AddSeconds( random.Between( 5, 40 ) ) : ( DateTime? ) null;

                    var fileId = nextFileId++;
                    var documentId = nextDocumentId++;
                    documentIds[registrant] = documentId;
                    files.Rows.Add( fileId, false, false, release.FileTypeId, FileName( name ), "application/pdf", release.StorageTypeId, pdf.Length, templateEntityTypeId, template.SignatureTemplateId.Value,
                        SeededRandom.StableGuid( $"{registrant.Guid}:signed-release-file" ), _foreignKey, registration.When, registration.When, registration.When );
                    data.Rows.Add( fileId, pdf, SeededRandom.StableGuid( $"{registrant.Guid}:signed-release-data" ), _foreignKey, registration.When, registration.When );
                    documents.Rows.Add( documentId, template.SignatureTemplateId.Value, name, registrant.Person.AliasId, registrar.AliasId, StatusSigned, signedAt,
                        fileId, registrar.AliasId, 0, text, signedName, ipAddress, userAgent, signedAt, signedEmail, ( object ) receiptSent ?? DBNull.Value, hash, string.Empty,
                        SeededRandom.StableGuid( $"{registrant.Guid}:signed-release" ), _foreignKey, registration.When, ( object ) receiptSent ?? registration.When );
                }
            }

            _db.BulkInsert( "BinaryFile", files, keepIdentity: true );
            _db.BulkInsert( "BinaryFileData", data, keepIdentity: false );
            _db.BulkInsert( "SignatureDocument", documents, keepIdentity: true );
            return documentIds;
        }

        /// <summary>
        /// The name Rock's PDF generator gives the file: spaces as underscores, characters a file name cannot hold
        /// replaced (MakeValidFileName, with Windows' list), and ".pdf".
        /// </summary>
        private static string FileName( string name ) => InvalidFileNameCharacters.Replace( name.Replace( " ", "_" ), "_" ).Replace( ";", string.Empty ).Replace( ",", string.Empty ) + ".pdf";

        /// <summary>
        /// The registrar's address, from the ranges reserved for documentation, the same one their email opens use.
        /// </summary>
        private static string IpAddress( LoadedPerson person )
        {
            var nets = new[] { "192.0.2", "198.51.100", "203.0.113" };
            return $"{nets[person.PersonId % 3]}.{( person.PersonId / 3 % 254 ) + 1}";
        }

        /// <summary>
        /// The email a registrar with none in Rock types into the signature form.
        /// </summary>
        private static string TypedEmail( LoadedPerson person )
        {
            string Part( string value ) => new string( ( value ?? string.Empty ).ToLowerInvariant().Where( char.IsLetter ).ToArray() );
            return $"{Part( person.Cast.NickName ?? person.Cast.FirstName )}.{Part( person.Cast.LastName )}@example.org";
        }

        private static string FullName( LoadedPerson person ) => $"{person.Cast.NickName ?? person.Cast.FirstName} {person.Cast.LastName}";

        private string OrganizationName()
        {
            return _db.Scalar( "SELECT TOP 1 ISNULL( NULLIF( [av].[Value], '' ), [a].[DefaultValue] ) FROM [Attribute] AS [a] LEFT JOIN [AttributeValue] AS [av] ON [av].[AttributeId] = [a].[Id] WHERE [a].[Key] = 'OrganizationName' AND [a].[EntityTypeId] IS NULL" ) as string
                ?? throw new InvalidOperationException( "Rock's OrganizationName global attribute was not found." );
        }

        private int NextId( string table ) => Convert.ToInt32( _db.Scalar( $"SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [{table}]" ) );
    }
}
