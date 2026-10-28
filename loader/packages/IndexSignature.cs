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
using System.Text.Json;

namespace RockSampleData.Loader.Packages
{
    /// <summary>
    /// Checks the signature on the package index (decision 172). The release workflow signs the
    /// index's exact bytes with a key in Spark's Azure Key Vault and carries them, base64, in the index's
    /// "signed" object. This class checks the signature against the public keys in
    /// release/index-signing-keys.json, which the loader embeds, and returns those exact bytes as text.
    /// The index is read only from them, so nothing outside the signature is ever trusted. The format is in
    /// the loader README, under "The package index".
    /// </summary>
    internal static class IndexSignature
    {
        /// <summary>
        /// The only algorithm an index may be signed with: ECDSA on P-256 with SHA-256, the signature as r
        /// then s, 32 bytes each, as Key Vault returns it.
        /// </summary>
        internal const string Algorithm = "ES256";

        private const string KeysResourceName = "RockSampleData.Loader.index-signing-keys.json";

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private static readonly Lazy<IReadOnlyList<IndexSigningKey>> EmbeddedKeys = new Lazy<IReadOnlyList<IndexSigningKey>>( ReadEmbeddedKeys );

        /// <summary>
        /// Spark's public keys: every key, active or spare, that may sign an index this loader reads.
        /// </summary>
        internal static IReadOnlyList<IndexSigningKey> SparkKeys => EmbeddedKeys.Value;

        /// <summary>
        /// Checks the signature on an index file and returns the signed index as text.
        /// </summary>
        /// <param name="json">The whole index file.</param>
        /// <param name="keys">The keys to trust. Shipped code passes <see cref="SparkKeys"/>; only tests pass others.</param>
        /// <exception cref="InvalidDataException">The index is not signed, or not by one of the keys, or the signature does not match.</exception>
        internal static string Verify( string json, IReadOnlyList<IndexSigningKey> keys )
        {
            SignedIndexFile file;
            try
            {
                file = JsonSerializer.Deserialize<SignedIndexFile>( json ?? string.Empty, Json );
            }
            catch ( JsonException ex )
            {
                throw new InvalidDataException( $"The package index is not valid JSON: {ex.Message}", ex );
            }

            var signed = file?.Signed;
            if ( signed == null )
            {
                throw new InvalidDataException( "The package index is not signed, so this loader can't trust it. A loader from 0.1.0-alpha.14 on reads only an index Spark signed." );
            }

            if ( signed.Algorithm != Algorithm )
            {
                throw new InvalidDataException( $"The package index is signed with {signed.Algorithm ?? "no algorithm"}; this loader checks only {Algorithm}." );
            }

            var key = keys.FirstOrDefault( k => k.KeyId == signed.KeyId );
            if ( key == null )
            {
                throw new InvalidDataException( $"The package index is signed by key '{signed.KeyId}', which this loader does not know. A newer loader may know it." );
            }

            var payload = FromBase64( signed.Payload, "payload" );
            var signature = FromBase64( signed.Signature, "signature" );

            if ( signature.Length != 64 || !key.Verifies( payload, signature ) )
            {
                throw new InvalidDataException( "The package index's signature does not match its contents, so it was not used." );
            }

            return new UTF8Encoding( false, true ).GetString( payload );
        }

        private static byte[] FromBase64( string value, string name )
        {
            try
            {
                return Convert.FromBase64String( value ?? string.Empty );
            }
            catch ( FormatException ex )
            {
                throw new InvalidDataException( $"The package index's signed {name} is not base64.", ex );
            }
        }

        private static IReadOnlyList<IndexSigningKey> ReadEmbeddedKeys()
        {
            using ( var stream = typeof( IndexSignature ).Assembly.GetManifestResourceStream( KeysResourceName ) )
            using ( var reader = new StreamReader( stream ?? throw new InvalidOperationException( $"The loader is missing its {KeysResourceName} resource." ) ) )
            {
                return ParseKeys( reader.ReadToEnd() );
            }
        }

        /// <summary>
        /// Reads a keys file in the form of release/index-signing-keys.json.
        /// </summary>
        internal static IReadOnlyList<IndexSigningKey> ParseKeys( string json )
        {
            var file = JsonSerializer.Deserialize<KeysFile>( json, Json );
            return ( file?.Keys ?? new List<KeyEntry>() )
                .Select( k => new IndexSigningKey( k.KeyId, Convert.FromBase64String( k.X ), Convert.FromBase64String( k.Y ) ) )
                .ToList();
        }

        private sealed class SignedIndexFile
        {
            public SignedPart Signed { get; set; }
        }

        private sealed class SignedPart
        {
            public string KeyId { get; set; }

            public string Algorithm { get; set; }

            public string Payload { get; set; }

            public string Signature { get; set; }
        }

        private sealed class KeysFile
        {
            public List<KeyEntry> Keys { get; set; }
        }

        private sealed class KeyEntry
        {
            public string KeyId { get; set; }

            public string X { get; set; }

            public string Y { get; set; }
        }
    }

    /// <summary>
    /// One public key that may sign the package index: a P-256 point.
    /// </summary>
    internal sealed class IndexSigningKey
    {
        private readonly byte[] _x;
        private readonly byte[] _y;

        internal IndexSigningKey( string keyId, byte[] x, byte[] y )
        {
            if ( string.IsNullOrWhiteSpace( keyId ) || x?.Length != 32 || y?.Length != 32 )
            {
                throw new InvalidDataException( $"Index signing key '{keyId}' is not a P-256 public key." );
            }

            KeyId = keyId;
            _x = x;
            _y = y;
        }

        /// <summary>
        /// The key's name, as an index's "signed.keyId" gives it.
        /// </summary>
        internal string KeyId { get; }

        /// <summary>
        /// True when the signature, r then s, is this key's signature on SHA-256 of the data.
        /// </summary>
        internal bool Verifies( byte[] data, byte[] signature )
        {
            var parameters = new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = _x, Y = _y }
            };

            using ( var ecdsa = ECDsa.Create( parameters ) )
            using ( var sha256 = SHA256.Create() )
            {
                return ecdsa.VerifyHash( sha256.ComputeHash( data ), signature );
            }
        }
    }
}
