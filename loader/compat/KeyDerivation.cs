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

namespace RockSampleData.Loader.Compat
{
    /*
        10/21/26 - CLAUDE

        Rock's Encryption derives its keys with PBKDF2 and then HKDF. .NET Framework has PBKDF2 only
        through the Rfc2898DeriveBytes constructor, and has no HKDF at all, so this is RFC 5869 written
        out. Check scanner fields and note mentions are keyed by these bytes, so both targets must give
        exactly the same output. compat-check/ proves it against values from .NET 10's own methods.

        Reason: The loader builds for Rock's .NET Framework 4.7.2 too (decision D-rock-loader-1).
    */

    /// <summary>
    /// PBKDF2 and HKDF with the same output on .NET 10 and .NET Framework.
    /// </summary>
    internal static class KeyDerivation
    {
        /// <summary>
        /// The same bytes as .NET 10's <c>Rfc2898DeriveBytes.Pbkdf2</c>. .NET Framework needs a salt of at least
        /// eight bytes; Rock's is 172 bytes.
        /// </summary>
        public static byte[] Pbkdf2( byte[] password, byte[] salt, int iterations, HashAlgorithmName hashAlgorithm, int length )
        {
#if NETFRAMEWORK
            using var pbkdf2 = new Rfc2898DeriveBytes( password, salt, iterations, hashAlgorithm );
            return pbkdf2.GetBytes( length );
#else
            return Rfc2898DeriveBytes.Pbkdf2( password, salt, iterations, hashAlgorithm, length );
#endif
        }

        /// <summary>
        /// The same bytes as .NET 10's <c>HKDF.DeriveKey</c> with SHA-256.
        /// </summary>
        public static byte[] HkdfSha256( byte[] inputKey, int length, byte[] salt, byte[] info )
        {
#if NETFRAMEWORK
            // Extract: the pseudorandom key is HMAC( salt, input key ). An empty salt means a block of zeros.
            byte[] pseudorandomKey;
            using ( var extract = new HMACSHA256( salt == null || salt.Length == 0 ? new byte[32] : salt ) )
            {
                pseudorandomKey = extract.ComputeHash( inputKey );
            }

            // Expand: T(i) = HMAC( key, T(i - 1) | info | i ), concatenated until there are enough bytes.
            var output = new byte[length];
            var block = new byte[0];
            using var expand = new HMACSHA256( pseudorandomKey );
            for ( var written = 0; written < length; )
            {
                var counter = ( byte ) ( written / 32 + 1 );
                block = expand.ComputeHash( block.Concat( info ?? new byte[0] ).Concat( new[] { counter } ).ToArray() );
                var count = Math.Min( block.Length, length - written );
                Array.Copy( block, 0, output, written, count );
                written += count;
            }

            return output;
#else
            return HKDF.DeriveKey( HashAlgorithmName.SHA256, inputKey, length, salt, info );
#endif
        }
    }
}
