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

using System.Collections.Concurrent;
using System.Diagnostics;

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Packages;

namespace RockSampleData.Loader.Loading
{
    /// <summary>
    /// Draws scanned check images (front and back) for recent check gifts and stores them as
    /// Rock transaction images, in database storage.
    /// </summary>
    internal sealed class CheckImageLoader
    {
        private static readonly Guid TransactionImageFileTypeGuid = new Guid( "6D18A9C4-34AB-444A-B95B-C644019465AC" );

        /// <summary>
        /// The church's own (fictional) bank and deposit account, shown in the endorsement stamp.
        /// </summary>
        private const string DepositBank = "Desert Sky Community Bank";
        private const string DepositAccountLast4 = "4471";

        private readonly RockDatabase _db;
        private readonly AnchorDates _dates;
        private readonly string _foreignKey;
        private readonly string _seed;
        private readonly IPackageFiles _files;

        /// <param name="files">The package, which holds the fonts the checks are drawn with.</param>
        public CheckImageLoader( RockDatabase db, AnchorDates dates, string foreignKey, string seed, IPackageFiles files )
        {
            _db = db;
            _dates = dates;
            _foreignKey = foreignKey;
            _seed = seed;
            _files = files;
        }

        /// <summary>
        /// Draws and stores images for check gifts within the given number of months (0 means all of them).
        /// Copies are also written to <paramref name="copyFolder"/> when one is given.
        /// </summary>
        public void Load( List<CheckGift> checks, int months, string copyFolder )
        {
            var cutoff = months > 0 ? _dates.Anchor.AddMonths( -months ) : DateTime.MinValue;
            var selected = checks.Where( c => c.When >= cutoff ).OrderBy( c => c.TransactionId ).ToList();
            if ( selected.Count == 0 )
            {
                Console.WriteLine( "  No check gifts in range." );
                return;
            }

            var payee = OrganizationName();
            var timer = Stopwatch.StartNew();
            var renderer = new CheckImageRenderer( _files );
            var images = new ConcurrentDictionary<int, (byte[] Front, byte[] Back)>();
            Parallel.ForEach( selected, check => images[check.TransactionId] = renderer.Render( ImageData( check, payee ) ) );
            Console.WriteLine( $"  Drew {selected.Count:N0} checks (front and back) in {timer.ElapsedMilliseconds:N0} ms" );

            var fileTypeId = _db.RequireIdByGuid( "BinaryFileType", TransactionImageFileTypeGuid );
            var storageTypeId = Convert.ToInt32( _db.Scalar( "SELECT [StorageEntityTypeId] FROM [BinaryFileType] WHERE [Id] = @Id", RockDatabase.P( "@Id", fileTypeId ) ) );
            var now = DateTime.Now;
            var nextFileId = NextId( "BinaryFile" );
            var nextImageId = NextId( "FinancialTransactionImage" );

            var files = TableFactory.Create( "Id:int", "IsTemporary:bool", "IsSystem:bool", "BinaryFileTypeId:int", "FileName:string", "MimeType:string", "StorageEntityTypeId:int", "FileSize:int", "Width:int", "Height:int", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date", "ContentLastModified:date" );
            var data = TableFactory.Create( "Id:int", "Content:bytes", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var links = TableFactory.Create( "Id:int", "TransactionId:int", "BinaryFileId:int", "Order:int", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            foreach ( var check in selected )
            {
                var (front, back) = images[check.TransactionId];
                var sides = new[] { ("front", front), ("back", back) };
                for ( var order = 0; order < sides.Length; order++ )
                {
                    var fileId = nextFileId++;
                    var name = $"check-{check.CheckNumber}-{sides[order].Item1}.jpg";
                    var scanned = check.When.AddMinutes( 90 );
                    files.Rows.Add( fileId, false, false, fileTypeId, name, "image/jpeg", storageTypeId, sides[order].Item2.Length, CheckImageRenderer.Width, CheckImageRenderer.Height, Guid.NewGuid(), _foreignKey, scanned, scanned, scanned );
                    data.Rows.Add( fileId, sides[order].Item2, Guid.NewGuid(), _foreignKey, scanned, scanned );
                    links.Rows.Add( nextImageId++, check.TransactionId, fileId, order, Guid.NewGuid(), _foreignKey, scanned, now );

                    if ( !string.IsNullOrEmpty( copyFolder ) )
                    {
                        Directory.CreateDirectory( copyFolder );
                        File.WriteAllBytes( Path.Combine( copyFolder, $"{check.TransactionId}-{check.Wallet.Bank.Split( ' ' )[0].ToLowerInvariant()}-{name}" ), sides[order].Item2 );
                    }
                }
            }

            _db.BulkInsert( "BinaryFile", files, keepIdentity: true );
            _db.BulkInsert( "BinaryFileData", data, keepIdentity: false );
            _db.BulkInsert( "FinancialTransactionImage", links, keepIdentity: true );

            var megabytes = images.Values.Sum( i => ( long ) i.Front.Length + i.Back.Length ) / 1024.0 / 1024.0;
            Console.WriteLine( $"  Check images: {selected.Count:N0} checks, {files.Rows.Count:N0} images, {megabytes:N1} MB" );
        }

        /// <summary>
        /// Everything one check shows, taken from the same records the loader wrote for the gift.
        /// </summary>
        private CheckImageData ImageData( CheckGift check, string payee )
        {
            var random = new SeededRandom( $"{_seed}:check:{check.TransactionId}" );
            var household = check.Household;
            var giver = check.Giver.Cast;
            var isBusiness = giver.RecordType == "business";

            // A business prints its name. A joint account prints both spouses; anyone else prints alone.
            var adults = household.Members.Where( m => m.IsAdult && m.FamilyId == household.FamilyId && m.Cast.RecordType != "business" ).Select( m => m.Cast ).ToList();
            var spouse = adults.FirstOrDefault( a => a.Guid != giver.Guid && IsMarried( a ) && IsMarried( giver ) );
            var nameLine = isBusiness
                ? giver.LastName
                : spouse == null
                    ? $"{First( giver )} {giver.LastName}"
                    : spouse.LastName == giver.LastName
                        ? $"{First( giver )} & {First( spouse )} {giver.LastName}"
                        : $"{First( giver )} {giver.LastName} & {First( spouse )} {spouse.LastName}";

            var lines = new List<string> { nameLine };
            var address = household.Cast.Address;
            if ( address != null )
            {
                lines.Add( address.Street1 );
                if ( !string.IsNullOrEmpty( address.Street2 ) )
                {
                    lines.Add( address.Street2 );
                }

                lines.Add( $"{address.City}, {address.State} {address.PostalCode}" );
            }

            var phone = giver.Phones?.Work;
            if ( isBusiness && phone?.Length == 10 )
            {
                lines.Add( $"({phone.Substring( 0, 3 )}) {phone.Substring( 3, 3 )}-{phone.Substring( 6 )}" );
            }

            // Personal checks are mostly written that Sunday, some at home earlier in the week.
            // Mailed business checks are printed a few days before they arrive.
            var written = isBusiness
                ? check.When.Date.AddDays( -random.Between( 2, 5 ) )
                : random.Chance( 0.7 ) ? check.When.Date : check.When.Date.AddDays( -random.Between( 1, 6 ) );
            var deposit = check.When.Date.AddDays( check.When.DayOfWeek == DayOfWeek.Sunday ? 1 : 2 );

            return new CheckImageData(
                lines,
                isBusiness,
                check.CheckNumber,
                written,
                payee,
                check.Amount,
                isBusiness ? "Ministry partner gift" : Memo( check.Funds, check.Wallet.HandwritingIndex ),
                check.Signer ?? $"{First( giver )} {giver.LastName}",
                check.Wallet.Bank,
                check.Wallet.RoutingNumber,
                check.Wallet.AccountNumber,
                check.Wallet.HandwritingIndex,
                deposit,
                DepositBank,
                DepositAccountLast4,
                8000000000L + check.TransactionId );
        }

        /// <summary>
        /// What a giver writes on the memo line, from the funds the gift went to.
        /// </summary>
        private static string Memo( List<string> funds, int handwritingIndex )
        {
            if ( funds.Count > 1 )
            {
                return string.Join( " + ", funds.Select( f => f.Replace( " Fund", string.Empty ).ToLowerInvariant() ) );
            }

            return funds.FirstOrDefault() switch
            {
                "Building Fund" => "Building fund",
                "Mission Fund" => "Missions",
                _ => new[] { "Tithe", "Offering", string.Empty, "General fund" }[handwritingIndex % 4]
            };
        }

        private static bool IsMarried( Model.CastPerson person )
        {
            return string.Equals( person.MaritalStatus, "married", StringComparison.OrdinalIgnoreCase );
        }

        private static string First( Model.CastPerson person )
        {
            return string.IsNullOrEmpty( person.NickName ) ? person.FirstName : person.NickName;
        }

        /// <summary>
        /// The church name from Rock's global attributes, or the sample church's name when it is blank.
        /// </summary>
        private string OrganizationName()
        {
            var value = _db.Scalar( "SELECT TOP 1 ISNULL( NULLIF( [av].[Value], '' ), [a].[DefaultValue] ) FROM [Attribute] AS [a] LEFT JOIN [AttributeValue] AS [av] ON [av].[AttributeId] = [a].[Id] WHERE [a].[Key] = 'OrganizationName' AND [a].[EntityTypeId] IS NULL" ) as string;
            return string.IsNullOrWhiteSpace( value ) ? "Rock Solid Church" : value;
        }

        private int NextId( string table )
        {
            return Convert.ToInt32( _db.Scalar( $"SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [{table}]" ) );
        }
    }
}
