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

using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

using HashidsNet;

using RockSampleData.Loader.Compat;

namespace RockSampleData.Loader.Database
{
    /*
        10/15/26 - CLAUDE

        Rock's check scanner finds who wrote a check by hashing the routing and account
        numbers with the server's PasswordKey (FinancialPersonBankAccount.AccountNumberSecured).
        The builder runs outside Rock, so it reads the key from a Rock checkout's web.config
        (--rock, ROCK_PATH, or --web-config), which development sites run with. Hashes then
        match a Rock site running from the same checkout. A real install generates its own
        keys, so the loader inside Rock must hash with the server's key instead.

        Reason: Scanning a sample check in Rock should find the person who wrote it.

        10/16/26 - CLAUDE

        Rock's two-way encryption (Encryption.EncryptString, v2: AES-CBC with an HMAC-SHA256 tag,
        keys derived from the DataEncryptionKey) is now copied too, for CheckMicrParts and
        CheckMicrEncrypted. Rock's transaction matching decrypts CheckMicrParts to suggest who wrote
        an unmatched check. The copy was checked by decrypting its output with Rock's own Rock.dll.
        Its source is Rock/Security/Encryption.cs, which the version guard watches.

        Reason: The sample data's open batch of unmatched checks should show Rock's suggestions.
    */

    /// <summary>
    /// The PasswordKey and DataEncryptionKey from a Rock web.config, with Rock's keyed hash and two-way encryption.
    /// </summary>
    internal sealed class RockKeys
    {
        private static readonly byte[] Salt = Encoding.ASCII.GetBytes( "rsduYVC2leenXKTLYLkO9qsWU95HGCvWlbXcBTjtrj5dBJ7RPeGYiw7U3lZE+LWkT+jGrLP9deRMc8sUHJtc/wu2l4vANBx5f+p1zpRwQ2bB/E6Ta8k7haPiTRc4wYhrmWMrg8VfQ4MhAsSlijIfT9u+DszEkB2ba2k0FIPMSWk=" );
        private static readonly byte[] HkdfInfo = Encoding.UTF8.GetBytes( "Rock.Security.Encryption.v2" );
        private static readonly byte[] V2Footer = Encoding.ASCII.GetBytes( "V2" );

        private readonly byte[] _passwordKey;
        private readonly byte[] _encryptionKey;
        private readonly byte[] _macKey;
        private readonly Hashids _idHasher;

        private RockKeys( byte[] passwordKey, string dataEncryptionKey )
        {
            _passwordKey = passwordKey;
            if ( string.IsNullOrWhiteSpace( dataEncryptionKey ) )
            {
                return;
            }

            // IdHasher: Hashids salted with the first 40 characters of the DataEncryptionKey, at least 10 characters long.
            _idHasher = new Hashids( dataEncryptionKey.Length > 40 ? dataEncryptionKey.Substring( 0, 40 ) : dataEncryptionKey, 10 );

            // Encryption._dataEncryptionKeyBytes: PBKDF2 (SHA-1, 1,000 rounds) over the key text, then
            // DeriveKeys: HKDF-SHA256 with the same salt, split into an encryption key and a MAC key.
            var keyBytes = KeyDerivation.Pbkdf2( Encoding.UTF8.GetBytes( dataEncryptionKey ), Salt, 1000, HashAlgorithmName.SHA1, 32 );
            var okm = KeyDerivation.HkdfSha256( keyBytes, 64, Salt, HkdfInfo );
            _encryptionKey = okm.Take( 32 ).ToArray();
            _macKey = okm.Skip( 32 ).ToArray();
        }

        /// <summary>
        /// Whether the web.config had a DataEncryptionKey, so <see cref="EncryptString"/> works.
        /// </summary>
        public bool CanEncrypt => _encryptionKey != null;

        /// <summary>
        /// The same value as Rock's IdHasher for an Id (an IdKey), such as the identifier in a note mention.
        /// Null when the web.config had no DataEncryptionKey.
        /// </summary>
        public string IdKey( int id ) => _idHasher?.Encode( id );

        /// <summary>
        /// Reads the PasswordKey and the DataEncryptionKey from a Rock web.config. Either is null when the
        /// file or that setting is missing.
        /// </summary>
        public static (string PasswordKey, string DataEncryptionKey) ReadWebConfig( string webConfigPath )
        {
            if ( !File.Exists( webConfigPath ) )
            {
                return (null, null);
            }

            var settings = XDocument.Load( webConfigPath ).Descendants( "appSettings" ).Elements( "add" ).ToList();
            string Setting( string name ) => settings.FirstOrDefault( e => ( string ) e.Attribute( "key" ) == name )?.Attribute( "value" )?.Value;
            return (Setting( "PasswordKey" ), Setting( "DataEncryptionKey" ));
        }

        /// <summary>
        /// The keys as Rock's web.config holds them: the PasswordKey in hex, and the DataEncryptionKey as text.
        /// Returns null when there is no PasswordKey. The DataEncryptionKey may be null.
        /// </summary>
        /// <exception cref="ArgumentException">The PasswordKey is not hex.</exception>
        public static RockKeys FromKeys( string passwordKey, string dataEncryptionKey )
        {
            return string.IsNullOrWhiteSpace( passwordKey ) ? null : new RockKeys( ParseHex( passwordKey.Trim() ), dataEncryptionKey );
        }

        private static byte[] ParseHex( string hex )
        {
            if ( hex.Length % 2 != 0 || !hex.All( Uri.IsHexDigit ) )
            {
                throw new ArgumentException( "The PasswordKey must be hex, as it is in Rock's web.config." );
            }

            return Enumerable.Range( 0, hex.Length / 2 ).Select( i => Convert.ToByte( hex.Substring( i * 2, 2 ), 16 ) ).ToArray();
        }

        /// <summary>
        /// The same value as Rock's Encryption.EncryptString (v2): Base64 of [IV length][IV][ciphertext][HMAC tag]["V2"].
        /// The IV is random, so each call gives a different (equally valid) value.
        /// </summary>
        public string EncryptString( string plainText )
        {
            if ( string.IsNullOrEmpty( plainText ) )
            {
                return string.Empty;
            }

            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = _encryptionKey;

            using var output = new MemoryStream();
            using ( var encryptor = aes.CreateEncryptor( aes.Key, aes.IV ) )
            {
                output.Write( BitConverter.GetBytes( aes.IV.Length ), 0, sizeof( int ) );
                output.Write( aes.IV, 0, aes.IV.Length );

                // Rock writes the text with a StreamWriter in UTF-8, which puts a byte order mark first.
                using var crypto = new CryptoStream( output, encryptor, CryptoStreamMode.Write, true );
                using var writer = new StreamWriter( crypto, Encoding.UTF8 );
                writer.Write( plainText );
            }

            var data = output.ToArray();
            using var hmac = new HMACSHA256( _macKey );
            var tag = hmac.ComputeHash( data );
            output.Write( tag, 0, tag.Length );
            output.Write( V2Footer, 0, V2Footer.Length );
            return Convert.ToBase64String( output.ToArray() );
        }

        /// <summary>
        /// The same value as Rock's Encryption.GetSHA1Hash: HMAC-SHA1 over the UTF-16 text, keyed with the PasswordKey, in Base64.
        /// </summary>
        public string Hash( string plainText )
        {
            using var hmac = new HMACSHA1( _passwordKey );
            return Convert.ToBase64String( hmac.ComputeHash( Encoding.Unicode.GetBytes( plainText ) ) );
        }

        /// <summary>
        /// The same value as FinancialPersonBankAccount.EncodeAccountNumber: the hash of "routing|account".
        /// </summary>
        public string BankAccountHash( string routingNumber, string accountNumber )
        {
            return Hash( $"{routingNumber.Trim()}|{accountNumber.Trim()}" );
        }
    }
}
